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

            var labels = samples.Select(s => s.FrameIndex.ToString(CultureInfo.InvariantCulture)).ToList();
            var ra = samples.Select(s => Math.Round(s.DeltaRaArcSec, 3)).ToList();
            var dec = samples.Select(s => Math.Round(s.DeltaDecArcSec, 3)).ToList();

            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang=\"en\"><head><meta charset=\"utf-8\"/>");
            sb.AppendLine($"<title>{Escape(title)}</title>");
            sb.AppendLine("<script src=\"https://cdn.jsdelivr.net/npm/chart.js@4.4.1/dist/chart.umd.min.js\"></script>");
            sb.AppendLine("<style>body{font-family:Segoe UI,Arial,sans-serif;margin:24px;background:#111;color:#eee}");
            sb.AppendLine("h1{font-size:1.2rem} canvas{max-width:100%}</style></head><body>");
            sb.AppendLine($"<h1>{Escape(title)}</h1>");
            sb.AppendLine($"<p>Frames: {samples.Count} · Generated {DateTime.Now:yyyy-MM-dd HH:mm}</p>");
            sb.AppendLine("<canvas id=\"c\" height=\"120\"></canvas>");
            sb.AppendLine("<script>");
            sb.AppendLine($"const labels = {ToJsonArray(labels)};");
            sb.AppendLine($"const ra = {ToJsonArray(ra)};");
            sb.AppendLine($"const dec = {ToJsonArray(dec)};");
            sb.AppendLine(@"const ctx = document.getElementById('c');
new Chart(ctx, {
  type: 'line',
  data: {
    labels,
    datasets: [
      { label: 'ΔRA (arcsec)', data: ra, borderColor: '#6cf', backgroundColor: 'transparent', tension: 0.15, pointRadius: 2 },
      { label: 'ΔDec (arcsec)', data: dec, borderColor: '#f9a', backgroundColor: 'transparent', tension: 0.15, pointRadius: 2 }
    ]
  },
  options: {
    responsive: true,
    scales: {
      x: { title: { display: true, text: 'Frame index (0-based)' } },
      y: { title: { display: true, text: 'arcsec from first frame' } }
    }
  }
});");
            sb.AppendLine("</script></body></html>");

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        private static string Escape(string s) {
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }

        private static string ToJsonArray(IReadOnlyList<string> items) {
            return "[" + string.Join(",", items.Select(i => "\"" + Escape(i) + "\"")) + "]";
        }

        private static string ToJsonArray(IReadOnlyList<double> items) {
            return "[" + string.Join(",", items.Select(i => i.ToString(CultureInfo.InvariantCulture))) + "]";
        }
    }
}
