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
    /// Plate-solves LIGHT files referenced by NINA “Saved image to …” log lines (SeeDrift Start→Stop scans logs under
    /// <c>%LocalAppData%\NINA\Logs</c>; previous session report uses a log file you choose) and writes the rolling night HTML report.
    /// </summary>
    public sealed class DriftTrackingService : IDisposable {

        public sealed class CompletedTarget {
            public string Name { get; init; } = "";
            public IReadOnlyList<DriftSample> Samples { get; init; } = Array.Empty<DriftSample>();

            /// <summary>NINA log file path(s) read for this batch (Stop = folder listing; previous session report = chosen file).</summary>
            public IReadOnlyList<string> SourceLogPaths { get; init; } = Array.Empty<string>();

            /// <summary>
            /// When plate solving was skipped because FITS OBJECT counts could not reach minimum exposures, the largest
            /// LIGHT frame count seen on any single target (from headers before solve).
            /// </summary>
            public int? PresolveMaxLightsPerBestTarget { get; init; }

            /// <summary>Wall time for this run (log read, plate solves, correlation, HTML write).</summary>
            public TimeSpan RunDuration { get; init; }
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
                var logFiles = NinaLogCorrelator.GetAllNinaLogFiles();
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
                    out var filesOpenedOk)) {
                SeeDriftLog.Warning("SeeDrift: could not open any of the listed log file(s).");
                Report("Stopped — could not read the log file (path wrong, missing, or blocked).");
                return false;
            }

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
                        SourceLogPaths = logFilesToRead,
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
                        "Check Plugins → SeeDrift → Night report folder (empty = Documents\\SeeDrift). See %LocalAppData%\\NINA\\SeeDrift\\SeeDrift.log.");
                    SeeDriftLog.Error($"SeeDrift: night HTML save failed (pre-solve skip path) — {presolveNightErr}");
                    return false;
                }

                var dur = RunDurationFormatter.ToReadable(runStopwatch.Elapsed);
                var completeDetail =
                    $"Complete — night report saved in {dur}. No target in this run had at least {minExpTarget} LIGHT frame(s) for any FITS target " +
                    $"(best target: {maxLightsAnyTarget}); plate solving was skipped. " +
                    "Lower Minimum exposures per target in Plugins → SeeDrift, or capture more frames per target. " +
                    $"{presolveNightPath}";
                _plugin.NotifyNightReportSaved(presolveNightPath!, completeDetail, postDiscordAfterSave);
                Report(completeDetail, 1, 1);
                SeeDriftLog.Info($"SeeDrift: run finished without plate solve in {dur} — HTML → {presolveNightPath}");
                return true;
            }

            Report("Initializing plate solver…");

            progress?.Report(new ApplicationStatus {
                Source = "SeeDrift",
                Status = $"Plate solving {windowed.Count} lights…",
                Progress = 0,
                MaxProgress = windowed.Count
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
                        var solver = pool.Take(ct);
                        try {
                            coords[idx] = await TrySolveOneAsync(entry, solver, ct).ConfigureAwait(false);
                        } finally {
                            pool.Add(solver);
                        }

                        var done = Interlocked.Increment(ref completed);
                        progress?.Report(new ApplicationStatus {
                            Source = "SeeDrift",
                            Status = $"Solving {done}/{windowed.Count} — {Path.GetFileName(entry.Path)}",
                            Progress = done,
                            MaxProgress = windowed.Count
                        });
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
                AccumulateFromParsed(sc.Value.RaHours, sc.Value.DecDeg, entry.ExposureUtc, entry.Path, label, trace, null, null, out var sample);
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
                    built.Count,
                    Math.Max(built.Count, 1));
                JumpDetector.AnnotateJumps(built);
                JumpCount = JumpDetector.CountJumps(built);
                LogCorrelatedCount = 0;
                LogWasFound = false;
                LogTriggerCount = 0;
                LogSequencerEdgeCount = 0;
                LogTraceDitherTriggerCount = 0;
                LogTraceCenterTriggerCount = 0;
            } else {
                Report("Correlating sequencer lines and writing HTML…", built.Count, Math.Max(built.Count, 1));
                ApplyJumpAndLogAnnotation(built, logFilesToRead);
            }

            var targetName = HtmlReportExporter.SummarizeTargetsForBatch(built);

            string? nightSavedPath = null;
            string? nightSaveError = null;

            await Application.Current!.Dispatcher.InvokeAsync(() => {
                CompletedTargets.Add(new CompletedTarget {
                    Name = targetName,
                    Samples = built,
                    SourceLogPaths = logFilesToRead,
                    RunDuration = runStopwatch.Elapsed
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
                    "Check Plugins → SeeDrift → Night report folder (empty = Documents\\SeeDrift). See %LocalAppData%\\NINA\\SeeDrift\\SeeDrift.log.",
                    built.Count,
                    built.Count);
                SeeDriftLog.Error($"SeeDrift: night HTML save failed after successful solve — {nightSaveError}");
                return false;
            }

            var elapsedReadable = RunDurationFormatter.ToReadable(runStopwatch.Elapsed);
            if (skipLogCorrelation) {
                var msgSkip =
                    $"Complete — night report saved in {elapsedReadable}. No target in this run had at least {minExpTarget} solved exposure(s) for any FITS target " +
                    $"(best target: {maxSolvedPerTarget}). Lower Minimum exposures per target in Plugins → SeeDrift, or capture more frames per target. " +
                    $"{nightSavedPath}";
                _plugin.NotifyNightReportSaved(nightSavedPath!, msgSkip, postDiscordAfterSave);
                Report(msgSkip, built.Count, built.Count);
            } else {
                var msgOk =
                    $"Complete — night report saved ({built.Count} frames, {elapsedReadable}): {nightSavedPath}";
                _plugin.NotifyNightReportSaved(nightSavedPath!, msgOk, postDiscordAfterSave);
                Report(msgOk, built.Count, built.Count);
            }

            SeeDriftLog.Info($"SeeDrift: batch complete — {built.Count} solved frames in {elapsedReadable} → {nightSavedPath}");
            return true;
        }

        private void ApplyJumpAndLogAnnotation(List<DriftSample> frames, IReadOnlyList<string>? correlatorLogPaths) {
            JumpDetector.AnnotateJumps(frames);
            var (logMatched, logFound, triggersLoaded, sequencerEdges, traceDithers, traceCenters) =
                NinaLogCorrelator.AnnotateWithLogEvents(frames, correlatorLogPaths);
            JumpCount = JumpDetector.CountJumps(frames);
            LogCorrelatedCount = logMatched;
            LogWasFound = logFound;
            LogTriggerCount = triggersLoaded;
            LogSequencerEdgeCount = sequencerEdges;
            LogTraceDitherTriggerCount = traceDithers;
            LogTraceCenterTriggerCount = traceCenters;
        }

        /// <summary>
        /// Writes all <see cref="CompletedTargets"/> to the rolling night HTML file
        /// (<c>SeeDrift_ranYYYYMMDD_sessYYYYMMDD.html</c> — run date when written, session date from data).
        /// Uses <see cref="SeeDriftPlugin.HtmlExportFolder"/> when set (trimmed); otherwise
        /// <c>%USERPROFILE%\Documents\SeeDrift</c> — not the Desktop unless you set that folder explicitly.
        /// </summary>
        private bool TryWriteNightReport(out string? fullPath, out string? errorMessage) {
            fullPath = null;
            errorMessage = null;
            if (CompletedTargets.Count == 0) {
                errorMessage = "internal: no batches to write";
                return false;
            }

            try {
                var raw = _plugin.HtmlExportFolder;
                var folder = string.IsNullOrWhiteSpace(raw?.Trim())
                    ? Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        "SeeDrift")
                    : raw.Trim();
                folder = Path.GetFullPath(folder);
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

        /// <summary>Night report file name: local run date + session calendar day (NINA log filename date when available — same as HTML header).</summary>
        private static string FormatNightReportHtmlFileName(IReadOnlyList<CompletedTarget> targets) {
            var ran = DateTime.Now.ToString("yyyyMMdd");
            var sess = ResolveSessionDateStamp(targets);
            return $"SeeDrift_ran{ran}_sess{sess}.html";
        }

        /// <summary>Local <c>YYYYMMDD</c> for the imaging session (same rule as HTML header — log filename date first).</summary>
        private static string ResolveSessionDateStamp(IReadOnlyList<CompletedTarget> targets) =>
            SessionReportDates.ResolveSessionCalendarDay(targets).ToString("yyyyMMdd", CultureInfo.InvariantCulture);

        /// <summary>Largest number of plate-solved samples on a single FITS OBJECT in this run.</summary>
        private static int MaxSolvedSamplesPerBestTarget(IReadOnlyList<DriftSample> built) {
            if (built == null || built.Count == 0)
                return 0;
            return built
                .GroupBy(
                    s => string.IsNullOrWhiteSpace(s.TargetName) ? "Unknown" : s.TargetName.Trim(),
                    StringComparer.OrdinalIgnoreCase)
                .Max(g => g.Count());
        }

        /// <summary>Largest number of LIGHT frames on a single FITS OBJECT (from headers) in this run.</summary>
        private static int MaxLightFramesPerBestTarget(IReadOnlyList<FitsReplayEntry> entries) {
            if (entries == null || entries.Count == 0)
                return 0;
            return entries
                .GroupBy(
                    e => string.IsNullOrWhiteSpace(e.TargetLabel) ? "Unknown" : e.TargetLabel.Trim(),
                    StringComparer.OrdinalIgnoreCase)
                .Max(g => g.Count());
        }

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
                CumulativePixelY = cumulativePixelY
            };
        }

        public void Dispose() {
            if (_disposed) return;
            _disposed = true;
        }
    }
}
