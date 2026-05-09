using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NINA.Core.Utility;
using NINA.Plugin.SeeDrift.Models;

namespace NINA.Plugin.SeeDrift.Utility {

    /// <summary>
    /// Correlates drift samples with NINA 3.x log lines: sequencer
    /// <c>Starting Trigger:</c>, <c>DirectGuider</c> dither pulses, and inter-frame intervals.
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

        /// <summary>NINA guider commanded dither step (guider coordinate units).</summary>
        private static readonly Regex RxDitherPulse = new Regex(
            @"DirectGuider\.cs\|SelectDitherPulse\|.*\|Dither target from \(([-0-9.eE]+),([-0-9.eE]+)\) to \(([-0-9.eE]+),([-0-9.eE]+)\)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // NINA 3.x log line timestamp — prefix before first | or INFO field.
        private static readonly Regex[] _tsPatterns = {
            new Regex(@"^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})", RegexOptions.Compiled),
            new Regex(@"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})", RegexOptions.Compiled),
            new Regex(@"^\[?(\d{2}/\d{2}/\d{4} \d{2}:\d{2}:\d{2})\]?", RegexOptions.Compiled),
        };

        private enum TriggerKind {
            Center,
            Dither
        }

        private sealed class TimedTrigger {
            public DateTime UtcTime { get; set; }
            public TriggerKind Kind { get; set; }
            public string Label { get; set; } = "";
        }

        private sealed class DitherPulse {
            public DateTime UtcTime { get; set; }
            public double Dx { get; set; }
            public double Dy { get; set; }
        }

        /// <summary>
        /// Loads sequencer triggers and dither pulses; sets hints and inter-frame edge fields.
        /// Returns matched jump count (including interval match to next frame), log found, trigger count, edge marker count.
        /// </summary>
        public static (int MatchedJumps, bool LogFound, int TriggersLoaded, int SequencerEdges) AnnotateWithLogEvents(
                List<DriftSample> samples) {
            if (samples.Count == 0) return (0, false, 0, 0);
            try {
                foreach (var s in samples) {
                    s.SequencerLogHint = null;
                    s.EdgeHadDitherTrigger = false;
                    s.EdgeHadCenterTrigger = false;
                    s.EdgeDitherClaimedDx = null;
                    s.EdgeDitherClaimedDy = null;
                    s.EdgeSequencerHover = null;
                }

                var sessionDate = samples[0].ExposureStartUtc.Date;
                Logger.Debug($"SeeDrift: log correlator — sessionDate={sessionDate:yyyy-MM-dd}, first sample UTC={samples[0].ExposureStartUtc:o}");

                var triggers = new List<TimedTrigger>();
                var pulses = new List<DitherPulse>();
                if (!LoadAndParseSessionLogs(sessionDate, triggers, pulses))
                    return (0, false, 0, 0);

                triggers.Sort((a, b) => a.UtcTime.CompareTo(b.UtcTime));
                pulses.Sort((a, b) => a.UtcTime.CompareTo(b.UtcTime));

                Logger.Debug($"SeeDrift: log correlator — triggers={triggers.Count}, dither pulses={pulses.Count}");
                AssignInterFrameEdges(samples, triggers, pulses);

                var logEventsForHints = triggers.Select(t => new LogEventLite { UtcTime = t.UtcTime, Label = t.Label }).ToList();
                if (triggers.Count == 0) {
                    return (0, true, 0, CountSequencerEdges(samples));
                }

                var firstJump = samples.OrderBy(s => s.FrameIndex).FirstOrDefault(s => s.IsJump);
                if (firstJump != null) {
                    var nearest = triggers.OrderBy(e => (e.UtcTime - firstJump!.ExposureStartUtc).Duration()).First();
                    var gap = (nearest.UtcTime - firstJump.ExposureStartUtc).Duration();
                    Logger.Debug(
                        $"SeeDrift: log correlator — first jump UTC={firstJump.ExposureStartUtc:o}, nearest trigger '{nearest.Label}' UTC={nearest.UtcTime:o}, gap={gap.TotalSeconds:F0}s (window={TriggerMatchWindow.TotalMinutes:F0} min)");
                }

                foreach (var sample in samples.OrderBy(s => s.FrameIndex)) {
                    var n = FindNearestWithinWindow(logEventsForHints, sample.ExposureStartUtc, TriggerMatchWindow);
                    sample.SequencerLogHint = n == null
                        ? null
                        : $"{n.Label} @ {n.UtcTime.ToLocalTime():HH:mm:ss}";
                }

                var matchedJumps = 0;
                foreach (var sample in samples.OrderBy(s => s.FrameIndex)) {
                    if (!sample.IsJump)
                        continue;

                    var matched = false;
                    var n = FindNearestWithinWindow(logEventsForHints, sample.ExposureStartUtc, TriggerMatchWindow);
                    if (n != null) {
                        var note = $"→ {n.Label} @ {n.UtcTime.ToLocalTime():HH:mm:ss}";
                        sample.JumpReason = string.IsNullOrEmpty(sample.JumpReason)
                            ? note
                            : $"{sample.JumpReason} {note}";
                        matched = true;
                    }

                    var next = samples.FirstOrDefault(x => x.FrameIndex == sample.FrameIndex + 1);
                    if (next != null && (next.EdgeHadDitherTrigger || next.EdgeHadCenterTrigger)) {
                        if (!matched) {
                            var bits = new List<string>();
                            if (next.EdgeHadDitherTrigger) bits.Add("dither interval");
                            if (next.EdgeHadCenterTrigger) bits.Add("center interval");
                            var note = $"→ log: {string.Join(", ", bits)} (frames {sample.FrameIndex + 1}→{next.FrameIndex + 1})";
                            sample.JumpReason = string.IsNullOrEmpty(sample.JumpReason)
                                ? note
                                : $"{sample.JumpReason} {note}";
                        }
                        matched = true;
                    }

                    if (matched)
                        matchedJumps++;
                }

                Logger.Debug($"SeeDrift: log correlator — matched jumps={matchedJumps}");
                return (matchedJumps, true, triggers.Count, CountSequencerEdges(samples));
            } catch (Exception ex) {
                Logger.Debug($"SeeDrift: log correlation skipped: {ex.Message}");
                return (0, false, 0, 0);
            }
        }

        private sealed class LogEventLite {
            public DateTime UtcTime { get; set; }
            public string Label { get; set; } = "";
        }

        private static int CountSequencerEdges(List<DriftSample> samples) =>
            samples.Count(s => s.EdgeHadDitherTrigger || s.EdgeHadCenterTrigger);

        private static void AssignInterFrameEdges(
                List<DriftSample> samples,
                List<TimedTrigger> triggers,
                List<DitherPulse> pulses) {

            var ordered = samples.OrderBy(s => s.FrameIndex).ToList();
            for (var i = 1; i < ordered.Count; i++) {
                var prev = ordered[i - 1];
                var cur = ordered[i];
                var t0 = prev.ExposureStartUtc;
                var t1 = cur.ExposureStartUtc;
                if (t1 <= t0)
                    continue;

                DateTime? ditherTriggerUtc = null;
                DateTime? centerTriggerUtc = null;
                foreach (var tr in triggers.Where(t => t.UtcTime > t0 && t.UtcTime < t1).OrderBy(t => t.UtcTime)) {
                    if (tr.Kind == TriggerKind.Dither) {
                        cur.EdgeHadDitherTrigger = true;
                        ditherTriggerUtc ??= tr.UtcTime;
                    }
                    if (tr.Kind == TriggerKind.Center) {
                        cur.EdgeHadCenterTrigger = true;
                        centerTriggerUtc ??= tr.UtcTime;
                    }
                }

                DitherPulse? pulse = null;
                if (cur.EdgeHadDitherTrigger && ditherTriggerUtc.HasValue) {
                    pulse = pulses
                        .Where(p => p.UtcTime >= ditherTriggerUtc.Value && p.UtcTime < t1)
                        .OrderBy(p => p.UtcTime)
                        .FirstOrDefault();
                }
                if (pulse == null && cur.EdgeHadDitherTrigger) {
                    pulse = pulses
                        .Where(p => p.UtcTime > t0 && p.UtcTime < t1)
                        .OrderBy(p => p.UtcTime)
                        .FirstOrDefault();
                }
                if (pulse != null) {
                    cur.EdgeDitherClaimedDx = pulse.Dx;
                    cur.EdgeDitherClaimedDy = pulse.Dy;
                }

                var lines = new List<string> {
                    $"Between frames {prev.FrameIndex + 1} → {cur.FrameIndex + 1}"
                };
                if (cur.EdgeHadDitherTrigger)
                    lines.Add(ditherTriggerUtc.HasValue
                        ? $"DitherAfterExposures @ {ditherTriggerUtc.Value.ToLocalTime():HH:mm:ss}"
                        : "DitherAfterExposures");
                if (cur.EdgeHadCenterTrigger)
                    lines.Add(centerTriggerUtc.HasValue
                        ? $"CenterAfterDrift @ {centerTriggerUtc.Value.ToLocalTime():HH:mm:ss}"
                        : "CenterAfterDrift");
                if (cur.EdgeDitherClaimedDx.HasValue && cur.EdgeDitherClaimedDy.HasValue) {
                    lines.Add(
                        $"NINA guider Δ: ({cur.EdgeDitherClaimedDx.Value:F1}, {cur.EdgeDitherClaimedDy.Value:F1}) [guider units]");
                }
                var dHdrRa = cur.DeltaRaArcSec - prev.DeltaRaArcSec;
                var dHdrDec = cur.DeltaDecArcSec - prev.DeltaDecArcSec;
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

                if (cur.EdgeHadDitherTrigger || cur.EdgeHadCenterTrigger)
                    cur.EdgeSequencerHover = string.Join(Environment.NewLine, lines);
            }
        }

        private static LogEventLite? FindNearestWithinWindow(
                IReadOnlyList<LogEventLite> events,
                DateTime sampleUtc,
                TimeSpan maxGap) {
            LogEventLite? best = null;
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

        private static bool LoadAndParseSessionLogs(DateTime sessionDate, List<TimedTrigger> triggers, List<DitherPulse> pulses) {
            if (!Directory.Exists(LogFolder))
                return false;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var logFound = false;

            for (var offset = -1; offset <= 1; offset++) {
                foreach (var path in FindAllLogFilesForDate(sessionDate.AddDays(offset))) {
                    if (!seen.Add(path)) continue;
                    logFound = true;
                    ParseLogLines(path, triggers, pulses);
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

        private static void ParseLogLines(string path, List<TimedTrigger> triggers, List<DitherPulse> pulses) {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs);
            string? line;
            while ((line = sr.ReadLine()) != null) {
                if (!TryParseTimestamp(line, out var ts))
                    continue;

                if (RxTriggerCenter.IsMatch(line)) {
                    triggers.Add(new TimedTrigger {
                        UtcTime = ts,
                        Kind = TriggerKind.Center,
                        Label = "Center after drift"
                    });
                    continue;
                }
                if (RxTriggerDither.IsMatch(line)) {
                    triggers.Add(new TimedTrigger {
                        UtcTime = ts,
                        Kind = TriggerKind.Dither,
                        Label = "Dither (after exposures)"
                    });
                    continue;
                }

                var mPulse = RxDitherPulse.Match(line);
                if (mPulse.Success
                    && double.TryParse(mPulse.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var x0)
                    && double.TryParse(mPulse.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var y0)
                    && double.TryParse(mPulse.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var x1)
                    && double.TryParse(mPulse.Groups[4].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var y1)) {
                    pulses.Add(new DitherPulse {
                        UtcTime = ts,
                        Dx = x1 - x0,
                        Dy = y1 - y0
                    });
                }
            }
        }

        /// <summary>NINA file logs are usually local wall time without <c>Z</c>; FITS <c>DATE-OBS</c> may be UT — pairing uses interval logic within a session.</summary>
        private static bool TryParseTimestamp(string line, out DateTime utc) {
            utc = default;
            foreach (var rx in _tsPatterns) {
                var m = rx.Match(line);
                if (!m.Success) continue;

                if (DateTime.TryParse(m.Groups[1].Value,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind,
                        out var dtRoundtrip) && dtRoundtrip.Kind == DateTimeKind.Utc) {
                    utc = dtRoundtrip;
                    return true;
                }

                if (DateTime.TryParse(m.Groups[1].Value,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeLocal | DateTimeStyles.AdjustToUniversal,
                        out utc))
                    return true;
            }
            return false;
        }
    }
}
