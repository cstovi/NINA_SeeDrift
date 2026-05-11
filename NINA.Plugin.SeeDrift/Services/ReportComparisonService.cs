using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using NINA.Plugin.SeeDrift.Models;
using NINA.Plugin.SeeDrift.Utility;

namespace NINA.Plugin.SeeDrift.Services {

    internal static class ReportComparisonService {
        private const int SupportedReportSchemaVersion = 1;

        private static readonly Regex RxPayload = new(
            "<script\\s+type=\"application/json\"\\s+id=\"seedrift-report-data\"\\s*>(.*?)</script>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        public static bool TryWriteComparison(
                string beforePath,
                string afterPath,
                string outputFolder,
                out string outputPath,
                out string errorMessage) {
            outputPath = "";
            errorMessage = "";

            if (!TryReadPayload(beforePath, out var before, out errorMessage))
                return false;
            if (!TryReadPayload(afterPath, out var after, out errorMessage))
                return false;

            var result = Compare(beforePath, afterPath, before, after);
            var folder = SeeDriftPaths.ResolveReportFolder();
            Directory.CreateDirectory(folder);
            var deviceToken = ComparisonDeviceToken(before.SeestarDevice, after.SeestarDevice);
            var devicePart = string.IsNullOrWhiteSpace(deviceToken) ? "" : $"_{deviceToken}";
            outputPath = Path.Combine(folder, $"SeeDrift_v{FileVersionStamp()}_compare{devicePart}_{DateTime.Now:yyyyMMdd_HHmmss}.html");
            result.OutputPath = outputPath;
            File.WriteAllText(outputPath, BuildHtml(result), Encoding.UTF8);
            return true;
        }

        private static bool TryReadPayload(string path, out SeeDriftReportPayload payload, out string errorMessage) {
            payload = new SeeDriftReportPayload();
            errorMessage = "";
            try {
                if (string.IsNullOrWhiteSpace(path)) {
                    errorMessage = "Choose both SeeDrift HTML reports first.";
                    return false;
                }
                var p = path.Trim();
                if (!File.Exists(p)) {
                    errorMessage = $"Report file not found: {p}";
                    return false;
                }
                var html = File.ReadAllText(p);
                var m = RxPayload.Match(html);
                if (!m.Success) {
                    errorMessage = $"No SeeDrift report data payload found in {Path.GetFileName(p)}. Recreate that report with the analytics build first.";
                    return false;
                }
                var parsed = JsonSerializer.Deserialize<SeeDriftReportPayload>(
                    m.Groups[1].Value,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (parsed == null || parsed.Targets.Count == 0) {
                    errorMessage = $"Report payload was empty in {Path.GetFileName(p)}.";
                    return false;
                }
                if (parsed.SchemaVersion != SupportedReportSchemaVersion) {
                    errorMessage = $"Report schema {parsed.SchemaVersion} in {Path.GetFileName(p)} is not supported by this build. Supported schema: {SupportedReportSchemaVersion}. Recreate the report or update SeeDrift.";
                    return false;
                }
                payload = parsed;
                return true;
            } catch (Exception ex) {
                errorMessage = $"Could not read report payload: {ex.Message}";
                return false;
            }
        }

        private static ReportComparisonResult Compare(
                string beforePath,
                string afterPath,
                SeeDriftReportPayload before,
                SeeDriftReportPayload after) {
            var result = new ReportComparisonResult {
                BeforePath = beforePath,
                AfterPath = afterPath,
                BeforePluginVersion = before.PluginVersion,
                AfterPluginVersion = after.PluginVersion,
                BeforeSchemaVersion = before.SchemaVersion,
                AfterSchemaVersion = after.SchemaVersion,
                BeforeSeestarDevice = before.SeestarDevice ?? SeestarDeviceInfo.Unknown,
                AfterSeestarDevice = after.SeestarDevice ?? SeestarDeviceInfo.Unknown,
                DeviceComparisonAdvisory = BuildDeviceComparisonAdvisory(before.SeestarDevice, after.SeestarDevice)
            };

            var beforeStats = BuildStats(before);
            var afterStats = BuildStats(after);
            result.ScopeSummary = BuildScopeSummary(beforeStats, afterStats);
            result.Metrics.Add(BuildMetric(
                "Average dither RA movement",
                beforeStats.AverageAbsDitherRaArcSec,
                afterStats.AverageAbsDitherRaArcSec,
                "\"",
                higherIsBetter: true,
                "More RA separation on average",
                "Less RA separation on average"));
            result.Metrics.Add(BuildMetric(
                "Average dither Dec movement",
                beforeStats.AverageAbsDitherDecArcSec,
                afterStats.AverageAbsDitherDecArcSec,
                "\"",
                higherIsBetter: true,
                "More Dec separation on average",
                "Less Dec separation on average"));
            result.Metrics.Add(BuildMetric(
                "Typical dither (median |Δ|)",
                beforeStats.MedianDitherMoveArcSec,
                afterStats.MedianDitherMoveArcSec,
                "\"",
                higherIsBetter: true,
                "Stronger typical dither magnitude",
                "Weaker typical dither magnitude"));
            result.Metrics.Add(BuildMetric(
                "Σ|ΔRA| over assessed dither intervals",
                beforeStats.SumAbsDitherRaArcSec,
                afterStats.SumAbsDitherRaArcSec,
                "\"",
                higherIsBetter: true,
                "More total RA separation across the run",
                "Less total RA separation across the run"));
            result.Metrics.Add(BuildMetric(
                "Σ|ΔDec| over assessed dither intervals",
                beforeStats.SumAbsDitherDecArcSec,
                afterStats.SumAbsDitherDecArcSec,
                "\"",
                higherIsBetter: true,
                "More total Dec separation across the run",
                "Less total Dec separation across the run"));
            result.Metrics.Add(BuildMetric(
                "Dither RA/Dec balance",
                beforeStats.DitherAxisBalancePercent,
                afterStats.DitherAxisBalancePercent,
                "%",
                higherIsBetter: true,
                "More balanced between axes",
                "Less balanced between axes"));
            result.Metrics.Add(BuildMetric(
                "Weak dither rate",
                beforeStats.WeakDitherRatePercent,
                afterStats.WeakDitherRatePercent,
                "%",
                higherIsBetter: false,
                "Fewer weak dithers",
                "More weak dithers"));
            result.Metrics.Add(BuildMetric(
                "Average center improvement",
                beforeStats.AverageCenterImprovementPercent,
                afterStats.AverageCenterImprovementPercent,
                "%",
                higherIsBetter: true,
                "Center-after-drift improved more on average",
                "Center-after-drift improved less on average"));
            result.Metrics.Add(BuildMetric(
                "Ineffective center rate",
                beforeStats.IneffectiveCenterRatePercent,
                afterStats.IneffectiveCenterRatePercent,
                "%",
                higherIsBetter: false,
                "Fewer ineffective centers",
                "More ineffective centers"));
            result.Metrics.Add(BuildMetric(
                "Dither vs drift headroom",
                beforeStats.AverageDitherHeadroom,
                afterStats.AverageDitherHeadroom,
                "x",
                higherIsBetter: true,
                "More headroom between dithers and drift",
                "Less headroom between dithers and drift"));
            result.Metrics.Add(BuildMetric(
                "Worst short-window drift",
                beforeStats.AverageWorstWindowDriftPixels,
                afterStats.AverageWorstWindowDriftPixels,
                " px",
                higherIsBetter: false,
                "Less worst-window drift across the run",
                "More worst-window drift across the run"));
            result.SettingsRows.AddRange(BuildSettingsRows(before.SequencerSettings, after.SequencerSettings));
            result.SettingsDiffer = result.SettingsRows.Any(r => r.Status == "changed");
            result.OverallSummary = BuildOverallSummary(result.Metrics, result.SettingsDiffer);
            return result;
        }

        /// <summary>
        /// Builds the comparison rows for the run-wide "Session settings used" block. Rows are emitted only when at
        /// least one of the two reports has data for that row; absent values are rendered as "—" with status="missing".
        /// </summary>
        private static IEnumerable<ReportComparisonSettingsRow> BuildSettingsRows(
                SessionSequencerSettings? before, SessionSequencerSettings? after) {
            if (before == null && after == null)
                yield break;

            // Mount Dither Pixels (median)
            if (before?.DitherPixelsMedian.HasValue == true || after?.DitherPixelsMedian.HasValue == true) {
                yield return CompareNumeric(
                    "Mount Dither Pixels (median, px)",
                    before?.DitherPixelsMedian, after?.DitherPixelsMedian,
                    suffix: " px", changedTolerance: 1.0, decimals: 1,
                    note: "Median commanded |Δ| from DirectGuider pulses; proxy for the NINA Mount Dither Pixels setting.");
            }

            // CenterAfterDrift threshold (arc-min)
            if (before?.CenterMaxArcMin.HasValue == true || after?.CenterMaxArcMin.HasValue == true) {
                yield return CompareNumeric(
                    "Center After Drift — Max (arc-min)",
                    before?.CenterMaxArcMin, after?.CenterMaxArcMin,
                    suffix: "′", changedTolerance: 0.1, decimals: 1,
                    note: "Configured CenterAfterDrift threshold (from evaluation lines).");
            }

            // CenterAfterDrift evaluate cadence
            if (before?.CenterEvaluateAfterExposures.HasValue == true || after?.CenterEvaluateAfterExposures.HasValue == true) {
                yield return CompareCadence(
                    "Center After Drift — Evaluate every N exp.",
                    before?.CenterEvaluateAfterExposures, after?.CenterEvaluateAfterExposures,
                    note: "Inferred from frame gaps between CenterAfterDrift evaluation lines.");
            }

            // DitherAfterExposures cadence
            if (before?.DitherAfterExposuresN.HasValue == true || after?.DitherAfterExposuresN.HasValue == true) {
                var beforeNote = before?.DitherCadenceInferred == true ? "before: inferred" : "before: from log";
                var afterNote = after?.DitherCadenceInferred == true ? "after: inferred" : "after: from log";
                yield return CompareCadence(
                    "Dither After Exposures — Cadence (every N exp.)",
                    before?.DitherAfterExposuresN, after?.DitherAfterExposuresN,
                    note: $"DitherAfterExposures trigger cadence ({beforeNote}; {afterNote}).");
            }

            // Dither pulse guide durations (RA / Dec)
            if ((before?.DitherGuideDurationFirstSecMedian.HasValue == true && before?.DitherGuideDurationSecondSecMedian.HasValue == true)
                || (after?.DitherGuideDurationFirstSecMedian.HasValue == true && after?.DitherGuideDurationSecondSecMedian.HasValue == true)) {
                var beforeStr = FormatPulseDurations(before);
                var afterStr = FormatPulseDurations(after);
                var status = string.Equals(beforeStr, afterStr, StringComparison.Ordinal)
                    ? "same"
                    : (beforeStr == "—" || afterStr == "—" ? "missing" : "changed");
                yield return new ReportComparisonSettingsRow {
                    Setting = "Dither pulse durations (s, RA · Dec)",
                    BeforeValue = beforeStr,
                    AfterValue = afterStr,
                    Status = status,
                    Note = "Median seconds across dither pulses; proxy for the Seestar Alpaca RA/Dec guide rate."
                };
            }
        }

        private static string FormatPulseDurations(SessionSequencerSettings? s) {
            if (s?.DitherGuideDurationFirstSecMedian.HasValue != true
                || s?.DitherGuideDurationSecondSecMedian.HasValue != true)
                return "—";
            return string.Format(CultureInfo.InvariantCulture,
                "{0:0.00} · {1:0.00}", s.DitherGuideDurationFirstSecMedian!.Value, s.DitherGuideDurationSecondSecMedian!.Value);
        }

        private static ReportComparisonSettingsRow CompareNumeric(
                string setting, double? before, double? after, string suffix, double changedTolerance, int decimals, string note) {
            string beforeStr = before.HasValue
                ? before.Value.ToString("0." + new string('#', decimals), CultureInfo.InvariantCulture) + suffix
                : "—";
            string afterStr = after.HasValue
                ? after.Value.ToString("0." + new string('#', decimals), CultureInfo.InvariantCulture) + suffix
                : "—";
            string status;
            if (!before.HasValue || !after.HasValue)
                status = "missing";
            else if (Math.Abs(before.Value - after.Value) < changedTolerance)
                status = "same";
            else
                status = "changed";
            return new ReportComparisonSettingsRow {
                Setting = setting,
                BeforeValue = beforeStr,
                AfterValue = afterStr,
                Status = status,
                Note = note
            };
        }

        private static ReportComparisonSettingsRow CompareCadence(string setting, int? before, int? after, string note) {
            string beforeStr = before.HasValue ? $"every {before.Value}" : "—";
            string afterStr = after.HasValue ? $"every {after.Value}" : "—";
            string status;
            if (!before.HasValue || !after.HasValue)
                status = "missing";
            else if (before.Value == after.Value)
                status = "same";
            else
                status = "changed";
            return new ReportComparisonSettingsRow {
                Setting = setting,
                BeforeValue = beforeStr,
                AfterValue = afterStr,
                Status = status,
                Note = note
            };
        }

        private static ComparisonStats BuildStats(SeeDriftReportPayload report) {
            var targets = report.Targets ?? new List<SeeDriftReportTargetPayload>();
            var dithers = targets
                .SelectMany(t => t.Analysis.Dithers ?? new List<DitherEventAnalysis>())
                .Where(d => !d.IsSuspect)
                .ToList();
            var centers = targets
                .SelectMany(t => t.Analysis.Centers ?? new List<CenterEventAnalysis>())
                .ToList();
            var avgRa = AverageOrZero(dithers.Select(d => Math.Abs(d.DeltaRaArcSec)));
            var avgDec = AverageOrZero(dithers.Select(d => Math.Abs(d.DeltaDecArcSec)));
            var largerAxis = Math.Max(avgRa, avgDec);
            var balance = largerAxis > 0.001
                ? 100.0 * Math.Min(avgRa, avgDec) / largerAxis
                : 0;

            // Sums of assessed (non-suspect) interval magnitudes — prefer the analysis-stored
            // values when present; older reports fall back to summing the dither events directly.
            var sumRa = targets.Sum(t => t.Analysis.DitherIntervalAssessedSumAbsRaArcSec);
            var sumDec = targets.Sum(t => t.Analysis.DitherIntervalAssessedSumAbsDecArcSec);
            if (sumRa <= 0 && sumDec <= 0) {
                sumRa = dithers.Sum(d => Math.Abs(d.DeltaRaArcSec));
                sumDec = dithers.Sum(d => Math.Abs(d.DeltaDecArcSec));
            }
            double medianMove = 0;
            var medians = targets
                .Select(t => t.Analysis.DitherIntervalMedianMoveArcSec)
                .Where(v => v.HasValue)
                .Select(v => v!.Value)
                .ToList();
            if (medians.Count > 0)
                medianMove = medians.Average();
            else if (dithers.Count > 0) {
                var sorted = dithers.Select(d => d.MoveArcSec).OrderBy(v => v).ToList();
                medianMove = sorted[(sorted.Count - 1) / 2];
            }

            // Walking-noise specific metrics. DriftRisk is per-target — average across targets we have.
            var headrooms = targets
                .Select(t => t.Analysis.DriftRisk?.DitherHeadroomRatio)
                .Where(v => v.HasValue)
                .Select(v => v!.Value)
                .ToList();
            var avgHeadroom = headrooms.Count > 0 ? headrooms.Average() : 0;
            var worstWindowsPx = targets
                .Select(t => t.Analysis.DriftRisk?.WorstWindowDriftPixels)
                .Where(v => v.HasValue)
                .Select(v => v!.Value)
                .ToList();
            var avgWorstWindowPx = worstWindowsPx.Count > 0 ? worstWindowsPx.Average() : 0;
            return new ComparisonStats {
                TargetCount = targets.Count,
                FrameCount = targets.Sum(t => t.FrameCount),
                AssessedDitherCount = dithers.Count,
                SuspectDitherCount = targets.Sum(t => t.Analysis.SuspectDitherCount),
                CenterCount = centers.Count,
                AverageAbsDitherRaArcSec = avgRa,
                AverageAbsDitherDecArcSec = avgDec,
                SumAbsDitherRaArcSec = sumRa,
                SumAbsDitherDecArcSec = sumDec,
                MedianDitherMoveArcSec = medianMove,
                DitherAxisBalancePercent = balance,
                WeakDitherRatePercent = Percent(dithers.Count(d => d.Assessment == "Weak"), dithers.Count),
                AverageCenterImprovementPercent = AverageOrZero(centers.Select(c => c.ImprovementPercent)),
                IneffectiveCenterRatePercent = Percent(centers.Count(c => c.Assessment == "Ineffective"), centers.Count),
                AverageDitherHeadroom = avgHeadroom,
                AverageWorstWindowDriftPixels = avgWorstWindowPx
            };
        }

        private static ReportComparisonMetricResult BuildMetric(
                string metric,
                double before,
                double after,
                string suffix,
                bool higherIsBetter,
                string improvedText,
                string worsenedText) {
            var delta = after - before;
            var threshold = suffix == "%" ? 2.0
                : suffix == "x" ? 0.5
                : 0.2;
            if (Math.Abs(delta) < threshold) {
                return new ReportComparisonMetricResult {
                    Metric = metric,
                    BeforeValue = FormatValue(before, suffix),
                    AfterValue = FormatValue(after, suffix),
                    Direction = "similar",
                    Read = "Similar"
                };
            }

            var improved = higherIsBetter ? delta > 0 : delta < 0;
            return new ReportComparisonMetricResult {
                Metric = metric,
                BeforeValue = FormatValue(before, suffix),
                AfterValue = FormatValue(after, suffix),
                Direction = improved ? "better" : "worse",
                Read = improved ? improvedText : worsenedText
            };
        }

        private static string BuildScopeSummary(ComparisonStats before, ComparisonStats after) {
            return FormattableString.Invariant(
                $"Whole-report averages: {before.TargetCount} target(s), {before.FrameCount} frame(s), {before.AssessedDitherCount} assessed dither(s), {before.CenterCount} center event(s) before; {after.TargetCount} target(s), {after.FrameCount} frame(s), {after.AssessedDitherCount} assessed dither(s), {after.CenterCount} center event(s) after. Suspect dither intervals excluded: {before.SuspectDitherCount} before, {after.SuspectDitherCount} after.");
        }

        private static string BuildDeviceComparisonAdvisory(SeestarDeviceInfo? before, SeestarDeviceInfo? after) {
            before ??= SeestarDeviceInfo.Unknown;
            after ??= SeestarDeviceInfo.Unknown;
            if (before.IsKnown && after.IsKnown
                && !string.Equals(before.DisplayName, after.DisplayName, StringComparison.OrdinalIgnoreCase)) {
                return "Different Seestar devices detected; compare arcsecond and pixel-scale-sensitive metrics cautiously.";
            }
            return "";
        }

        private static string BuildOverallSummary(IReadOnlyList<ReportComparisonMetricResult> metrics, bool settingsDiffer) {
            var better = metrics.Count(m => m.Direction == "better");
            var worse = metrics.Count(m => m.Direction == "worse");
            string baseLine;
            if (better > worse)
                baseLine = $"Overall read: better on average ({better} improved, {worse} worsened).";
            else if (worse > better)
                baseLine = $"Overall read: worse on average ({better} improved, {worse} worsened).";
            else
                baseLine = $"Overall read: mixed or similar on average ({better} improved, {worse} worsened).";
            return settingsDiffer
                ? baseLine + " Session settings differed between runs — see the Settings table for what changed."
                : baseLine;
        }

        private static double AverageOrZero(IEnumerable<double> values) {
            var list = values.Where(v => !double.IsNaN(v) && !double.IsInfinity(v)).ToList();
            return list.Count == 0 ? 0 : list.Average();
        }

        private static double Percent(int count, int total) {
            return total <= 0 ? 0 : 100.0 * count / total;
        }

        private static string BuildHtml(ReportComparisonResult result) {
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"/><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"/>");
            sb.AppendLine($"<meta name=\"seedrift-report-kind\" content=\"comparison\"/><meta name=\"seedrift-generator-version\" content=\"{Escape(CurrentPluginVersion())}\"/><meta name=\"seedrift-supported-schema\" content=\"{SupportedReportSchemaVersion}\"/>");
            sb.AppendLine("<title>SeeDrift comparison</title><script src=\"https://cdn.tailwindcss.com\"></script></head>");
            sb.AppendLine("<body class=\"bg-slate-950 text-slate-200\"><main class=\"mx-auto max-w-5xl px-4 py-8\">");
            sb.AppendLine("<h1 class=\"text-xl font-semibold text-white\">SeeDrift before/after comparison</h1>");
            sb.AppendLine($"<p class=\"mt-2 break-all text-xs text-slate-400\">Before: {Escape(result.BeforePath)}</p>");
            sb.AppendLine($"<p class=\"mt-1 break-all text-xs text-slate-400\">After: {Escape(result.AfterPath)}</p>");
            sb.AppendLine($"<p class=\"mt-2 text-xs text-slate-500\">Report versions: before v{Escape(result.BeforePluginVersion)} / schema {result.BeforeSchemaVersion}; after v{Escape(result.AfterPluginVersion)} / schema {result.AfterSchemaVersion}. Compatible schemas can compare across plugin versions.</p>");
            sb.AppendLine($"<p class=\"mt-1 text-xs text-slate-500\">Scopes: before <span class=\"text-slate-300\">{Escape(DeviceDisplay(result.BeforeSeestarDevice))}</span>; after <span class=\"text-slate-300\">{Escape(DeviceDisplay(result.AfterSeestarDevice))}</span>.</p>");
            if (!string.IsNullOrWhiteSpace(result.DeviceComparisonAdvisory)) {
                sb.AppendLine($"<div class=\"mt-4 rounded-lg border border-amber-500/40 bg-amber-500/10 p-3 text-sm text-amber-100\">{Escape(result.DeviceComparisonAdvisory)}</div>");
            }
            sb.AppendLine($"<div class=\"mt-5 rounded-lg border border-sky-900/50 bg-sky-950/30 p-4\"><p class=\"font-semibold text-sky-100\">{Escape(result.OverallSummary)}</p><p class=\"mt-2 text-xs text-slate-400\">{Escape(result.ScopeSummary)}</p></div>");
            sb.AppendLine("<div class=\"mt-6 overflow-x-auto rounded-lg border border-slate-700\"><table class=\"min-w-full divide-y divide-slate-700 text-left text-sm\">");
            sb.AppendLine("<thead class=\"bg-slate-900 text-sky-300\"><tr><th class=\"px-3 py-2\">Metric</th><th class=\"px-3 py-2\">Before</th><th class=\"px-3 py-2\">After</th><th class=\"px-3 py-2\">Read</th></tr></thead>");
            sb.AppendLine("<tbody class=\"divide-y divide-slate-800 bg-slate-950/40\">");
            foreach (var r in result.Metrics) {
                sb.AppendLine("<tr>");
                sb.AppendLine($"<td class=\"px-3 py-2 text-slate-100\">{Escape(r.Metric)}</td>");
                sb.AppendLine($"<td class=\"px-3 py-2\">{Escape(r.BeforeValue)}</td>");
                sb.AppendLine($"<td class=\"px-3 py-2\">{Escape(r.AfterValue)}</td>");
                sb.AppendLine($"<td class=\"px-3 py-2 text-slate-300\">{Escape(r.Read)}</td>");
                sb.AppendLine("</tr>");
            }
            sb.AppendLine("</tbody></table></div>");

            if (result.SettingsRows.Count > 0) {
                sb.AppendLine("<h2 class=\"mt-8 text-base font-semibold text-sky-200\">Session settings used</h2>");
                sb.AppendLine("<p class=\"mt-1 text-xs text-slate-500\">Observed in NINA logs during each run. Use this to see whether changes above correlate with a configuration change between sessions. Older reports without embedded settings show <span class=\"font-mono\">—</span>.</p>");
                sb.AppendLine("<div class=\"mt-3 overflow-x-auto rounded-lg border border-slate-700\"><table class=\"min-w-full divide-y divide-slate-700 text-left text-sm\">");
                sb.AppendLine("<thead class=\"bg-slate-900 text-sky-300\"><tr><th class=\"px-3 py-2\">Setting</th><th class=\"px-3 py-2\">Before</th><th class=\"px-3 py-2\">After</th><th class=\"px-3 py-2\">Change</th></tr></thead>");
                sb.AppendLine("<tbody class=\"divide-y divide-slate-800 bg-slate-950/40\">");
                foreach (var r in result.SettingsRows) {
                    var (badgeClass, badgeText) = r.Status switch {
                        "changed" => ("border-amber-500/40 bg-amber-500/10 text-amber-200", "Changed"),
                        "same" => ("border-slate-700 bg-slate-800/60 text-slate-300", "Same"),
                        _ => ("border-slate-800 bg-slate-900/40 text-slate-500", "—")
                    };
                    sb.AppendLine("<tr>");
                    sb.AppendLine($"<td class=\"px-3 py-2 text-slate-100\">{Escape(r.Setting)}<div class=\"mt-1 text-[11px] text-slate-500\">{Escape(r.Note)}</div></td>");
                    sb.AppendLine($"<td class=\"px-3 py-2\">{Escape(r.BeforeValue)}</td>");
                    sb.AppendLine($"<td class=\"px-3 py-2\">{Escape(r.AfterValue)}</td>");
                    sb.AppendLine($"<td class=\"px-3 py-2\"><span class=\"rounded border {badgeClass} px-2 py-0.5 text-[11px] uppercase tracking-wide\">{Escape(badgeText)}</span></td>");
                    sb.AppendLine("</tr>");
                }
                sb.AppendLine("</tbody></table></div>");
            }

            sb.AppendLine("<p class=\"mt-4 text-xs text-slate-500\">Comparison uses embedded SeeDrift report data across the whole report. It does not require matching target names or session order. No FITS files are scanned and no plate solving is run.</p>");
            sb.AppendLine("</main></body></html>");
            return sb.ToString();
        }

        private static string FormatValue(double value, string suffix) {
            var number = value.ToString("0.###", CultureInfo.InvariantCulture);
            return suffix == "\"" ? number + "″" : number + suffix;
        }

        private static string CurrentPluginVersion() =>
            typeof(ReportComparisonService).Assembly.GetName().Version?.ToString() ?? "";

        private static string FileVersionStamp() =>
            CurrentPluginVersion().Replace('.', '_');

        private static string DeviceDisplay(SeestarDeviceInfo? device) =>
            string.IsNullOrWhiteSpace(device?.DisplayName) ? "Unknown scope" : device.DisplayName.Trim();

        private static string ComparisonDeviceToken(SeestarDeviceInfo? before, SeestarDeviceInfo? after) {
            before ??= SeestarDeviceInfo.Unknown;
            after ??= SeestarDeviceInfo.Unknown;
            if (!before.IsKnown && !after.IsKnown)
                return "";
            if (before.IsKnown && after.IsKnown
                && string.Equals(before.FileNameToken, after.FileNameToken, StringComparison.OrdinalIgnoreCase))
                return before.FileNameToken;
            return "mixed_scopes";
        }

        private static string Escape(string s) =>
            s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

        private sealed class ComparisonStats {
            public int TargetCount { get; init; }
            public int FrameCount { get; init; }
            public int AssessedDitherCount { get; init; }
            public int SuspectDitherCount { get; init; }
            public int CenterCount { get; init; }
            public double AverageAbsDitherRaArcSec { get; init; }
            public double AverageAbsDitherDecArcSec { get; init; }
            public double SumAbsDitherRaArcSec { get; init; }
            public double SumAbsDitherDecArcSec { get; init; }
            public double MedianDitherMoveArcSec { get; init; }
            public double DitherAxisBalancePercent { get; init; }
            public double WeakDitherRatePercent { get; init; }
            public double AverageCenterImprovementPercent { get; init; }
            public double IneffectiveCenterRatePercent { get; init; }
            public double AverageDitherHeadroom { get; init; }
            public double AverageWorstWindowDriftPixels { get; init; }
        }
    }
}
