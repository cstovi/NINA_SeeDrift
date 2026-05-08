using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using NINA.Core.Utility;
using NINA.Equipment.Model;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.Plugin.SeeDrift.Models;
using NINA.Plugin.SeeDrift.Utility;

namespace NINA.Plugin.SeeDrift.Services {

    /// <summary>
    /// Accumulates drift samples from saved LIGHT frames via <see cref="IImageSaveMediator.ImageSaved"/>
    /// or from offline FITS folder replay.
    /// </summary>
    public sealed class DriftTrackingService : IDisposable {
        private sealed class TraceState {
            public double? RefRaHours;
            public double? RefDecDeg;
            public int NextFrameIndex;
        }

        private readonly IImageSaveMediator _imageSaveMediator;
        private readonly TraceState _trace = new();
        private bool _disposed;

        public ObservableDriftSamples Samples { get; } = new();

        public DriftTrackingService(IImageSaveMediator imageSaveMediator) {
            _imageSaveMediator = imageSaveMediator;
            _imageSaveMediator.ImageSaved += OnImageSaved;
        }

        public void ResetSession() {
            Application.Current?.Dispatcher.Invoke(() => {
                Samples.Clear();
                _trace.RefRaHours = null;
                _trace.RefDecDeg = null;
                _trace.NextFrameIndex = 0;
            });
        }

        /// <summary>
        /// Clears the trace and loads all qualifying FITS in the folder (non-recursive), sorted by DATE-OBS when present.
        /// Builds samples off the UI thread, then applies one collection reset so every frame appears on the plot.
        /// </summary>
        public void ImportFitsFolder(string folderPath) {
            if (_disposed || string.IsNullOrWhiteSpace(folderPath))
                return;
            if (!Directory.Exists(folderPath))
                return;

            ResetSession();

            var entries = FitsFolderImport.EnumerateSorted(folderPath);
            var importTrace = new TraceState();
            var built = new List<DriftSample>(entries.Count);
            var skippedParse = 0;

            foreach (var e in entries) {
                if (!FitsCoordinates.TryReadCoordinates(e.Path, out var raHours, out var decDeg, out var objectName, out _)) {
                    skippedParse++;
                    continue;
                }

                var label = !string.IsNullOrEmpty(objectName) ? objectName! : e.TargetLabel;

                AccumulateFromParsed(raHours, decDeg, e.ExposureUtc, e.Path, label, importTrace, out var sample);

                built.Add(sample);
            }

            Application.Current?.Dispatcher.Invoke(() => {
                CopyTrace(importTrace, _trace);
                Samples.ReplaceAll(built);
            });

            Logger.Info(
                $"SeeDrift: replay {built.Count}/{entries.Count} FITS from {folderPath} (skipped: no coords {skippedParse})");
        }

        private static void CopyTrace(TraceState from, TraceState to) {
            to.RefRaHours = from.RefRaHours;
            to.RefDecDeg = from.RefDecDeg;
            to.NextFrameIndex = from.NextFrameIndex;
        }

        private void OnImageSaved(object? sender, ImageSavedEventArgs e) {
            try {
                if (_disposed) return;
                var imgType = e.MetaData?.Image?.ImageType;
                if (!string.Equals(imgType, CaptureSequence.ImageTypes.LIGHT, StringComparison.OrdinalIgnoreCase))
                    return;

                var path = e.PathToImage?.LocalPath;
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    return;

                var targetFromSequence = e.MetaData?.Target?.Name?.Trim();
                var fallbackTarget = !string.IsNullOrEmpty(targetFromSequence)
                    ? targetFromSequence!
                    : "Unknown";

                TryAppendSampleFromFits(path, ToUtc(e.MetaData?.Image?.ExposureStart), fallbackTarget);
            } catch (Exception ex) {
                Logger.Error($"SeeDrift: error handling saved image: {ex.Message}");
            }
        }

        private void TryAppendSampleFromFits(string path, DateTime exposureUtc, string targetLabel) {
            try {
                if (!FitsCoordinates.TryReadCoordinates(path, out var raHours, out var decDeg, out var objectName, out _))
                    return;

                var label = !string.IsNullOrEmpty(objectName) ? objectName! : targetLabel;

                Application.Current?.Dispatcher.Invoke(() => {
                    AccumulateFromParsed(raHours, decDeg, exposureUtc, path, label, _trace, out var sample);
                    Samples.Add(sample);
                });
            } catch (Exception ex) {
                Logger.Error($"SeeDrift: append sample failed for {path}: {ex.Message}");
            }
        }

        /// <summary>Shared drift math for live capture and folder replay.</summary>
        private static void AccumulateFromParsed(
            double raHours,
            double decDeg,
            DateTime exposureUtc,
            string path,
            string label,
            TraceState st,
            out DriftSample sample) {

            if (st.RefRaHours == null || st.RefDecDeg == null) {
                st.RefRaHours = raHours;
                st.RefDecDeg = decDeg;
            }

            AstrometryMath.DeltaArcSec(st.RefRaHours.Value, st.RefDecDeg.Value, raHours, decDeg,
                out var dRa, out var dDec);

            var utc = exposureUtc.Kind == DateTimeKind.Utc ? exposureUtc : exposureUtc.ToUniversalTime();

            sample = new DriftSample {
                FrameIndex = st.NextFrameIndex++,
                ExposureStartUtc = utc,
                FileName = Path.GetFileName(path),
                TargetName = label,
                DeltaRaArcSec = dRa,
                DeltaDecArcSec = dDec,
                RawRaHours = raHours,
                RawDecDeg = decDeg
            };
        }

        private static DateTime ToUtc(DateTime? dt) {
            if (dt == null) return DateTime.UtcNow;
            var v = dt.Value;
            return v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime();
        }

        public void Dispose() {
            if (_disposed) return;
            _disposed = true;
            _imageSaveMediator.ImageSaved -= OnImageSaved;
        }
    }
}
