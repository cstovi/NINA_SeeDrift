using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NINA.Core.Utility;
using NINA.Plugin.SeeDrift.Models;

namespace NINA.Plugin.SeeDrift.Utility {

    /// <summary>
    /// Correlates drift samples with NINA 3.x log lines. **Primary** sources are sequencer
    /// <c>Starting Trigger:</c> lines (intent). Broad patterns (plate solve, per-frame drift metrics)
    /// are not used for matching so the nearest event is not wrong noise.
    /// </summary>
    internal static class NinaLogCorrelator {

        /// <summary>
        /// Max |Δt| between a FITS exposure start and a log event. Large enough for long subs +
        /// delay before NINA logs the trigger after the last exposure of a group.
        /// </summary>
        private static readonly TimeSpan TriggerMatchWindow = TimeSpan.FromMinutes(45);

        private static readonly string LogFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NINA", "Logs");

        /// <summary>Center-after-drift trigger fired (NINA sequencer).</summary>
        private static readonly Regex RxTriggerCenter = new Regex(
            @"Starting\s+Trigger:.*CenterAfterDrift", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Dither-after-exposures trigger fired (NINA sequencer).</summary>
        private static readonly Regex RxTriggerDither = new Regex(
            @"Starting\s+Trigger:.*DitherAfterExposures", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // NINA 3.x log line timestamp — prefix before first | or INFO field.
        private static readonly Regex[] _tsPatterns = {
            new Regex(@"^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})", RegexOptions.Compiled),
            new Regex(@"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})", RegexOptions.Compiled),
            new Regex(@"^\[?(\d{2}/\d{2}/\d{4} \d{2}:\d{2}:\d{2})\]?", RegexOptions.Compiled),
        };

        /// <summary>
        /// Loads sequencer triggers from logs; sets <see cref="DriftSample.SequencerLogHint"/> on every
        /// frame with a nearby trigger; augments <see cref="DriftSample.JumpReason"/> on jumps when matched.
        /// Returns <c>(matched jump count, logFound)</c>.
        /// </summary>
        public static (int Matched, bool LogFound) AnnotateWithLogEvents(List<DriftSample> samples) {
            if (samples.Count == 0) return (0, false);
            try {
                foreach (var s in samples)
                    s.SequencerLogHint = null;

                var sessionDate = samples[0].ExposureStartUtc.Date;
                Logger.Debug($"SeeDrift: log correlator — sessionDate={sessionDate:yyyy-MM-dd}, first sample UTC={samples[0].ExposureStartUtc:o}");

                var triggers = new List<LogEvent>();
                if (!LoadSequencerTriggers(sessionDate, triggers))
                    return (0, false);

                Logger.Debug($"SeeDrift: log correlator — sequencer triggers={triggers.Count}");
                if (triggers.Count == 0) {
                    foreach (var s in samples)
                        s.SequencerLogHint = null;
                    return (0, true);
                }

                var matchedJumps = 0;
                var firstJump = samples.FirstOrDefault(s => s.IsJump);
                if (firstJump != null) {
                    var nearest = triggers.OrderBy(e => (e.UtcTime - firstJump!.ExposureStartUtc).Duration()).First();
                    var gap = (nearest.UtcTime - firstJump.ExposureStartUtc).Duration();
                    Logger.Debug(
                        $"SeeDrift: log correlator — first jump UTC={firstJump.ExposureStartUtc:o}, nearest trigger '{nearest.Label}' UTC={nearest.UtcTime:o}, gap={gap.TotalSeconds:F0}s (window={TriggerMatchWindow.TotalMinutes:F0} min)");
                }

                foreach (var sample in samples.OrderBy(s => s.FrameIndex)) {
                    var n = FindNearestWithinWindow(triggers, sample.ExposureStartUtc, TriggerMatchWindow);
                    sample.SequencerLogHint = n == null
                        ? null
                        : $"{n.Label} @ {n.UtcTime.ToLocalTime():HH:mm:ss}";

                    if (!sample.IsJump || n == null)
                        continue;

                    var note = $"→ {n.Label} @ {n.UtcTime.ToLocalTime():HH:mm:ss}";
                    sample.JumpReason = string.IsNullOrEmpty(sample.JumpReason)
                        ? note
                        : $"{sample.JumpReason} {note}";
                    matchedJumps++;
                }

                Logger.Debug($"SeeDrift: log correlator — matched jumps={matchedJumps}");
                return (matchedJumps, true);
            } catch (Exception ex) {
                Logger.Debug($"SeeDrift: log correlation skipped: {ex.Message}");
                return (0, false);
            }
        }

        private sealed class LogEvent {
            public DateTime UtcTime { get; set; }
            public string Label { get; set; } = "";
        }

        /// <summary>Smallest gap within window; null if none within maxGap (inclusive boundary).</summary>
        private static LogEvent? FindNearestWithinWindow(
                IReadOnlyList<LogEvent> events,
                DateTime sampleUtc,
                TimeSpan maxGap) {
            LogEvent? best = null;
            var bestGap = TimeSpan.MaxValue;
            foreach (var ev in events) {
                var gap = (ev.UtcTime - sampleUtc).Duration();
                if (gap > maxGap)
                    continue;
                if (gap < bestGap) {
                    bestGap = gap;
                    best = ev;
                }
            }
            return best;
        }

        /// <returns>False if NINA log folder missing or no log file read.</returns>
        private static bool LoadSequencerTriggers(DateTime sessionDate, List<LogEvent> triggers) {
            if (!Directory.Exists(LogFolder))
                return false;

            var logFound = false;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var offset = -1; offset <= 1; offset++) {
                var candidate = FindLogFile(sessionDate.AddDays(offset));
                if (candidate == null || !seen.Add(candidate)) continue;
                logFound = true;
                ParseSequencerTriggers(candidate, triggers);
            }

            triggers.Sort((a, b) => a.UtcTime.CompareTo(b.UtcTime));
            return logFound;
        }

        private static string? FindLogFile(DateTime date) {
            if (!Directory.Exists(LogFolder)) return null;

            var patterns = new[] {
                $"*{date:yyyy-MM-dd}*",
                $"*{date:yyyyMMdd}*",
                $"*{date:MM-dd-yyyy}*",
            };

            foreach (var p in patterns) {
                var files = Directory.GetFiles(LogFolder, p, SearchOption.TopDirectoryOnly);
                if (files.Length > 0) return files[0];
            }

            var allLogs = Directory.GetFiles(LogFolder, "*.log", SearchOption.TopDirectoryOnly);
            if (allLogs.Length == 0) return null;
            Array.Sort(allLogs, (a, b) => File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));
            return allLogs[0];
        }

        private static void ParseSequencerTriggers(string path, List<LogEvent> triggers) {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs);
            string? line;
            while ((line = sr.ReadLine()) != null) {
                if (!TryParseTimestamp(line, out var ts))
                    continue;

                if (RxTriggerCenter.IsMatch(line)) {
                    triggers.Add(new LogEvent { UtcTime = ts, Label = "Center after drift" });
                    continue;
                }
                if (RxTriggerDither.IsMatch(line))
                    triggers.Add(new LogEvent { UtcTime = ts, Label = "Dither (after exposures)" });
            }
        }

        private static bool TryParseTimestamp(string line, out DateTime utc) {
            utc = default;
            foreach (var rx in _tsPatterns) {
                var m = rx.Match(line);
                if (!m.Success) continue;

                if (DateTime.TryParse(m.Groups[1].Value,
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.RoundtripKind,
                        out var dtRoundtrip) && dtRoundtrip.Kind == DateTimeKind.Utc) {
                    utc = dtRoundtrip;
                    return true;
                }

                if (DateTime.TryParse(m.Groups[1].Value,
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AssumeLocal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                        out utc))
                    return true;
            }
            return false;
        }
    }
}
