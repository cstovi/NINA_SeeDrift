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
        private static readonly int[] ClockHourItems = Enumerable.Range(0, 24).ToArray();
        private static readonly int[] ClockMinuteItems = Enumerable.Range(0, 60).ToArray();

        private bool _isInitializing;
        private bool _isSyncing;

        /// <summary>Calendar date only (midnight, <see cref="DateTimeKind.Unspecified"/>).</summary>
        private DateTime _testStartDate;
        private int _testStartHour;
        private int _testStartMinute;
        private DateTime _testEndDate;
        private int _testEndHour;
        private int _testEndMinute;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public IProfileService ProfileService { get; }

        public SeeDriftSettings Settings { get; }
        public DriftTrackingService DriftTracker { get; }

        public ICommand ResetSessionCommand { get; }
        public ICommand RunTestReportCommand { get; }

        /// <summary>ComboBox items for hour (0–23), local wall clock.</summary>
        public IReadOnlyList<int> ObservationClockHours => ClockHourItems;

        /// <summary>ComboBox items for minute (0–59), local wall clock.</summary>
        public IReadOnlyList<int> ObservationClockMinutes => ClockMinuteItems;

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

        /// <summary>Local calendar date for test window start; time uses <see cref="TestObservationStartHour"/> / <see cref="TestObservationStartMinute"/>.</summary>
        public DateTime TestObservationStartDate {
            get => _testStartDate;
            set {
                var d = new DateTime(value.Year, value.Month, value.Day, 0, 0, 0, DateTimeKind.Unspecified);
                if (d == _testStartDate) return;
                _testStartDate = d;
                RaisePropertyChanged();
                SyncSettingsFromProperties();
            }
        }

        public int TestObservationStartHour {
            get => _testStartHour;
            set {
                var h = Math.Clamp(value, 0, 23);
                if (h == _testStartHour) return;
                _testStartHour = h;
                RaisePropertyChanged();
                SyncSettingsFromProperties();
            }
        }

        public int TestObservationStartMinute {
            get => _testStartMinute;
            set {
                var m = Math.Clamp(value, 0, 59);
                if (m == _testStartMinute) return;
                _testStartMinute = m;
                RaisePropertyChanged();
                SyncSettingsFromProperties();
            }
        }

        /// <summary>Local calendar date for test window end.</summary>
        public DateTime TestObservationEndDate {
            get => _testEndDate;
            set {
                var d = new DateTime(value.Year, value.Month, value.Day, 0, 0, 0, DateTimeKind.Unspecified);
                if (d == _testEndDate) return;
                _testEndDate = d;
                RaisePropertyChanged();
                SyncSettingsFromProperties();
            }
        }

        public int TestObservationEndHour {
            get => _testEndHour;
            set {
                var h = Math.Clamp(value, 0, 23);
                if (h == _testEndHour) return;
                _testEndHour = h;
                RaisePropertyChanged();
                SyncSettingsFromProperties();
            }
        }

        public int TestObservationEndMinute {
            get => _testEndMinute;
            set {
                var m = Math.Clamp(value, 0, 59);
                if (m == _testEndMinute) return;
                _testEndMinute = m;
                RaisePropertyChanged();
                SyncSettingsFromProperties();
            }
        }

        private void ApplyTestWindowFromSettingsStrings() {
            static DateTime TodayDateOnly() {
                var t = DateTime.Today;
                return new DateTime(t.Year, t.Month, t.Day, 0, 0, 0, DateTimeKind.Unspecified);
            }

            if (!string.IsNullOrWhiteSpace(Settings.TestObservationStartUtcIso) &&
                    TryParseUtcIso(Settings.TestObservationStartUtcIso, out var startUtc)) {
                var local = startUtc.ToLocalTime();
                _testStartDate = new DateTime(local.Year, local.Month, local.Day, 0, 0, 0, DateTimeKind.Unspecified);
                _testStartHour = local.Hour;
                _testStartMinute = local.Minute;
            } else {
                _testStartDate = TodayDateOnly();
                _testStartHour = 0;
                _testStartMinute = 0;
            }

            if (!string.IsNullOrWhiteSpace(Settings.TestObservationEndUtcIso) &&
                    TryParseUtcIso(Settings.TestObservationEndUtcIso, out var endUtc)) {
                var local = endUtc.ToLocalTime();
                _testEndDate = new DateTime(local.Year, local.Month, local.Day, 0, 0, 0, DateTimeKind.Unspecified);
                _testEndHour = local.Hour;
                _testEndMinute = local.Minute;
            } else {
                _testEndDate = TodayDateOnly();
                _testEndHour = 23;
                _testEndMinute = 59;
            }

            RaisePropertyChanged(nameof(TestObservationStartDate));
            RaisePropertyChanged(nameof(TestObservationStartHour));
            RaisePropertyChanged(nameof(TestObservationStartMinute));
            RaisePropertyChanged(nameof(TestObservationEndDate));
            RaisePropertyChanged(nameof(TestObservationEndHour));
            RaisePropertyChanged(nameof(TestObservationEndMinute));
        }

        private async Task RunTestReportFireAsync() {
            await Application.Current!.Dispatcher.InvokeAsync(BeginTestReportUi);
            try {
                var startUtc = LocalWallClockToUtc(_testStartDate, _testStartHour, _testStartMinute);
                var endUtc = LocalWallClockToUtc(_testEndDate, _testEndHour, _testEndMinute);
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

        /// <summary>Interprets date + time as this PC’s local zone and returns UTC (for FITS window filtering).</summary>
        private static DateTime LocalWallClockToUtc(DateTime dateOnlyUnspecified, int hour, int minute) {
            var local = new DateTime(
                dateOnlyUnspecified.Year, dateOnlyUnspecified.Month, dateOnlyUnspecified.Day,
                hour, minute, 0, DateTimeKind.Local);
            return local.ToUniversalTime();
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

        /// <summary>Persists the chosen local instant as UTC ISO Z (settings remain compatible with earlier builds).</summary>
        private static string FormatUtcIso(DateTime dateOnlyUnspecified, int hour, int minute) {
            var utc = LocalWallClockToUtc(dateOnlyUnspecified, hour, minute);
            return utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
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
                Settings.TestObservationStartUtcIso = FormatUtcIso(_testStartDate, _testStartHour, _testStartMinute);
                Settings.TestObservationEndUtcIso = FormatUtcIso(_testEndDate, _testEndHour, _testEndMinute);
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
