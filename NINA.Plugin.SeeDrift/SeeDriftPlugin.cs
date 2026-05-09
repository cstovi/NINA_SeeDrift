using System;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using NINA.Core.Model;
using NINA.Core.Utility;
using static NINA.Core.Utility.Logger;
using NINA.Image.Interfaces;
using NINA.Plugin;
using NINA.Plugin.Interfaces;
using NINA.Plugin.SeeDrift.Services;
using NINA.Plugin.SeeDrift.Utility;
using NINA.PlateSolving.Interfaces;
using NINA.Profile.Interfaces;

namespace NINA.Plugin.SeeDrift {

    [Export(typeof(IPluginManifest))]
    [Export]
    public class SeeDriftPlugin : PluginBase, IPluginManifest, INotifyPropertyChanged {
        private bool _isInitializing;
        private bool _isSyncing;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public IProfileService ProfileService { get; }

        public SeeDriftSettings Settings { get; }
        public DriftTrackingService DriftTracker { get; }

        public ICommand ResetSessionCommand { get; }
        public ICommand RunTestReportCommand { get; }

        [ImportingConstructor]
        public SeeDriftPlugin(
                IProfileService profileService,
                IPlateSolverFactory plateSolverFactory,
                IImageDataFactory imageDataFactory) {
            ProfileService = profileService;

            Settings = SeeDriftSettings.Load();
            DriftTracker = new DriftTrackingService(this, plateSolverFactory, imageDataFactory);

            _isInitializing = true;
            HtmlExportFolder = Settings.HtmlExportFolder;
            TempWorkingFolder = Settings.TempWorkingFolder;
            TestObservationStartUtcIso = Settings.TestObservationStartUtcIso;
            TestObservationEndUtcIso = Settings.TestObservationEndUtcIso;
            _isInitializing = false;

            ResetSessionCommand = new RelayCommand(_ => DriftTracker.ResetSession());
            RunTestReportCommand = new RelayCommand(_ => { _ = RunTestReportFireAsync(); });
        }

        private async Task RunTestReportFireAsync() {
            try {
                if (!TryParseUtcIso(TestObservationStartUtcIso, out var startUtc)) {
                    Logger.Error("SeeDrift: Test observation start time is not a valid UTC ISO 8601 string.");
                    return;
                }
                if (!TryParseUtcIso(TestObservationEndUtcIso, out var endUtc)) {
                    Logger.Error("SeeDrift: Test observation end time is not a valid UTC ISO 8601 string.");
                    return;
                }
                await DriftTracker.RunTestReportAsync(startUtc, endUtc,
                        (IProgress<ApplicationStatus>?)null, CancellationToken.None)
                    .ConfigureAwait(false);
            } catch (Exception ex) {
                Logger.Error($"SeeDrift: Test report failed — {ex.Message}");
            }
        }

        private static bool TryParseUtcIso(string? text, out DateTime utc) {
            utc = default;
            if (string.IsNullOrWhiteSpace(text))
                return false;
            text = text.Trim();
            if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto)) {
                utc = dto.UtcDateTime;
                return true;
            }
            return false;
        }

        public override Task Teardown() {
            DriftTracker.Dispose();
            return base.Teardown();
        }

        public void SyncSettingsFromProperties() {
            if (_isInitializing || _isSyncing) return;
            _isSyncing = true;
            try {
                Settings.HtmlExportFolder = _htmlExportFolder;
                Settings.TempWorkingFolder = _tempWorkingFolder;
                Settings.TestObservationStartUtcIso = _testObservationStartUtcIso;
                Settings.TestObservationEndUtcIso = _testObservationEndUtcIso;
                Settings.Save();
            } finally {
                _isSyncing = false;
            }
        }

        private string _htmlExportFolder = "";
        public string HtmlExportFolder {
            get => _htmlExportFolder;
            set { _htmlExportFolder = value; RaisePropertyChanged(); SyncSettingsFromProperties(); }
        }

        private string _tempWorkingFolder = "";
        public string TempWorkingFolder {
            get => _tempWorkingFolder;
            set { _tempWorkingFolder = value; RaisePropertyChanged(); SyncSettingsFromProperties(); }
        }

        private string _testObservationStartUtcIso = "";
        public string TestObservationStartUtcIso {
            get => _testObservationStartUtcIso;
            set { _testObservationStartUtcIso = value; RaisePropertyChanged(); SyncSettingsFromProperties(); }
        }

        private string _testObservationEndUtcIso = "";
        public string TestObservationEndUtcIso {
            get => _testObservationEndUtcIso;
            set { _testObservationEndUtcIso = value; RaisePropertyChanged(); SyncSettingsFromProperties(); }
        }

        /// <summary>NINA image save root from the active profile (read-only).</summary>
        public string NINAImageDirectoryDisplay =>
            NinaPaths.TryGetDefaultImageDirectory(ProfileService) ?? "(not set or folder missing)";
    }
}
