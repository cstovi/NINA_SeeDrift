using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
using System.Windows.Threading;
using Application = System.Windows.Application;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Image.Interfaces;
using NINA.Plugin;
using NINA.Plugin.Interfaces;
using NINA.Plugin.SeeDrift.Models;
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
        public ICommand RefreshRecentLogSessionsCommand { get; }
        public ICommand OpenNightReportCommand { get; }
        public ICommand RunCompareReportsCommand { get; }
        public ICommand OpenCompareReportCommand { get; }
        public ICommand RefreshSavedReportsCommand { get; }
        public ICommand DeleteSelectedSavedReportCommand { get; }

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
            PlateSolveParallelism = NormalizePlateSolveParallelism(Settings.PlateSolveParallelism);
            MinExposuresPerTarget = NormalizeMinExposuresPerTarget(Settings.MinExposuresPerTarget);
            _testReportLogFilePath = Settings.TestReportLogFilePath ?? "";
            _compareBeforeReportPath = Settings.CompareBeforeReportPath ?? "";
            _compareAfterReportPath = Settings.CompareAfterReportPath ?? "";
            _discordWebhookUrl = Settings.DiscordWebhookUrl ?? "";
            _isInitializing = false;
            RaisePropertyChanged(nameof(TestReportLogFilePath));
            RaisePropertyChanged(nameof(DiscordWebhookUrl));

            RunTestReportCommand = new RelayCommand(_ => { _ = RunTestReportFireAsync(); });
            RefreshRecentLogSessionsCommand = new RelayCommand(_ => { _ = RefreshRecentLogSessionsAsync(); });
            OpenNightReportCommand = new RelayCommand(_ => OpenNightReport());
            RunCompareReportsCommand = new RelayCommand(_ => RunCompareReports());
            OpenCompareReportCommand = new RelayCommand(_ => OpenCompareReport());
            RefreshSavedReportsCommand = new RelayCommand(_ => RefreshSavedReports());
            DeleteSelectedSavedReportCommand = new RelayCommand(_ => DeleteSelectedSavedReport());

            _ = RefreshRecentLogSessionsAsync();
            RefreshSavedReports();
            ScheduleRefreshResolvedReportForSelectedLog();
        }

        /// <summary>Last-used NINA log path for Run report (persisted).</summary>
        public string TestReportLogFilePath {
            get => _testReportLogFilePath;
            set {
                if (value == _testReportLogFilePath) return;
                _testReportLogFilePath = value ?? "";
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(SelectedRecentLogSession));
                RaisePropertyChanged(nameof(SelectedRecentLogSessionDetailText));
                RaisePropertyChanged(nameof(SelectedRecentLogSessionDetailVisibility));
                SyncSettingsFromProperties();
                ScheduleRefreshResolvedReportForSelectedLog();
                var newNorm = NormalizeLogPath(value);
                var matchesCompletion = newNorm != null
                    && (_nightReportCompletionSourceLogsNormalized?.Contains(newNorm) ?? false);
                if (!matchesCompletion && !_testReportBusy) {
                    _testReportProgressValue = 0;
                    _testReportProgressMaximum = 1;
                    RaisePropertyChanged(nameof(TestReportProgressValue));
                    RaisePropertyChanged(nameof(TestReportProgressMaximum));
                }

                RaisePropertyChanged(nameof(TestReportStatusDisplayText));
                RaisePropertyChanged(nameof(TestReportChromeVisibility));
            }
        }

        public ObservableCollection<RecentNinaLogSession> RecentLogSessions { get; } = new();

        public RecentNinaLogSession? SelectedRecentLogSession {
            get => RecentLogSessions.FirstOrDefault(s =>
                string.Equals(s.LogPath, TestReportLogFilePath, StringComparison.OrdinalIgnoreCase));
            set {
                if (value == null)
                    return;
                TestReportLogFilePath = value.LogPath;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(SelectedRecentLogSessionDetailText));
                RaisePropertyChanged(nameof(SelectedRecentLogSessionDetailVisibility));
            }
        }

        public string SelectedRecentLogSessionDetailText =>
            SelectedRecentLogSession?.DetailText ?? "";

        public Visibility SelectedRecentLogSessionDetailVisibility =>
            SelectedRecentLogSession != null ? Visibility.Visible : Visibility.Collapsed;

        private string _recentLogSessionsStatusText = "Scanning recent NINA logs…";
        public string RecentLogSessionsStatusText {
            get => _recentLogSessionsStatusText;
            private set {
                if (value == _recentLogSessionsStatusText) return;
                _recentLogSessionsStatusText = value;
                RaisePropertyChanged();
            }
        }

        private async Task RefreshRecentLogSessionsAsync() {
            RecentLogSessionsStatusText = "Scanning recent NINA logs…";
            try {
                var sessions = await Task.Run(RecentNinaLogSessionService.LoadRecentSessions).ConfigureAwait(false);
                await Application.Current!.Dispatcher.InvokeAsync(() => {
                    RecentLogSessions.Clear();
                    foreach (var session in sessions)
                        RecentLogSessions.Add(session);

                    if (string.IsNullOrWhiteSpace(TestReportLogFilePath) && RecentLogSessions.Count > 0)
                        TestReportLogFilePath = RecentLogSessions[0].LogPath;

                    RaisePropertyChanged(nameof(SelectedRecentLogSession));
                    RecentLogSessionsStatusText = sessions.Count == 0
                        ? "No NINA logs found from the last 14 days."
                        : $"Recent NINA logs — last 14 days ({sessions.Count}).";
                    ScheduleRefreshResolvedReportForSelectedLog();
                });
            } catch (Exception ex) {
                SeeDriftLog.Warning($"SeeDrift: recent log session scan failed — {ex.Message}");
                await Application.Current!.Dispatcher.InvokeAsync(() => {
                    RecentLogSessionsStatusText = $"Recent log scan failed — {ex.Message}";
                });
            }
        }

        private SavedSeeDriftReport? _compareBeforeReport;
        public SavedSeeDriftReport? CompareBeforeReport {
            get => _compareBeforeReport;
            set {
                if (ReferenceEquals(value, _compareBeforeReport)) return;
                _compareBeforeReport = value;
                _compareBeforeReportPath = value?.Path ?? "";
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(CompareBeforeReportDetailText));
                RaisePropertyChanged(nameof(CompareBeforeReportDetailVisibility));
                SyncSettingsFromProperties();
            }
        }

        private SavedSeeDriftReport? _compareAfterReport;
        public SavedSeeDriftReport? CompareAfterReport {
            get => _compareAfterReport;
            set {
                if (ReferenceEquals(value, _compareAfterReport)) return;
                _compareAfterReport = value;
                _compareAfterReportPath = value?.Path ?? "";
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(CompareAfterReportDetailText));
                RaisePropertyChanged(nameof(CompareAfterReportDetailVisibility));
                SyncSettingsFromProperties();
            }
        }

        public string CompareBeforeReportDetailText => CompareBeforeReport?.DetailText ?? "";

        public Visibility CompareBeforeReportDetailVisibility =>
            CompareBeforeReport != null ? Visibility.Visible : Visibility.Collapsed;

        public string CompareAfterReportDetailText => CompareAfterReport?.DetailText ?? "";

        public Visibility CompareAfterReportDetailVisibility =>
            CompareAfterReport != null ? Visibility.Visible : Visibility.Collapsed;

        private async Task RunTestReportFireAsync() {
            if (string.IsNullOrWhiteSpace(TestReportLogFilePath)) {
                MessageBox.Show(
                    "Choose a NINA log from the dropdown first.",
                    "SeeDrift — Report",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            await Application.Current!.Dispatcher.InvokeAsync(BeginTestReportUi);
            await Task.Yield();
            SeeDriftLog.Info($"SeeDrift: Report from log starting — {TestReportLogFilePath.Trim()}");
            var ok = false;
            try {
                var progress = new Progress<ApplicationStatus>(OnTestReportApplicationStatus);
                ok = await DriftTracker.RunTestReportFromLogAsync(TestReportLogFilePath.Trim(), progress, CancellationToken.None)
                    .ConfigureAwait(false);
            } catch (Exception ex) {
                SeeDriftLog.Error($"SeeDrift: Report from log failed — {ex.Message}");
            } finally {
                await Application.Current!.Dispatcher.InvokeAsync(EndTestReportUi);
            }

            if (!ok) {
                MessageBox.Show(
                    "The report did not add a batch to the night HTML.\n\n" +
                    "Common causes:\n" +
                    "• Fewer than two usable lights — NINA often saves .xisf (now accepted); logs must contain “Saved image to …” lines.\n" +
                    "• Files moved/deleted since the session.\n" +
                    "• Frames classified as calibration in FITS headers.\n" +
                    "• Plate solve failures.\n\n" +
                    "See %LocalAppData%\\NINA\\SeeDrift\\SeeDrift.log and NINA’s application log for lines starting with SeeDrift.",
                    "SeeDrift — Report",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private bool _testReportBusy;
        private string _testReportStatusText = "";
        private string? _nightReportLinkPath;

        /// <summary>NINA log paths that contributed to the last saved night report / completion line (normalized). Scoped to current selection.</summary>
        private HashSet<string>? _nightReportCompletionSourceLogsNormalized;

        private string? _resolvedNightReportPath;
        private string _resolvedNightReportSummary = "";
        private string _selectedLogNoReportHint = "";
        private bool _resolvedLookupDone;
        private DispatcherTimer? _resolveDebounceTimer;
        private string _compareReportStatusText = "";
        private string? _compareReportLinkPath;
        private double _testReportProgressValue;
        private int _testReportProgressMaximum = 1;
        private bool _testReportIndeterminate;

        /// <summary>False while a report run is in progress (disables Run report).</summary>
        public bool TestReportNotBusy => !_testReportBusy;

        public string TestReportStatusText => _testReportStatusText;

        public double TestReportProgressValue => _testReportProgressValue;

        public int TestReportProgressMaximum => _testReportProgressMaximum;

        public bool TestReportIndeterminate => _testReportIndeterminate;

        /// <summary>Shows the progress row while a report runs, or content scoped to the **currently selected** log.</summary>
        public Visibility TestReportChromeVisibility =>
            ShouldShowPreviousSessionReportPanel() ? Visibility.Visible : Visibility.Collapsed;

        private bool ShouldShowPreviousSessionReportPanel() {
            if (_testReportBusy)
                return true;
            if (!string.IsNullOrWhiteSpace(_testReportStatusText) && CurrentLogMatchesLastNightReportCompletion())
                return true;
            if (!string.IsNullOrWhiteSpace(_resolvedNightReportPath))
                return true;
            return _resolvedLookupDone
                && !string.IsNullOrWhiteSpace(TestReportLogFilePath)
                && !string.IsNullOrWhiteSpace(_selectedLogNoReportHint);
        }

        /// <summary>
        /// Prefer on-disk report for the selected log; otherwise the last completed run file only if that run used this same log path.
        /// </summary>
        private string? EffectiveNightReportOpenPath {
            get {
                if (!string.IsNullOrWhiteSpace(_resolvedNightReportPath))
                    return _resolvedNightReportPath.Trim();
                if (string.IsNullOrWhiteSpace(_nightReportLinkPath))
                    return null;
                if (!CurrentLogMatchesLastNightReportCompletion())
                    return null;
                return _nightReportLinkPath.Trim();
            }
        }

        /// <summary>Status line scoped to the selected log — completion text only when it refers to the log path in the path box.</summary>
        public string TestReportStatusDisplayText => FormatPreviousSessionStatusDisplayText();

        private string FormatPreviousSessionStatusDisplayText() {
            var trimPath = EffectiveNightReportOpenPath ?? _nightReportLinkPath;
            if (_testReportBusy)
                return FormatStatusForPanel(_testReportStatusText, trimPath);
            if (string.IsNullOrWhiteSpace(_testReportStatusText))
                return "";
            if (!CurrentLogMatchesLastNightReportCompletion())
                return "";
            return FormatStatusForPanel(_testReportStatusText, trimPath);
        }

        /// <summary>True when the path box matches a log that contributed to the last SeeDrift night HTML save this session.</summary>
        private bool CurrentLogMatchesLastNightReportCompletion() {
            var cur = NormalizeLogPath(TestReportLogFilePath);
            if (string.IsNullOrEmpty(cur) || _nightReportCompletionSourceLogsNormalized == null
                || _nightReportCompletionSourceLogsNormalized.Count == 0)
                return false;
            return _nightReportCompletionSourceLogsNormalized.Contains(cur);
        }

        private static string? NormalizeLogPath(string? p) {
            if (string.IsNullOrWhiteSpace(p))
                return null;
            try {
                return Path.GetFullPath(p.Trim());
            } catch {
                return p.Trim();
            }
        }

        private static HashSet<string>? NormalizeLogPathSet(IReadOnlyList<string>? paths) {
            if (paths == null || paths.Count == 0)
                return null;
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in paths) {
                var n = NormalizeLogPath(p);
                if (!string.IsNullOrEmpty(n))
                    set.Add(n);
            }

            return set.Count > 0 ? set : null;
        }

        /// <summary>Visible when the selected log has a resolvable night HTML on disk or the last run left an open path.</summary>
        public Visibility NightReportLinkChromeVisibility =>
            string.IsNullOrWhiteSpace(EffectiveNightReportOpenPath) ? Visibility.Collapsed : Visibility.Visible;

        /// <summary>File name only (for the open link label).</summary>
        public string NightReportLinkFileName =>
            string.IsNullOrWhiteSpace(EffectiveNightReportOpenPath)
                ? ""
                : Path.GetFileName(EffectiveNightReportOpenPath.Trim());

        /// <summary>Full path for the open-link tooltip.</summary>
        public string NightReportLinkPathHint => EffectiveNightReportOpenPath ?? "";

        /// <summary>Summary for the report file matched to the current log path (frames, processing time).</summary>
        public string SelectedLogSavedReportSummaryText => _resolvedNightReportSummary;

        public Visibility SelectedLogSavedReportSummaryVisibility =>
            string.IsNullOrWhiteSpace(_resolvedNightReportSummary) ? Visibility.Collapsed : Visibility.Visible;

        public string SelectedLogNoReportHintText => _selectedLogNoReportHint;

        public Visibility SelectedLogNoReportHintVisibility =>
            _resolvedLookupDone
            && string.IsNullOrWhiteSpace(_resolvedNightReportPath)
            && !string.IsNullOrWhiteSpace(TestReportLogFilePath)
            && !string.IsNullOrWhiteSpace(_selectedLogNoReportHint)
                ? Visibility.Visible
                : Visibility.Collapsed;

        public string CompareReportStatusText => _compareReportStatusText;

        public ObservableCollection<SavedSeeDriftReport> SavedReports { get; } = new();

        private string _savedReportsStatusText = "Saved reports library";
        public string SavedReportsStatusText {
            get => _savedReportsStatusText;
            private set {
                if (value == _savedReportsStatusText) return;
                _savedReportsStatusText = value;
                RaisePropertyChanged();
            }
        }

        public Visibility CompareReportChromeVisibility =>
            string.IsNullOrWhiteSpace(_compareReportStatusText) && string.IsNullOrWhiteSpace(_compareReportLinkPath)
                ? Visibility.Collapsed
                : Visibility.Visible;

        public Visibility CompareReportLinkChromeVisibility =>
            string.IsNullOrWhiteSpace(_compareReportLinkPath) ? Visibility.Collapsed : Visibility.Visible;

        public string CompareReportLinkFileName =>
            string.IsNullOrWhiteSpace(_compareReportLinkPath) ? "" : Path.GetFileName(_compareReportLinkPath.Trim());

        public string CompareReportLinkPathHint => _compareReportLinkPath ?? "";

        private void RefreshSavedReports() {
            try {
                var reports = SavedSeeDriftReportService.LoadReports();
                SavedReports.Clear();
                foreach (var report in reports)
                    SavedReports.Add(report);

                RebindCompareReportsFromPaths();
                SavedReportsStatusText = reports.Count == 0
                    ? $"No SeeDrift HTML reports found in {SeeDriftPaths.ReportLibrary}."
                    : $"Saved SeeDrift reports ({reports.Count}) from the report library.";
            } catch (Exception ex) {
                SavedReportsStatusText = $"Saved report scan failed — {ex.Message}";
            }
        }

        private void RebindCompareReportsFromPaths() {
            CompareBeforeReport = FindSavedReportByPath(_compareBeforeReportPath);
            CompareAfterReport = FindSavedReportByPath(_compareAfterReportPath);
        }

        private SavedSeeDriftReport? FindSavedReportByPath(string? path) {
            if (string.IsNullOrWhiteSpace(path))
                return null;
            return SavedReports.FirstOrDefault(r =>
                string.Equals(r.Path, path.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        private void ScheduleRefreshResolvedReportForSelectedLog() {
            var app = Application.Current;
            if (app?.Dispatcher == null) {
                RefreshResolvedReportForSelectedLog();
                return;
            }

            if (!app.Dispatcher.CheckAccess()) {
                app.Dispatcher.Invoke(ScheduleRefreshResolvedReportForSelectedLog);
                return;
            }

            if (_resolveDebounceTimer == null) {
                _resolveDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                _resolveDebounceTimer.Tick += (_, _) => {
                    _resolveDebounceTimer!.Stop();
                    RefreshResolvedReportForSelectedLog();
                };
            }

            _resolveDebounceTimer.Stop();
            _resolveDebounceTimer.Start();
        }

        private void RefreshResolvedReportForSelectedLog() {
            _resolvedNightReportPath = null;
            _resolvedNightReportSummary = "";
            _selectedLogNoReportHint = "";
            _resolvedLookupDone = false;
            RaiseResolvedReportUiProperties();

            if (string.IsNullOrWhiteSpace(TestReportLogFilePath)) {
                _resolvedLookupDone = true;
                RaiseResolvedReportUiProperties();
                return;
            }

            try {
                var logPath = TestReportLogFilePath.Trim();
                if (ReportLibraryLookup.TryFindNightReportForNinaLog(logPath, out var reportPath, out var summary)) {
                    _resolvedNightReportPath = reportPath;
                    _resolvedNightReportSummary = summary;
                } else {
                    _selectedLogNoReportHint =
                        "No saved SeeDrift HTML in the report library lists this log yet. Click Run report or check %LocalAppData%\\NINA\\SeeDrift\\Reports.";
                }
            } catch (Exception ex) {
                _selectedLogNoReportHint = $"Could not scan report library — {ex.Message}";
            }

            _resolvedLookupDone = true;
            RaiseResolvedReportUiProperties();
        }

        private void RaiseResolvedReportUiProperties() {
            RaisePropertyChanged(nameof(TestReportChromeVisibility));
            RaisePropertyChanged(nameof(NightReportLinkChromeVisibility));
            RaisePropertyChanged(nameof(NightReportLinkFileName));
            RaisePropertyChanged(nameof(NightReportLinkPathHint));
            RaisePropertyChanged(nameof(TestReportStatusDisplayText));
            RaisePropertyChanged(nameof(SelectedLogSavedReportSummaryText));
            RaisePropertyChanged(nameof(SelectedLogSavedReportSummaryVisibility));
            RaisePropertyChanged(nameof(SelectedLogNoReportHintText));
            RaisePropertyChanged(nameof(SelectedLogNoReportHintVisibility));
        }

        private void DeleteSelectedSavedReport() {
            var target = CompareAfterReport ?? CompareBeforeReport;
            if (target == null) {
                MessageBox.Show(
                    "Choose a Before or After report in the dropdowns first. Delete removes only that single file—not the whole library.",
                    "SeeDrift — Delete report",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var path = target.Path;
            var name = target.FileName;
            var confirm = MessageBox.Show(
                $"Delete only this one report file from disk?\n\n{name}\n\nOther saved reports are left untouched. This cannot be undone.",
                "SeeDrift — Delete report",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (confirm != MessageBoxResult.Yes)
                return;
            if (!SavedSeeDriftReportService.TryDeleteFromLibrary(path, out var error)) {
                MessageBox.Show(
                    error,
                    "SeeDrift — Delete report",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (string.Equals(path, CompareBeforeReport?.Path, StringComparison.OrdinalIgnoreCase))
                CompareBeforeReport = null;
            if (string.Equals(path, CompareAfterReport?.Path, StringComparison.OrdinalIgnoreCase))
                CompareAfterReport = null;
            if (string.Equals(path, _nightReportLinkPath?.Trim(), StringComparison.OrdinalIgnoreCase))
                ClearNightReportLink();

            RefreshSavedReports();
            RefreshResolvedReportForSelectedLog();
        }

        internal void ClearNightReportLink() {
            void Apply() {
                _nightReportLinkPath = null;
                _nightReportCompletionSourceLogsNormalized = null;
                RaisePropertyChanged(nameof(NightReportLinkChromeVisibility));
                RaisePropertyChanged(nameof(NightReportLinkFileName));
                RaisePropertyChanged(nameof(NightReportLinkPathHint));
                RaisePropertyChanged(nameof(TestReportStatusDisplayText));
                RaisePropertyChanged(nameof(TestReportChromeVisibility));
                RaiseResolvedReportUiProperties();
            }

            var app = Application.Current;
            if (app == null)
                Apply();
            else if (app.Dispatcher.CheckAccess())
                Apply();
            else
                app.Dispatcher.Invoke(Apply);
        }

        internal void NotifyNightReportSaved(
                string absolutePath,
                string completeStatusLine,
                bool postDiscordIfConfigured,
                IReadOnlyList<string>? completionSourceLogPaths = null) {
            void Apply() {
                _nightReportLinkPath = absolutePath;
                _testReportStatusText = completeStatusLine;
                _nightReportCompletionSourceLogsNormalized = NormalizeLogPathSet(completionSourceLogPaths);
                RaisePropertyChanged(nameof(NightReportLinkChromeVisibility));
                RaisePropertyChanged(nameof(NightReportLinkFileName));
                RaisePropertyChanged(nameof(NightReportLinkPathHint));
                RaisePropertyChanged(nameof(TestReportStatusText));
                RaisePropertyChanged(nameof(TestReportStatusDisplayText));
                RaisePropertyChanged(nameof(TestReportChromeVisibility));
                RefreshSavedReports();
                RefreshResolvedReportForSelectedLog();
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

        /// <summary>Stop finished with nothing chartable — no night HTML; optional Discord text-only webhook.</summary>
        internal void NotifyStopFinishedWithoutReport(string discordContent, bool postDiscordIfConfigured) {
            if (postDiscordIfConfigured)
                DiscordWebhookNotifier.EnqueueTextMessage(_discordWebhookUrl, discordContent);
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
                var p = EffectiveNightReportOpenPath;
                if (string.IsNullOrWhiteSpace(p))
                    return;
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
                if (CompareBeforeReport == null || CompareAfterReport == null) {
                    _compareReportLinkPath = null;
                    _compareReportStatusText = "Choose both a Before and an After report from the dropdowns.";
                    RaisePropertyChanged(nameof(CompareReportStatusText));
                    RaisePropertyChanged(nameof(CompareReportChromeVisibility));
                    return;
                }

                if (ReportComparisonService.TryWriteComparison(
                        CompareBeforeReport.Path,
                        CompareAfterReport.Path,
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
            RefreshSavedReports();
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
            return SeeDriftPaths.ResolveReportFolder();
        }

        private void BeginTestReportUi() {
            _testReportBusy = true;
            _testReportStatusText = "Report — starting (status updates appear here)…";
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

        private int _plateSolveParallelism =
            Math.Clamp(CpuTopology.PhysicalCoreCount, 1, CpuTopology.MaxPlateSolveParallelism);

        /// <summary>Concurrent plate solves during Stop or Run report (clamped to 1 … <see cref="CpuTopology.MaxPlateSolveParallelism"/>). Fresh defaults match physical CPU cores (clamped to that ceiling).</summary>
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
