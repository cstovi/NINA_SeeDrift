using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using NINA.Plugin.SeeDrift.Models;

namespace NINA.Plugin.SeeDrift.Services {

    public static class HtmlReportExporter {

        // CDN pins (night HTML is opened offline-capable only after first load; versions documented for reproducibility)
        private const string CdnHammer = "https://cdn.jsdelivr.net/npm/hammerjs@2.0.8/hammer.min.js";
        private const string CdnChartJs = "https://cdn.jsdelivr.net/npm/chart.js@4.4.1/dist/chart.umd.min.js";
        private const string CdnChartZoom = "https://cdn.jsdelivr.net/npm/chartjs-plugin-zoom@2.2.0/dist/chartjs-plugin-zoom.min.js";
        private const string CdnTailwind = "https://cdn.tailwindcss.com";

        /// <summary>
        /// Writes a single rolling nightly HTML with one chart section per completed batch (each SeeDrift Stop or Test report run).
        /// Overwrites the file each call so it always reflects the full session so far.
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
                var target = targets[t];
                var samples = target.Samples;
                var sectionClass = t < targets.Count - 1
                    ? "mb-12 border-b border-slate-800 pb-12"
                    : "pb-4";

                const string xTitle = "ΔRA (arcsec)";
                const string yTitle = "ΔDec (arcsec)";
                var canvasId = $"c{t}";
                var title = SummarizeTargetsForBatch(samples);
                var titleJson = JsonSerializer.Serialize(title);
                var labelJson = JsonSerializer.Serialize(title + " — drift path");

                sb.AppendLine($"<section class=\"{sectionClass}\">");
                sb.AppendLine($"  <h2 class=\"text-lg font-semibold text-sky-300\">{Escape(title)}</h2>");
                sb.AppendLine($"  <p class=\"mt-1 text-sm text-slate-400\">{samples.Count} frames · plate-solved drift · stopped {target.StoppedUtc.ToLocalTime():HH:mm}</p>");
                sb.AppendLine($"  <div class=\"seedrift-chart-box mt-4 rounded-lg border border-slate-700 bg-slate-900/60 p-2\">");
                sb.AppendLine($"    <canvas id=\"{canvasId}\"></canvas>");
                sb.AppendLine("  </div>");

                var pts = new StringBuilder("[");
                for (var i = 0; i < samples.Count; i++) {
                    if (i > 0) pts.Append(',');
                    pts.AppendFormat(CultureInfo.InvariantCulture,
                        "{{\"x\":{0},\"y\":{1}}}",
                        Math.Round(samples[i].DeltaRaArcSec, 4),
                        Math.Round(samples[i].DeltaDecArcSec, 4));
                }
                pts.Append(']');

                sb.AppendLine("<script>");
                sb.AppendLine("(function(){");
                sb.AppendLine($"  const pts = {pts};");
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

                AppendSequencerSection(sb, samples);
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

        private static void AppendSequencerSection(StringBuilder sb, IReadOnlyList<DriftSample> samples) {
            var rows = new List<(string fromFn, string toFn, string kind, string detail)>();
            for (var i = 0; i < samples.Count; i++) {
                var m = samples[i].EdgeSequencerMarkers;
                if (m == null || m.Count == 0) continue;
                var prevFn = i > 0 ? samples[i - 1].FileName : "";
                foreach (var e in m) {
                    var kind = e.IsDither ? "DitherAfterExposures" : "CenterAfterDrift";
                    rows.Add((prevFn, samples[i].FileName, kind, e.Tooltip));
                }
            }

            sb.AppendLine("  <div class=\"mt-8\">");
            sb.AppendLine("    <h3 class=\"text-sm font-semibold uppercase tracking-wide text-sky-400\">Sequencer events (NINA logs)</h3>");

            if (rows.Count == 0) {
                sb.AppendLine("    <div class=\"mt-3 rounded-lg border border-slate-700 bg-slate-900/40 p-4\">");
                sb.AppendLine("      <p class=\"text-sm text-slate-300\">No correlated dither or center-after-drift events for this batch.</p>");
                sb.AppendLine("      <p class=\"mt-2 text-xs leading-relaxed text-slate-500\">SeeDrift matches lines in <code class=\"rounded bg-slate-800 px-1 py-0.5 text-slate-300\">%LocalAppData%\\NINA\\Logs</code> to your frame times (NINA lines are usually local wall time). If logs were rotated, observation times do not overlap your captures, or session paths differ, this section stays empty — it is not a bug in the drift chart.</p>");
                sb.AppendLine("    </div>");
                sb.AppendLine("  </div>");
                return;
            }

            sb.AppendLine("    <p class=\"mt-2 text-xs text-slate-500\">Between-frame triggers aligned to exposure intervals (requires matching session log).</p>");
            sb.AppendLine("    <div class=\"mt-3 overflow-x-auto rounded-lg border border-slate-700\">");
            sb.AppendLine("      <table class=\"min-w-full divide-y divide-slate-700 text-left text-xs\">");
            sb.AppendLine("        <thead class=\"bg-slate-900/80 text-sky-300\"><tr>");
            sb.AppendLine("          <th class=\"whitespace-nowrap px-3 py-2 font-medium\">From frame</th>");
            sb.AppendLine("          <th class=\"whitespace-nowrap px-3 py-2 font-medium\">To frame</th>");
            sb.AppendLine("          <th class=\"whitespace-nowrap px-3 py-2 font-medium\">Kind</th>");
            sb.AppendLine("          <th class=\"px-3 py-2 font-medium\">Detail</th>");
            sb.AppendLine("        </tr></thead>");
            sb.AppendLine("        <tbody class=\"divide-y divide-slate-800 bg-slate-950/40 text-slate-300\">");
            foreach (var r in rows) {
                sb.AppendLine("        <tr class=\"align-top\">");
                sb.AppendLine($"          <td class=\"whitespace-nowrap px-3 py-2 font-mono text-[11px]\">{Escape(r.fromFn)}</td>");
                sb.AppendLine($"          <td class=\"whitespace-nowrap px-3 py-2 font-mono text-[11px]\">{Escape(r.toFn)}</td>");
                sb.AppendLine($"          <td class=\"whitespace-nowrap px-3 py-2\">{Escape(r.kind)}</td>");
                sb.AppendLine($"          <td class=\"whitespace-pre-wrap px-3 py-2 text-[11px] text-slate-400\">{Escape(r.detail)}</td>");
                sb.AppendLine("        </tr>");
            }
            sb.AppendLine("        </tbody>");
            sb.AppendLine("      </table>");
            sb.AppendLine("    </div>");
            sb.AppendLine("  </div>");
        }

        private static string Escape(string s) {
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }
    }
}
