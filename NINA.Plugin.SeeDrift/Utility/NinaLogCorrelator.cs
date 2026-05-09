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

        private static readonly string LogFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NINA", "Logs");

        /// <summary>Center-after-drift trigger fired (NINA sequencer).</summary>
        private static readonly Regex RxTriggerCenter = new Regex(
            @"Starting\s+Trigger:.*CenterAfterDrift", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Dither-after-exposures trigger fired (NINA sequencer).</summary>
        private static readonly Regex RxTriggerDither = new Regex(
            @"Starting\s+Trigger:.*DitherAfterExposures", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>NINA guider commanded dither: full line includes from→to and optional guide durations (seconds).</summary>
        private static readonly Regex RxDitherPulse = new Regex(
            @"DirectGuider\.cs\|SelectDitherPulse\|.*\|Dither target from \(([-0-9.eE]+)\s*,\s*([-0-9.eE]+)\) to \(([-0-9.eE]+)\s*,\s*([-0-9.eE]+)\)(?: using guide durations of ([-0-9.eE]+) and ([-0-9.eE]+) seconds)?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Center-after-drift plate-solve follower: reported drift vs threshold (arc minutes).</summary>
        private static readonly Regex RxCenterDriftArcMin = new Regex(
            @"CenterAfterDriftTrigger\.cs\|PlatesolvingImageFollower_PropertyChanged\|\d+\|Drift:\s*([-0-9.eE]+)\s*/\s*([-0-9.eE]+)\s*arc\s*minutes?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary><c>BaseImageData.SaveToDisk</c> — timestamp + path share the same basis as other log lines.</summary>
        private static readonly Regex RxSavedImageTo = new Regex(
            @"BaseImageData\.cs\|SaveToDisk\|.*\|Saved image to\s+(.+)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // NINA 3.x log line timestamp — prefix before first | or INFO field (include fractional seconds).
        private static readonly Regex[] _tsPatterns = {
            new Regex(@"^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?)", RegexOptions.Compiled),
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
        /// Loads sequencer triggers and dither pulses; sets hints and inter-frame edge fields.
        /// Returns matched jump count (jumps whose next frame has a strict between-exposure dither/center interval),
        /// log found, trigger count, edge marker count.
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
                var centerDriftLines = new List<CenterDriftLogLine>();
                var imageSaves = new List<ImageSaveEvent>();
                if (!LoadAndParseSessionLogs(sessionDate, triggers, pulses, centerDriftLines, imageSaves))
                    return (0, false, 0, 0);

                triggers.Sort((a, b) => a.UtcTime.CompareTo(b.UtcTime));
                pulses.Sort((a, b) => a.UtcTime.CompareTo(b.UtcTime));
                centerDriftLines.Sort((a, b) => a.UtcTime.CompareTo(b.UtcTime));
                imageSaves.Sort((a, b) => a.UtcTime.CompareTo(b.UtcTime));

                Logger.Debug(
                    $"SeeDrift: log correlator — triggers={triggers.Count}, dither pulses={pulses.Count}, center drift lines={centerDriftLines.Count}, image saves={imageSaves.Count}");
                AssignInterFrameEdges(samples, triggers, pulses, centerDriftLines, imageSaves);

                if (triggers.Count == 0) {
                    Logger.Debug("SeeDrift: log correlator — no Starting Trigger lines in date window");
                    return (CountJumpsWithNextFrameLogInterval(samples), true, 0, CountSequencerEdges(samples));
                }

                var firstJump = samples.OrderBy(s => s.FrameIndex).FirstOrDefault(s => s.IsJump);
                if (firstJump != null) {
                    var nearest = triggers.OrderBy(e => (e.UtcTime - firstJump!.ExposureStartUtc).Duration()).First();
                    var gap = (nearest.UtcTime - firstJump.ExposureStartUtc).Duration();
                    Logger.Debug(
                        $"SeeDrift: log correlator — first jump UTC={firstJump.ExposureStartUtc:o}, nearest trigger '{nearest.Label}' UTC={nearest.UtcTime:o}, gap={gap.TotalSeconds:F0}s (not used for UI — only strict between-frame intervals)");
                }

                var matchedJumps = CountJumpsWithNextFrameLogInterval(samples);
                Logger.Debug($"SeeDrift: log correlator — jumps with next-frame log interval={matchedJumps}");
                return (matchedJumps, true, triggers.Count, CountSequencerEdges(samples));
            } catch (Exception ex) {
                Logger.Debug($"SeeDrift: log correlation skipped: {ex.Message}");
                return (0, false, 0, 0);
            }
        }

        /// <summary>Jumps where the <em>following</em> frame boundary has a logged dither/center between exposures.</summary>
        private static int CountJumpsWithNextFrameLogInterval(List<DriftSample> samples) {
            var n = 0;
            foreach (var sample in samples.Where(s => s.IsJump)) {
                var next = samples.FirstOrDefault(x => x.FrameIndex == sample.FrameIndex + 1);
                if (next != null && (next.EdgeHadDitherTrigger || next.EdgeHadCenterTrigger))
                    n++;
            }
            return n;
        }

        private static int CountSequencerEdges(List<DriftSample> samples) =>
            samples.Count(s => s.EdgeHadDitherTrigger || s.EdgeHadCenterTrigger);

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

        private static bool ShouldIgnoreSavePath(string fullPath) {
            var p = fullPath.Replace('/', '\\');
            return p.IndexOf("PlateSolver", StringComparison.OrdinalIgnoreCase) >= 0
                || p.IndexOf(@"\AppData\Local\Temp\", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Uses NINA exposure index from file names when both parse; otherwise trace position (<c>FrameIndex + 1</c>).
        /// </summary>
        private static string BetweenFramesHoverLine(DriftSample prev, DriftSample cur) {
            if (FitsFolderImport.TryExposureSequenceFromFileName(prev.FileName, out var a)
                && FitsFolderImport.TryExposureSequenceFromFileName(cur.FileName, out var b))
                return $"Between frames {a} → {b} ({prev.FileName} → {cur.FileName})";
            return $"Between frames {prev.FrameIndex + 1} → {cur.FrameIndex + 1} ({prev.FileName} → {cur.FileName})";
        }

        private static void AssignInterFrameEdges(
                List<DriftSample> samples,
                List<TimedTrigger> triggers,
                List<DitherPulse> pulses,
                List<CenterDriftLogLine> centerDriftLines,
                List<ImageSaveEvent> imageSaves) {

            var saveByFileName = BuildSaveUtcByFileName(imageSaves);
            var ordered = samples.OrderBy(s => s.FrameIndex).ToList();
            for (var i = 1; i < ordered.Count; i++) {
                var prev = ordered[i - 1];
                var cur = ordered[i];
                var t0 = prev.ExposureStartUtc;
                var t1 = cur.ExposureStartUtc;
                if (TryGetLogSaveUtc(prev, saveByFileName, out var save0)
                    && TryGetLogSaveUtc(cur, saveByFileName, out var save1)
                    && save1 > save0) {
                    t0 = save0;
                    t1 = save1;
                    // End window at next exposure start when FITS time is sane — avoids classifying the
                    // following gap's trigger inside (save_prev, save_cur) when save_cur is very late.
                    var expCur = cur.ExposureStartUtc;
                    if (expCur > t0 && expCur < t1)
                        t1 = expCur;
                }
                if (t1 <= t0)
                    continue;

                var t1Log = t1 + InterFrameLogUpperSlop;

                DateTime? ditherTriggerUtc = null;
                DateTime? centerTriggerUtc = null;
                foreach (var tr in triggers.Where(t => t.UtcTime > t0 && t.UtcTime < t1Log).OrderBy(t => t.UtcTime)) {
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
                        .Where(p => p.UtcTime >= ditherTriggerUtc.Value && p.UtcTime < t1Log)
                        .OrderBy(p => p.UtcTime)
                        .FirstOrDefault();
                }
                if (pulse == null && cur.EdgeHadDitherTrigger) {
                    pulse = pulses
                        .Where(p => p.UtcTime > t0 && p.UtcTime < t1Log)
                        .OrderBy(p => p.UtcTime)
                        .FirstOrDefault();
                }
                if (pulse != null) {
                    cur.EdgeDitherClaimedDx = pulse.Dx;
                    cur.EdgeDitherClaimedDy = pulse.Dy;
                }

                CenterDriftLogLine? centerDrift = null;
                if (cur.EdgeHadCenterTrigger) {
                    var w0 = centerTriggerUtc ?? t0;
                    centerDrift = centerDriftLines
                        .Where(d => d.UtcTime >= w0 && d.UtcTime < t1Log)
                        .OrderBy(d => d.UtcTime)
                        .LastOrDefault();
                    if (centerDrift == null) {
                        centerDrift = centerDriftLines
                            .Where(d => d.UtcTime > t0 && d.UtcTime < t1Log)
                            .OrderBy(d => d.UtcTime)
                            .LastOrDefault();
                    }
                }

                var lines = new List<string> {
                    BetweenFramesHoverLine(prev, cur)
                };
                if (cur.EdgeHadDitherTrigger)
                    lines.Add(ditherTriggerUtc.HasValue
                        ? $"DitherAfterExposures @ {ditherTriggerUtc.Value.ToLocalTime():HH:mm:ss}"
                        : "DitherAfterExposures");
                if (cur.EdgeHadCenterTrigger)
                    lines.Add(centerTriggerUtc.HasValue
                        ? $"CenterAfterDrift @ {centerTriggerUtc.Value.ToLocalTime():HH:mm:ss}"
                        : "CenterAfterDrift");
                if (pulse != null) {
                    lines.Add(
                        $"NINA dither target (guider): ({pulse.FromX:F2}, {pulse.FromY:F2}) → ({pulse.ToX:F2}, {pulse.ToY:F2}); move Δx {pulse.Dx:F2}, Δy {pulse.Dy:F2}");
                    if (pulse.GuideDurationFirstSec.HasValue && pulse.GuideDurationSecondSec.HasValue) {
                        lines.Add(
                            $"NINA dither guide durations (log): {pulse.GuideDurationFirstSec.Value:F2} s, {pulse.GuideDurationSecondSec.Value:F2} s");
                    }
                } else if (cur.EdgeDitherClaimedDx.HasValue && cur.EdgeDitherClaimedDy.HasValue) {
                    lines.Add(
                        $"NINA guider move (log): Δx {cur.EdgeDitherClaimedDx.Value:F1}, Δy {cur.EdgeDitherClaimedDy.Value:F1} [guider units]");
                }
                if (centerDrift != null) {
                    lines.Add(
                        $"NINA center drift (log): {centerDrift.DriftArcMinutes:F3}′ vs {centerDrift.ThresholdArcMinutes:F3}′ threshold");
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
                List<ImageSaveEvent> imageSaves) {
            if (!Directory.Exists(LogFolder))
                return false;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var logFound = false;

            for (var offset = -1; offset <= 1; offset++) {
                foreach (var path in FindAllLogFilesForDate(sessionDate.AddDays(offset))) {
                    if (!seen.Add(path)) continue;
                    logFound = true;
                    ParseLogLines(path, triggers, pulses, centerDriftLines, imageSaves);
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
                List<ImageSaveEvent> imageSaves) {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs);
            string? line;
            while ((line = sr.ReadLine()) != null) {
                if (!TryParseTimestamp(line, out var ts))
                    continue;

                var mSave = RxSavedImageTo.Match(line);
                if (mSave.Success) {
                    var rawPath = mSave.Groups[1].Value.Trim().Trim('\'', '"');
                    if (!string.IsNullOrEmpty(rawPath) && !ShouldIgnoreSavePath(rawPath)) {
                        imageSaves.Add(new ImageSaveEvent { UtcTime = ts, FilePath = rawPath });
                    }
                    continue;
                }

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
                        out var dtRoundtrip) && dtRoundtrip.Kind == DateTimeKind.Utc) {
                    utc = dtRoundtrip;
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
    }
}
