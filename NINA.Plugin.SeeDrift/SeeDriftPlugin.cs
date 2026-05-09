using System;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Application = System.Windows.Application;
using Microsoft.Win32;
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

        private string _testReportLogFilePath = "";

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public IProfileService ProfileService { get; }

        public SeeDriftSettings Settings { get; }
        public DriftTrackingService DriftTracker { get; }

        public ICommand ResetSessionCommand { get; }
        public ICommand RunTestReportCommand { get; }
        public ICommand BrowseTestReportLogCommand { get; }

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
            _testReportLogFilePath = Settings.TestReportLogFilePath ?? "";
            _isInitializing = false;
            RaisePropertyChanged(nameof(TestReportLogFilePath));

            ResetSessionCommand = new RelayCommand(_ => DriftTracker.ResetSession());
            RunTestReportCommand = new RelayCommand(_ => { _ = RunTestReportFireAsync(); });
            BrowseTestReportLogCommand = new RelayCommand(_ => BrowseTestReportLog());
        }

        /// <summary>Last-used NINA log path for Test report (persisted).</summary>
        public string TestReportLogFilePath {
            get => _testReportLogFilePath;
            set {
                if (value == _testReportLogFilePath) return;
                _testReportLogFilePath = value ?? "";
                RaisePropertyChanged();
                SyncSettingsFromProperties();
            }
        }

        private void BrowseTestReportLog() {
            var logsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NINA", "Logs");
            var dlg = new OpenFileDialog {
                Filter = "NINA log (*.log)|*.log|All files (*.*)|*.*",
                CheckFileExists = true
            };
            if (Directory.Exists(logsDir))
                dlg.InitialDirectory = logsDir;
            if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.FileName))
                TestReportLogFilePath = dlg.FileName;
        }

        private async Task RunTestReportFireAsync() {
            if (string.IsNullOrWhiteSpace(TestReportLogFilePath)) {
                MessageBox.Show(
                    "Choose a NINA log file first (Browse…) or paste the full path to a .log file.",
                    "SeeDrift — Test report",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            await Application.Current!.Dispatcher.InvokeAsync(BeginTestReportUi);
            try {
                var progress = new Progress<ApplicationStatus>(OnTestReportApplicationStatus);
                await DriftTracker.RunTestReportFromLogAsync(TestReportLogFilePath.Trim(), progress, CancellationToken.None)
                    .ConfigureAwait(false);
            } catch (Exception ex) {
                Logger.Error($"SeeDrift: Test report failed — {ex.Message}");
            } finally {
                await Application.Current!.Dispatcher.InvokeAsync(EndTestReportUi);
            }
        }

        private bool _testReportBusy;
        private string _testReportStatusText = "";
        private double _testReportProgressValue;
        private int _testReportProgressMaximum = 1;
        private bool _testReportIndeterminate;

        /// <summary>False while a test report run is in progress (disables Run).</summary>
        public bool TestReportNotBusy => !_testReportBusy;

        public string TestReportStatusText => _testReportStatusText;

        public double TestReportProgressValue => _testReportProgressValue;

        public int TestReportProgressMaximum => _testReportProgressMaximum;

        public bool TestReportIndeterminate => _testReportIndeterminate;

        /// <summary>Shows the progress row while a test report is running.</summary>
        public Visibility TestReportChromeVisibility =>
            _testReportBusy ? Visibility.Visible : Visibility.Collapsed;

        private void BeginTestReportUi() {
            _testReportBusy = true;
            _testReportStatusText = "Starting…";
            _testReportProgressValue = 0;
            _testReportProgressMaximum = 1;
            _testReportIndeterminate = true;
            RaisePropertyChanged(nameof(TestReportNotBusy));
            RaisePropertyChanged(nameof(TestReportStatusText));
            RaisePropertyChanged(nameof(TestReportProgressValue));
            RaisePropertyChanged(nameof(TestReportProgressMaximum));
            RaisePropertyChanged(nameof(TestReportIndeterminate));
            RaisePropertyChanged(nameof(TestReportChromeVisibility));
        }

        private void EndTestReportUi() {
            _testReportBusy = false;
            _testReportStatusText = "";
            _testReportIndeterminate = false;
            RaisePropertyChanged(nameof(TestReportNotBusy));
            RaisePropertyChanged(nameof(TestReportStatusText));
            RaisePropertyChanged(nameof(TestReportProgressValue));
            RaisePropertyChanged(nameof(TestReportProgressMaximum));
            RaisePropertyChanged(nameof(TestReportIndeterminate));
            RaisePropertyChanged(nameof(TestReportChromeVisibility));
        }

        private void OnTestReportApplicationStatus(ApplicationStatus status) {
            void Apply() {
                _testReportStatusText = status.Status ?? "";
                var max = status.MaxProgress;
                _testReportProgressMaximum = max > 0 ? max : 1;
                _testReportProgressValue = status.Progress;
                _testReportIndeterminate = _testReportBusy && max <= 0;
                RaisePropertyChanged(nameof(TestReportStatusText));
                RaisePropertyChanged(nameof(TestReportProgressValue));
                RaisePropertyChanged(nameof(TestReportProgressMaximum));
                RaisePropertyChanged(nameof(TestReportIndeterminate));
            }

            var app = Application.Current;
            if (app == null) {
                Apply();
                return;
            }
            if (app.Dispatcher.CheckAccess())
                Apply();
            else
                app.Dispatcher.Invoke(Apply);
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
                Settings.TestReportLogFilePath = _testReportLogFilePath;
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

        /// <summary>NINA image save root from the active profile (read-only).</summary>
        public string NINAImageDirectoryDisplay =>
            NinaPaths.TryGetDefaultImageDirectory(ProfileService) ?? "(not set or folder missing)";
    }
}
