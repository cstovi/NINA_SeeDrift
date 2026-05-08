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
            public string? LastObjectName;
            public int NextFrameIndex;
        }

        private readonly SeeDriftPlugin _plugin;
        private readonly IImageSaveMediator _imageSaveMediator;
        private readonly TraceState _trace = new();
        private bool _disposed;

        public ObservableDriftSamples Samples { get; } = new();

        public DriftTrackingService(SeeDriftPlugin plugin, IImageSaveMediator imageSaveMediator) {
            _plugin = plugin;
            _imageSaveMediator = imageSaveMediator;
            _imageSaveMediator.ImageSaved += OnImageSaved;
        }

        public void ResetSession() {
            Application.Current?.Dispatcher.Invoke(() => {
                Samples.Clear();
                _trace.RefRaHours = null;
                _trace.RefDecDeg = null;
                _trace.LastObjectName = null;
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
            var skippedSeestar = 0;

            foreach (var e in entries) {
                if (!FitsCoordinates.TryReadCoordinates(e.Path, out var raHours, out var decDeg, out var objectName, out var instrument)) {
                    skippedParse++;
                    continue;
                }

                if (!PassesSeestarFilter(instrument, folderReplay: true)) {
                    skippedSeestar++;
                    continue;
                }

                var label = !string.IsNullOrEmpty(objectName) ? objectName! : e.TargetLabel;

                AccumulateFromParsed(raHours, decDeg, objectName, e.ExposureUtc, e.Path, label, importTrace,
                    suppressAutoReset: true,
                    out var sample, out _);

                built.Add(sample);
            }

            Application.Current?.Dispatcher.Invoke(() => {
                CopyTrace(importTrace, _trace);
                Samples.ReplaceAll(built);
            });

            Logger.Info(
                $"SeeDrift: replay {built.Count}/{entries.Count} FITS from {folderPath} (skipped: no coords {skippedParse}, Seestar filter {skippedSeestar})");
        }

        private static void CopyTrace(TraceState from, TraceState to) {
            to.RefRaHours = from.RefRaHours;
            to.RefDecDeg = from.RefDecDeg;
            to.LastObjectName = from.LastObjectName;
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
                if (!FitsCoordinates.TryReadCoordinates(path, out var raHours, out var decDeg, out var objectName, out var instrument))
                    return;

                if (!PassesSeestarFilter(instrument, folderReplay: false))
                    return;

                var label = !string.IsNullOrEmpty(objectName) ? objectName! : targetLabel;

                Application.Current?.Dispatcher.Invoke(() => {
                    AccumulateFromParsed(raHours, decDeg, objectName, exposureUtc, path, label, _trace,
                        suppressAutoReset: false,
                        out var sample, out var clearedTrace);

                    if (clearedTrace)
                        Samples.Clear();

                    Samples.Add(sample);
                });
            } catch (Exception ex) {
                Logger.Error($"SeeDrift: append sample failed for {path}: {ex.Message}");
            }
        }

        /// <summary>Shared drift math for live capture and folder replay.</summary>
        /// <param name="suppressAutoReset">Folder replay passes true so OBJECT header quirks do not clear the trace mid-folder.</param>
        private void AccumulateFromParsed(
            double raHours,
            double decDeg,
            string? objectName,
            DateTime exposureUtc,
            string path,
            string label,
            TraceState st,
            bool suppressAutoReset,
            out DriftSample sample,
            out bool clearedTrace) {

            clearedTrace = false;
            if (!suppressAutoReset && _plugin.Settings.AutoResetOnTargetChange && objectName != null && st.LastObjectName != null
                && !string.Equals(st.LastObjectName, objectName, StringComparison.OrdinalIgnoreCase)) {
                clearedTrace = true;
                st.RefRaHours = null;
                st.RefDecDeg = null;
                st.NextFrameIndex = 0;
            }

            if (objectName != null)
                st.LastObjectName = objectName;

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

        /// <summary>
        /// Live: connected Seestar camera passes; else FITS INSTRUME must contain Seestar when filter is on.
        /// Folder replay: same, but if INSTRUME is missing we still accept (no camera / stripped keyword — cannot infer non-Seestar).
        /// </summary>
        private bool PassesSeestarFilter(string? instrument, bool folderReplay) {
            if (!_plugin.Settings.OnlySeestarCameras)
                return true;

            try {
                var camName = _plugin.CameraMediator.GetInfo()?.Name ?? "";
                if (camName.IndexOf("Seestar", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            } catch { }

            if (folderReplay && string.IsNullOrWhiteSpace(instrument))
                return true;

            return !string.IsNullOrEmpty(instrument)
                && instrument.IndexOf("Seestar", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public void Dispose() {
            if (_disposed) return;
            _disposed = true;
            _imageSaveMediator.ImageSaved -= OnImageSaved;
        }
    }
}
