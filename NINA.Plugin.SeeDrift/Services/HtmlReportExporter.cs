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

            var ra = samples.Select(s => Math.Round(s.DeltaRaArcSec, 4)).ToList();
            var dec = samples.Select(s => Math.Round(s.DeltaDecArcSec, 4)).ToList();
            var scatterPts = new StringBuilder();
            scatterPts.Append('[');
            for (var i = 0; i < samples.Count; i++) {
                if (i > 0) scatterPts.Append(',');
                scatterPts.AppendFormat(CultureInfo.InvariantCulture, "{{\"x\":{0},\"y\":{1}}}", ra[i], dec[i]);
            }
            scatterPts.Append(']');

            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang=\"en\"><head><meta charset=\"utf-8\"/>");
            sb.AppendLine($"<title>{Escape(title)}</title>");
            sb.AppendLine("<script src=\"https://cdn.jsdelivr.net/npm/chart.js@4.4.1/dist/chart.umd.min.js\"></script>");
            sb.AppendLine("<style>body{font-family:Segoe UI,Arial,sans-serif;margin:24px;background:#141418;color:#e8e8ee}");
            sb.AppendLine("h1{font-size:1.2rem} canvas{max-width:100%;max-height:70vh}</style></head><body>");
            sb.AppendLine($"<h1>{Escape(title)}</h1>");
            sb.AppendLine($"<p>Frames: {samples.Count} · ΔRA horizontal, ΔDec vertical (arcsec from first frame) · Generated {DateTime.Now:yyyy-MM-dd HH:mm}</p>");
            sb.AppendLine("<canvas id=\"c\"></canvas>");
            sb.AppendLine("<script>");
            sb.AppendLine($"const pts = {scatterPts};");
            sb.AppendLine(@"const ctx = document.getElementById('c');
new Chart(ctx, {
  type: 'scatter',
  data: {
    datasets: [{
      label: 'Drift path (frame order)',
      data: pts,
      borderColor: '#64c8ff',
      backgroundColor: 'rgba(100,200,255,0.35)',
      showLine: true,
      tension: 0.12,
      pointRadius: 3,
      borderWidth: 1.5
    }]
  },
  options: {
    responsive: true,
    maintainAspectRatio: true,
    aspectRatio: 1,
    scales: {
      x: {
        title: { display: true, text: 'ΔRA (arcsec)', color: '#bbb' },
        grid: { color: 'rgba(255,255,255,0.08)' },
        ticks: { color: '#aaa' }
      },
      y: {
        title: { display: true, text: 'ΔDec (arcsec)', color: '#bbb' },
        grid: { color: 'rgba(255,255,255,0.08)' },
        ticks: { color: '#aaa' }
      }
    },
    plugins: { legend: { labels: { color: '#ccc' } } }
  }
});");
            sb.AppendLine("</script></body></html>");

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        private static string Escape(string s) {
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }
    }
}
