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
            sb.AppendLine("</style></head><body>");
            sb.AppendLine($"<h1>SeeDrift — night {DateTime.Now:yyyy-MM-dd}</h1>");
            sb.AppendLine($"<p>Generated {DateTime.Now:HH:mm} · {targets.Count} target{(targets.Count == 1 ? "" : "s")}</p>");

            for (var t = 0; t < targets.Count; t++) {
                var target = targets[t];
                var samples = target.Samples;
                if (t > 0) sb.AppendLine("<hr/>");

                var pixel = samples.Count > 0 && samples[0].IsPixelPath;
                var xTitle = pixel ? "Cumulative X (pixels)" : "ΔRA (arcsec)";
                var yTitle = pixel ? "Cumulative Y (pixels)" : "ΔDec (arcsec)";
                var canvasId = $"c{t}";

                sb.AppendLine($"<h2>{Escape(target.Name)}</h2>");
                sb.AppendLine($"<p>{samples.Count} frames · stopped {target.StoppedUtc.ToLocalTime():HH:mm}</p>");
                sb.AppendLine($"<canvas id=\"{canvasId}\"></canvas>");

                var pts = new StringBuilder("[");
                for (var i = 0; i < samples.Count; i++) {
                    if (i > 0) pts.Append(',');
                    if (pixel) {
                        pts.AppendFormat(CultureInfo.InvariantCulture,
                            "{{\"x\":{0},\"y\":{1}}}",
                            samples[i].CumulativePixelX!.Value,
                            samples[i].CumulativePixelY!.Value);
                    } else {
                        pts.AppendFormat(CultureInfo.InvariantCulture,
                            "{{\"x\":{0},\"y\":{1}}}",
                            Math.Round(samples[i].DeltaRaArcSec, 4),
                            Math.Round(samples[i].DeltaDecArcSec, 4));
                    }
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
            }

            sb.AppendLine("</body></html>");
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        public static void WriteReport(string path, IReadOnlyList<DriftSample> samples, string title) {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var pixel = samples.Count > 0 && samples[0].IsPixelPath;
            var scatterPts = new StringBuilder();
            scatterPts.Append('[');
            if (pixel) {
                for (var i = 0; i < samples.Count; i++) {
                    if (i > 0) scatterPts.Append(',');
                    var px = samples[i].CumulativePixelX!.Value;
                    var py = samples[i].CumulativePixelY!.Value;
                    scatterPts.AppendFormat(CultureInfo.InvariantCulture, "{{\"x\":{0},\"y\":{1}}}", px, py);
                }
            } else {
                for (var i = 0; i < samples.Count; i++) {
                    if (i > 0) scatterPts.Append(',');
                    var dRa = Math.Round(samples[i].DeltaRaArcSec, 4);
                    var dDec = Math.Round(samples[i].DeltaDecArcSec, 4);
                    scatterPts.AppendFormat(CultureInfo.InvariantCulture, "{{\"x\":{0},\"y\":{1}}}", dRa, dDec);
                }
            }
            scatterPts.Append(']');

            var xTitle = pixel ? "Cumulative X (pixels)" : "ΔRA (arcsec)";
            var yTitle = pixel ? "Cumulative Y (pixels)" : "ΔDec (arcsec)";
            var blurb = pixel
                ? $"Frames: {samples.Count} · Cumulative pixel shifts from phase correlation · frame 1 = origin (0, 0)"
                : $"Frames: {samples.Count} · ΔRA / ΔDec in arcsec relative to frame 1 · sorted by DATE-OBS";

            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang=\"en\"><head><meta charset=\"utf-8\"/>");
            sb.AppendLine($"<title>{Escape(title)}</title>");
            sb.AppendLine("<script src=\"https://cdn.jsdelivr.net/npm/chart.js@4.4.1/dist/chart.umd.min.js\"></script>");
            sb.AppendLine("<style>body{font-family:Segoe UI,Arial,sans-serif;margin:24px;background:#141418;color:#e8e8ee}");
            sb.AppendLine("h1{font-size:1.2rem} canvas{max-width:100%;max-height:70vh}</style></head><body>");
            sb.AppendLine($"<h1>{Escape(title)}</h1>");
            sb.AppendLine($"<p>{Escape(blurb)} · Generated {DateTime.Now:yyyy-MM-dd HH:mm}</p>");
            sb.AppendLine("<canvas id=\"c\"></canvas>");
            sb.AppendLine("<script>");
            sb.AppendLine($"const pts = {scatterPts};");
            sb.AppendLine("const ctx = document.getElementById('c');");
            sb.AppendLine("new Chart(ctx, {");
            sb.AppendLine("  type: 'scatter',");
            sb.AppendLine("  data: {");
            sb.AppendLine("    datasets: [{");
            sb.AppendLine("      label: 'Pointing path (frame order)',");
            sb.AppendLine("      data: pts,");
            sb.AppendLine("      borderColor: '#64c8ff',");
            sb.AppendLine("      backgroundColor: 'rgba(100,200,255,0.35)',");
            sb.AppendLine("      showLine: true,");
            sb.AppendLine("      tension: 0.12,");
            sb.AppendLine("      pointRadius: 3,");
            sb.AppendLine("      borderWidth: 1.5");
            sb.AppendLine("    }]");
            sb.AppendLine("  },");
            sb.AppendLine("  options: {");
            sb.AppendLine("    responsive: true,");
            sb.AppendLine("    maintainAspectRatio: true,");
            sb.AppendLine("    scales: {");
            sb.AppendLine("      x: {");
            sb.AppendLine($"        title: {{ display: true, text: '{xTitle}', color: '#bbb' }},");
            sb.AppendLine("        grid: { color: 'rgba(255,255,255,0.08)' },");
            sb.AppendLine("        ticks: { color: '#aaa' }");
            sb.AppendLine("      },");
            sb.AppendLine("      y: {");
            sb.AppendLine($"        title: {{ display: true, text: '{yTitle}', color: '#bbb' }},");
            sb.AppendLine("        grid: { color: 'rgba(255,255,255,0.08)' },");
            sb.AppendLine("        ticks: { color: '#aaa' }");
            sb.AppendLine("      }");
            sb.AppendLine("    },");
            sb.AppendLine("    plugins: { legend: { labels: { color: '#ccc' } } }");
            sb.AppendLine("  }");
            sb.AppendLine("});");
            sb.AppendLine("</script></body></html>");

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        private static string Escape(string s) {
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }
    }
}
