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
            outputPath = Path.Combine(folder, $"SeeDrift_compare_{DateTime.Now:yyyyMMdd_HHmmss}.html");
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
                AfterPath = afterPath
            };

            var afterByName = after.Targets
                .GroupBy(t => t.TargetName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var b in before.Targets) {
                if (!afterByName.TryGetValue(b.TargetName, out var a))
                    continue;

                var beforeRate = b.Analysis.DriftRate.TotalArcSecPerMinute;
                var afterRate = a.Analysis.DriftRate.TotalArcSecPerMinute;
                var weakBefore = b.Analysis.Dithers.Count(d => d.Assessment == "Weak");
                var weakAfter = a.Analysis.Dithers.Count(d => d.Assessment == "Weak");
                var badCenterBefore = b.Analysis.Centers.Count(c => c.Assessment == "Ineffective");
                var badCenterAfter = a.Analysis.Centers.Count(c => c.Assessment == "Ineffective");
                result.Targets.Add(new ReportComparisonTargetResult {
                    TargetName = b.TargetName,
                    BeforeFrames = b.FrameCount,
                    AfterFrames = a.FrameCount,
                    BeforeDriftRateArcSecPerMinute = beforeRate,
                    AfterDriftRateArcSecPerMinute = afterRate,
                    BeforeWeakDithers = weakBefore,
                    AfterWeakDithers = weakAfter,
                    BeforeIneffectiveCenters = badCenterBefore,
                    AfterIneffectiveCenters = badCenterAfter,
                    Summary = BuildSummary(beforeRate, afterRate, weakBefore, weakAfter, badCenterBefore, badCenterAfter)
                });
            }

            if (result.Targets.Count == 0 && before.Targets.Count > 0 && after.Targets.Count > 0) {
                var b = before.Targets[0];
                var a = after.Targets[0];
                result.Targets.Add(new ReportComparisonTargetResult {
                    TargetName = $"{b.TargetName} → {a.TargetName}",
                    BeforeFrames = b.FrameCount,
                    AfterFrames = a.FrameCount,
                    BeforeDriftRateArcSecPerMinute = b.Analysis.DriftRate.TotalArcSecPerMinute,
                    AfterDriftRateArcSecPerMinute = a.Analysis.DriftRate.TotalArcSecPerMinute,
                    BeforeWeakDithers = b.Analysis.Dithers.Count(d => d.Assessment == "Weak"),
                    AfterWeakDithers = a.Analysis.Dithers.Count(d => d.Assessment == "Weak"),
                    BeforeIneffectiveCenters = b.Analysis.Centers.Count(c => c.Assessment == "Ineffective"),
                    AfterIneffectiveCenters = a.Analysis.Centers.Count(c => c.Assessment == "Ineffective"),
                    Summary = "No matching target names; compared the first listed target in each report."
                });
            }

            return result;
        }

        private static string BuildSummary(
                double beforeRate,
                double afterRate,
                int weakBefore,
                int weakAfter,
                int badCenterBefore,
                int badCenterAfter) {
            var parts = new List<string>();
            var rateDelta = afterRate - beforeRate;
            if (Math.Abs(rateDelta) > 0.05)
                parts.Add(rateDelta < 0 ? "drift rate improved" : "drift rate worsened");
            else
                parts.Add("drift rate similar");

            if (weakAfter < weakBefore)
                parts.Add("fewer weak dithers");
            else if (weakAfter > weakBefore)
                parts.Add("more weak dithers");

            if (badCenterAfter < badCenterBefore)
                parts.Add("center recovery improved");
            else if (badCenterAfter > badCenterBefore)
                parts.Add("center recovery worsened");

            return string.Join("; ", parts);
        }

        private static string BuildHtml(ReportComparisonResult result) {
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"/><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"/>");
            sb.AppendLine("<title>SeeDrift comparison</title><script src=\"https://cdn.tailwindcss.com\"></script></head>");
            sb.AppendLine("<body class=\"bg-slate-950 text-slate-200\"><main class=\"mx-auto max-w-5xl px-4 py-8\">");
            sb.AppendLine("<h1 class=\"text-xl font-semibold text-white\">SeeDrift before/after comparison</h1>");
            sb.AppendLine($"<p class=\"mt-2 break-all text-xs text-slate-400\">Before: {Escape(result.BeforePath)}</p>");
            sb.AppendLine($"<p class=\"mt-1 break-all text-xs text-slate-400\">After: {Escape(result.AfterPath)}</p>");
            sb.AppendLine("<div class=\"mt-6 overflow-x-auto rounded-lg border border-slate-700\"><table class=\"min-w-full divide-y divide-slate-700 text-left text-sm\">");
            sb.AppendLine("<thead class=\"bg-slate-900 text-sky-300\"><tr><th class=\"px-3 py-2\">Target</th><th class=\"px-3 py-2\">Frames</th><th class=\"px-3 py-2\">Drift rate</th><th class=\"px-3 py-2\">Weak dithers</th><th class=\"px-3 py-2\">Ineffective centers</th><th class=\"px-3 py-2\">Read</th></tr></thead>");
            sb.AppendLine("<tbody class=\"divide-y divide-slate-800 bg-slate-950/40\">");
            foreach (var r in result.Targets) {
                sb.AppendLine("<tr>");
                sb.AppendLine($"<td class=\"px-3 py-2 text-slate-100\">{Escape(r.TargetName)}</td>");
                sb.AppendLine($"<td class=\"px-3 py-2\">{r.BeforeFrames} → {r.AfterFrames}</td>");
                sb.AppendLine($"<td class=\"px-3 py-2\">{Fmt(r.BeforeDriftRateArcSecPerMinute)}″/min → {Fmt(r.AfterDriftRateArcSecPerMinute)}″/min</td>");
                sb.AppendLine($"<td class=\"px-3 py-2\">{r.BeforeWeakDithers} → {r.AfterWeakDithers}</td>");
                sb.AppendLine($"<td class=\"px-3 py-2\">{r.BeforeIneffectiveCenters} → {r.AfterIneffectiveCenters}</td>");
                sb.AppendLine($"<td class=\"px-3 py-2 text-slate-300\">{Escape(r.Summary)}</td>");
                sb.AppendLine("</tr>");
            }
            sb.AppendLine("</tbody></table></div>");
            sb.AppendLine("<p class=\"mt-4 text-xs text-slate-500\">Comparison uses embedded SeeDrift report data. No FITS files are scanned and no plate solving is run.</p>");
            sb.AppendLine("</main></body></html>");
            return sb.ToString();
        }

        private static string Fmt(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        private static string Escape(string s) =>
            s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    }
}
