using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using NINA.Core.Enum;
using NINA.Core.Model;
using NINA.Image.Interfaces;
using NINA.PlateSolving;
using NINA.PlateSolving.Interfaces;
using NINA.Plugin.SeeDrift.Models;
using NINA.Plugin.SeeDrift.Utility;
namespace NINA.Plugin.SeeDrift.Services {

    /// <summary>
    /// Plate-solves LIGHT files referenced by NINA “Saved image to …” log lines (SeeDrift Start→Stop reads matching logs under
    /// <c>%LocalAppData%\NINA\Logs</c> for the arm/disarm window; previous session report uses a log file you choose) and writes the rolling night HTML report.
    /// </summary>
    public sealed class DriftTrackingService : IDisposable {

        public sealed class CompletedTarget {
            public string Name { get; init; } = "";
            public IReadOnlyList<DriftSample> Samples { get; init; } = Array.Empty<DriftSample>();

            /// <summary>
            /// NINA log file path(s) that contributed saved-image lines for this batch (Stop = contributing logs in the arm/disarm window;
            /// previous session report = the single file you chose).
            /// </summary>
            public IReadOnlyList<string> SourceLogPaths { get; init; } = Array.Empty<string>();

            public SeestarDeviceInfo SeestarDevice { get; init; } = SeestarDeviceInfo.Unknown;

            /// <summary>
            /// When plate solving was skipped because FITS OBJECT counts could not reach minimum exposures, the largest
            /// LIGHT frame count seen on any single target (from headers before solve).
            /// </summary>
            public int? PresolveMaxLightsPerBestTarget { get; init; }

            /// <summary>Processing time for this run (log read, plate solves, correlation, HTML write).</summary>
            public TimeSpan RunDuration { get; init; }

            /// <summary>NINA log observations (configured threshold, dither pulse magnitudes / durations, cadence hints) for this batch.</summary>
            public SessionLogObservations? LogObservations { get; init; }
        }

        private sealed class TraceState {
            public double? RefRaHours;
            public double? RefDecDeg;
            public int NextFrameIndex;
            public string? RefTargetName;
        }

        private readonly record struct SolvedCoords(double RaHours, double DecDeg);

        private readonly SeeDriftPlugin _plugin;
        private readonly IPlateSolverFactory _plateSolverFactory;
        private readonly IImageDataFactory _imageDataFactory;
        private bool _disposed;

        private DateTime? _armUtc;

        public List<CompletedTarget> CompletedTargets { get; } = new();

        public bool IsArmed { get; private set; }

        public int JumpCount { get; private set; }
        public int LogCorrelatedCount { get; private set; }
        public bool LogWasFound { get; private set; }
        public int LogTriggerCount { get; private set; }
        public int LogSequencerEdgeCount { get; private set; }
        public int LogTraceDitherTriggerCount { get; private set; }
        public int LogTraceCenterTriggerCount { get; private set; }

        public DriftTrackingService(
                SeeDriftPlugin plugin,
                IPlateSolverFactory plateSolverFactory,
                IImageDataFactory imageDataFactory) {
            _plugin = plugin;
            _plateSolverFactory = plateSolverFactory;
            _imageDataFactory = imageDataFactory;
        }

        public void Arm() {
            Application.Current?.Dispatcher.Invoke(() => {
                _armUtc = DateTime.UtcNow;
                IsArmed = true;
                SeeDriftLog.Info("SeeDrift: armed (plate-solve on Stop).");
            });
        }

        /// <summary>Process files from NINA default image path between <see cref="_armUtc"/> and disarm time.</summary>
        public async Task DisarmAsync(IProgress<ApplicationStatus>? progress, CancellationToken token) {
            if (!IsArmed) {
                SeeDriftLog.Warning("SeeDrift: Disarm called while not armed — ignored.");
                return;
            }

            var disarmUtc = DateTime.UtcNow;
            var startUtc = _armUtc ?? disarmUtc;

            await Application.Current!.Dispatcher.InvokeAsync(() => { IsArmed = false; });

            try {
                var allLogs = NinaLogCorrelator.GetAllNinaLogFiles();
                var filtered = NinaLogCorrelator.FilterLogFilesForStopWindow(allLogs, startUtc, disarmUtc);
                IReadOnlyList<string> logFiles;
                if (filtered.Count > 0) {
                    logFiles = filtered;
                    if (filtered.Count < allLogs.Count)
                        SeeDriftLog.Info($"SeeDrift: Stop log pre-filter {allLogs.Count} → {filtered.Count} file(s) by arm/disarm local dates.");
                } else if (allLogs.Count > 0) {
                    logFiles = allLogs;
                    SeeDriftLog.Warning("SeeDrift: no NINA logs matched arm/disarm date filter — scanning all log files (fallback).");
                } else {
                    logFiles = allLogs;
                }

                await RunBatchFromLogsAsync(
                        logFiles,
                        startUtc,
                        disarmUtc,
                        resetSession: false,
                        postDiscordAfterSave: true,
                        progress,
                        token)
                    .ConfigureAwait(false);
            } finally {
                _armUtc = null;
            }
        }

        /// <summary>Previous session report: plate-solves lights listed in the chosen NINA log file (full file).</summary>
        /// <returns><c>true</c> if a batch was written to the night HTML.</returns>
        public async Task<bool> RunTestReportFromLogAsync(
                string logFilePath,
                IProgress<ApplicationStatus>? progress,
                CancellationToken token) {
            if (string.IsNullOrWhiteSpace(logFilePath)) {
                SeeDriftLog.Error("SeeDrift: Previous session report — no log file path.");
                progress?.Report(StatusOnly("Previous session report failed — no log file path."));
                return false;
            }

            try {
                logFilePath = Path.GetFullPath(logFilePath.Trim());
            } catch {
                SeeDriftLog.Error("SeeDrift: Previous session report — invalid log file path.");
                progress?.Report(StatusOnly("Previous session report failed — invalid log file path."));
                return false;
            }

            if (!File.Exists(logFilePath)) {
                SeeDriftLog.Error($"SeeDrift: Previous session report — log file not found: {logFilePath}");
                progress?.Report(StatusOnly($"Log file not found — {Path.GetFileName(logFilePath)}"));
                return false;
            }

            return await RunBatchFromLogsAsync(
                    new[] { logFilePath },
                    windowStartUtc: null,
                    windowEndUtc: null,
                    resetSession: true,
                    postDiscordAfterSave: false,
                    progress,
                    token)
                .ConfigureAwait(false);
        }

        private static ApplicationStatus StatusOnly(string msg) =>
            new ApplicationStatus { Source = "SeeDrift", Status = msg, Progress = 0, MaxProgress = 0 };

        public void ResetSession() {
            CompletedTargets.Clear();
            IsArmed = false;
            _armUtc = null;
            JumpCount = 0;
            LogCorrelatedCount = 0;
            LogWasFound = false;
            LogTriggerCount = 0;
            LogSequencerEdgeCount = 0;
            LogTraceDitherTriggerCount = 0;
            LogTraceCenterTriggerCount = 0;
        }

        private async Task<bool> RunBatchFromLogsAsync(
                IReadOnlyList<string> logFilesToRead,
                DateTime? windowStartUtc,
                DateTime? windowEndUtc,
                bool resetSession,
                bool postDiscordAfterSave,
                IProgress<ApplicationStatus>? progress,
                CancellationToken token) {

            void Report(string msg, int prog = 0, int max = 0) {
                progress?.Report(new ApplicationStatus {
                    Source = "SeeDrift",
                    Status = msg,
                    Progress = prog,
                    MaxProgress = max
                });
            }

            var runStopwatch = Stopwatch.StartNew();

            if (resetSession)
                ResetSession();

            _plugin.ClearNightReportLink();

            var profile = _plugin.ProfileService.ActiveProfile;
            if (profile?.PlateSolveSettings == null) {
                SeeDriftLog.Error("SeeDrift: No active NINA profile or plate-solve settings.");
                Report("Stopped — no plate-solve settings in the active NINA profile.");
                return false;
            }

            if (logFilesToRead == null || logFilesToRead.Count == 0) {
                SeeDriftLog.Warning("SeeDrift: no NINA log files to read — check %LocalAppData%\\NINA\\Logs exists and contains .log files.");
                Report("Stopped — no .log files found under %LocalAppData%\\NINA\\Logs.");
                return false;
            }

            Report(windowStartUtc.HasValue && windowEndUtc.HasValue
                ? "Reading log(s) for “Saved image to …” lines between Start and Stop…"
                : "Reading log file for “Saved image to …” lines (large logs can take a bit)…");

            if (!NinaLogCorrelator.TryCollectSavedImagePathsFromLogs(
                    logFilesToRead,
                    windowStartUtc,
                    windowEndUtc,
                    out var orderedSaves,
                    out var filesOpenedOk,
                    out var contributingLogPaths)) {
                SeeDriftLog.Warning("SeeDrift: could not open any of the listed log file(s).");
                Report("Stopped — could not read the log file (path wrong, missing, or blocked).");
                return false;
            }

            var sourceLogsForBatch = (IReadOnlyList<string>)(contributingLogPaths.Count > 0
                ? contributingLogPaths
                : logFilesToRead);

            if (orderedSaves.Count < 2) {
                SeeDriftLog.Warning(
                    $"SeeDrift: fewer than 2 saved-light lines in log(s) — no report ({orderedSaves.Count} candidates, filesOpened={filesOpenedOk.Count}).");
                Report(
                    $"Stopped — found {orderedSaves.Count} saved-light path line(s) SeeDrift could parse (need ≥2). " +
                    "Typical causes: log level hides SaveToDisk lines, timestamps SeeDrift does not recognize, or no “Saved image to …” lines in this file.");
                return false;
            }

            Report($"Found {orderedSaves.Count} saved paths — checking FITS headers (LIGHT vs calibration)…");

            var windowed = FitsFolderImport.BuildEntriesFromLogSaveOrder(orderedSaves, msg => Report(msg));

            if (windowed.Count < 2) {
                SeeDriftLog.Warning($"SeeDrift: fewer than 2 LIGHT frames after FITS filter — no report ({windowed.Count} kept).");
                Report($"Stopped — only {windowed.Count} LIGHT frame(s) after FITS filter (paths missing, wrong type, or not FITS).");
                return false;
            }

            var minExpTarget = Math.Max(1, Math.Min(500, _plugin.MinExposuresPerTarget));
            var maxLightsAnyTarget = MaxLightFramesPerBestTarget(windowed);
            if (maxLightsAnyTarget < minExpTarget) {
                SeeDriftLog.Info(
                    $"SeeDrift: skipping plate solve — no FITS OBJECT has ≥{minExpTarget} LIGHT frames (best target={maxLightsAnyTarget}).");
                Report(
                    $"Skipping plate solve — no target has {minExpTarget}+ LIGHT frames in this log (best target has {maxLightsAnyTarget}). " +
                    "Lower Minimum exposures per target or capture more frames.");

                string? presolveNightPath = null;
                string? presolveNightErr = null;
                await Application.Current!.Dispatcher.InvokeAsync(() => {
                    CompletedTargets.Add(new CompletedTarget {
                        Name = "",
                        Samples = Array.Empty<DriftSample>(),
                        SourceLogPaths = sourceLogsForBatch,
                        SeestarDevice = SeestarDeviceInfoService.FromLogFiles(sourceLogsForBatch),
                        PresolveMaxLightsPerBestTarget = maxLightsAnyTarget,
                        RunDuration = runStopwatch.Elapsed
                    });
                    if (!TryWriteNightReport(out presolveNightPath, out presolveNightErr)) {
                        if (CompletedTargets.Count > 0)
                            CompletedTargets.RemoveAt(CompletedTargets.Count - 1);
                    }
                });

                if (presolveNightErr != null) {
                    Report(
                        $"Stopped — could not save night HTML: {presolveNightErr}. " +
                        "Check %LocalAppData%\\NINA\\SeeDrift\\Reports and %LocalAppData%\\NINA\\SeeDrift\\SeeDrift.log.");
                    SeeDriftLog.Error($"SeeDrift: night HTML save failed (pre-solve skip path) — {presolveNightErr}");
                    return false;
                }

                var dur = RunDurationFormatter.ToReadable(runStopwatch.Elapsed);
                var completeDetail =
                    $"Complete — night report saved in {dur}. No target in this run had at least {minExpTarget} LIGHT frame(s) for any FITS target " +
                    $"(best target: {maxLightsAnyTarget}); plate solving was skipped. " +
                    "Lower Minimum exposures per target in Plugins → SeeDrift, or capture more frames per target. " +
                    $"{presolveNightPath}";
                _plugin.NotifyNightReportSaved(presolveNightPath!, completeDetail, postDiscordAfterSave, sourceLogsForBatch);
                Report(completeDetail, 100, 100);
                SeeDriftLog.Info($"SeeDrift: run finished without plate solve in {dur} — HTML → {presolveNightPath}");
                return true;
            }

            Report("Initializing plate solver…");

            progress?.Report(new ApplicationStatus {
                Source = "SeeDrift",
                Status = $"Plate solving {windowed.Count} lights…",
                Progress = 0,
                MaxProgress = 100
            });

            var parallelismCfg = Math.Clamp(
                _plugin.PlateSolveParallelism,
                1,
                CpuTopology.MaxPlateSolveParallelism);
            var poolSize = Math.Min(parallelismCfg, windowed.Count);
            SeeDriftLog.Info($"SeeDrift: plate-solve concurrency={poolSize} (setting max={parallelismCfg})");

            IImageSolver MakeSolver() {
                var primary = _plateSolverFactory.GetPlateSolver(profile.PlateSolveSettings!);
                var blind = _plateSolverFactory.GetBlindSolver(profile.PlateSolveSettings!);
                return _plateSolverFactory.GetImageSolver(primary, blind);
            }

            var coords = new SolvedCoords?[windowed.Count];
            var completed = 0;

            var frameTargetIdx = new int[windowed.Count];
            var targetKeyToIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < windowed.Count; i++) {
                var key = NormalizeTargetLabelForGrouping(windowed[i].TargetLabel);
                if (!targetKeyToIndex.TryGetValue(key, out var ti)) {
                    ti = targetKeyToIndex.Count;
                    targetKeyToIndex[key] = ti;
                }

                frameTargetIdx[i] = ti;
            }

            var targetCount = targetKeyToIndex.Count;
            var totalsPerTarget = new int[targetCount];
            for (var i = 0; i < windowed.Count; i++)
                totalsPerTarget[frameTargetIdx[i]]++;

            var finishedPerTarget = new int[targetCount];
            var successPerTarget = new int[targetCount];
            var boundsLock = new object();
            var hopelessFlag = 0;
            var hopelessLogOnce = 0;

            void RegisterSolveAttemptOutcome(int ti, bool solved, string pathForProgress) {
                lock (boundsLock) {
                    finishedPerTarget[ti]++;
                    if (solved)
                        successPerTarget[ti]++;
                    var maxPotentialSuccessesOnAnyTarget = 0;
                    for (var k = 0; k < targetCount; k++) {
                        var upperBound = successPerTarget[k] + (totalsPerTarget[k] - finishedPerTarget[k]);
                        if (upperBound > maxPotentialSuccessesOnAnyTarget)
                            maxPotentialSuccessesOnAnyTarget = upperBound;
                    }

                    if (maxPotentialSuccessesOnAnyTarget < minExpTarget) {
                        Volatile.Write(ref hopelessFlag, 1);
                        if (Interlocked.Exchange(ref hopelessLogOnce, 1) == 0) {
                            SeeDriftLog.Info(
                                $"SeeDrift: skipping remaining plate solves — no FITS OBJECT can still reach ≥{minExpTarget} successful solves " +
                                $"(best-case successes on any single target ≤{maxPotentialSuccessesOnAnyTarget}).");
                        }
                    }
                }

                var done = Interlocked.Increment(ref completed);
                var pct = (int)Math.Clamp((100 * done + windowed.Count / 2) / windowed.Count, 0, 100);
                progress?.Report(new ApplicationStatus {
                    Source = "SeeDrift",
                    Status = $"Solving {done}/{windowed.Count} — {Path.GetFileName(pathForProgress)}",
                    Progress = pct,
                    MaxProgress = 100
                });
            }

            var pool = new BlockingCollection<IImageSolver>();
            var ownedSolvers = new List<IImageSolver>(poolSize);
            for (var p = 0; p < poolSize; p++) {
                var s = MakeSolver();
                ownedSolvers.Add(s);
                pool.Add(s);
            }

            try {
                await Parallel.ForEachAsync(
                    Enumerable.Range(0, windowed.Count),
                    new ParallelOptions { MaxDegreeOfParallelism = poolSize, CancellationToken = token },
                    async (idx, ct) => {
                        ct.ThrowIfCancellationRequested();
                        var entry = windowed[idx];
                        var ti = frameTargetIdx[idx];

                        if (Volatile.Read(ref hopelessFlag) != 0) {
                            coords[idx] = null;
                            RegisterSolveAttemptOutcome(ti, false, entry.Path);
                            return;
                        }

                        var solver = pool.Take(ct);
                        try {
                            try {
                                coords[idx] = await TrySolveOneAsync(entry, solver, ct).ConfigureAwait(false);
                            } catch (OperationCanceledException) {
                                coords[idx] = null;
                                RegisterSolveAttemptOutcome(ti, false, entry.Path);
                                throw;
                            }

                            RegisterSolveAttemptOutcome(ti, coords[idx].HasValue, entry.Path);
                        } finally {
                            pool.Add(solver);
                        }
                    }).ConfigureAwait(false);
            } finally {
                foreach (var s in ownedSolvers) {
                    if (s is IDisposable d)
                        d.Dispose();
                }
            }

            var built = new List<DriftSample>(windowed.Count);
            var trace = new TraceState();
            for (var idx = 0; idx < windowed.Count; idx++) {
                var sc = coords[idx];
                if (!sc.HasValue)
                    continue;
                var entry = windowed[idx];
                var label = entry.TargetLabel;
                double? nomScale = null;
                double? exposureSeconds = null;
                if (TryResolveHeaderCards(entry, out var hdrCards)) {
                    if (FitsCoordinates.TryReadPlateScale(hdrCards, out var sPx))
                        nomScale = sPx;
                    if (FitsCoordinates.TryReadExposureSeconds(hdrCards, out var expSec))
                        exposureSeconds = expSec;
                }
                AccumulateFromParsed(
                    sc.Value.RaHours,
                    sc.Value.DecDeg,
                    entry.ExposureUtc,
                    entry.Path,
                    label,
                    trace,
                    null,
                    null,
                    nomScale,
                    exposureSeconds,
                    out var sample);
                built.Add(sample);
            }

            if (built.Count < 2) {
                SeeDriftLog.Warning($"SeeDrift: fewer than 2 plate-solved frames — no report segment.");
                Report("Stopped — fewer than 2 frames solved (check plate solver profile and FITS readability).");
                return false;
            }

            var maxSolvedPerTarget = MaxSolvedSamplesPerBestTarget(built);
            var skipLogCorrelation = maxSolvedPerTarget < minExpTarget;
            if (skipLogCorrelation) {
                SeeDriftLog.Info(
                    $"SeeDrift: skipping NINA log correlation — no target has ≥{minExpTarget} solved frames (best target={maxSolvedPerTarget}).");
                Report(
                    $"Skipping sequencer log correlation — best target has only {maxSolvedPerTarget} solved frame(s) (need ≥{minExpTarget}). Writing HTML…",
                    100,
                    100);
                JumpDetector.AnnotateJumps(built);
                JumpCount = JumpDetector.CountJumps(built);
                LogCorrelatedCount = 0;
                LogWasFound = false;
                LogTriggerCount = 0;
                LogSequencerEdgeCount = 0;
                LogTraceDitherTriggerCount = 0;
                LogTraceCenterTriggerCount = 0;
            } else {
                Report("Correlating sequencer lines and writing HTML…", 100, 100);
                ApplyJumpAndLogAnnotation(built, sourceLogsForBatch);
            }

            var targetName = HtmlReportExporter.SummarizeTargetsForBatch(built);

            string? nightSavedPath = null;
            string? nightSaveError = null;

            await Application.Current!.Dispatcher.InvokeAsync(() => {
                CompletedTargets.Add(new CompletedTarget {
                    Name = targetName,
                    Samples = built,
                    SourceLogPaths = sourceLogsForBatch,
                    SeestarDevice = SeestarDeviceInfoService.FromLogFiles(sourceLogsForBatch),
                    RunDuration = runStopwatch.Elapsed,
                    LogObservations = LatestLogObservations
                });
                if (!TryWriteNightReport(out nightSavedPath, out nightSaveError)) {
                    // Avoid leaving a batch in memory that never reached disk.
                    if (CompletedTargets.Count > 0)
                        CompletedTargets.RemoveAt(CompletedTargets.Count - 1);
                }
            });

            if (nightSaveError != null) {
                Report(
                    $"Stopped — could not save night HTML: {nightSaveError}. " +
                    "Check %LocalAppData%\\NINA\\SeeDrift\\Reports and %LocalAppData%\\NINA\\SeeDrift\\SeeDrift.log.");
                SeeDriftLog.Error($"SeeDrift: night HTML save failed after successful solve — {nightSaveError}");
                return false;
            }

            var elapsedReadable = RunDurationFormatter.ToReadable(runStopwatch.Elapsed);
            if (skipLogCorrelation) {
                var msgSkip =
                    $"Complete — night report saved in {elapsedReadable}. No target in this run had at least {minExpTarget} solved exposure(s) for any FITS target " +
                    $"(best target: {maxSolvedPerTarget}). Lower Minimum exposures per target in Plugins → SeeDrift, or capture more frames per target. " +
                    $"{nightSavedPath}";
                _plugin.NotifyNightReportSaved(nightSavedPath!, msgSkip, postDiscordAfterSave, sourceLogsForBatch);
                Report(msgSkip, 100, 100);
            } else {
                var msgOk =
                    $"Complete — night report saved ({built.Count} frames, {elapsedReadable}): {nightSavedPath}";
                _plugin.NotifyNightReportSaved(nightSavedPath!, msgOk, postDiscordAfterSave, sourceLogsForBatch);
                Report(msgOk, 100, 100);
            }

            SeeDriftLog.Info($"SeeDrift: batch complete — {built.Count} solved frames in {elapsedReadable} → {nightSavedPath}");
            return true;
        }

        private void ApplyJumpAndLogAnnotation(List<DriftSample> frames, IReadOnlyList<string>? correlatorLogPaths) {
            JumpDetector.AnnotateJumps(frames);
            var (logMatched, logFound, triggersLoaded, sequencerEdges, traceDithers, traceCenters) =
                NinaLogCorrelator.AnnotateWithLogEvents(frames, out var observations, correlatorLogPaths);
            JumpCount = JumpDetector.CountJumps(frames);
            LogCorrelatedCount = logMatched;
            LogWasFound = logFound;
            LogTriggerCount = triggersLoaded;
            LogSequencerEdgeCount = sequencerEdges;
            LogTraceDitherTriggerCount = traceDithers;
            LogTraceCenterTriggerCount = traceCenters;
            LatestLogObservations = observations;
        }

        /// <summary>Last <see cref="SessionLogObservations"/> built by the correlator for this batch (null before the first run).</summary>
        public SessionLogObservations? LatestLogObservations { get; private set; }

        /// <summary>
        /// Writes all <see cref="CompletedTargets"/> to the rolling night HTML file
        /// (<c>SeeDrift_ranYYYYMMDD_sessYYYYMMDD.html</c> — run date when written, session date from data).
        /// Uses the stable SeeDrift report library under <c>%LocalAppData%\NINA\SeeDrift\Reports</c>.
        /// </summary>
        private bool TryWriteNightReport(out string? fullPath, out string? errorMessage) {
            fullPath = null;
            errorMessage = null;
            if (CompletedTargets.Count == 0) {
                errorMessage = "internal: no batches to write";
                return false;
            }

            try {
                var folder = SeeDriftPaths.ResolveReportFolder();
                Directory.CreateDirectory(folder);
                var path = Path.Combine(folder, FormatNightReportHtmlFileName(CompletedTargets));
                path = Path.GetFullPath(path);
                HtmlReportExporter.WriteNightReport(CompletedTargets, path, _plugin.MinExposuresPerTarget);
                if (!File.Exists(path)) {
                    errorMessage = "file missing after write";
                    return false;
                }

                fullPath = path;
                SeeDriftLog.Info($"SeeDrift: night report saved → {path}");
                return true;
            } catch (Exception ex) {
                errorMessage = ex.Message;
                SeeDriftLog.Error($"SeeDrift: failed to write night report: {ex.Message}");
                return false;
            }
        }

        /// <summary>Night report file name: plugin version + local run date + session calendar day (NINA log filename date when available — same as HTML header).</summary>
        private static string FormatNightReportHtmlFileName(IReadOnlyList<CompletedTarget> targets) {
            var ran = DateTime.Now.ToString("yyyyMMdd");
            var sess = ResolveSessionDateStamp(targets);
            var device = ResolveDeviceFileNameToken(targets);
            var devicePart = string.IsNullOrWhiteSpace(device) ? "" : $"_{device}";
            return $"SeeDrift_v{FileVersionStamp()}{devicePart}_ran{ran}_sess{sess}.html";
        }

        private static string FileVersionStamp() {
            var version = typeof(DriftTrackingService).Assembly.GetName().Version?.ToString() ?? "";
            return version.Replace('.', '_');
        }

        /// <summary>Local <c>YYYYMMDD</c> for the imaging session (same rule as HTML header — log filename date first).</summary>
        private static string ResolveSessionDateStamp(IReadOnlyList<CompletedTarget> targets) =>
            SessionReportDates.ResolveSessionCalendarDay(targets).ToString("yyyyMMdd", CultureInfo.InvariantCulture);

        private static string ResolveDeviceFileNameToken(IReadOnlyList<CompletedTarget> targets) {
            var devices = targets
                .Select(t => t.SeestarDevice)
                .Where(d => d is { IsKnown: true } && !string.IsNullOrWhiteSpace(d.FileNameToken))
                .Select(d => d.FileNameToken.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (devices.Count == 0)
                return "";
            if (devices.Count > 1 || devices.Any(d => d.Equals(SeestarDeviceInfo.Mixed.FileNameToken, StringComparison.OrdinalIgnoreCase)))
                return SeestarDeviceInfo.Mixed.FileNameToken;
            return devices[0];
        }

        /// <summary>Largest number of plate-solved samples on a single FITS OBJECT in this run.</summary>
        private static int MaxSolvedSamplesPerBestTarget(IReadOnlyList<DriftSample> built) {
            if (built == null || built.Count == 0)
                return 0;
            return built
                .GroupBy(s => NormalizeTargetLabelForGrouping(s.TargetName), StringComparer.OrdinalIgnoreCase)
                .Max(g => g.Count());
        }

        /// <summary>Largest number of LIGHT frames on a single FITS OBJECT (from headers) in this run.</summary>
        private static int MaxLightFramesPerBestTarget(IReadOnlyList<FitsReplayEntry> entries) {
            if (entries == null || entries.Count == 0)
                return 0;
            return entries
                .GroupBy(e => NormalizeTargetLabelForGrouping(e.TargetLabel), StringComparer.OrdinalIgnoreCase)
                .Max(g => g.Count());
        }

        private static string NormalizeTargetLabelForGrouping(string? label) =>
            string.IsNullOrWhiteSpace(label) ? "Unknown" : label.Trim();

        private static bool TryResolveHeaderCards(FitsReplayEntry entry, out Dictionary<string, string> cards) {
            if (entry.PrimaryHeaderCards != null) {
                cards = entry.PrimaryHeaderCards;
                return true;
            }

            return FitsCoordinates.TryReadPrimaryHeader(entry.Path, out cards);
        }

        private async Task<SolvedCoords?> TrySolveOneAsync(FitsReplayEntry entry, IImageSolver solver, CancellationToken token) {
            if (!TryResolveHeaderCards(entry, out var cards))
                return null;

            var ps = PlateSolveHelper.BuildPlateSolveParameter(cards);

            NINA.Image.Interfaces.IImageData? img = null;
            try {
                img = await _imageDataFactory.CreateFromFile(
                    entry.Path, 32, false, RawConverterEnum.FREEIMAGE, token).ConfigureAwait(false);
            } catch (Exception ex) {
                SeeDriftLog.Warning($"SeeDrift: could not load image for solve — {entry.Path}: {ex.Message}");
                return null;
            }

            try {
                PlateSolveResult? result = null;
                try {
                    result = await solver.Solve(img, ps, null, token).ConfigureAwait(false);
                } catch (Exception ex) {
                    SeeDriftLog.Warning($"SeeDrift: plate solve failed — {entry.Path}: {ex.Message}");
                    return null;
                }

                if (result == null || !PlateSolveHelper.TryResultToRaDecHours(result, out var raH, out var decD))
                    return null;

                return new SolvedCoords(raH, decD);
            } finally {
                if (img is IDisposable d)
                    d.Dispose();
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
            double? nominalPlateScaleArcSecPerPx,
            double? exposureDurationSeconds,
            out DriftSample sample) {

            if (st.RefRaHours == null || st.RefDecDeg == null) {
                st.RefRaHours = raHours;
                st.RefDecDeg = decDeg;
                st.RefTargetName = label;
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
                CumulativePixelY = cumulativePixelY,
                NominalPlateScaleArcSecPerPx = nominalPlateScaleArcSecPerPx,
                ExposureDurationSeconds = exposureDurationSeconds
            };
        }

        public void Dispose() {
            if (_disposed) return;
            _disposed = true;
        }
    }
}
