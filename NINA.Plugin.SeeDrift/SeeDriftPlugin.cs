using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Application = System.Windows.Application;
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
        private static readonly int[] UtcHourItems = Enumerable.Range(0, 24).ToArray();
        private static readonly int[] UtcMinuteItems = Enumerable.Range(0, 60).ToArray();

        private bool _isInitializing;
        private bool _isSyncing;

        private DateTime _testStartDateUtc;
        private int _testStartHourUtc;
        private int _testStartMinuteUtc;
        private DateTime _testEndDateUtc;
        private int _testEndHourUtc;
        private int _testEndMinuteUtc;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public IProfileService ProfileService { get; }

        public SeeDriftSettings Settings { get; }
        public DriftTrackingService DriftTracker { get; }

        public ICommand ResetSessionCommand { get; }
        public ICommand RunTestReportCommand { get; }

        /// <summary>ComboBox items for UTC hour (0–23).</summary>
        public IReadOnlyList<int> UtcHours => UtcHourItems;

        /// <summary>ComboBox items for UTC minute (0–59).</summary>
        public IReadOnlyList<int> UtcMinutes => UtcMinuteItems;

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
            ApplyTestWindowFromSettingsStrings();
            _isInitializing = false;
            if (string.IsNullOrWhiteSpace(Settings.TestObservationStartUtcIso) ||
                    string.IsNullOrWhiteSpace(Settings.TestObservationEndUtcIso))
                SyncSettingsFromProperties();

            ResetSessionCommand = new RelayCommand(_ => DriftTracker.ResetSession());
            RunTestReportCommand = new RelayCommand(_ => { _ = RunTestReportFireAsync(); });
        }

        /// <summary>UTC calendar date for test window start; time uses <see cref="TestObservationStartHourUtc"/> / Minute.</summary>
        public DateTime TestObservationStartDateUtc {
            get => _testStartDateUtc;
            set {
                var d = new DateTime(value.Year, value.Month, value.Day, 0, 0, 0, DateTimeKind.Utc);
                if (d == _testStartDateUtc) return;
                _testStartDateUtc = d;
                RaisePropertyChanged();
                SyncSettingsFromProperties();
            }
        }

        public int TestObservationStartHourUtc {
            get => _testStartHourUtc;
            set {
                var h = Math.Clamp(value, 0, 23);
                if (h == _testStartHourUtc) return;
                _testStartHourUtc = h;
                RaisePropertyChanged();
                SyncSettingsFromProperties();
            }
        }

        public int TestObservationStartMinuteUtc {
            get => _testStartMinuteUtc;
            set {
                var m = Math.Clamp(value, 0, 59);
                if (m == _testStartMinuteUtc) return;
                _testStartMinuteUtc = m;
                RaisePropertyChanged();
                SyncSettingsFromProperties();
            }
        }

        /// <summary>UTC calendar date for test window end.</summary>
        public DateTime TestObservationEndDateUtc {
            get => _testEndDateUtc;
            set {
                var d = new DateTime(value.Year, value.Month, value.Day, 0, 0, 0, DateTimeKind.Utc);
                if (d == _testEndDateUtc) return;
                _testEndDateUtc = d;
                RaisePropertyChanged();
                SyncSettingsFromProperties();
            }
        }

        public int TestObservationEndHourUtc {
            get => _testEndHourUtc;
            set {
                var h = Math.Clamp(value, 0, 23);
                if (h == _testEndHourUtc) return;
                _testEndHourUtc = h;
                RaisePropertyChanged();
                SyncSettingsFromProperties();
            }
        }

        public int TestObservationEndMinuteUtc {
            get => _testEndMinuteUtc;
            set {
                var m = Math.Clamp(value, 0, 59);
                if (m == _testEndMinuteUtc) return;
                _testEndMinuteUtc = m;
                RaisePropertyChanged();
                SyncSettingsFromProperties();
            }
        }

        private void ApplyTestWindowFromSettingsStrings() {
            var today = DateTime.UtcNow.Date;

            if (!string.IsNullOrWhiteSpace(Settings.TestObservationStartUtcIso) &&
                    TryParseUtcIso(Settings.TestObservationStartUtcIso, out var s)) {
                _testStartDateUtc = new DateTime(s.Year, s.Month, s.Day, 0, 0, 0, DateTimeKind.Utc);
                _testStartHourUtc = s.Hour;
                _testStartMinuteUtc = s.Minute;
            } else {
                _testStartDateUtc = today;
                _testStartHourUtc = 0;
                _testStartMinuteUtc = 0;
            }

            if (!string.IsNullOrWhiteSpace(Settings.TestObservationEndUtcIso) &&
                    TryParseUtcIso(Settings.TestObservationEndUtcIso, out var e)) {
                _testEndDateUtc = new DateTime(e.Year, e.Month, e.Day, 0, 0, 0, DateTimeKind.Utc);
                _testEndHourUtc = e.Hour;
                _testEndMinuteUtc = e.Minute;
            } else {
                _testEndDateUtc = today;
                _testEndHourUtc = 23;
                _testEndMinuteUtc = 59;
            }

            RaisePropertyChanged(nameof(TestObservationStartDateUtc));
            RaisePropertyChanged(nameof(TestObservationStartHourUtc));
            RaisePropertyChanged(nameof(TestObservationStartMinuteUtc));
            RaisePropertyChanged(nameof(TestObservationEndDateUtc));
            RaisePropertyChanged(nameof(TestObservationEndHourUtc));
            RaisePropertyChanged(nameof(TestObservationEndMinuteUtc));
        }

        private async Task RunTestReportFireAsync() {
            await Application.Current!.Dispatcher.InvokeAsync(BeginTestReportUi);
            try {
                var startUtc = CombineUtc(_testStartDateUtc, _testStartHourUtc, _testStartMinuteUtc);
                var endUtc = CombineUtc(_testEndDateUtc, _testEndHourUtc, _testEndMinuteUtc);
                var progress = new Progress<ApplicationStatus>(OnTestReportApplicationStatus);
                await DriftTracker.RunTestReportAsync(startUtc, endUtc, progress, CancellationToken.None)
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

        private static DateTime CombineUtc(DateTime dateUtc, int hour, int minute) =>
            new DateTime(dateUtc.Year, dateUtc.Month, dateUtc.Day, hour, minute, 0, DateTimeKind.Utc);

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

        private static string FormatUtcIso(DateTime dateUtc, int hour, int minute) {
            var dt = CombineUtc(dateUtc, hour, minute);
            return dt.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
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
                Settings.TestObservationStartUtcIso = FormatUtcIso(_testStartDateUtc, _testStartHourUtc, _testStartMinuteUtc);
                Settings.TestObservationEndUtcIso = FormatUtcIso(_testEndDateUtc, _testEndHourUtc, _testEndMinuteUtc);
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
