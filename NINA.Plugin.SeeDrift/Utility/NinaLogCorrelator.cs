using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NINA.Plugin.SeeDrift.Models;

namespace NINA.Plugin.SeeDrift.Utility {

    /// <summary>
    /// Correlates drift samples with NINA 3.x log lines: sequencer
    /// <c>Starting Trigger:</c>, <c>DirectGuider</c> dither pulses, and inter-frame intervals.
    /// </summary>
    internal static class NinaLogCorrelator {

        private static readonly string LogFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NINA", "Logs");

        /// <summary>All <c>*.log</c> files in <see cref="LogFolder"/> (SeeDrift Start→Stop uses these for saved paths + correlation).</summary>
        internal static IReadOnlyList<string> GetAllNinaLogFiles() {
            if (!Directory.Exists(LogFolder))
                return Array.Empty<string>();
            try {
                return Directory.GetFiles(LogFolder, "*.log", SearchOption.TopDirectoryOnly);
            } catch {
                return Array.Empty<string>();
            }
        }

        /// <summary>NINA 3.x log file name prefix <c>yyyyMMdd-HHmmss-</c> (local clock when the log was created).</summary>
        private static readonly Regex RxLogFileNameStart = new Regex(
            @"^(\d{8})-(\d{6})-",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// Parses the leading date/time from a NINA log file name as <b>local</b> time (filename convention).
        /// </summary>
        internal static bool TryParseNinaLogFileNameLocalStart(string logFilePath, out DateTime localStart) {
            localStart = default;
            var name = Path.GetFileName(logFilePath);
            var m = RxLogFileNameStart.Match(name);
            if (!m.Success)
                return false;
            var ymd = m.Groups[1].Value;
            var hms = m.Groups[2].Value;
            if (!DateTime.TryParseExact(
                    $"{ymd} {hms}",
                    "yyyyMMdd HHmmss",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal,
                    out var dt))
                return false;
            localStart = DateTime.SpecifyKind(dt, DateTimeKind.Local);
            return true;
        }

        /// <summary>
        /// Keeps log files whose name-derived local date is between one day before the earlier arm/disarm local date
        /// and the later local date (inclusive), so same-night sessions and log rotation across midnight are covered.
        /// Returns an empty list if nothing matched (caller should fall back to the full list).
        /// </summary>
        internal static IReadOnlyList<string> FilterLogFilesForStopWindow(
                IReadOnlyList<string> logFilePaths,
                DateTime armUtc,
                DateTime disarmUtc) {
            var armL = (armUtc.Kind == DateTimeKind.Utc ? armUtc : armUtc.ToUniversalTime()).ToLocalTime();
            var disL = (disarmUtc.Kind == DateTimeKind.Utc ? disarmUtc : disarmUtc.ToUniversalTime()).ToLocalTime();
            var d0 = armL.Date <= disL.Date ? armL.Date : disL.Date;
            var d1 = armL.Date >= disL.Date ? armL.Date : disL.Date;
            var low = d0.AddDays(-1);
            var high = d1;

            var picked = new List<string>();
            foreach (var p in logFilePaths) {
                if (string.IsNullOrWhiteSpace(p))
                    continue;
                if (!TryParseNinaLogFileNameLocalStart(p, out var localStart))
                    continue;
                if (localStart.Date >= low && localStart.Date <= high)
                    picked.Add(p);
            }

            return picked;
        }

        /// <summary>Center-after-drift trigger fired (NINA sequencer).</summary>
        private static readonly Regex RxTriggerCenter = new Regex(
            @"Starting\s+Trigger:.*CenterAfterDrift", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Dither-after-exposures trigger fired (NINA sequencer).</summary>
        private static readonly Regex RxTriggerDither = new Regex(
            @"Starting\s+Trigger:.*DitherAfterExposures", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>SeeDither plugin trigger (custom Category / Item line).</summary>
        private static readonly Regex RxSeeDitherTrigger = new Regex(
            @"Starting\s+Category:\s*SeeDither.*SeeDitherAfterExposuresTrigger",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// AfterExposures = N (DitherAfterExposures cadence) or EveryExposures = N (CenterAfterDrift cadence)
        /// that some NINA builds emit on or near the <c>Starting Trigger</c> line.
        /// </summary>
        private static readonly Regex RxAfterExposuresInline = new Regex(
            @"(?:After\s*Exposures|Every)\s*[=:]\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>NINA guider commanded dither: full line includes from→to and optional guide durations (seconds).</summary>
        private static readonly Regex RxDitherPulse = new Regex(
            @"DirectGuider\.cs\|SelectDitherPulse\|.*\|Dither target from \(([-0-9.eE]+)\s*,\s*([-0-9.eE]+)\) to \(([-0-9.eE]+)\s*,\s*([-0-9.eE]+)\)(?: using guide durations of ([-0-9.eE]+) and ([-0-9.eE]+) seconds)?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>SeeDither log line (message tail captured after the bracketed tag).</summary>
        private static readonly Regex RxSeeDitherLogLine = new Regex(
            @"\[SeeDither\]\s+(.*)$",
            RegexOptions.Compiled);

        /// <summary>SeeDither commanded move in arc-seconds (ΔRA / ΔDec).</summary>
        private static readonly Regex RxSeeDitherDelta = new Regex(
            @"\[SeeDither\]\s+Dithering\s+by\s+RA\s*=\s*([-0-9.eE]+)""\s+Dec\s*=\s*([-0-9.eE]+)""",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Center-after-drift plate-solve follower: reported drift vs threshold (arc minutes).</summary>
        private static readonly Regex RxCenterDriftArcMin = new Regex(
            @"CenterAfterDriftTrigger\.cs\|PlatesolvingImageFollower_PropertyChanged\|\d+\|Drift:\s*([-0-9.eE]+)\s*/\s*([-0-9.eE]+)\s*arc\s*minutes?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>NINA Target Scheduler <c>NewTargetStart</c> (return to a target panel).</summary>
        private static readonly Regex RxTargetSchedulerNewTarget = new Regex(
            @"TargetScheduler-NewTargetStart",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex RxTargetLabelInSchedulerLine = new Regex(
            @"(?:TargetName|target\s*name|Target)\s*[=:]\s*[""']?([^""'|,;\]]+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Ordered strict→loose: NINA builds vary (<c>BaseImageData</c> vs generic SaveToDisk, spacing, optional colon).
        /// </summary>
        private static readonly Regex[] RxSavedImagePatterns = {
            new Regex(@"BaseImageData\.cs\|SaveToDisk\|.*\|Saved image to:?\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex(@"\|SaveToDisk\|[^\|]*\|[^\|]*\|.*Saved image to:?\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex(@"SaveToDisk.*Saved image to:?\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex(@"(?i)Saved image to:?\s+(.+)$", RegexOptions.Compiled),
        };

        /// <summary>Find an ISO-like timestamp anywhere on the line (handles prefixes before the clock).</summary>
        private static readonly Regex RxTimestampEmbedded = new Regex(
            @"(?<!\d)(\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}(?::?\d{2})?)?)",
            RegexOptions.Compiled);

        // NINA 3.x log line timestamp — prefix before first | or INFO field (include fractional seconds).
        private static readonly Regex[] _tsPatterns = {
            new Regex(@"^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}(?::?\d{2})?)?)", RegexOptions.Compiled),
            new Regex(@"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}(?:\.\d+)?)", RegexOptions.Compiled),
            new Regex(@"^\[?(\d{2}/\d{2}/\d{4} \d{2}:\d{2}:\d{2}(?:\.\d+)?)\]?", RegexOptions.Compiled),
        };

        /// <summary>
        /// Log trigger/pulse lines can fall in the same second as FITS <c>DATE-OBS</c> (second resolution)
        /// while NINA logs sub-second times; extend the strict inter-frame upper bound slightly so those
        /// dithers are not dropped.
        /// </summary>
        private static readonly TimeSpan InterFrameLogUpperSlop = TimeSpan.FromSeconds(1.5);

        private enum TriggerKind {
            Center,
            Dither
        }

        private sealed class TimedTrigger {
            public DateTime UtcTime { get; set; }
            public TriggerKind Kind { get; set; }
            public string Label { get; set; } = "";
            public bool IsSeeDither { get; set; }

            /// <summary>
            /// When the NINA build prints <c>AfterExposures = N</c> on or near the trigger line, captured here.
            /// Null when no such number was visible — caller falls back to inferring N from frame gaps.
            /// </summary>
            public int? AfterExposuresFromLog { get; set; }
        }

        private sealed class DitherPulse {
            public DateTime UtcTime { get; set; }
            public double FromX { get; set; }
            public double FromY { get; set; }
            public double ToX { get; set; }
            public double ToY { get; set; }
            public double Dx { get; set; }
            public double Dy { get; set; }
            public double? GuideDurationFirstSec { get; set; }
            public double? GuideDurationSecondSec { get; set; }
        }

        private sealed class SeeDitherLogEntry {
            public DateTime UtcTime { get; set; }
            public string Message { get; set; } = "";
            public double? DeltaRaArcSec { get; set; }
            public double? DeltaDecArcSec { get; set; }
        }

        private sealed class CenterDriftLogLine {
            public DateTime UtcTime { get; set; }
            public double DriftArcMinutes { get; set; }
            public double ThresholdArcMinutes { get; set; }
        }

        private sealed class ImageSaveEvent {
            public DateTime UtcTime { get; set; }
            public string FilePath { get; set; } = "";
        }

        /// <summary>
        /// Collects “Saved image to …” paths from NINA logs. When both window endpoints are null, every save line in the
        /// listed files is kept (historic single-file replay). Otherwise keeps lines whose timestamp falls in
        /// <paramref name="windowStartUtc"/>…<paramref name="windowEndUtc"/> inclusive (UTC).
        /// Results are sorted by log time; duplicate paths keep the earliest line.
        /// </summary>
        /// <param name="contributingLogPaths">
        /// Distinct log files that contributed at least one kept “Saved image to …” line (sorted, for HTML + correlation).
        /// </param>
        internal static bool TryCollectSavedImagePathsFromLogs(
                IEnumerable<string> logFilePaths,
                DateTime? windowStartUtc,
                DateTime? windowEndUtc,
                out List<(string path, DateTime lineUtc)> orderedUniquePaths,
                out List<string> filesOpenedOk,
                out List<string> contributingLogPaths) {

            orderedUniquePaths = new List<(string path, DateTime lineUtc)>();
            filesOpenedOk = new List<string>();
            contributingLogPaths = new List<string>();
            var contributingSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            static DateTime AsUtc(DateTime t) =>
                t.Kind == DateTimeKind.Utc ? t : t.ToUniversalTime();

            DateTime? w0 = windowStartUtc.HasValue ? AsUtc(windowStartUtc.Value) : (DateTime?)null;
            DateTime? w1 = windowEndUtc.HasValue ? AsUtc(windowEndUtc.Value) : (DateTime?)null;
            if (w0.HasValue && w1.HasValue && w1.Value < w0.Value)
                (w0, w1) = (w1, w0);

            var windowed = w0.HasValue && w1.HasValue;
            if (!windowed && (windowStartUtc.HasValue || windowEndUtc.HasValue)) {
                SeeDriftLog.Warning("SeeDrift: log path collector — incomplete UTC window; ignoring window filter.");
                windowed = false;
                w0 = w1 = null;
            }

            var rawEvents = new List<(DateTime utc, string path)>();

            foreach (var logPath in logFilePaths) {
                if (string.IsNullOrWhiteSpace(logPath))
                    continue;
                try {
                    if (!File.Exists(logPath))
                        continue;
                    using var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var sr = new StreamReader(fs);
                    filesOpenedOk.Add(logPath);

                    string? line;
                    while ((line = sr.ReadLine()) != null) {
                        if (!TryParseLogLineUtc(line, out var ts))
                            continue;

                        if (windowed && (ts < w0!.Value || ts > w1!.Value))
                            continue;

                        if (!TryExtractSavedImagePath(line, out var rawPath)
                            || string.IsNullOrEmpty(rawPath)
                            || ShouldIgnoreSavePath(rawPath))
                            continue;

                        rawEvents.Add((ts, rawPath));
                        contributingSet.Add(logPath.Trim());
                    }
                } catch {
                    // skip unreadable log
                }
            }

            contributingLogPaths.AddRange(contributingSet.OrderBy(p => p, StringComparer.OrdinalIgnoreCase));

            rawEvents.Sort((x, y) => x.utc.CompareTo(y.utc));

            var seenPath = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (utc, p) in rawEvents) {
                if (seenPath.Add(p))
                    orderedUniquePaths.Add((p, utc));
            }

            return filesOpenedOk.Count > 0;
        }

        /// <summary>
        /// Loads sequencer triggers and dither pulses; sets hints and inter-frame edge fields.
        /// Returns matched jump count (jumps whose next frame has a strict between-exposure dither/center interval),
        /// log found, trigger count in the parsed date window, edge marker count,
        /// and dither vs center trigger counts whose timestamps fall from first to last frame (exposure start UTC, inclusive).
        /// </summary>
        /// <param name="explicitLogPaths">When non-null and non-empty, parse only these files (full content). Otherwise discover logs by session date.</param>
        public static (int MatchedJumps, bool LogFound, int TriggersLoaded, int SequencerEdges, int TraceDitherTriggers, int TraceCenterTriggers) AnnotateWithLogEvents(
                List<DriftSample> samples,
                IReadOnlyList<string>? explicitLogPaths = null) {
            return AnnotateWithLogEvents(samples, out _, explicitLogPaths);
        }

        /// <summary>
        /// Overload that also returns a <see cref="SessionLogObservations"/> built from the same parsed log data
        /// (configured CenterAfterDrift threshold, dither pulse magnitudes / guide durations, cadence hints).
        /// </summary>
        public static (int MatchedJumps, bool LogFound, int TriggersLoaded, int SequencerEdges, int TraceDitherTriggers, int TraceCenterTriggers) AnnotateWithLogEvents(
                List<DriftSample> samples,
                out SessionLogObservations observations,
                IReadOnlyList<string>? explicitLogPaths = null) {
            return AnnotateWithLogEvents(samples, out observations, out _, explicitLogPaths);
        }

        public static (int MatchedJumps, bool LogFound, int TriggersLoaded, int SequencerEdges, int TraceDitherTriggers, int TraceCenterTriggers) AnnotateWithLogEvents(
                List<DriftSample> samples,
                out SessionLogObservations observations,
                out List<TargetSchedulerStartEvent> schedulerStarts,
                IReadOnlyList<string>? explicitLogPaths = null) {
            observations = new SessionLogObservations();
            schedulerStarts = new List<TargetSchedulerStartEvent>();
            if (samples.Count == 0) return (0, false, 0, 0, 0, 0);
            try {
                foreach (var s in samples) {
                    s.SequencerLogHint = null;
                    s.EdgeSequencerMarkers = null;
                }

                // Log filenames (and typical NINA timestamps without "Z") follow local calendar days — use local date, not UTC midnight date.
                var sessionDate = samples[0].ExposureStartUtc.ToLocalTime().Date;
                SeeDriftLog.Debug($"SeeDrift: log correlator — sessionDate(local)={sessionDate:yyyy-MM-dd}, first sample UTC={samples[0].ExposureStartUtc:o}");

                var triggers = new List<TimedTrigger>();
                var pulses = new List<DitherPulse>();
                var centerDriftLines = new List<CenterDriftLogLine>();
                var imageSaves = new List<ImageSaveEvent>();
                var seeDitherEntries = new List<SeeDitherLogEntry>();

                bool loaded;
                if (explicitLogPaths != null && explicitLogPaths.Count > 0) {
                    loaded = false;
                    foreach (var path in explicitLogPaths.Distinct(StringComparer.OrdinalIgnoreCase)) {
                        if (!File.Exists(path))
                            continue;
                        loaded = true;
                        ParseLogLines(path, triggers, pulses, centerDriftLines, imageSaves, seeDitherEntries, schedulerStarts);
                    }
                    if (!loaded)
                        return (0, false, 0, 0, 0, 0);
                } else {
                    if (!LoadAndParseSessionLogs(sessionDate, triggers, pulses, centerDriftLines, imageSaves, seeDitherEntries, schedulerStarts))
                        return (0, false, 0, 0, 0, 0);
                }

                schedulerStarts.Sort((a, b) => a.UtcTime.CompareTo(b.UtcTime));

                triggers.Sort((a, b) => a.UtcTime.CompareTo(b.UtcTime));
                pulses.Sort((a, b) => a.UtcTime.CompareTo(b.UtcTime));
                centerDriftLines.Sort((a, b) => a.UtcTime.CompareTo(b.UtcTime));
                imageSaves.Sort((a, b) => a.UtcTime.CompareTo(b.UtcTime));

                SeeDriftLog.Debug(
                    $"SeeDrift: log correlator — triggers={triggers.Count}, dither pulses={pulses.Count}, center drift lines={centerDriftLines.Count}, image saves={imageSaves.Count}");
                AssignInterFrameEdges(samples, triggers, pulses, centerDriftLines, imageSaves, seeDitherEntries);

                observations = BuildSessionLogObservations(samples, triggers, pulses, centerDriftLines);

                if (triggers.Count == 0) {
                    SeeDriftLog.Debug("SeeDrift: log correlator — no Starting Trigger lines in date window");
                    return (CountJumpsWithNextFrameLogInterval(samples), true, 0, CountSequencerEdges(samples), 0, 0);
                }

                var firstJump = samples.OrderBy(s => s.FrameIndex).FirstOrDefault(s => s.IsJump);
                if (firstJump != null) {
                    var nearest = triggers.OrderBy(e => (e.UtcTime - firstJump!.ExposureStartUtc).Duration()).First();
                    var gap = (nearest.UtcTime - firstJump.ExposureStartUtc).Duration();
                    SeeDriftLog.Debug(
                        $"SeeDrift: log correlator — first jump UTC={firstJump.ExposureStartUtc:o}, nearest trigger '{nearest.Label}' UTC={nearest.UtcTime:o}, gap={gap.TotalSeconds:F0}s (not used for UI — only strict between-frame intervals)");
                }

                var matchedJumps = CountJumpsWithNextFrameLogInterval(samples);
                var (traceDithers, traceCenters) = CountSequencerTriggersInTraceSpan(samples, triggers);
                SeeDriftLog.Debug(
                    $"SeeDrift: log correlator — jumps with next-frame log interval={matchedJumps}, trace span triggers: dither={traceDithers}, center={traceCenters}");
                return (matchedJumps, true, triggers.Count, CountSequencerEdges(samples), traceDithers, traceCenters);
            } catch (Exception ex) {
                SeeDriftLog.Debug($"SeeDrift: log correlation skipped: {ex.Message}");
                return (0, false, 0, 0, 0, 0);
            }
        }

        /// <summary>
        /// Builds the run-wide "settings used" observations from the per-batch log parse. Cadence values are
        /// captured from log text first (when present) and supplemented with frame-index gaps between successive
        /// triggers / evaluation lines as a fallback that does not depend on the NINA build's exact ToString().
        /// </summary>
        private static SessionLogObservations BuildSessionLogObservations(
                IReadOnlyList<DriftSample> samples,
                IReadOnlyList<TimedTrigger> triggers,
                IReadOnlyList<DitherPulse> pulses,
                IReadOnlyList<CenterDriftLogLine> centerDriftLines) {
            var obs = new SessionLogObservations {
                ObservedCenterEvaluationCount = centerDriftLines.Count,
                ObservedDitherTriggerCount = triggers.Count(t => t.Kind == TriggerKind.Dither),
                ObservedDitherPulseCount = pulses.Count
            };

            foreach (var c in centerDriftLines) {
                if (c.ThresholdArcMinutes > 0)
                    obs.CenterAfterDriftThresholdArcMin.Add(c.ThresholdArcMinutes);
            }

            foreach (var t in triggers) {
                if (t.Kind == TriggerKind.Dither && t.AfterExposuresFromLog.HasValue)
                    obs.DitherAfterExposuresFromLog.Add(t.AfterExposuresFromLog.Value);
            }

            foreach (var p in pulses) {
                obs.DitherPulseMagnitudePixels.Add(Math.Max(Math.Abs(p.Dx), Math.Abs(p.Dy)));
                if (p.GuideDurationFirstSec.HasValue)
                    obs.DitherGuideDurationsFirstSec.Add(p.GuideDurationFirstSec.Value);
                if (p.GuideDurationSecondSec.HasValue)
                    obs.DitherGuideDurationsSecondSec.Add(p.GuideDurationSecondSec.Value);
            }

            if (samples.Count > 0) {
                var ordered = samples.OrderBy(s => s.FrameIndex).ToList();
                var tLo = ordered[0].ExposureStartUtc;
                var tHi = ordered[^1].ExposureStartUtc;
                if (tHi < tLo) (tLo, tHi) = (tHi, tLo);

                int? FrameIndexAtOrAfter(DateTime utc) {
                    foreach (var s in ordered) {
                        if (s.ExposureStartUtc >= utc)
                            return s.FrameIndex;
                    }
                    return null;
                }

                var ditherFrames = new List<int>();
                foreach (var t in triggers) {
                    if (t.Kind != TriggerKind.Dither) continue;
                    if (t.UtcTime < tLo || t.UtcTime > tHi) continue;
                    if (FrameIndexAtOrAfter(t.UtcTime) is { } f)
                        ditherFrames.Add(f);
                }
                for (var i = 1; i < ditherFrames.Count; i++) {
                    var gap = ditherFrames[i] - ditherFrames[i - 1];
                    if (gap > 0)
                        obs.DitherAfterExposuresInferredGapsFrames.Add(gap);
                }

                var centerFrames = new List<int>();
                foreach (var c in centerDriftLines) {
                    if (c.UtcTime < tLo || c.UtcTime > tHi) continue;
                    if (FrameIndexAtOrAfter(c.UtcTime) is { } f)
                        centerFrames.Add(f);
                }
                for (var i = 1; i < centerFrames.Count; i++) {
                    var gap = centerFrames[i] - centerFrames[i - 1];
                    if (gap > 0)
                        obs.CenterAfterDriftEvaluateGapsFrames.Add(gap);
                }
            }

            return obs;
        }

        /// <summary>
        /// Counts <c>Starting Trigger:</c> lines in <paramref name="triggers"/> whose UTC time lies between
        /// the first and last sample exposure starts (by <see cref="DriftSample.FrameIndex"/>), inclusive.
        /// </summary>
        private static (int Dither, int Center) CountSequencerTriggersInTraceSpan(
                IReadOnlyList<DriftSample> samples,
                IReadOnlyList<TimedTrigger> triggers) {
            if (samples.Count == 0) return (0, 0);
            var ordered = samples.OrderBy(s => s.FrameIndex).ToList();
            var tLo = ordered[0].ExposureStartUtc;
            var tHi = ordered[^1].ExposureStartUtc;
            if (tHi < tLo)
                (tLo, tHi) = (tHi, tLo);
            var d = 0;
            var c = 0;
            foreach (var tr in triggers) {
                if (tr.UtcTime < tLo || tr.UtcTime > tHi)
                    continue;
                if (tr.Kind == TriggerKind.Dither) d++;
                else c++;
            }
            return (d, c);
        }

        /// <summary>Jumps where the <em>following</em> frame boundary has a logged dither/center between exposures.</summary>
        private static int CountJumpsWithNextFrameLogInterval(List<DriftSample> samples) {
            var n = 0;
            foreach (var sample in samples.Where(s => s.IsJump)) {
                var next = samples.FirstOrDefault(x => x.FrameIndex == sample.FrameIndex + 1);
                if (next != null && next.EdgeSequencerMarkers is { Count: > 0 })
                    n++;
            }
            return n;
        }

        private static int CountSequencerEdges(List<DriftSample> samples) =>
            samples.Count(s => s.EdgeSequencerMarkers is { Count: > 0 });

        /// <summary>
        /// Earliest SaveToDisk UTC per FITS basename (ignores plate-solver temps).
        /// Uses the first logged save so a later re-save does not push <c>t0</c> past the real inter-frame gap.
        /// </summary>
        private static Dictionary<string, DateTime> BuildSaveUtcByFileName(IReadOnlyList<ImageSaveEvent> saves) {
            var map = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            foreach (var ev in saves) {
                if (string.IsNullOrWhiteSpace(ev.FilePath) || ShouldIgnoreSavePath(ev.FilePath))
                    continue;
                var name = Path.GetFileName(ev.FilePath.Trim());
                if (string.IsNullOrEmpty(name))
                    continue;
                if (!map.TryGetValue(name, out var prevT) || ev.UtcTime < prevT)
                    map[name] = ev.UtcTime;
            }
            return map;
        }

        internal static bool ShouldIgnoreSavePath(string fullPath) {
            var p = fullPath.Replace('/', '\\');
            return p.IndexOf("PlateSolver", StringComparison.OrdinalIgnoreCase) >= 0
                || p.IndexOf(@"\AppData\Local\Temp\", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Returns the first <c>AfterExposures = N</c> on the line, or null if absent.</summary>
        private static int? TryParseAfterExposures(string line) {
            var m = RxAfterExposuresInline.Match(line);
            if (!m.Success)
                return null;
            return int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0
                ? n
                : (int?)null;
        }

        /// <summary>
        /// Uses NINA exposure index from file names when both parse; otherwise trace position (<c>FrameIndex + 1</c>).
        /// </summary>
        private static string BetweenFramesHoverLine(DriftSample prev, DriftSample cur) {
            var frameRange = FitsFolderImport.FormatBetweenFramesHoverRange(
                prev.FileName, prev.FrameIndex, cur.FileName, cur.FrameIndex);
            return string.Join(Environment.NewLine, new[] {
                frameRange,
                prev.FileName ?? "",
                cur.FileName ?? ""
            });
        }

        private static void AssignInterFrameEdges(
                List<DriftSample> samples,
                List<TimedTrigger> triggers,
                List<DitherPulse> pulses,
                List<CenterDriftLogLine> centerDriftLines,
                List<ImageSaveEvent> imageSaves,
                List<SeeDitherLogEntry> seeDitherEntries) {

            var saveByFileName = BuildSaveUtcByFileName(imageSaves);
            var ordered = samples.OrderBy(s => s.FrameIndex).ToList();
            var usedSeeDitherEntries = new HashSet<SeeDitherLogEntry>();
            var deferredSeeDithers = new List<TimedTrigger>();
            for (var i = 1; i < ordered.Count; i++) {
                var prev = ordered[i - 1];
                var cur = ordered[i];
                var t0 = prev.ExposureStartUtc;
                var t1 = cur.ExposureStartUtc;
                var hasSaveBounds = false;
                if (TryGetLogSaveUtc(prev, saveByFileName, out var save0)
                    && TryGetLogSaveUtc(cur, saveByFileName, out var save1)
                    && save1 > save0) {
                    t0 = save0;
                    t1 = save1;
                    hasSaveBounds = true;
                    // End window at next exposure start when FITS time is sane — avoids classifying the
                    // following gap's trigger inside (save_prev, save_cur) when save_cur is very late.
                    var expCur = cur.ExposureStartUtc;
                    if (expCur > t0 && expCur < t1)
                        t1 = expCur;
                }
                if (t1 <= t0)
                    continue;

                var upperBound = hasSaveBounds ? t1 : t1 + InterFrameLogUpperSlop;

                var inGap = triggers
                    .Where(t =>
                        (hasSaveBounds ? t.UtcTime >= t0 : t.UtcTime > t0)
                        && t.UtcTime < upperBound)
                    .OrderBy(t => t.UtcTime)
                    .ToList();
                var combinedTriggers = new List<TimedTrigger>();
                if (deferredSeeDithers.Count > 0) {
                    SeeDriftLog.Debug($"NinaLogCorrelator: Adding {deferredSeeDithers.Count} deferred SeeDither(s) to frame pair {i-1}→{i} (frames {prev.FrameIndex}→{cur.FrameIndex})");
                    combinedTriggers.AddRange(deferredSeeDithers);
                }

                var nextDeferredSeeDithers = new List<TimedTrigger>();
                var isLastGap = i == ordered.Count - 1;
                // Check if NEXT frame (where we'd defer TO) is a different target
                var nextTargetName = (i < ordered.Count - 1) ? ordered[i + 1].TargetName?.Trim() ?? "" : "";
                var curTargetName = cur.TargetName?.Trim() ?? "";
                var wouldCrossTargetBoundary = !isLastGap && !string.Equals(curTargetName, nextTargetName, StringComparison.OrdinalIgnoreCase);

                foreach (var tr in inGap) {
                    if (tr.IsSeeDither && !isLastGap && !wouldCrossTargetBoundary) {
                        SeeDriftLog.Debug($"NinaLogCorrelator: Deferring SeeDither from frame pair {i-1}→{i} (frames {prev.FrameIndex}→{cur.FrameIndex}) to next pair");
                        nextDeferredSeeDithers.Add(tr);
                    } else {
                        combinedTriggers.Add(tr);
                    }
                }
                deferredSeeDithers = nextDeferredSeeDithers;
                if (combinedTriggers.Count == 0)
                    continue;

                var measuredTail = BuildMeasuredFrameToFrameLines(prev, cur);
                FrameToFrameStepAlongTrace(prev, cur, out var dHdrRa, out var dHdrDec);
                double? dPx = null;
                double? dPy = null;
                if (cur.IsPixelPath && prev.CumulativePixelX.HasValue && cur.CumulativePixelX.HasValue) {
                    dPx = cur.CumulativePixelX.Value - prev.CumulativePixelX.Value;
                    dPy = cur.CumulativePixelY!.Value - prev.CumulativePixelY!.Value;
                }
                var usedPulses = new HashSet<DitherPulse>();
                var markers = new List<SequencerEdgeMarker>();

                foreach (var tr in combinedTriggers) {
                    var eventLines = new List<string> { BetweenFramesHoverLine(prev, cur) };
                    DitherPulse? pulse = null;
                    if (tr.Kind == TriggerKind.Dither) {
                        var label = tr.IsSeeDither ? "SeeDither" : "DitherAfterExposures";
                        eventLines.Add($"{label} @ {tr.UtcTime.ToLocalTime():HH:mm:ss}");
                        if (tr.IsSeeDither) {
                            var seeLines = seeDitherEntries
                                .Where(e => e.UtcTime >= tr.UtcTime && e.UtcTime < upperBound && !usedSeeDitherEntries.Contains(e))
                                .OrderBy(e => e.UtcTime)
                                .ToList();
                            foreach (var entry in seeLines) {
                                var message = string.IsNullOrWhiteSpace(entry.Message)
                                    ? "[SeeDither] (log line)"
                                    : entry.Message;
                                eventLines.Add(message);
                                if (entry.DeltaRaArcSec.HasValue && entry.DeltaDecArcSec.HasValue) {
                                    eventLines.Add($"SeeDither commanded offset: ΔRA {entry.DeltaRaArcSec.Value:0.##}″, ΔDec {entry.DeltaDecArcSec.Value:0.##}″");
                                }
                                usedSeeDitherEntries.Add(entry);
                            }
                        } else {
                            pulse = pulses
                                .Where(p => p.UtcTime >= tr.UtcTime && p.UtcTime < upperBound && !usedPulses.Contains(p))
                                .OrderBy(p => p.UtcTime)
                                .FirstOrDefault();
                            if (pulse == null) {
                                pulse = pulses
                                    .Where(p => p.UtcTime > t0 && p.UtcTime < upperBound && !usedPulses.Contains(p))
                                    .OrderBy(p => p.UtcTime)
                                    .FirstOrDefault();
                            }
                            if (pulse != null) {
                                usedPulses.Add(pulse);
                                eventLines.Add(
                                    $"NINA dither target (guider): ({pulse.FromX:F2}, {pulse.FromY:F2}) → ({pulse.ToX:F2}, {pulse.ToY:F2}); move Δx {pulse.Dx:F2}, Δy {pulse.Dy:F2}");
                                if (pulse.GuideDurationFirstSec.HasValue && pulse.GuideDurationSecondSec.HasValue) {
                                    eventLines.Add(
                                        $"NINA dither guide durations (log): {pulse.GuideDurationFirstSec.Value:F2} s, {pulse.GuideDurationSecondSec.Value:F2} s");
                                }
                            }
                        }
                        eventLines.AddRange(measuredTail);
                        markers.Add(new SequencerEdgeMarker {
                            IsDither = true,
                            IsSeeDither = tr.IsSeeDither,
                            EventUtc = tr.UtcTime,
                            FromFrameIndex = prev.FrameIndex,
                            ToFrameIndex = cur.FrameIndex,
                            DeltaRaArcSec = dHdrRa,
                            DeltaDecArcSec = dHdrDec,
                            DeltaPixelX = dPx,
                            DeltaPixelY = dPy,
                            LoggedGuiderDx = pulse?.Dx,
                            LoggedGuiderDy = pulse?.Dy,
                            Tooltip = string.Join(Environment.NewLine, eventLines)
                        });
                    } else {
                        eventLines.Add($"CenterAfterDrift @ {tr.UtcTime.ToLocalTime():HH:mm:ss}");
                        var centerDrift = centerDriftLines
                            .Where(d => d.UtcTime >= tr.UtcTime && d.UtcTime < upperBound)
                            .OrderBy(d => d.UtcTime)
                            .LastOrDefault();
                        if (centerDrift == null) {
                            centerDrift = centerDriftLines
                                .Where(d => d.UtcTime > t0 && d.UtcTime < upperBound)
                                .OrderBy(d => d.UtcTime)
                                .LastOrDefault();
                        }
                        if (centerDrift != null) {
                            eventLines.Add(
                                $"NINA center drift (log): {centerDrift.DriftArcMinutes:F3}′ vs {centerDrift.ThresholdArcMinutes:F3}′ threshold");
                        }
                        eventLines.AddRange(measuredTail);
                        markers.Add(new SequencerEdgeMarker {
                            IsDither = false,
                            EventUtc = tr.UtcTime,
                            FromFrameIndex = prev.FrameIndex,
                            ToFrameIndex = cur.FrameIndex,
                            DeltaRaArcSec = dHdrRa,
                            DeltaDecArcSec = dHdrDec,
                            DeltaPixelX = dPx,
                            DeltaPixelY = dPy,
                            LoggedCenterDriftArcMin = centerDrift?.DriftArcMinutes,
                            LoggedCenterThresholdArcMin = centerDrift?.ThresholdArcMinutes,
                            Tooltip = string.Join(Environment.NewLine, eventLines)
                        });
                    }
                }

                cur.EdgeSequencerMarkers = markers;
            }
        }

        /// <summary>ΔRA/ΔDec step between consecutive frames (same geometry as the drift chart chord).</summary>
        private static void FrameToFrameStepAlongTrace(DriftSample prev, DriftSample cur, out double dRa, out double dDec) {
            dRa = cur.DeltaRaArcSec - prev.DeltaRaArcSec;
            dDec = cur.DeltaDecArcSec - prev.DeltaDecArcSec;
        }

        private static List<string> BuildMeasuredFrameToFrameLines(DriftSample prev, DriftSample cur) {
            var lines = new List<string>();
            FrameToFrameStepAlongTrace(prev, cur, out var dHdrRa, out var dHdrDec);
            lines.Add($"Measured header Δ: ΔRA {dHdrRa:F2}″, ΔDec {dHdrDec:F2}″ (frame-to-frame)");
            if (cur.HasPixelDerivedRaDec && prev.HasPixelDerivedRaDec) {
                var dRa = cur.PixelDerivedRaArcSec!.Value - prev.PixelDerivedRaArcSec!.Value;
                var dDec = cur.PixelDerivedDecArcSec!.Value - prev.PixelDerivedDecArcSec!.Value;
                lines.Add($"Measured derived Δ: ΔRA {dRa:F2}″, ΔDec {dDec:F2}″ (frame-to-frame)");
            }
            if (cur.IsPixelPath && prev.CumulativePixelX.HasValue && cur.CumulativePixelX.HasValue) {
                var dpx = cur.CumulativePixelX.Value - prev.CumulativePixelX.Value;
                var dpy = cur.CumulativePixelY!.Value - prev.CumulativePixelY!.Value;
                lines.Add($"Measured cumulative-pixel step: Δx {dpx:F2} px, Δy {dpy:F2} px");
            }
            return lines;
        }

        private static bool TryGetLogSaveUtc(DriftSample s, Dictionary<string, DateTime> saveByFileName, out DateTime utc) {
            utc = default;
            var name = s.FileName?.Trim();
            if (string.IsNullOrEmpty(name))
                return false;
            return saveByFileName.TryGetValue(name, out utc);
        }

        private static bool LoadAndParseSessionLogs(
                DateTime sessionDate,
                List<TimedTrigger> triggers,
                List<DitherPulse> pulses,
                List<CenterDriftLogLine> centerDriftLines,
                List<ImageSaveEvent> imageSaves,
                List<SeeDitherLogEntry> seeDitherEntries,
                List<TargetSchedulerStartEvent> schedulerStarts) {
            if (!Directory.Exists(LogFolder))
                return false;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var logFound = false;

            for (var offset = -1; offset <= 1; offset++) {
                foreach (var path in FindAllLogFilesForDate(sessionDate.AddDays(offset))) {
                    if (!seen.Add(path)) continue;
                    logFound = true;
                    ParseLogLines(path, triggers, pulses, centerDriftLines, imageSaves, seeDitherEntries, schedulerStarts);
                }
            }

            return logFound;
        }

        /// <summary>All .log paths matching any date pattern (not only files[0]).</summary>
        private static IEnumerable<string> FindAllLogFilesForDate(DateTime date) {
            if (!Directory.Exists(LogFolder)) yield break;

            var patterns = new[] {
                $"*{date:yyyy-MM-dd}*",
                $"*{date:yyyyMMdd}*",
                $"*{date:MM-dd-yyyy}*",
            };
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in patterns) {
                foreach (var f in Directory.GetFiles(LogFolder, p, SearchOption.TopDirectoryOnly)) {
                    if (seen.Add(f))
                        yield return f;
                }
            }
        }

        private static void ParseLogLines(
                string path,
                List<TimedTrigger> triggers,
                List<DitherPulse> pulses,
                List<CenterDriftLogLine> centerDriftLines,
                List<ImageSaveEvent> imageSaves,
                List<SeeDitherLogEntry> seeDitherEntries,
                List<TargetSchedulerStartEvent> schedulerStarts) {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs);
            string? line;
            while ((line = sr.ReadLine()) != null) {
                if (!TryParseLogLineUtc(line, out var ts))
                    continue;

                if (TryParseTargetSchedulerNewTarget(line, ts, out var schedEv)) {
                    schedulerStarts.Add(schedEv);
                    continue;
                }

                if (TryExtractSavedImagePath(line, out var rawPath)
                    && !string.IsNullOrEmpty(rawPath)
                    && !ShouldIgnoreSavePath(rawPath)) {
                    imageSaves.Add(new ImageSaveEvent { UtcTime = ts, FilePath = rawPath });
                    continue;
                }

                if (RxTriggerCenter.IsMatch(line)) {
                    triggers.Add(new TimedTrigger {
                        UtcTime = ts,
                        Kind = TriggerKind.Center,
                        Label = "Center after drift",
                        AfterExposuresFromLog = TryParseAfterExposures(line)
                    });
                    continue;
                }
                var isSeeDitherTrigger = RxSeeDitherTrigger.IsMatch(line);
                if (RxTriggerDither.IsMatch(line) || isSeeDitherTrigger) {
                    triggers.Add(new TimedTrigger {
                        UtcTime = ts,
                        Kind = TriggerKind.Dither,
                        Label = isSeeDitherTrigger ? "SeeDither (after exposures)" : "Dither (after exposures)",
                        AfterExposuresFromLog = TryParseAfterExposures(line),
                        IsSeeDither = isSeeDitherTrigger
                    });
                    continue;
                }

                var seeMsgMatch = RxSeeDitherLogLine.Match(line);
                if (seeMsgMatch.Success) {
                    double? deltaRa = null;
                    double? deltaDec = null;
                    var deltaMatch = RxSeeDitherDelta.Match(line);
                    if (deltaMatch.Success
                        && double.TryParse(deltaMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var dRa)
                        && double.TryParse(deltaMatch.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var dDec)) {
                        deltaRa = dRa;
                        deltaDec = dDec;
                    }

                    seeDitherEntries.Add(new SeeDitherLogEntry {
                        UtcTime = ts,
                        Message = seeMsgMatch.Groups[1].Value.Trim(),
                        DeltaRaArcSec = deltaRa,
                        DeltaDecArcSec = deltaDec
                    });
                    continue;
                }

                var mPulse = RxDitherPulse.Match(line);
                if (mPulse.Success
                    && double.TryParse(mPulse.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var x0)
                    && double.TryParse(mPulse.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var y0)
                    && double.TryParse(mPulse.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var x1)
                    && double.TryParse(mPulse.Groups[4].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var y1)) {
                    double? d0 = null;
                    double? d1 = null;
                    if (mPulse.Groups[5].Success && mPulse.Groups[6].Success
                        && double.TryParse(mPulse.Groups[5].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var gd0)
                        && double.TryParse(mPulse.Groups[6].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var gd1)) {
                        d0 = gd0;
                        d1 = gd1;
                    }
                    pulses.Add(new DitherPulse {
                        UtcTime = ts,
                        FromX = x0,
                        FromY = y0,
                        ToX = x1,
                        ToY = y1,
                        Dx = x1 - x0,
                        Dy = y1 - y0,
                        GuideDurationFirstSec = d0,
                        GuideDurationSecondSec = d1
                    });
                    continue;
                }

                var mCenterDrift = RxCenterDriftArcMin.Match(line);
                if (mCenterDrift.Success
                    && double.TryParse(mCenterDrift.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var driftAm)
                    && double.TryParse(mCenterDrift.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var threshAm)) {
                    centerDriftLines.Add(new CenterDriftLogLine {
                        UtcTime = ts,
                        DriftArcMinutes = driftAm,
                        ThresholdArcMinutes = threshAm
                    });
                }
            }
        }

        private static bool TryParseTargetSchedulerNewTarget(string line, DateTime utc, out TargetSchedulerStartEvent ev) {
            ev = null!;
            if (!RxTargetSchedulerNewTarget.IsMatch(line))
                return false;

            string? targetLabel = null;
            var mName = RxTargetLabelInSchedulerLine.Match(line);
            if (mName.Success)
                targetLabel = mName.Groups[1].Value.Trim();

            ev = new TargetSchedulerStartEvent {
                UtcTime = utc,
                TargetLabel = string.IsNullOrWhiteSpace(targetLabel) ? null : targetLabel
            };
            return true;
        }

        /// <summary>NINA file logs are usually local wall time without <c>Z</c>; FITS <c>DATE-OBS</c> may be UT — pairing uses interval logic within a session.</summary>
        private static bool TryParseTimestamp(string line, out DateTime utc) {
            utc = default;
            foreach (var rx in _tsPatterns) {
                var m = rx.Match(line);
                if (!m.Success) continue;

                var raw = m.Groups[1].Value;
                if (DateTime.TryParse(raw,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind,
                        out var dtRoundtrip)) {
                    if (dtRoundtrip.Kind == DateTimeKind.Utc) {
                        utc = dtRoundtrip;
                        return true;
                    }

                    if (dtRoundtrip.Kind == DateTimeKind.Local) {
                        utc = dtRoundtrip.ToUniversalTime();
                        return true;
                    }

                    if (dtRoundtrip.Kind == DateTimeKind.Unspecified) {
                        utc = DateTime.SpecifyKind(dtRoundtrip, DateTimeKind.Local).ToUniversalTime();
                        return true;
                    }
                }

                if (DateTimeOffset.TryParse(raw,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeLocal | DateTimeStyles.AdjustToUniversal,
                        out var dto)) {
                    utc = dto.UtcDateTime;
                    return true;
                }

                if (DateTime.TryParse(raw,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeLocal | DateTimeStyles.AdjustToUniversal,
                        out utc))
                    return true;
            }
            return false;
        }

        /// <summary>Line start patterns first; then first ISO-like timestamp anywhere (newer NINA layouts).</summary>
        private static bool TryParseLogLineUtc(string line, out DateTime utc) {
            if (TryParseTimestamp(line, out utc))
                return true;
            utc = default;
            var m = RxTimestampEmbedded.Match(line);
            if (!m.Success)
                return false;
            var raw = m.Value;
            if (DateTimeOffset.TryParse(raw,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal | DateTimeStyles.AdjustToUniversal,
                    out var dto)) {
                utc = dto.UtcDateTime;
                return true;
            }
            if (DateTime.TryParse(raw,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var dtRoundtrip) && dtRoundtrip.Kind == DateTimeKind.Utc) {
                utc = dtRoundtrip;
                return true;
            }
            return DateTime.TryParse(raw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal | DateTimeStyles.AdjustToUniversal,
                out utc);
        }

        private static bool TryExtractSavedImagePath(string line, out string rawPath) {
            rawPath = "";
            var trimmed = line.TrimEnd();
            foreach (var rx in RxSavedImagePatterns) {
                var m = rx.Match(trimmed);
                if (!m.Success)
                    continue;
                rawPath = m.Groups[1].Value.Trim().Trim('\'', '"');
                var pipeIdx = rawPath.IndexOf('|');
                if (pipeIdx > 0)
                    rawPath = rawPath.Substring(0, pipeIdx).TrimEnd();
                if (!string.IsNullOrEmpty(rawPath))
                    return true;
            }
            rawPath = "";
            return false;
        }
    }
}
