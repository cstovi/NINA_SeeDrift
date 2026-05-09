using System;
using System.Collections.Generic;
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
        /// <param name="minExposuresPerTarget">Targets with fewer solved frames in the batch are omitted from headings and subsections (minimum 1).</param>
        public static void WriteNightReport(
                IReadOnlyList<DriftTrackingService.CompletedTarget> targets, string path,
                int minExposuresPerTarget = 50) {

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var min = Math.Max(1, minExposuresPerTarget);
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
            sb.AppendLine("  /* Sequencer table: wrap long paths so the Detail column keeps usable width */");
            sb.AppendLine("  .seedrift-seq-table { word-break: break-word; overflow-wrap: anywhere; }");
            sb.AppendLine("</style>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body class=\"min-h-full bg-slate-950 text-slate-200 antialiased\">");
            sb.AppendLine("<main class=\"mx-auto max-w-5xl px-4 py-8 sm:px-6\">");
            sb.AppendLine($"<header class=\"mb-10 border-b border-slate-800 pb-6\">");
            sb.AppendLine($"  <h1 class=\"text-xl font-semibold tracking-tight text-white\">SeeDrift — night {Escape(DateTime.Now.ToString("yyyy-MM-dd"))}</h1>");
            sb.AppendLine($"  <p class=\"mt-2 text-sm text-slate-400\">Generated {DateTime.Now:HH:mm} · <span class=\"text-slate-300\">{targets.Count}</span> batch{(targets.Count == 1 ? "" : "es")}</p>");
            sb.AppendLine($"  <p class=\"mt-3 text-xs text-slate-500\">Drag to pan · Wheel to zoom in/out (can zoom out past the data for context) · Double-click chart to reset zoom</p>");
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

                var targetGroups = SplitBatchByTargetInOrder(orderedFull);
                var filteredGroups = targetGroups.Where(g => g.Samples.Count >= min).ToList();
                var allowed = new HashSet<string>(
                    filteredGroups.Select(g => g.Name),
                    StringComparer.OrdinalIgnoreCase);
                var titleSamples = orderedFull.Where(s => allowed.Contains(TargetKey(s))).ToList();
                var batchTitle = filteredGroups.Count == 0
                    ? $"No qualifying targets (≥{min} exposures)"
                    : SummarizeTargetsForBatch(titleSamples);

                sb.AppendLine($"<section class=\"{sectionClass}\">");
                sb.AppendLine($"  <h2 class=\"text-lg font-semibold text-sky-300\">{Escape(batchTitle)}</h2>");
                sb.AppendLine($"  <p class=\"mt-1 text-sm text-slate-400\">{samples.Count} frame{(samples.Count == 1 ? "" : "s")}</p>");

                if (filteredGroups.Count == 0) {
                    sb.AppendLine("  <div class=\"mt-6 rounded-lg border border-amber-900/40 bg-slate-900/60 p-4\">");
                    sb.AppendLine($"    <p class=\"text-sm text-slate-300\">No target in this batch had at least <span class=\"text-amber-300\">{min}</span> solved exposure{(min == 1 ? "" : "s")}. Lower <span class=\"text-sky-300\">Minimum exposures per target</span> in Plugins → SeeDrift, or capture more frames per target.</p>");
                    sb.AppendLine("  </div>");
                    sb.AppendLine("</section>");
                    continue;
                }

                for (var g = 0; g < filteredGroups.Count; g++) {
                    var (targetName, grp) = filteredGroups[g];
                    var canvasId = $"c{t}_{g}";
                    var labelJson = JsonSerializer.Serialize($"{targetName} — drift path");

                    sb.AppendLine($"  <div class=\"{(g > 0 ? "mt-12 border-t border-slate-800 pt-10" : "mt-8")}\">");
                    sb.AppendLine($"    <h3 class=\"text-base font-semibold text-sky-200\">Target: {Escape(targetName)}</h3>");
                    sb.AppendLine($"    <p class=\"mt-1 text-xs text-slate-500\">{grp.Count} frame{(grp.Count == 1 ? "" : "s")} · Δ vs first solved frame of this target</p>");
                    sb.AppendLine($"    <div class=\"seedrift-chart-box mt-4 rounded-lg border border-slate-700 bg-slate-900/60 p-2\">");
                    sb.AppendLine($"      <canvas id=\"{canvasId}\"></canvas>");
                    sb.AppendLine("    </div>");
                    sb.AppendLine($"    <p class=\"mt-2 text-xs text-slate-400\">{Escape(FormatMovementTotalsLine(grp))}</p>");
                    sb.AppendLine("    <p class=\"mt-2 text-xs text-slate-500\">Path: <span class=\"text-emerald-400\">●</span> start · <span class=\"text-orange-400\">●</span> end (<span class=\"text-amber-400\">●</span> if one frame). Log triggers: <span class=\"text-purple-400\">△</span> dither · <span class=\"text-pink-400\">□</span> center — placed along the segment between frames. Hover path for file name; hover △/□ for log detail.</p>");

                    var ptsJson = FormatScatterPointsJsonAnchored(grp);
                    var edgeJson = FormatEdgeMidpointMarkersJson(grp);

                    sb.AppendLine("<script>");
                    sb.AppendLine("(function(){");
                    sb.AppendLine($"  const pts = {ptsJson};");
                    sb.AppendLine($"  const edgeMarkers = {edgeJson};");
                    sb.AppendLine($"  const datasetLabel = {labelJson};");
                    sb.AppendLine($"  const el = document.getElementById('{canvasId}');");
                    sb.AppendLine("  function pointRadiusFn(ctx) {");
                    sb.AppendLine("    var n = ctx.dataset.data.length;");
                    sb.AppendLine("    if (n <= 1) return 7;");
                    sb.AppendLine("    var i = ctx.dataIndex;");
                    sb.AppendLine("    return (i === 0 || i === n - 1) ? 7 : 3;");
                    sb.AppendLine("  }");
                    sb.AppendLine("  function pointBgFn(ctx) {");
                    sb.AppendLine("    var n = ctx.dataset.data.length;");
                    sb.AppendLine("    var i = ctx.dataIndex;");
                    sb.AppendLine("    if (n <= 1) return 'rgba(234,179,8,0.85)';");
                    sb.AppendLine("    if (i === 0) return 'rgba(52,211,153,0.95)';");
                    sb.AppendLine("    if (i === n - 1) return 'rgba(251,146,60,0.95)';");
                    sb.AppendLine("    return 'rgba(56,189,248,0.35)';");
                    sb.AppendLine("  }");
                    sb.AppendLine("  function pointBorderFn(ctx) {");
                    sb.AppendLine("    var n = ctx.dataset.data.length;");
                    sb.AppendLine("    var i = ctx.dataIndex;");
                    sb.AppendLine("    if (n <= 1) return '#fbbf24';");
                    sb.AppendLine("    if (i === 0) return '#34d399';");
                    sb.AppendLine("    if (i === n - 1) return '#fb923c';");
                    sb.AppendLine("    return '#38bdf8';");
                    sb.AppendLine("  }");
                    sb.AppendLine("  var datasets = [{");
                    sb.AppendLine("      label: datasetLabel,");
                    sb.AppendLine("      data: pts, borderColor: '#38bdf8',");
                    sb.AppendLine("      backgroundColor: pointBgFn,");
                    sb.AppendLine("      pointBorderColor: pointBorderFn,");
                    sb.AppendLine("      pointBorderWidth: 2,");
                    sb.AppendLine("      showLine: true, tension: 0.12, pointRadius: pointRadiusFn, borderWidth: 1.5");
                    sb.AppendLine("    }];");
                    sb.AppendLine("  if (edgeMarkers.length > 0) {");
                    sb.AppendLine("    datasets.push({");
                    sb.AppendLine("      label: 'Sequencer (between frames)',");
                    sb.AppendLine("      data: edgeMarkers,");
                    sb.AppendLine("      showLine: false,");
                    sb.AppendLine("      borderColor: '#f8fafc',");
                    sb.AppendLine("      backgroundColor: function(ctx) {");
                    sb.AppendLine("        var d = ctx.raw;");
                    sb.AppendLine("        return d && d.isDither ? 'rgba(168,85,247,0.92)' : 'rgba(244,114,182,0.92)';");
                    sb.AppendLine("      },");
                    sb.AppendLine("      pointBorderColor: '#0f172a',");
                    sb.AppendLine("      pointBorderWidth: 1,");
                    sb.AppendLine("      pointRadius: 6,");
                    sb.AppendLine("      pointHoverRadius: 8,");
                    sb.AppendLine("      hitRadius: 14,");
                    sb.AppendLine("      pointStyle: function(ctx) {");
                    sb.AppendLine("        var d = ctx.raw;");
                    sb.AppendLine("        return (d && d.isDither) ? 'triangle' : 'rect';");
                    sb.AppendLine("      }");
                    sb.AppendLine("    });");
                    sb.AppendLine("  }");
                    sb.AppendLine("  const chart = new Chart(el, {");
                    sb.AppendLine("    type: 'scatter',");
                    sb.AppendLine("    data: { datasets: datasets },");
                    sb.AppendLine("    options: {");
                    sb.AppendLine("      responsive: true, maintainAspectRatio: false,");
                    sb.AppendLine("      scales: {");
                    sb.AppendLine($"        x: {{ title: {{ display: true, text: '{xTitle}', color: '#94a3b8' }}, grid: {{ color: 'rgba(148,163,184,0.12)' }}, ticks: {{ color: '#cbd5e1' }}, border: {{ color: '#475569' }} }},");
                    sb.AppendLine($"        y: {{ reverse: true, title: {{ display: true, text: '{yTitle}', color: '#94a3b8' }}, grid: {{ color: 'rgba(148,163,184,0.12)' }}, ticks: {{ color: '#cbd5e1' }}, border: {{ color: '#475569' }} }}");
                    sb.AppendLine("      },");
                    sb.AppendLine("      plugins: {");
                    sb.AppendLine("        tooltip: {");
                    sb.AppendLine("          callbacks: {");
                    sb.AppendLine("            title: function() { return ''; },");
                    sb.AppendLine("            label: function(ctx) {");
                    sb.AppendLine("              if (ctx.datasetIndex > 0) {");
                    sb.AppendLine("                var raw = ctx.raw;");
                    sb.AppendLine("                var tip = raw && raw.tooltip ? String(raw.tooltip) : '';");
                    sb.AppendLine("                var kind = raw && raw.isDither ? 'DitherAfterExposures / pulse' : 'CenterAfterDrift';");
                    sb.AppendLine("                var hdr = kind + ' · ΔRA ' + ctx.parsed.x + '\" · ΔDec ' + ctx.parsed.y + '\"';");
                    sb.AppendLine("                var lines = tip ? tip.split(/\\r?\\n/) : [];");
                    sb.AppendLine("                return [hdr].concat(lines);");
                    sb.AppendLine("              }");
                    sb.AppendLine("              var raw = ctx.raw;");
                    sb.AppendLine("              var fn = raw && raw.filename ? String(raw.filename) : '';");
                    sb.AppendLine("              var line2 = 'ΔRA ' + ctx.parsed.x + '\" · ΔDec ' + ctx.parsed.y + '\"';");
                    sb.AppendLine("              return fn ? [fn, line2] : [line2];");
                    sb.AppendLine("            }");
                    sb.AppendLine("          }");
                    sb.AppendLine("        },");
                    sb.AppendLine("        legend: { labels: { color: '#e2e8f0', filter: function(item) { return item.datasetIndex === 0; } } },");
                    sb.AppendLine("        zoom: {");
                    sb.AppendLine("          pan: { enabled: true, mode: 'xy' },");
                    sb.AppendLine("          zoom: {");
                    sb.AppendLine("            wheel: { enabled: true },");
                    sb.AppendLine("            pinch: { enabled: true },");
                    sb.AppendLine("            mode: 'xy'");
                    sb.AppendLine("          }");
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
        /// One Stop/Test batch may include frames with different FITS OBJECT names; builds a readable heading from distinct
        /// targets in first-seen frame order (same order as Target subsections and charts below).
        /// </summary>
        public static string SummarizeTargetsForBatch(IReadOnlyList<DriftSample> samples) {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();
            foreach (var s in samples) {
                var label = string.IsNullOrWhiteSpace(s.TargetName) ? "Unknown" : s.TargetName.Trim();
                if (seen.Add(label))
                    order.Add(label);
            }

            if (order.Count == 0)
                return "Unknown";
            if (order.Count == 1)
                return order[0];
            const int maxListed = 4;
            if (order.Count <= maxListed)
                return string.Join(" · ", order);
            return string.Join(" · ", order.Take(maxListed)) + $" (+{order.Count - maxListed} more)";
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
            return orderKeys.Select(k => (Name: k, Samples: groups[k])).ToList();
        }

        private static string TargetKey(DriftSample s) =>
            string.IsNullOrWhiteSpace(s.TargetName) ? "Unknown" : s.TargetName.Trim();

        /// <summary>ΔRA/ΔDec arcseconds for frame <paramref name="index"/> vs first frame of <paramref name="group"/>.</summary>
        private static void GetAnchoredPlotPoint(IReadOnlyList<DriftSample> group, int index, out double x, out double y) {
            var r0 = group[0].RawRaHours;
            var d0 = group[0].RawDecDeg;
            AstrometryMath.DeltaArcSec(r0, d0, group[index].RawRaHours, group[index].RawDecDeg,
                out var dRa, out var dDec);
            x = Math.Round(dRa, 4);
            y = Math.Round(dDec, 4);
        }

        /// <summary>
        /// Sums absolute ΔRA and ΔDec step sizes between consecutive plotted points (same geometry as the chart).
        /// </summary>
        private static void SumAbsoluteSegmentMovementAlongTrace(IReadOnlyList<DriftSample> group, out double sumAbsRa, out double sumAbsDec) {
            sumAbsRa = 0;
            sumAbsDec = 0;
            if (group.Count < 2)
                return;

            GetAnchoredPlotPoint(group, 0, out var prevX, out var prevY);
            for (var i = 1; i < group.Count; i++) {
                GetAnchoredPlotPoint(group, i, out var x, out var y);
                sumAbsRa += Math.Abs(x - prevX);
                sumAbsDec += Math.Abs(y - prevY);
                prevX = x;
                prevY = y;
            }
        }

        private static string FormatMovementTotalsLine(IReadOnlyList<DriftSample> group) {
            if (group.Count < 2)
                return "Single frame — no frame-to-frame movement to sum.";
            SumAbsoluteSegmentMovementAlongTrace(group, out var sumRa, out var sumDec);
            return FormattableString.Invariant(
                $"Total movement along trace (Σ |Δstep| between consecutive frames): ΔRA {sumRa:0.###}″ · ΔDec {sumDec:0.###}″");
        }

        private static string FormatScatterPointsJsonAnchored(IReadOnlyList<DriftSample> group) {
            if (group.Count == 0)
                return "[]";
            var points = new List<(double x, double y, string filename)>(group.Count);
            for (var i = 0; i < group.Count; i++) {
                GetAnchoredPlotPoint(group, i, out var px, out var py);
                var fn = group[i].FileName ?? "";
                points.Add((px, py, fn));
            }

            return JsonSerializer.Serialize(
                points.Select(p => new { x = p.x, y = p.y, filename = p.filename }));
        }

        /// <summary>
        /// Midpoints along each consecutive pair (in plot space), spaced along the chord when multiple markers share one edge.
        /// </summary>
        private static string FormatEdgeMidpointMarkersJson(IReadOnlyList<DriftSample> group) {
            if (group.Count < 2)
                return "[]";
            var list = new List<object>();
            for (var i = 1; i < group.Count; i++) {
                var markers = group[i].EdgeSequencerMarkers;
                if (markers == null || markers.Count == 0)
                    continue;

                GetAnchoredPlotPoint(group, i - 1, out var px, out var py);
                GetAnchoredPlotPoint(group, i, out var cx, out var cy);
                var n = markers.Count;
                for (var j = 0; j < n; j++) {
                    var t = (j + 1.0) / (n + 1.0);
                    var mx = px + t * (cx - px);
                    var my = py + t * (cy - py);
                    list.Add(new {
                        x = Math.Round(mx, 4),
                        y = Math.Round(my, 4),
                        isDither = markers[j].IsDither,
                        tooltip = markers[j].Tooltip ?? ""
                    });
                }
            }

            return JsonSerializer.Serialize(list);
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
            sb.AppendLine("        <table class=\"seedrift-seq-table min-w-full table-fixed divide-y divide-slate-700 text-left text-xs\">");
            sb.AppendLine("          <thead class=\"bg-slate-900/80 text-sky-300\"><tr>");
            sb.AppendLine("            <th class=\"w-[22%] px-3 py-2 font-medium\">From frame</th>");
            sb.AppendLine("            <th class=\"w-[22%] px-3 py-2 font-medium\">To frame</th>");
            sb.AppendLine("            <th class=\"w-[14%] whitespace-nowrap px-3 py-2 font-medium\">Kind</th>");
            sb.AppendLine("            <th class=\"w-[42%] px-3 py-2 font-medium\">Detail</th>");
            sb.AppendLine("          </tr></thead>");
            sb.AppendLine("          <tbody class=\"divide-y divide-slate-800 bg-slate-950/40 text-slate-300\">");
            foreach (var r in rows) {
                sb.AppendLine("          <tr class=\"align-top\">");
                sb.AppendLine($"            <td class=\"break-all px-3 py-2 font-mono text-[11px] leading-snug\">{Escape(r.fromFn)}</td>");
                sb.AppendLine($"            <td class=\"break-all px-3 py-2 font-mono text-[11px] leading-snug\">{Escape(r.toFn)}</td>");
                sb.AppendLine($"            <td class=\"whitespace-normal px-3 py-2 align-top\">{Escape(r.kind)}</td>");
                sb.AppendLine($"            <td class=\"break-words px-3 py-2 text-[11px] leading-snug text-slate-400\">{Escape(r.detail)}</td>");
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
