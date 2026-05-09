using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using NINA.Plugin.SeeDrift.Models;
using NINA.Plugin.SeeDrift.Utility;

namespace NINA.Plugin.SeeDrift.Services {

    public static class HtmlReportExporter {

        // CDN pins (night HTML is opened offline-capable only after first load; versions documented for reproducibility)
        private const string CdnHammer = "https://cdn.jsdelivr.net/npm/hammerjs@2.0.8/hammer.min.js";
        private const string CdnChartJs = "https://cdn.jsdelivr.net/npm/chart.js@4.4.1/dist/chart.umd.min.js";
        private const string CdnChartZoom = "https://cdn.jsdelivr.net/npm/chartjs-plugin-zoom@2.2.0/dist/chartjs-plugin-zoom.min.js";
        private const string CdnTailwind = "https://cdn.tailwindcss.com";

        /// <summary>
        /// Writes a single rolling nightly HTML with one subsection per target within each batch (each Stop/Test run).
        /// Drift is re-anchored to the first solved frame <em>of that target</em>; sequencer rows only include edges where
        /// both consecutive frames belong to the same target.
        /// </summary>
        public static void WriteNightReport(
                IReadOnlyList<DriftTrackingService.CompletedTarget> targets, string path) {

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang=\"en\" class=\"h-full\">");
            sb.AppendLine("<head><meta charset=\"utf-8\"/><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"/>");
            sb.AppendLine($"<title>SeeDrift — night {DateTime.Now:yyyy-MM-dd}</title>");
            sb.AppendLine($"<script src=\"{CdnTailwind}\"></script>");
            sb.AppendLine($"<script src=\"{CdnHammer}\"></script>");
            sb.AppendLine($"<script src=\"{CdnChartJs}\"></script>");
            sb.AppendLine($"<script src=\"{CdnChartZoom}\"></script>");
            sb.AppendLine("<style>");
            sb.AppendLine("  /* Chart container needs explicit height for responsive canvas + pan/zoom */");
            sb.AppendLine("  .seedrift-chart-box { position: relative; height: 24rem; width: 100%; }");
            sb.AppendLine("</style>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body class=\"min-h-full bg-slate-950 text-slate-200 antialiased\">");
            sb.AppendLine("<main class=\"mx-auto max-w-5xl px-4 py-8 sm:px-6\">");
            sb.AppendLine($"<header class=\"mb-10 border-b border-slate-800 pb-6\">");
            sb.AppendLine($"  <h1 class=\"text-xl font-semibold tracking-tight text-white\">SeeDrift — night {Escape(DateTime.Now.ToString("yyyy-MM-dd"))}</h1>");
            sb.AppendLine($"  <p class=\"mt-2 text-sm text-slate-400\">Generated {DateTime.Now:HH:mm} · <span class=\"text-slate-300\">{targets.Count}</span> batch{(targets.Count == 1 ? "" : "es")}</p>");
            sb.AppendLine($"  <p class=\"mt-3 text-xs text-slate-500\">Drag to pan · Wheel to zoom · Double-click chart to reset zoom (Chart.js + zoom plugin)</p>");
            sb.AppendLine("</header>");

            for (var t = 0; t < targets.Count; t++) {
                var batch = targets[t];
                var samples = batch.Samples;
                var orderedFull = samples.OrderBy(s => s.FrameIndex).ToList();
                var sectionClass = t < targets.Count - 1
                    ? "mb-12 border-b border-slate-800 pb-12"
                    : "pb-4";

                const string xTitle = "ΔRA (arcsec)";
                const string yTitle = "ΔDec (arcsec)";

                var batchTitle = SummarizeTargetsForBatch(samples);

                sb.AppendLine($"<section class=\"{sectionClass}\">");
                sb.AppendLine($"  <h2 class=\"text-lg font-semibold text-sky-300\">{Escape(batchTitle)}</h2>");
                sb.AppendLine($"  <p class=\"mt-1 text-sm text-slate-400\">{samples.Count} frames · stopped {batch.StoppedUtc.ToLocalTime():HH:mm}</p>");

                var targetGroups = SplitBatchByTargetInOrder(orderedFull);
                for (var g = 0; g < targetGroups.Count; g++) {
                    var (targetName, grp) = targetGroups[g];
                    var canvasId = $"c{t}_{g}";
                    var labelJson = JsonSerializer.Serialize($"{targetName} — drift path");

                    sb.AppendLine($"  <div class=\"{(g > 0 ? "mt-12 border-t border-slate-800 pt-10" : "mt-8")}\">");
                    sb.AppendLine($"    <h3 class=\"text-base font-semibold text-sky-200\">Target: {Escape(targetName)}</h3>");
                    sb.AppendLine($"    <p class=\"mt-1 text-xs text-slate-500\">{grp.Count} frame{(grp.Count == 1 ? "" : "s")} · Δ vs first solved frame of this target</p>");
                    sb.AppendLine($"    <div class=\"seedrift-chart-box mt-4 rounded-lg border border-slate-700 bg-slate-900/60 p-2\">");
                    sb.AppendLine($"      <canvas id=\"{canvasId}\"></canvas>");
                    sb.AppendLine("    </div>");

                    var ptsJson = FormatScatterPointsJsonAnchored(grp);

                    sb.AppendLine("<script>");
                    sb.AppendLine("(function(){");
                    sb.AppendLine($"  const pts = {ptsJson};");
                    sb.AppendLine($"  const datasetLabel = {labelJson};");
                    sb.AppendLine($"  const el = document.getElementById('{canvasId}');");
                    sb.AppendLine("  const chart = new Chart(el, {");
                    sb.AppendLine("    type: 'scatter',");
                    sb.AppendLine("    data: { datasets: [{");
                    sb.AppendLine("      label: datasetLabel,");
                    sb.AppendLine("      data: pts, borderColor: '#38bdf8',");
                    sb.AppendLine("      backgroundColor: 'rgba(56,189,248,0.25)',");
                    sb.AppendLine("      showLine: true, tension: 0.12, pointRadius: 3, borderWidth: 1.5");
                    sb.AppendLine("    }]},");
                    sb.AppendLine("    options: {");
                    sb.AppendLine("      responsive: true, maintainAspectRatio: false,");
                    sb.AppendLine("      scales: {");
                    sb.AppendLine($"        x: {{ title: {{ display: true, text: '{xTitle}', color: '#94a3b8' }}, grid: {{ color: 'rgba(148,163,184,0.12)' }}, ticks: {{ color: '#cbd5e1' }}, border: {{ color: '#475569' }} }},");
                    sb.AppendLine($"        y: {{ title: {{ display: true, text: '{yTitle}', color: '#94a3b8' }}, grid: {{ color: 'rgba(148,163,184,0.12)' }}, ticks: {{ color: '#cbd5e1' }}, border: {{ color: '#475569' }} }}");
                    sb.AppendLine("      },");
                    sb.AppendLine("      plugins: {");
                    sb.AppendLine("        legend: { labels: { color: '#e2e8f0' } },");
                    sb.AppendLine("        zoom: {");
                    sb.AppendLine("          pan: { enabled: true, mode: 'xy' },");
                    sb.AppendLine("          zoom: {");
                    sb.AppendLine("            wheel: { enabled: true },");
                    sb.AppendLine("            pinch: { enabled: true },");
                    sb.AppendLine("            mode: 'xy'");
                    sb.AppendLine("          },");
                    sb.AppendLine("          limits: { x: { min: 'original', max: 'original' }, y: { min: 'original', max: 'original' } }");
                    sb.AppendLine("        }");
                    sb.AppendLine("      }");
                    sb.AppendLine("    }");
                    sb.AppendLine("  });");
                    sb.AppendLine("  el.addEventListener('dblclick', function() { chart.resetZoom(); });");
                    sb.AppendLine("})();");
                    sb.AppendLine("</script>");

                    AppendSequencerSection(sb, orderedFull, targetName);
                    sb.AppendLine("  </div>");
                }

                sb.AppendLine("</section>");
            }

            sb.AppendLine("</main></body></html>");
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        /// <summary>
        /// One Stop/Test batch may include frames with different FITS OBJECT names; builds a readable heading from distinct <see cref="DriftSample.TargetName"/> values.
        /// </summary>
        public static string SummarizeTargetsForBatch(IReadOnlyList<DriftSample> samples) {
            var distinct = samples
                .Select(s => s.TargetName?.Trim())
                .Where(n => !string.IsNullOrEmpty(n))
                .Select(n => n!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (distinct.Count == 0)
                return "Unknown";
            if (distinct.Count == 1)
                return distinct[0];
            const int maxListed = 4;
            if (distinct.Count <= maxListed)
                return string.Join(" · ", distinct);
            return string.Join(" · ", distinct.Take(maxListed)) + $" (+{distinct.Count - maxListed} more)";
        }

        /// <summary>Groups samples by target; order of groups follows first appearance in frame sequence.</summary>
        private static List<(string Name, List<DriftSample> Samples)> SplitBatchByTargetInOrder(IReadOnlyList<DriftSample> orderedByFrame) {
            var groups = new Dictionary<string, List<DriftSample>>(StringComparer.OrdinalIgnoreCase);
            var orderKeys = new List<string>();
            foreach (var s in orderedByFrame) {
                var key = string.IsNullOrWhiteSpace(s.TargetName) ? "Unknown" : s.TargetName.Trim();
                if (!groups.TryGetValue(key, out var list)) {
                    list = new List<DriftSample>();
                    groups[key] = list;
                    orderKeys.Add(key);
                }
                list.Add(s);
            }
            return orderKeys.Select(k => (k, groups[k])).ToList();
        }

        private static string FormatScatterPointsJsonAnchored(IReadOnlyList<DriftSample> group) {
            if (group.Count == 0)
                return "[]";
            var r0 = group[0].RawRaHours;
            var d0 = group[0].RawDecDeg;
            var pts = new StringBuilder("[");
            for (var i = 0; i < group.Count; i++) {
                if (i > 0)
                    pts.Append(',');
                AstrometryMath.DeltaArcSec(r0, d0, group[i].RawRaHours, group[i].RawDecDeg,
                    out var dRa, out var dDec);
                pts.AppendFormat(CultureInfo.InvariantCulture,
                    "{{\"x\":{0},\"y\":{1}}}",
                    Math.Round(dRa, 4),
                    Math.Round(dDec, 4));
            }
            pts.Append(']');
            return pts.ToString();
        }

        private static bool SameSectionTarget(string? sampleTarget, string sectionTargetName) {
            var a = string.IsNullOrWhiteSpace(sampleTarget) ? "Unknown" : sampleTarget.Trim();
            return string.Equals(a, sectionTargetName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Rows where both endpoints of the interval belong to <paramref name="sectionTargetName"/> (same-target consecutive frames in this batch).
        /// </summary>
        private static void AppendSequencerSection(StringBuilder sb, IReadOnlyList<DriftSample> fullBatchOrdered, string sectionTargetName) {
            var rows = new List<(string fromFn, string toFn, string kind, string detail)>();
            for (var i = 1; i < fullBatchOrdered.Count; i++) {
                var prev = fullBatchOrdered[i - 1];
                var cur = fullBatchOrdered[i];
                if (!SameSectionTarget(prev.TargetName, sectionTargetName)
                    || !SameSectionTarget(cur.TargetName, sectionTargetName))
                    continue;

                var m = cur.EdgeSequencerMarkers;
                if (m == null || m.Count == 0)
                    continue;

                var prevFn = prev.FileName;
                foreach (var e in m) {
                    var kind = e.IsDither ? "DitherAfterExposures" : "CenterAfterDrift";
                    rows.Add((prevFn, cur.FileName, kind, e.Tooltip));
                }
            }

            sb.AppendLine("    <div class=\"mt-8\">");
            sb.AppendLine("      <h4 class=\"text-sm font-semibold uppercase tracking-wide text-sky-400\">Sequencer events (NINA logs)</h4>");

            if (rows.Count == 0) {
                sb.AppendLine("      <div class=\"mt-3 rounded-lg border border-slate-700 bg-slate-900/40 p-4\">");
                sb.AppendLine($"        <p class=\"text-sm text-slate-300\">No correlated dither or center-after-drift events between consecutive frames for target <span class=\"text-sky-300\">{Escape(sectionTargetName)}</span>.</p>");
                sb.AppendLine("        <p class=\"mt-2 text-xs leading-relaxed text-slate-500\">SeeDrift reads the same NINA log(s) used for this batch. Events attach only to intervals between two lights of the same target.</p>");
                sb.AppendLine("      </div>");
                sb.AppendLine("    </div>");
                return;
            }

            sb.AppendLine("      <p class=\"mt-2 text-xs text-slate-500\">Between-frame triggers for this target (same-target consecutive frames only).</p>");
            sb.AppendLine("      <div class=\"mt-3 overflow-x-auto rounded-lg border border-slate-700\">");
            sb.AppendLine("        <table class=\"min-w-full divide-y divide-slate-700 text-left text-xs\">");
            sb.AppendLine("          <thead class=\"bg-slate-900/80 text-sky-300\"><tr>");
            sb.AppendLine("            <th class=\"whitespace-nowrap px-3 py-2 font-medium\">From frame</th>");
            sb.AppendLine("            <th class=\"whitespace-nowrap px-3 py-2 font-medium\">To frame</th>");
            sb.AppendLine("            <th class=\"whitespace-nowrap px-3 py-2 font-medium\">Kind</th>");
            sb.AppendLine("            <th class=\"px-3 py-2 font-medium\">Detail</th>");
            sb.AppendLine("          </tr></thead>");
            sb.AppendLine("          <tbody class=\"divide-y divide-slate-800 bg-slate-950/40 text-slate-300\">");
            foreach (var r in rows) {
                sb.AppendLine("          <tr class=\"align-top\">");
                sb.AppendLine($"            <td class=\"whitespace-nowrap px-3 py-2 font-mono text-[11px]\">{Escape(r.fromFn)}</td>");
                sb.AppendLine($"            <td class=\"whitespace-nowrap px-3 py-2 font-mono text-[11px]\">{Escape(r.toFn)}</td>");
                sb.AppendLine($"            <td class=\"whitespace-nowrap px-3 py-2\">{Escape(r.kind)}</td>");
                sb.AppendLine($"            <td class=\"whitespace-pre-wrap px-3 py-2 text-[11px] text-slate-400\">{Escape(r.detail)}</td>");
                sb.AppendLine("          </tr>");
            }
            sb.AppendLine("          </tbody>");
            sb.AppendLine("        </table>");
            sb.AppendLine("      </div>");
            sb.AppendLine("    </div>");
        }

        private static string Escape(string s) {
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }
    }
}
