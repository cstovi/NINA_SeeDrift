using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NINA.Plugin.SeeDrift.Models;

namespace NINA.Plugin.SeeDrift.Services {

    public static class HtmlReportExporter {

        /// <summary>
        /// Writes a single rolling nightly HTML with one chart section per completed target.
        /// Overwrites the file each call so it always reflects the full session so far.
        /// </summary>
        public static void WriteNightReport(
                IReadOnlyList<DriftTrackingService.CompletedTarget> targets, string path) {

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang=\"en\"><head><meta charset=\"utf-8\"/>");
            sb.AppendLine($"<title>SeeDrift — night {DateTime.Now:yyyy-MM-dd}</title>");
            sb.AppendLine("<script src=\"https://cdn.jsdelivr.net/npm/chart.js@4.4.1/dist/chart.umd.min.js\"></script>");
            sb.AppendLine("<style>");
            sb.AppendLine("  body{font-family:Segoe UI,Arial,sans-serif;margin:24px;background:#141418;color:#e8e8ee}");
            sb.AppendLine("  h1{font-size:1.3rem;margin-bottom:4px}");
            sb.AppendLine("  h2{font-size:1.05rem;margin:32px 0 4px;color:#aac8ff}");
            sb.AppendLine("  p{margin:2px 0 10px;font-size:0.85rem;color:#888}");
            sb.AppendLine("  canvas{max-width:100%;max-height:55vh;margin-bottom:8px}");
            sb.AppendLine("  hr{border:none;border-top:1px solid #333;margin:24px 0}");
            sb.AppendLine("  table.logtbl{border-collapse:collapse;font-size:0.8rem;margin:12px 0 0}");
            sb.AppendLine("  table.logtbl th,table.logtbl td{border:1px solid #333;padding:4px 8px;text-align:left}");
            sb.AppendLine("  table.logtbl th{color:#aac8ff}");
            sb.AppendLine("</style></head><body>");
            sb.AppendLine($"<h1>SeeDrift — night {DateTime.Now:yyyy-MM-dd}</h1>");
            sb.AppendLine($"<p>Generated {DateTime.Now:HH:mm} · {targets.Count} target{(targets.Count == 1 ? "" : "s")}</p>");

            for (var t = 0; t < targets.Count; t++) {
                var target = targets[t];
                var samples = target.Samples;
                if (t > 0) sb.AppendLine("<hr/>");

                const string xTitle = "ΔRA (arcsec)";
                const string yTitle = "ΔDec (arcsec)";
                var canvasId = $"c{t}";

                sb.AppendLine($"<h2>{Escape(target.Name)}</h2>");
                sb.AppendLine($"<p>{samples.Count} frames · plate-solved drift · stopped {target.StoppedUtc.ToLocalTime():HH:mm}</p>");
                sb.AppendLine($"<canvas id=\"{canvasId}\"></canvas>");

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
                sb.AppendLine($"(function(){{");
                sb.AppendLine($"  const pts = {pts};");
                sb.AppendLine($"  new Chart(document.getElementById('{canvasId}'), {{");
                sb.AppendLine("    type: 'scatter',");
                sb.AppendLine("    data: { datasets: [{");
                sb.AppendLine($"      label: '{Escape(target.Name)}',");
                sb.AppendLine("      data: pts, borderColor: '#64c8ff',");
                sb.AppendLine("      backgroundColor: 'rgba(100,200,255,0.35)',");
                sb.AppendLine("      showLine: true, tension: 0.12, pointRadius: 3, borderWidth: 1.5");
                sb.AppendLine("    }]},");
                sb.AppendLine("    options: { responsive: true, maintainAspectRatio: true,");
                sb.AppendLine("      scales: {");
                sb.AppendLine($"        x: {{ title: {{ display: true, text: '{xTitle}', color: '#bbb' }}, grid: {{ color: 'rgba(255,255,255,0.08)' }}, ticks: {{ color: '#aaa' }} }},");
                sb.AppendLine($"        y: {{ title: {{ display: true, text: '{yTitle}', color: '#bbb' }}, grid: {{ color: 'rgba(255,255,255,0.08)' }}, ticks: {{ color: '#aaa' }} }}");
                sb.AppendLine("      },");
                sb.AppendLine("      plugins: { legend: { labels: { color: '#ccc' } } }");
                sb.AppendLine("    }");
                sb.AppendLine("  });");
                sb.AppendLine("})();");
                sb.AppendLine("</script>");
                AppendSequencerEdgeTable(sb, samples);
            }

            sb.AppendLine("</body></html>");
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        private static void AppendSequencerEdgeTable(StringBuilder sb, IReadOnlyList<DriftSample> samples) {
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
            if (rows.Count == 0)
                return;

            sb.AppendLine("<h3 style=\"font-size:0.95rem;margin-top:18px;color:#aac8ff\">Sequencer events (NINA logs)</h3>");
            sb.AppendLine("<p style=\"font-size:0.8rem;color:#888\">Between-frame triggers aligned to exposure intervals — requires matching session log.</p>");
            sb.AppendLine("<table class=\"logtbl\"><thead><tr><th>From frame</th><th>To frame</th><th>Kind</th><th>Detail</th></tr></thead><tbody>");
            foreach (var r in rows) {
                sb.AppendLine($"<tr><td>{Escape(r.fromFn)}</td><td>{Escape(r.toFn)}</td><td>{Escape(r.kind)}</td><td style=\"white-space:pre-wrap\">{Escape(r.detail)}</td></tr>");
            }
            sb.AppendLine("</tbody></table>");
        }

        private static string Escape(string s) {
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }
    }
}
