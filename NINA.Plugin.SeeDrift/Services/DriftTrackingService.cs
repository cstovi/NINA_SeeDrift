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

        private readonly SeeDriftPlugin _plugin;
        private readonly IImageSaveMediator _imageSaveMediator;
        private readonly TraceState _trace = new();
        private bool _disposed;

        public ObservableDriftSamples Samples { get; } = new();

        /// <summary>Plate scale derived from XPIXSZ+FOCALLEN of the first imported FITS, or null.</summary>
        public double? PlateScaleArcSecPerPx { get; private set; }

        /// <summary>Number of jump frames detected in the most recent import.</summary>
        public int JumpCount { get; private set; }

        /// <summary>Number of jumps that were matched to a NINA log event.</summary>
        public int LogCorrelatedCount { get; private set; }

        /// <summary>True when a NINA log file was found for the session date.</summary>
        public bool LogWasFound { get; private set; }

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
                _trace.NextFrameIndex = 0;
                PlateScaleArcSecPerPx = null;
                JumpCount = 0;
                LogCorrelatedCount = 0;
                LogWasFound = false;
            });
        }

        /// <summary>
        /// Clears the trace and loads all qualifying FITS in the folder (non-recursive), sorted by DATE-OBS when present.
        /// </summary>
        public void ImportFitsFolder(string folderPath) {
            if (_disposed || string.IsNullOrWhiteSpace(folderPath))
                return;
            if (!Directory.Exists(folderPath))
                return;

            ResetSession();

            if (_plugin.Settings.FolderImportPlotMode == FolderPlotMode.PixelRegistration) {
                ImportFitsFolderPixelRegistration(folderPath);
                return;
            }

            ImportFitsFolderHeaderCoordinates(folderPath);
        }

        private void ImportFitsFolderHeaderCoordinates(string folderPath) {
            var entries = FitsFolderImport.EnumerateSorted(folderPath);
            var importTrace = new TraceState();
            var built = new List<DriftSample>(entries.Count);
            var skippedParse = 0;
            double? plateScale = null;

            foreach (var e in entries) {
                if (!FitsCoordinates.TryReadPrimaryHeader(e.Path, out var cards)) {
                    skippedParse++;
                    continue;
                }
                if (!FitsCoordinates.TryParsePointing(cards, out var raHours, out var decDeg, out var objectName, out _)) {
                    skippedParse++;
                    continue;
                }

                if (plateScale == null && FitsCoordinates.TryReadPlateScale(cards, out var ps))
                    plateScale = ps;

                var label = !string.IsNullOrEmpty(objectName) ? objectName! : e.TargetLabel;
                AccumulateFromParsed(raHours, decDeg, e.ExposureUtc, e.Path, label, importTrace, null, null, out var sample);
                built.Add(sample);
            }

            JumpDetector.AnnotateJumps(built);
            var (logMatched, logFound) = NinaLogCorrelator.AnnotateWithLogEvents(built);
            var jumps = JumpDetector.CountJumps(built);

            Application.Current?.Dispatcher.Invoke(() => {
                CopyTrace(importTrace, _trace);
                PlateScaleArcSecPerPx = plateScale;
                JumpCount = jumps;
                LogCorrelatedCount = logMatched;
                LogWasFound = logFound;
                Samples.ReplaceAll(built);
            });

            Logger.Info(
                $"SeeDrift: replay {built.Count}/{entries.Count} FITS from {folderPath} " +
                $"(skipped: no coords {skippedParse}) jumps={jumps} logFound={logFound} logMatched={logMatched}");
        }

        private void ImportFitsFolderPixelRegistration(string folderPath) {
            var entries = FitsFolderImport.EnumerateSorted(folderPath);
            var cropSize = Math.Clamp(_plugin.Settings.RegistrationCropSize, 64, 4096);
            var importTrace = new TraceState();
            var built = new List<DriftSample>(entries.Count);
            var skippedParse = 0;
            var skippedImage = 0;
            double? plateScale = null;

            if (entries.Count == 0) {
                Logger.Warning($"SeeDrift: no FITS in {folderPath}");
                return;
            }

            // Frame-to-frame cumulative: compare each frame against the previous one.
            // Sub-pixel parabolic refinement in the correlator means each step is a
            // float (e.g. 0.43 px) rather than an integer, so the cumulative sum
            // produces distinct positions for every frame even with slow drift.
            double[,]? prevCrop = null;
            double cumX = 0;
            double cumY = 0;

            for (var i = 0; i < entries.Count; i++) {
                var e = entries[i];
                if (!FitsCoordinates.TryReadPrimaryHeader(e.Path, out var cards)) {
                    skippedParse++;
                    continue;
                }
                if (!FitsCoordinates.TryParsePointing(cards, out var raHours, out var decDeg, out var objectName, out _)) {
                    skippedParse++;
                    continue;
                }

                // Read plate scale from the first FITS that has the keywords.
                if (plateScale == null && FitsCoordinates.TryReadPlateScale(cards, out var ps))
                    plateScale = ps;

                if (!FitsImageCrop.TryLoadCentralCrop(e.Path, cropSize, out var crop) || crop == null) {
                    skippedImage++;
                    continue;
                }

                var label = !string.IsNullOrEmpty(objectName) ? objectName! : e.TargetLabel;

                if (prevCrop == null) {
                    // Frame 1 is the origin.
                    prevCrop = crop;
                    AccumulateFromParsed(raHours, decDeg, e.ExposureUtc, e.Path, label, importTrace, 0.0, 0.0, out var s0);
                    built.Add(s0);
                    continue;
                }

                double dx = 0;
                double dy = 0;
                if (!PhaseCorrelation.TryEstimateShift(prevCrop, crop, out dy, out dx))
                    skippedImage++;

                cumX += dx;
                cumY += dy;

                AccumulateFromParsed(raHours, decDeg, e.ExposureUtc, e.Path, label, importTrace, cumX, cumY, out var sample);
                built.Add(sample);

                prevCrop = crop;
            }

            JumpDetector.AnnotateJumps(built);
            var (logMatched2, logFound2) = NinaLogCorrelator.AnnotateWithLogEvents(built);
            var jumps = JumpDetector.CountJumps(built);

            Application.Current?.Dispatcher.Invoke(() => {
                CopyTrace(importTrace, _trace);
                PlateScaleArcSecPerPx = plateScale;
                JumpCount = jumps;
                LogCorrelatedCount = logMatched2;
                LogWasFound = logFound2;
                Samples.ReplaceAll(built);
            });

            Logger.Info(
                $"SeeDrift: pixel replay {built.Count}/{entries.Count} FITS from {folderPath} " +
                $"(crop {cropSize}px, frame-to-frame + subpixel, skipped: no coords {skippedParse}, image {skippedImage}, jumps {jumps} logFound={logFound2} logMatched={logMatched2})");
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
                    AccumulateFromParsed(raHours, decDeg, exposureUtc, path, label, _trace, null, null, out var sample);
                    Samples.Add(sample);
                });
            } catch (Exception ex) {
                Logger.Error($"SeeDrift: append sample failed for {path}: {ex.Message}");
            }
        }

        private static void AccumulateFromParsed(
            double raHours,
            double decDeg,
            DateTime exposureUtc,
            string path,
            string label,
            TraceState st,
            double? cumulativePixelX,
            double? cumulativePixelY,
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
                RawDecDeg = decDeg,
                CumulativePixelX = cumulativePixelX,
                CumulativePixelY = cumulativePixelY
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
