using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NINA.Core.Utility;
using NINA.Plugin.SeeDrift.Models;

namespace NINA.Plugin.SeeDrift.Utility {

    /// <summary>
    /// Best-effort correlator: scans the nearest NINA log file for dither / slew / center /
    /// flip / plate-solve events and appends those events to the JumpReason of any jump sample
    /// whose timestamp is within a configurable window.
    /// </summary>
    internal static class NinaLogCorrelator {

        /// <summary>
        /// Maximum gap between a FITS frame timestamp and a log event to consider them correlated.
        /// Jumps use exposure start time; sequencer triggers may log up to roughly one exposure later.
        /// </summary>
        private static readonly TimeSpan MatchWindow = TimeSpan.FromSeconds(300);

        private static readonly string LogFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NINA", "Logs");

        // Patterns: first match wins per line. Put sequence *triggers* (intent) before generic guider text.
        // NINA 3.2 uses names like CenterAfterDriftTrigger — not "center" + sep + "after".
        private static readonly (Regex Pattern, string Label)[] _eventPatterns = {
            (new Regex(@"Starting\s+Trigger:.*CenterAfterDrift", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Center after drift"),
            (new Regex(@"Starting\s+Trigger:.*DitherAfterExposures", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Dither (after exposures)"),
            (new Regex(@"centerafterdrift", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Center after drift"),
            (new Regex(@"ditherafterexposures", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Dither (after exposures)"),
            (new Regex(@"meridian.?flip", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Meridian flip"),
            (new Regex(@"dither", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Dither"),
            (new Regex(@"plate.?solv", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Plate solve"),
            (new Regex(@"slewto|slew\.to|\bslewing\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Slew"),
        };

        // NINA 3.x log line timestamp patterns — try several common NLog layouts.
        private static readonly Regex[] _tsPatterns = {
            new Regex(@"^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})", RegexOptions.Compiled),
            new Regex(@"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})", RegexOptions.Compiled),
            new Regex(@"^\[?(\d{2}/\d{2}/\d{4} \d{2}:\d{2}:\d{2})\]?", RegexOptions.Compiled),
        };

        /// <summary>
        /// Tries to correlate jump samples with events in the NINA log for the session date.
        /// Returns <c>(matched, logFound)</c> — <c>logFound</c> is true even if zero jumps
        /// were within the match window, as long as the log file itself was readable.
        /// </summary>
        public static (int Matched, bool LogFound) AnnotateWithLogEvents(List<DriftSample> samples) {
            if (samples.Count == 0) return (0, false);
            try {
                var sessionDate = samples[0].ExposureStartUtc.Date;
                Logger.Debug($"SeeDrift: log correlator — sessionDate={sessionDate:yyyy-MM-dd}, first sample UTC={samples[0].ExposureStartUtc:o}");

                var (events, logFound) = LoadEvents(sessionDate);
                Logger.Debug($"SeeDrift: log correlator — logFound={logFound}, events loaded={events.Count}");
                if (!logFound) return (0, false);
                if (events.Count == 0) return (0, true);

                var jumpSamples = samples.Where(s => s.IsJump).ToList();
                Logger.Debug($"SeeDrift: log correlator — jump samples to match={jumpSamples.Count}, matchWindow={MatchWindow.TotalSeconds}s");
                if (jumpSamples.Count > 0) {
                    // Log the closest gap for the first jump so we can diagnose window issues.
                    var firstJump = jumpSamples[0];
                    var nearest = events.OrderBy(e => (e.UtcTime - firstJump.ExposureStartUtc).Duration()).First();
                    Logger.Debug($"SeeDrift: log correlator — first jump UTC={firstJump.ExposureStartUtc:o}, nearest event '{nearest.Label}' UTC={nearest.UtcTime:o}, gap={( nearest.UtcTime - firstJump.ExposureStartUtc).Duration().TotalSeconds:F0}s");
                }

                var matched = 0;
                foreach (var sample in samples) {
                    if (!sample.IsJump) continue;
                    var closest = FindClosestEvent(events, sample.ExposureStartUtc);
                    if (closest == null) continue;

                    sample.JumpReason = string.IsNullOrEmpty(sample.JumpReason)
                        ? $"→ {closest.Label} @ {closest.UtcTime:HH:mm:ss}"
                        : $"{sample.JumpReason} → {closest.Label} @ {closest.UtcTime:HH:mm:ss}";
                    matched++;
                }
                Logger.Debug($"SeeDrift: log correlator — matched={matched}");
                return (matched, true);
            } catch (Exception ex) {
                Logger.Debug($"SeeDrift: log correlation skipped: {ex.Message}");
                return (0, false);
            }
        }

        // -------------------------------------------------------------------

        private sealed class LogEvent {
            public DateTime UtcTime { get; set; }
            public string Label { get; set; } = "";
        }

        private static LogEvent? FindClosestEvent(List<LogEvent> events, DateTime sampleUtc) {
            LogEvent? best = null;
            var bestGap = MatchWindow;
            foreach (var ev in events) {
                var gap = (ev.UtcTime - sampleUtc).Duration();
                if (gap < bestGap) { bestGap = gap; best = ev; }
            }
            return best;
        }

        private static (List<LogEvent> Events, bool LogFound) LoadEvents(DateTime sessionDate) {
            var events = new List<LogEvent>();
            if (!Directory.Exists(LogFolder)) return (events, false);

            var logFound = false;
            var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Try ±1 day to handle evening sessions that roll past midnight.
            for (var offset = -1; offset <= 1; offset++) {
                var candidate = FindLogFile(sessionDate.AddDays(offset));
                if (candidate == null || !seen.Add(candidate)) continue;
                logFound = true;
                ParseLog(candidate, events);
            }

            return (events, logFound);
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

            // Fallback: newest .log file in the folder
            var allLogs = Directory.GetFiles(LogFolder, "*.log", SearchOption.TopDirectoryOnly);
            if (allLogs.Length == 0) return null;
            Array.Sort(allLogs, (a, b) => File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));
            return allLogs[0];
        }

        private static void ParseLog(string path, List<LogEvent> events) {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs);
            string? line;
            while ((line = sr.ReadLine()) != null) {
                if (!TryParseTimestamp(line, out var ts)) continue;
                foreach (var (pattern, label) in _eventPatterns) {
                    if (pattern.IsMatch(line)) {
                        events.Add(new LogEvent { UtcTime = ts, Label = label });
                        break; // only one label per line
                    }
                }
            }
        }

        private static bool TryParseTimestamp(string line, out DateTime utc) {
            utc = default;
            foreach (var rx in _tsPatterns) {
                var m = rx.Match(line);
                if (!m.Success) continue;

                // If the string carries an explicit UTC marker (Z / +00:00) honour it.
                if (DateTime.TryParse(m.Groups[1].Value,
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.RoundtripKind,
                        out var dtRoundtrip) && dtRoundtrip.Kind == DateTimeKind.Utc) {
                    utc = dtRoundtrip;
                    return true;
                }

                // NINA logs local time (no UTC suffix) — treat as local and convert to UTC.
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
