using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Application = System.Windows.Application;
using Microsoft.Win32;
using NINA.Core.Model;
using NINA.Core.Utility;
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
        private string _compareBeforeReportPath = "";
        private string _compareAfterReportPath = "";

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public IProfileService ProfileService { get; }

        public SeeDriftSettings Settings { get; }
        public DriftTrackingService DriftTracker { get; }

        /// <summary>Concurrency dropdown entries (1 … <see cref="CpuTopology.MaxPlateSolveParallelism"/>).</summary>
        public IReadOnlyList<int> PlateSolveParallelismOptions { get; } =
            Enumerable.Range(1, CpuTopology.MaxPlateSolveParallelism).ToList();

        public ICommand RunTestReportCommand { get; }
        public ICommand BrowseTestReportLogCommand { get; }
        public ICommand OpenNightReportCommand { get; }
        public ICommand BrowseCompareBeforeReportCommand { get; }
        public ICommand BrowseCompareAfterReportCommand { get; }
        public ICommand RunCompareReportsCommand { get; }
        public ICommand OpenCompareReportCommand { get; }

        [ImportingConstructor]
        public SeeDriftPlugin(
                IProfileService profileService,
                IPlateSolverFactory plateSolverFactory,
                IImageDataFactory imageDataFactory) {
            SeeDriftIconRegistration.Register();
            ProfileService = profileService;

            Settings = SeeDriftSettings.Load();
            DriftTracker = new DriftTrackingService(this, plateSolverFactory, imageDataFactory);

            _isInitializing = true;
            HtmlExportFolder = Settings.HtmlExportFolder;
            PlateSolveParallelism = NormalizePlateSolveParallelism(Settings.PlateSolveParallelism);
            MinExposuresPerTarget = NormalizeMinExposuresPerTarget(Settings.MinExposuresPerTarget);
            _testReportLogFilePath = Settings.TestReportLogFilePath ?? "";
            _compareBeforeReportPath = Settings.CompareBeforeReportPath ?? "";
            _compareAfterReportPath = Settings.CompareAfterReportPath ?? "";
            _discordWebhookUrl = Settings.DiscordWebhookUrl ?? "";
            _isInitializing = false;
            RaisePropertyChanged(nameof(TestReportLogFilePath));
            RaisePropertyChanged(nameof(CompareBeforeReportPath));
            RaisePropertyChanged(nameof(CompareAfterReportPath));
            RaisePropertyChanged(nameof(DiscordWebhookUrl));

            RunTestReportCommand = new RelayCommand(_ => { _ = RunTestReportFireAsync(); });
            BrowseTestReportLogCommand = new RelayCommand(_ => BrowseTestReportLog());
            OpenNightReportCommand = new RelayCommand(_ => OpenNightReport());
            BrowseCompareBeforeReportCommand = new RelayCommand(_ => BrowseCompareReport(before: true));
            BrowseCompareAfterReportCommand = new RelayCommand(_ => BrowseCompareReport(before: false));
            RunCompareReportsCommand = new RelayCommand(_ => RunCompareReports());
            OpenCompareReportCommand = new RelayCommand(_ => OpenCompareReport());
        }

        /// <summary>Last-used NINA log path for previous session report (persisted).</summary>
        public string TestReportLogFilePath {
            get => _testReportLogFilePath;
            set {
                if (value == _testReportLogFilePath) return;
                _testReportLogFilePath = value ?? "";
                RaisePropertyChanged();
                SyncSettingsFromProperties();
            }
        }

        public string CompareBeforeReportPath {
            get => _compareBeforeReportPath;
            set {
                var v = value ?? "";
                if (v == _compareBeforeReportPath) return;
                _compareBeforeReportPath = v;
                RaisePropertyChanged();
                SyncSettingsFromProperties();
            }
        }

        public string CompareAfterReportPath {
            get => _compareAfterReportPath;
            set {
                var v = value ?? "";
                if (v == _compareAfterReportPath) return;
                _compareAfterReportPath = v;
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

        private void BrowseCompareReport(bool before) {
            var dlg = new OpenFileDialog {
                Filter = "SeeDrift HTML (*.html)|*.html|All files (*.*)|*.*",
                CheckFileExists = true
            };
            var folder = ResolveHtmlExportFolder();
            if (Directory.Exists(folder))
                dlg.InitialDirectory = folder;
            if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.FileName))
                return;
            if (before)
                CompareBeforeReportPath = dlg.FileName;
            else
                CompareAfterReportPath = dlg.FileName;
        }

        private async Task RunTestReportFireAsync() {
            if (string.IsNullOrWhiteSpace(TestReportLogFilePath)) {
                MessageBox.Show(
                    "Choose a NINA log file first (Browse…) or paste the full path to a .log file.",
                    "SeeDrift — Previous session report",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            await Application.Current!.Dispatcher.InvokeAsync(BeginTestReportUi);
            await Task.Yield();
            SeeDriftLog.Info($"SeeDrift: Previous session report starting — {TestReportLogFilePath.Trim()}");
            var ok = false;
            try {
                var progress = new Progress<ApplicationStatus>(OnTestReportApplicationStatus);
                ok = await DriftTracker.RunTestReportFromLogAsync(TestReportLogFilePath.Trim(), progress, CancellationToken.None)
                    .ConfigureAwait(false);
            } catch (Exception ex) {
                SeeDriftLog.Error($"SeeDrift: Previous session report failed — {ex.Message}");
            } finally {
                await Application.Current!.Dispatcher.InvokeAsync(EndTestReportUi);
            }

            if (!ok) {
                MessageBox.Show(
                    "Previous session report did not add a batch to the night HTML.\n\n" +
                    "Common causes:\n" +
                    "• Fewer than two usable lights — NINA often saves .xisf (now accepted); logs must contain “Saved image to …” lines.\n" +
                    "• Files moved/deleted since the session.\n" +
                    "• Frames classified as calibration in FITS headers.\n" +
                    "• Plate solve failures.\n\n" +
                    "See %LocalAppData%\\NINA\\SeeDrift\\SeeDrift.log and NINA’s application log for lines starting with SeeDrift.",
                    "SeeDrift — Previous session report",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private bool _testReportBusy;
        private string _testReportStatusText = "";
        private string? _nightReportLinkPath;
        private string _compareReportStatusText = "";
        private string? _compareReportLinkPath;
        private double _testReportProgressValue;
        private int _testReportProgressMaximum = 1;
        private bool _testReportIndeterminate;

        /// <summary>False while a previous session report run is in progress (disables Run).</summary>
        public bool TestReportNotBusy => !_testReportBusy;

        public string TestReportStatusText => _testReportStatusText;

        public double TestReportProgressValue => _testReportProgressValue;

        public int TestReportProgressMaximum => _testReportProgressMaximum;

        public bool TestReportIndeterminate => _testReportIndeterminate;

        /// <summary>Shows the progress row while a previous session report runs, and keeps it visible after completion until the next run.</summary>
        public Visibility TestReportChromeVisibility =>
            _testReportBusy || !string.IsNullOrWhiteSpace(_testReportStatusText) || !string.IsNullOrWhiteSpace(_nightReportLinkPath)
                ? Visibility.Visible
                : Visibility.Collapsed;

        /// <summary>Status line with file path stripped when <see cref="NightReportLinkChromeVisibility"/> shows the open link (options UI only).</summary>
        public string TestReportStatusDisplayText => FormatStatusForPanel(_testReportStatusText, _nightReportLinkPath);

        /// <summary>Visible when the last completed run wrote a night HTML file path we can open.</summary>
        public Visibility NightReportLinkChromeVisibility =>
            string.IsNullOrWhiteSpace(_nightReportLinkPath) ? Visibility.Collapsed : Visibility.Visible;

        /// <summary>File name only (for the open link label).</summary>
        public string NightReportLinkFileName =>
            string.IsNullOrWhiteSpace(_nightReportLinkPath) ? "" : Path.GetFileName(_nightReportLinkPath.Trim());

        /// <summary>Full path for the open-link tooltip.</summary>
        public string NightReportLinkPathHint => _nightReportLinkPath ?? "";

        public string CompareReportStatusText => _compareReportStatusText;

        public Visibility CompareReportChromeVisibility =>
            string.IsNullOrWhiteSpace(_compareReportStatusText) && string.IsNullOrWhiteSpace(_compareReportLinkPath)
                ? Visibility.Collapsed
                : Visibility.Visible;

        public Visibility CompareReportLinkChromeVisibility =>
            string.IsNullOrWhiteSpace(_compareReportLinkPath) ? Visibility.Collapsed : Visibility.Visible;

        public string CompareReportLinkFileName =>
            string.IsNullOrWhiteSpace(_compareReportLinkPath) ? "" : Path.GetFileName(_compareReportLinkPath.Trim());

        public string CompareReportLinkPathHint => _compareReportLinkPath ?? "";

        internal void ClearNightReportLink() {
            void Apply() {
                if (_nightReportLinkPath == null) return;
                _nightReportLinkPath = null;
                RaisePropertyChanged(nameof(NightReportLinkChromeVisibility));
                RaisePropertyChanged(nameof(NightReportLinkFileName));
                RaisePropertyChanged(nameof(NightReportLinkPathHint));
                RaisePropertyChanged(nameof(TestReportStatusDisplayText));
                RaisePropertyChanged(nameof(TestReportChromeVisibility));
            }

            var app = Application.Current;
            if (app == null)
                Apply();
            else if (app.Dispatcher.CheckAccess())
                Apply();
            else
                app.Dispatcher.Invoke(Apply);
        }

        internal void NotifyNightReportSaved(string absolutePath, string completeStatusLine, bool postDiscordIfConfigured) {
            void Apply() {
                _nightReportLinkPath = absolutePath;
                _testReportStatusText = completeStatusLine;
                RaisePropertyChanged(nameof(NightReportLinkChromeVisibility));
                RaisePropertyChanged(nameof(NightReportLinkFileName));
                RaisePropertyChanged(nameof(NightReportLinkPathHint));
                RaisePropertyChanged(nameof(TestReportStatusText));
                RaisePropertyChanged(nameof(TestReportStatusDisplayText));
                RaisePropertyChanged(nameof(TestReportChromeVisibility));
            }

            var app = Application.Current;
            if (app == null)
                Apply();
            else if (app.Dispatcher.CheckAccess())
                Apply();
            else
                app.Dispatcher.Invoke(Apply);

            if (postDiscordIfConfigured)
                DiscordWebhookNotifier.EnqueueUpload(_discordWebhookUrl, absolutePath);
        }

        private static string FormatStatusForPanel(string? status, string? path) {
            if (string.IsNullOrEmpty(status))
                return "";
            if (string.IsNullOrEmpty(path))
                return status;
            var p = path.Trim();
            if (status.Length >= p.Length && status.EndsWith(p, StringComparison.OrdinalIgnoreCase))
                return status.AsSpan(0, status.Length - p.Length).TrimEnd().TrimEnd(':').ToString();
            return status;
        }

        private void OpenNightReport() {
            try {
                if (string.IsNullOrWhiteSpace(_nightReportLinkPath))
                    return;
                var p = _nightReportLinkPath.Trim();
                if (!File.Exists(p)) {
                    MessageBox.Show(
                        $"File no longer exists:\n{p}",
                        "SeeDrift",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                Process.Start(new ProcessStartInfo(p) { UseShellExecute = true });
            } catch (Exception ex) {
                MessageBox.Show(
                    $"Could not open file:\n{ex.Message}",
                    "SeeDrift",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void RunCompareReports() {
            try {
                if (ReportComparisonService.TryWriteComparison(
                        CompareBeforeReportPath,
                        CompareAfterReportPath,
                        ResolveHtmlExportFolder(),
                        out var outputPath,
                        out var error)) {
                    _compareReportLinkPath = outputPath;
                    _compareReportStatusText = $"Comparison saved: {outputPath}";
                } else {
                    _compareReportLinkPath = null;
                    _compareReportStatusText = $"Comparison not created — {error}";
                }
            } catch (Exception ex) {
                _compareReportLinkPath = null;
                _compareReportStatusText = $"Comparison failed — {ex.Message}";
            }

            RaisePropertyChanged(nameof(CompareReportStatusText));
            RaisePropertyChanged(nameof(CompareReportChromeVisibility));
            RaisePropertyChanged(nameof(CompareReportLinkChromeVisibility));
            RaisePropertyChanged(nameof(CompareReportLinkFileName));
            RaisePropertyChanged(nameof(CompareReportLinkPathHint));
        }

        private void OpenCompareReport() {
            try {
                if (string.IsNullOrWhiteSpace(_compareReportLinkPath))
                    return;
                var p = _compareReportLinkPath.Trim();
                if (!File.Exists(p)) {
                    MessageBox.Show(
                        $"File no longer exists:\n{p}",
                        "SeeDrift",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                Process.Start(new ProcessStartInfo(p) { UseShellExecute = true });
            } catch (Exception ex) {
                MessageBox.Show(
                    $"Could not open file:\n{ex.Message}",
                    "SeeDrift",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private string ResolveHtmlExportFolder() {
            if (!string.IsNullOrWhiteSpace(HtmlExportFolder))
                return HtmlExportFolder.Trim();
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return string.IsNullOrWhiteSpace(docs)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "SeeDrift")
                : Path.Combine(docs, "SeeDrift");
        }

        private void BeginTestReportUi() {
            _testReportBusy = true;
            _testReportStatusText = "Previous session report — starting (status updates appear here)…";
            _testReportProgressValue = 0;
            _testReportProgressMaximum = 1;
            _testReportIndeterminate = true;
            RaisePropertyChanged(nameof(TestReportNotBusy));
            RaisePropertyChanged(nameof(TestReportStatusText));
            RaisePropertyChanged(nameof(TestReportStatusDisplayText));
            RaisePropertyChanged(nameof(TestReportProgressValue));
            RaisePropertyChanged(nameof(TestReportProgressMaximum));
            RaisePropertyChanged(nameof(TestReportIndeterminate));
            RaisePropertyChanged(nameof(TestReportChromeVisibility));
        }

        private void EndTestReportUi() {
            _testReportBusy = false;
            _testReportIndeterminate = false;
            // Keep last status line (Complete — … or Stopped — …) so the panel does not go blank.
            RaisePropertyChanged(nameof(TestReportNotBusy));
            RaisePropertyChanged(nameof(TestReportStatusText));
            RaisePropertyChanged(nameof(TestReportStatusDisplayText));
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
                RaisePropertyChanged(nameof(TestReportStatusDisplayText));
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
                Settings.TestReportLogFilePath = _testReportLogFilePath;
                Settings.CompareBeforeReportPath = _compareBeforeReportPath;
                Settings.CompareAfterReportPath = _compareAfterReportPath;
                Settings.DiscordWebhookUrl = _discordWebhookUrl;
                Settings.PlateSolveParallelism = _plateSolveParallelism;
                Settings.MinExposuresPerTarget = _minExposuresPerTarget;
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

        private int _plateSolveParallelism =
            Math.Clamp(CpuTopology.PhysicalCoreCount, 1, CpuTopology.MaxPlateSolveParallelism);

        /// <summary>Concurrent plate solves during Stop or previous session report (clamped to 1 … <see cref="CpuTopology.MaxPlateSolveParallelism"/>). Fresh defaults match physical CPU cores (clamped to that ceiling).</summary>
        public int PlateSolveParallelism {
            get => _plateSolveParallelism;
            set {
                var v = NormalizePlateSolveParallelism(value);
                if (v == _plateSolveParallelism) return;
                _plateSolveParallelism = v;
                RaisePropertyChanged();
                SyncSettingsFromProperties();
            }
        }

        private static int NormalizePlateSolveParallelism(int value) {
            var max = CpuTopology.MaxPlateSolveParallelism;
            if (value < 1) return Math.Clamp(CpuTopology.PhysicalCoreCount, 1, max);
            if (value > max) return max;
            return value;
        }

        private string _discordWebhookUrl = "";

        /// <summary>Optional Discord webhook URL (Execute Webhook). Empty disables upload. URL contains a secret token — never logged.</summary>
        public string DiscordWebhookUrl {
            get => _discordWebhookUrl;
            set {
                var v = value ?? "";
                if (v == _discordWebhookUrl) return;
                _discordWebhookUrl = v;
                RaisePropertyChanged();
                SyncSettingsFromProperties();
            }
        }

        private int _minExposuresPerTarget = 50;

        /// <summary>
        /// Minimum solved frames per FITS target for that target to appear in the night HTML (1–500; default 50).
        /// </summary>
        public int MinExposuresPerTarget {
            get => _minExposuresPerTarget;
            set {
                var v = NormalizeMinExposuresPerTarget(value);
                if (v == _minExposuresPerTarget) return;
                _minExposuresPerTarget = v;
                RaisePropertyChanged();
                SyncSettingsFromProperties();
            }
        }

        private static int NormalizeMinExposuresPerTarget(int value) {
            if (value < 1) return 1;
            if (value > 500) return 500;
            return value;
        }
    }
}
