using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using NINA.Core.Utility;
using NINA.Equipment.Model;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.Plugin.SeeDrift.Models;
using NINA.Plugin.SeeDrift.Utility;

namespace NINA.Plugin.SeeDrift.Services {

    /// <summary>
    /// Accumulates drift samples from saved LIGHT frames via <see cref="IImageSaveMediator.ImageSaved"/>.
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

        private void OnImageSaved(object? sender, ImageSavedEventArgs e) {
            try {
                if (_disposed) return;
                var imgType = e.MetaData?.Image?.ImageType;
                if (!string.Equals(imgType, CaptureSequence.ImageTypes.LIGHT, StringComparison.OrdinalIgnoreCase))
                    return;

                var path = e.PathToImage?.LocalPath;
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    return;

                if (!FitsCoordinates.TryReadCoordinates(path, out var raHours, out var decDeg, out var objectName, out var instrument))
                    return;

                if (_plugin.Settings.OnlySeestarCameras && !IsSeestarCamera(instrument))
                    return;

                var targetFromSequence = e.MetaData?.Target?.Name?.Trim();
                var targetLabel = !string.IsNullOrEmpty(targetFromSequence)
                    ? targetFromSequence!
                    : (!string.IsNullOrEmpty(objectName) ? objectName! : "Unknown");

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
                        ExposureStartUtc = ToUtc(e.MetaData?.Image?.ExposureStart),
                        FileName = Path.GetFileName(path),
                        TargetName = targetLabel,
                        DeltaRaArcSec = dRa,
                        DeltaDecArcSec = dDec,
                        RawRaHours = raHours,
                        RawDecDeg = decDeg
                    };

                    Samples.Add(sample);
                });
            } catch (Exception ex) {
                Logger.Error($"SeeDrift: error handling saved image: {ex.Message}");
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
