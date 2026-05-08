using System;
using System.Collections.ObjectModel;
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
        private readonly SeeDriftPlugin _plugin;
        private readonly IImageSaveMediator _imageSaveMediator;
        private bool _disposed;

        private double? _refRaHours;
        private double? _refDecDeg;
        private string? _lastObjectName;
        private int _nextFrameIndex;

        public ObservableCollection<DriftSample> Samples { get; } = new();

        public DriftTrackingService(SeeDriftPlugin plugin, IImageSaveMediator imageSaveMediator) {
            _plugin = plugin;
            _imageSaveMediator = imageSaveMediator;
            _imageSaveMediator.ImageSaved += OnImageSaved;
        }

        public void ResetSession() {
            Application.Current?.Dispatcher.Invoke(() => {
                Samples.Clear();
                _refRaHours = null;
                _refDecDeg = null;
                _lastObjectName = null;
                _nextFrameIndex = 0;
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

            var entries = FitsFolderImport.EnumerateSorted(folderPath);
            foreach (var e in entries)
                TryAppendSampleFromFits(e.Path, e.ExposureUtc, e.TargetLabel);

            Logger.Info($"SeeDrift: replay loaded {entries.Count} FITS files from {folderPath}");
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

        /// <summary>
        /// Parses RA/Dec from FITS, applies Seestar filter, updates reference frame and samples (must run from any thread — marshals to UI).
        /// </summary>
        private void TryAppendSampleFromFits(string path, DateTime exposureUtc, string targetLabel) {
            try {
                if (!FitsCoordinates.TryReadCoordinates(path, out var raHours, out var decDeg, out var objectName, out var instrument))
                    return;

                if (_plugin.Settings.OnlySeestarCameras && !IsSeestarCamera(instrument))
                    return;

                var label = !string.IsNullOrEmpty(objectName) ? objectName! : targetLabel;

                Application.Current?.Dispatcher.Invoke(() => {
                    if (_plugin.Settings.AutoResetOnTargetChange && objectName != null && _lastObjectName != null
                        && !string.Equals(_lastObjectName, objectName, StringComparison.OrdinalIgnoreCase)) {
                        Samples.Clear();
                        _refRaHours = null;
                        _refDecDeg = null;
                        _nextFrameIndex = 0;
                    }

                    if (objectName != null)
                        _lastObjectName = objectName;

                    if (_refRaHours == null || _refDecDeg == null) {
                        _refRaHours = raHours;
                        _refDecDeg = decDeg;
                    }

                    AstrometryMath.DeltaArcSec(_refRaHours.Value, _refDecDeg.Value, raHours, decDeg,
                        out var dRa, out var dDec);

                    var sample = new DriftSample {
                        FrameIndex = _nextFrameIndex++,
                        ExposureStartUtc = exposureUtc.Kind == DateTimeKind.Utc ? exposureUtc : exposureUtc.ToUniversalTime(),
                        FileName = Path.GetFileName(path),
                        TargetName = label,
                        DeltaRaArcSec = dRa,
                        DeltaDecArcSec = dDec,
                        RawRaHours = raHours,
                        RawDecDeg = decDeg
                    };

                    Samples.Add(sample);
                });
            } catch (Exception ex) {
                Logger.Error($"SeeDrift: append sample failed for {path}: {ex.Message}");
            }
        }

        private static DateTime ToUtc(DateTime? dt) {
            if (dt == null) return DateTime.UtcNow;
            var v = dt.Value;
            return v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime();
        }

        private bool IsSeestarCamera(string? instrument) {
            try {
                var camName = _plugin.CameraMediator.GetInfo()?.Name ?? "";
                if (camName.IndexOf("Seestar", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            } catch { }

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
