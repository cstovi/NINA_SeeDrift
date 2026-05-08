using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NINA.Plugin.SeeDrift.Models;

namespace NINA.Plugin.SeeDrift.Services {

    public static class HtmlReportExporter {

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
                var raDeg = samples.Select(s => Math.Round(s.RawRaHours * 15.0, 6)).ToList();
                var decDeg = samples.Select(s => Math.Round(s.RawDecDeg, 6)).ToList();
                for (var i = 0; i < samples.Count; i++) {
                    if (i > 0) scatterPts.Append(',');
                    scatterPts.AppendFormat(CultureInfo.InvariantCulture, "{{\"x\":{0},\"y\":{1}}}", raDeg[i], decDeg[i]);
                }
            }
            scatterPts.Append(']');

            var xTitle = pixel ? "Cumulative X (pixels)" : "RA (degrees)";
            var yTitle = pixel ? "Cumulative Y (pixels)" : "Dec (degrees)";
            var blurb = pixel
                ? $"Frames: {samples.Count} · Cumulative shifts from phase correlation on central crops (detector coords, start at 0,0)"
                : $"Frames: {samples.Count} · Horizontal RA°, vertical Dec° (FITS per frame, chronological order)";

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
