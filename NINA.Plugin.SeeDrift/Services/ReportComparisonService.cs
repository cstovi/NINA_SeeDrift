using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using NINA.Plugin.SeeDrift.Models;

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
            var folder = string.IsNullOrWhiteSpace(outputFolder)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SeeDrift")
                : outputFolder.Trim();
            Directory.CreateDirectory(folder);
            outputPath = Path.Combine(folder, $"SeeDrift_v{FileVersionStamp()}_compare_{DateTime.Now:yyyyMMdd_HHmmss}.html");
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
                AfterSchemaVersion = after.SchemaVersion
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
            result.OverallSummary = BuildOverallSummary(result.Metrics);
            return result;
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
            return new ComparisonStats {
                TargetCount = targets.Count,
                FrameCount = targets.Sum(t => t.FrameCount),
                AssessedDitherCount = dithers.Count,
                SuspectDitherCount = targets.Sum(t => t.Analysis.SuspectDitherCount),
                CenterCount = centers.Count,
                AverageAbsDitherRaArcSec = avgRa,
                AverageAbsDitherDecArcSec = avgDec,
                DitherAxisBalancePercent = balance,
                WeakDitherRatePercent = Percent(dithers.Count(d => d.Assessment == "Weak"), dithers.Count),
                AverageCenterImprovementPercent = AverageOrZero(centers.Select(c => c.ImprovementPercent)),
                IneffectiveCenterRatePercent = Percent(centers.Count(c => c.Assessment == "Ineffective"), centers.Count)
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
            var threshold = suffix == "%" ? 2.0 : 0.2;
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

        private static string BuildOverallSummary(IReadOnlyList<ReportComparisonMetricResult> metrics) {
            var better = metrics.Count(m => m.Direction == "better");
            var worse = metrics.Count(m => m.Direction == "worse");
            if (better > worse)
                return $"Overall read: better on average ({better} improved, {worse} worsened).";
            if (worse > better)
                return $"Overall read: worse on average ({better} improved, {worse} worsened).";
            return $"Overall read: mixed or similar on average ({better} improved, {worse} worsened).";
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
            public double DitherAxisBalancePercent { get; init; }
            public double WeakDitherRatePercent { get; init; }
            public double AverageCenterImprovementPercent { get; init; }
            public double IneffectiveCenterRatePercent { get; init; }
        }
    }
}
