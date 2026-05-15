using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
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

        /// <summary>Fallback SVG motif matching the sequencer icon (axes + drift trace).</summary>
        private const string SeeDriftIconSvgFallback =
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\" width=\"24\" height=\"24\" class=\"shrink-0\" aria-hidden=\"true\" focusable=\"false\">" +
            "<path d=\"M6 18V8H18 M8 15l3-3 3 1 3-4\" fill=\"none\" stroke=\"#38bdf8\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>" +
            "</svg>";

        /// <summary>Embedded PNG logical name — must match <c>EmbeddedResource</c> <c>LogicalName</c> in the csproj.</summary>
        private const string EmbeddedFeaturedImageManifestName = "SeeDriftFeatured.png";

        private const string FeaturedImgClass =
            "max-h-20 w-auto max-w-[min(100vw,18rem)] object-contain object-left rounded border border-slate-700/50 bg-slate-900/40";

        /// <summary>Prefers embedded PNG (offline); then assembly FeaturedImageURL; then <see cref="SeeDriftIconSvgFallback"/>.</summary>
        private static string BuildHeaderBrandMarkup() {
            if (TryReadEmbeddedFeaturedImageAsDataUri(out var dataUri)) {
                return $"<img src=\"{dataUri}\" alt=\"SeeDrift\" class=\"{FeaturedImgClass}\" decoding=\"async\" />";
            }

            var url = TryGetAssemblyMetadataValue("FeaturedImageURL");
            if (!string.IsNullOrWhiteSpace(url)) {
                var safe = Escape(url.Trim());
                return $"<img src=\"{safe}\" alt=\"SeeDrift\" class=\"{FeaturedImgClass}\" loading=\"lazy\" decoding=\"async\" />";
            }

            return SeeDriftIconSvgFallback;
        }

        private static bool TryReadEmbeddedFeaturedImageAsDataUri(out string dataUri) {
            dataUri = "";
            try {
                using var stream = typeof(HtmlReportExporter).Assembly.GetManifestResourceStream(EmbeddedFeaturedImageManifestName);
                if (stream == null)
                    return false;
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                dataUri = "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
                return true;
            } catch {
                return false;
            }
        }

        private static string? TryGetAssemblyMetadataValue(string key) {
            foreach (var attr in typeof(HtmlReportExporter).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()) {
                if (string.Equals(attr.Key, key, StringComparison.Ordinal))
                    return attr.Value;
            }

            return null;
        }

        private static string CurrentPluginVersion() =>
            typeof(HtmlReportExporter).Assembly.GetName().Version?.ToString() ?? "";

        /// <summary>
        /// Writes a single rolling HTML report with one subsection per target within each batch (each Stop or previous-session-report run).
        /// Drift is re-anchored to the first solved frame <em>of that target</em>; sequencer rows only include edges where
        /// both consecutive frames belong to the same target.
        /// </summary>
        /// <param name="minExposuresPerTarget">Targets with fewer solved frames in the batch are omitted from headings and subsections (minimum 1).</param>
        public static void WriteNightReport(
                IReadOnlyList<DriftTrackingService.CompletedTarget> targets, string path,
                int minExposuresPerTarget = 50) {

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var min = Math.Max(1, minExposuresPerTarget);
            var sessionLogDay = SessionReportDates.ResolveSessionCalendarDay(targets);
            var sessionLogLabel = sessionLogDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var seestarDevice = ResolveReportDevice(targets);
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang=\"en\" class=\"h-full\">");
            sb.AppendLine("<head><meta charset=\"utf-8\"/><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"/>");
            sb.AppendLine($"<meta name=\"seedrift-report-kind\" content=\"night\"/><meta name=\"seedrift-generator-version\" content=\"{Escape(CurrentPluginVersion())}\"/><meta name=\"seedrift-report-schema\" content=\"1\"/>");
            sb.AppendLine($"<meta name=\"seedrift-seestar-device\" content=\"{Escape(seestarDevice.DisplayName)}\"/>");
            sb.AppendLine($"<title>SeeDrift — Session Log {sessionLogLabel}</title>");
            sb.AppendLine($"<script src=\"{CdnTailwind}\"></script>");
            sb.AppendLine($"<script src=\"{CdnHammer}\"></script>");
            sb.AppendLine($"<script src=\"{CdnChartJs}\"></script>");
            sb.AppendLine($"<script src=\"{CdnChartZoom}\"></script>");
            sb.AppendLine("<style>");
            sb.AppendLine("  /* Square chart so equal ΔRA/ΔDec arcsec limits map to equal pixels (isotropic) */");
            sb.AppendLine("  .seedrift-chart-box { position: relative; width: 100%; max-width: 36rem; margin-left: auto; margin-right: auto; aspect-ratio: 1 / 1; }");
            sb.AppendLine("  /* Sequencer table: wrap long paths so the Detail column keeps usable width */");
            sb.AppendLine("  .seedrift-seq-table { word-break: break-word; overflow-wrap: anywhere; }");
            sb.AppendLine("</style>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body class=\"min-h-full bg-slate-950 text-slate-200 antialiased\">");
            sb.AppendLine("<main class=\"mx-auto max-w-5xl px-4 py-8 sm:px-6\">");
            sb.AppendLine("<header class=\"mb-10 border-b border-slate-800 pb-6\">");
            sb.AppendLine("  <div class=\"flex flex-row items-start gap-4\">");
            sb.AppendLine("    <div class=\"flex-shrink-0 pt-0.5\">");
            sb.AppendLine("      " + BuildHeaderBrandMarkup());
            sb.AppendLine("    </div>");
            sb.AppendLine("    <div class=\"min-w-0 flex-1\">");
            sb.AppendLine($"      <h1 class=\"text-xl font-semibold tracking-tight text-white\">SeeDrift — Session Log {Escape(sessionLogLabel)}</h1>");
            sb.AppendLine(
                $"      <p class=\"mt-2 text-sm text-slate-400\">Generated {Escape(DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))} <span class=\"text-slate-500\">(local)</span></p>");
            sb.AppendLine(
                $"      <p class=\"mt-1 text-xs text-slate-500\">Report version v{Escape(CurrentPluginVersion())} · schema 1</p>");
            sb.AppendLine(
                $"      <p class=\"mt-1 text-xs text-slate-500\">Scope: <span class=\"text-slate-300\">{Escape(seestarDevice.DisplayName)}</span></p>");
            sb.Append(FormatPageHeaderLogsHtml(targets));
            sb.AppendLine(
                "      <p class=\"mt-3 text-xs text-slate-600\">Reset drift chart zoom: <strong>double-click</strong> the chart, or press <kbd class=\"rounded border border-slate-600 bg-slate-900 px-1.5 py-0.5 font-mono text-[10px] text-slate-300\">R</kbd> or <kbd class=\"rounded border border-slate-600 bg-slate-900 px-1.5 py-0.5 font-mono text-[10px] text-slate-300\">Esc</kbd> (when not typing in a field).</p>");
            sb.AppendLine("    </div>");
            sb.AppendLine("  </div>");
            sb.AppendLine("</header>");

            // Pre-build per-target analyses up-front so the run-wide Session Settings panel can pool
            // the realized-dither medians (the run-wide value is the mean of per-target medians).
            // Reused later inside the per-target loop to avoid duplicate work.
            var preBuiltAnalyses = new Dictionary<(int BatchIndex, string TargetKey), TargetAnalysis>();
            var allAnalyses = new List<TargetAnalysis>();
            for (var t0 = 0; t0 < targets.Count; t0++) {
                var orderedFull0 = targets[t0].Samples.OrderBy(s => s.FrameIndex).ToList();
                var groups0 = SplitBatchByTargetInOrder(orderedFull0);
                var filtered0 = groups0.Where(g => g.Samples.Count >= min).ToList();
                foreach (var (name0, grp0) in filtered0) {
                    var analysis0 = SessionAnalysisService.AnalyzeTarget(name0, grp0);
                    preBuiltAnalyses[(t0, name0)] = analysis0;
                    allAnalyses.Add(analysis0);
                }
            }

            var sequencerSettings = SessionAnalysisService.BuildSequencerSettings(targets, allAnalyses);
            if (sequencerSettings != null)
                sb.Append(FormatSessionSettingsHtml(sequencerSettings));

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
                sb.AppendLine(
                    $"  <p class=\"mt-1 text-sm text-slate-400\">{Escape(FormatBatchFramesAndDuration(samples.Count, batch.RunDuration))}</p>");

                if (filteredGroups.Count == 0) {
                    sb.AppendLine("  <div class=\"mt-6 rounded-lg border border-amber-900/40 bg-slate-900/60 p-4\">");
                    if (samples.Count == 0 && batch.PresolveMaxLightsPerBestTarget is int cap) {
                        sb.AppendLine(
                            $"    <p class=\"text-sm text-slate-300\">No target in this run had at least <span class=\"text-amber-300\">{min}</span> LIGHT frame(s) for any FITS target (best target: <span class=\"text-amber-300\">{cap}</span>); plate solving was skipped. Lower <span class=\"text-sky-300\">Minimum exposures per target</span> in Plugins → SeeDrift, or capture more frames per target.</p>");
                    } else {
                        sb.AppendLine(
                            $"    <p class=\"text-sm text-slate-300\">No target in this run had at least <span class=\"text-amber-300\">{min}</span> solved exposure{(min == 1 ? "" : "s")}. Lower <span class=\"text-sky-300\">Minimum exposures per target</span> in Plugins → SeeDrift, or capture more frames per target.</p>");
                    }

                    sb.AppendLine("  </div>");
                    sb.AppendLine("</section>");
                    continue;
                }

                for (var g = 0; g < filteredGroups.Count; g++) {
                    var (targetName, grp) = filteredGroups[g];
                    var canvasId = $"c{t}_{g}";
                    var labelJson = JsonSerializer.Serialize($"{targetName} — drift path");
                    var analysis = preBuiltAnalyses.TryGetValue((t, targetName), out var preBuilt)
                        ? preBuilt
                        : SessionAnalysisService.AnalyzeTarget(targetName, grp);

                    sb.AppendLine($"  <div class=\"{(g > 0 ? "mt-12 border-t border-slate-800 pt-10" : "mt-8")}\">");
                    sb.AppendLine("    <div class=\"flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between\">");
                    sb.AppendLine("      <div class=\"min-w-0 flex-1\">");
                    sb.AppendLine($"        <h3 class=\"text-base font-semibold text-sky-200\">Target: {Escape(targetName)}</h3>");
                    sb.AppendLine(
                        $"        <p class=\"mt-1 text-xs text-slate-500\">{Escape(FormatTargetFramesSubtitle(grp))}</p>");
                    sb.AppendLine("      </div>");
                    sb.AppendLine(
                        $"      <div class=\"shrink-0 text-xs leading-snug text-slate-400 sm:max-w-[min(100%,20rem)] sm:text-right\">{FormatTargetExposureRangeHtml(grp)}</div>");
                    sb.AppendLine("    </div>");
                    sb.AppendLine($"    <div class=\"seedrift-chart-box mt-4 rounded-lg border border-slate-700 bg-slate-900/60 p-2\">");
                    sb.AppendLine($"      <canvas id=\"{canvasId}\"></canvas>");
                    sb.AppendLine("    </div>");
                    sb.AppendLine("    <p class=\"mt-2 text-xs text-slate-500\">Path: <span class=\"text-emerald-400\">●</span> start · <span class=\"text-orange-400\">●</span> end (<span class=\"text-amber-400\">●</span> if one frame). Log triggers: <span class=\"text-purple-400\">△</span> dither (tip toward next frame) · <span class=\"text-pink-400\">□</span> center. <span class=\"text-amber-300\">?</span> marks possibly missing/unsolved frames between plotted points. Hover path, △/□, or ? markers for detail.</p>");
                    sb.AppendLine($"    <p class=\"mt-2 text-xs text-slate-400\">{Escape(FormatMovementTotalsLine(grp))}</p>");
                    var ditherSegHtml = FormatDitherIntervalMovementHtml(grp);
                    if (!string.IsNullOrEmpty(ditherSegHtml))
                        sb.Append(ditherSegHtml);
                    sb.Append(FormatAnalysisSummaryHtml(analysis));
                    sb.Append(FormatTimelineHtml(analysis));

                    var ptsJson = FormatScatterPointsJsonAnchored(grp);
                    var edgeJson = FormatEdgeMidpointMarkersJson(grp);
                    var gapJson = FormatPossibleGapMarkersJson(grp);

                    sb.AppendLine("<script>");
                    sb.AppendLine("(function(){");
                    sb.AppendLine($"  const pts = {ptsJson};");
                    sb.AppendLine($"  const edgeMarkers = {edgeJson};");
                    sb.AppendLine($"  const gapMarkers = {gapJson};");
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
                    sb.AppendLine("  function seedriftDitherRotationDeg(chart, raw) {");
                    sb.AppendLine("    if (!raw || !raw.isDither) return 0;");
                    sb.AppendLine("    var sx = chart.scales.x, sy = chart.scales.y;");
                    sb.AppendLine("    if (!sx || !sy) return 0;");
                    sb.AppendLine("    var x0 = sx.getPixelForValue(raw.x0), y0 = sy.getPixelForValue(raw.y0);");
                    sb.AppendLine("    var x1 = sx.getPixelForValue(raw.x1), y1 = sy.getPixelForValue(raw.y1);");
                    sb.AppendLine("    var dx = x1 - x0, dy = y1 - y0;");
                    sb.AppendLine("    if (dx === 0 && dy === 0) return 0;");
                    sb.AppendLine("    return (Math.atan2(dy, dx) * 180 / Math.PI) + 90;");
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
                    sb.AppendLine("      },");
                    sb.AppendLine("      pointRotation: function(ctx) {");
                    sb.AppendLine("        return seedriftDitherRotationDeg(ctx.chart, ctx.raw);");
                    sb.AppendLine("      }");
                    sb.AppendLine("    });");
                    sb.AppendLine("  }");
                    sb.AppendLine("  if (gapMarkers.length > 0) {");
                    sb.AppendLine("    datasets.push({");
                    sb.AppendLine("      label: 'Possibly missing/unsolved frames',");
                    sb.AppendLine("      data: gapMarkers,");
                    sb.AppendLine("      showLine: false,");
                    sb.AppendLine("      borderColor: '#fbbf24',");
                    sb.AppendLine("      backgroundColor: 'rgba(251,191,36,0.18)',");
                    sb.AppendLine("      pointBorderColor: '#fbbf24',");
                    sb.AppendLine("      pointBorderWidth: 1.5,");
                    sb.AppendLine("      pointRadius: 7,");
                    sb.AppendLine("      pointHoverRadius: 9,");
                    sb.AppendLine("      hitRadius: 16,");
                    sb.AppendLine("      pointStyle: 'rectRounded'");
                    sb.AppendLine("    });");
                    sb.AppendLine("  }");
                    sb.AppendLine("  function seedriftEqualArcsecBounds(mainPts, edgePts) {");
                    sb.AppendLine("    var xs = [], ys = [];");
                    sb.AppendLine("    for (var i = 0; i < mainPts.length; i++) { xs.push(mainPts[i].x); ys.push(mainPts[i].y); }");
                    sb.AppendLine("    if (edgePts && edgePts.length) {");
                    sb.AppendLine("      for (var j = 0; j < edgePts.length; j++) { xs.push(edgePts[j].x); ys.push(edgePts[j].y); }");
                    sb.AppendLine("    }");
                    sb.AppendLine("    if (gapMarkers && gapMarkers.length) {");
                    sb.AppendLine("      for (var k = 0; k < gapMarkers.length; k++) { xs.push(gapMarkers[k].x); ys.push(gapMarkers[k].y); }");
                    sb.AppendLine("    }");
                    sb.AppendLine("    if (xs.length === 0) return null;");
                    sb.AppendLine("    var minX = Math.min.apply(null, xs), maxX = Math.max.apply(null, xs);");
                    sb.AppendLine("    var minY = Math.min.apply(null, ys), maxY = Math.max.apply(null, ys);");
                    sb.AppendLine("    var rx = maxX - minX, ry = maxY - minY;");
                    sb.AppendLine("    var spanData = Math.max(rx, ry, 1e-9);");
                    sb.AppendLine("    if (spanData < 0.25) spanData = 0.25;");
                    sb.AppendLine("    var pad = spanData * 0.06;");
                    sb.AppendLine("    var span = spanData + 2 * pad;");
                    sb.AppendLine("    var half = span / 2;");
                    sb.AppendLine("    var cx = (minX + maxX) / 2, cy = (minY + maxY) / 2;");
                    sb.AppendLine("    return { xMin: cx - half, xMax: cx + half, yMin: cy - half, yMax: cy + half };");
                    sb.AppendLine("  }");
                    sb.AppendLine("  var eq = seedriftEqualArcsecBounds(pts, edgeMarkers);");
                    sb.AppendLine("  const chart = new Chart(el, {");
                    sb.AppendLine("    type: 'scatter',");
                    sb.AppendLine("    data: { datasets: datasets },");
                    sb.AppendLine("    plugins: [{");
                    sb.AppendLine("      id: 'seedriftGapLabels',");
                    sb.AppendLine("      afterDatasetsDraw: function(chart) {");
                    sb.AppendLine("        var dsIndex = chart.data.datasets.findIndex(function(ds) { return ds.label === 'Possibly missing/unsolved frames'; });");
                    sb.AppendLine("        if (dsIndex < 0) return;");
                    sb.AppendLine("        var meta = chart.getDatasetMeta(dsIndex);");
                    sb.AppendLine("        var ctx = chart.ctx;");
                    sb.AppendLine("        ctx.save();");
                    sb.AppendLine("        ctx.font = 'bold 10px sans-serif';");
                    sb.AppendLine("        ctx.textAlign = 'center';");
                    sb.AppendLine("        ctx.textBaseline = 'middle';");
                    sb.AppendLine("        ctx.fillStyle = '#fbbf24';");
                    sb.AppendLine("        for (var i = 0; i < meta.data.length; i++) {");
                    sb.AppendLine("          var p = meta.data[i];");
                    sb.AppendLine("          if (p && !p.hidden) ctx.fillText('?', p.x, p.y);");
                    sb.AppendLine("        }");
                    sb.AppendLine("        ctx.restore();");
                    sb.AppendLine("      }");
                    sb.AppendLine("    }],");
                    sb.AppendLine("    options: {");
                    sb.AppendLine("      responsive: true, maintainAspectRatio: true, aspectRatio: 1,");
                    sb.AppendLine("      scales: {");
                    sb.AppendLine($"        x: {{ title: {{ display: true, text: '{xTitle}', color: '#94a3b8' }}, grid: {{ color: 'rgba(148,163,184,0.12)' }}, ticks: {{ color: '#cbd5e1' }}, border: {{ color: '#475569' }}, min: eq ? eq.xMin : undefined, max: eq ? eq.xMax : undefined }},");
                    sb.AppendLine($"        y: {{ reverse: true, title: {{ display: true, text: '{yTitle}', color: '#94a3b8' }}, grid: {{ color: 'rgba(148,163,184,0.12)' }}, ticks: {{ color: '#cbd5e1' }}, border: {{ color: '#475569' }}, min: eq ? eq.yMin : undefined, max: eq ? eq.yMax : undefined }}");
                    sb.AppendLine("      },");
                    sb.AppendLine("      plugins: {");
                    sb.AppendLine("        tooltip: {");
                    sb.AppendLine("          callbacks: {");
                    sb.AppendLine("            title: function() { return ''; },");
                    sb.AppendLine("            label: function(ctx) {");
                    sb.AppendLine("              if (ctx.datasetIndex > 0) {");
                    sb.AppendLine("                var raw = ctx.raw;");
                    sb.AppendLine("                if (raw && raw.isGap) return [raw.label || 'Possibly missing/unsolved frames', raw.tooltip || ''];");
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
                    sb.AppendLine("  window.seedriftCharts = window.seedriftCharts || [];");
                    sb.AppendLine("  window.seedriftCharts.push(chart);");
                    sb.AppendLine("})();");
                    sb.AppendLine("</script>");

                    AppendSequencerSection(sb, orderedFull, targetName);
                    sb.AppendLine("  </div>");
                }

                sb.AppendLine("</section>");
            }

            sb.AppendLine("<script type=\"application/json\" id=\"seedrift-report-data\">");
            sb.AppendLine(FormatReportPayloadJson(targets, min, sessionLogLabel));
            sb.AppendLine("</script>");
            sb.AppendLine("<script>");
            sb.AppendLine("(function(){");
            sb.AppendLine("  document.addEventListener('keydown', function(ev) {");
            sb.AppendLine("    var k = ev.key;");
            sb.AppendLine("    if (k !== 'r' && k !== 'R' && k !== 'Escape') return;");
            sb.AppendLine("    var t = ev.target;");
            sb.AppendLine("    if (t && t.closest && t.closest('input, textarea, select, [contenteditable=\"true\"]')) return;");
            sb.AppendLine("    var charts = window.seedriftCharts;");
            sb.AppendLine("    if (!charts || !charts.length) return;");
            sb.AppendLine("    ev.preventDefault();");
            sb.AppendLine("    for (var i = 0; i < charts.length; i++) {");
            sb.AppendLine("      try { charts[i].resetZoom(); } catch (e) {}");
            sb.AppendLine("    }");
            sb.AppendLine("  });");
            sb.AppendLine("})();");
            sb.AppendLine("</script>");
            sb.AppendLine("</main></body></html>");
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        /// <summary>
        /// One Stop or previous-session-report batch may include frames with different FITS OBJECT names; builds a readable heading from distinct
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

        /// <summary>Sums absolute cumulative-pixel steps between consecutive frames (detector axes).</summary>
        private static void SumAbsoluteSegmentMovementInPixels(IReadOnlyList<DriftSample> group, out double sumAbsPx, out double sumAbsPy) {
            sumAbsPx = 0;
            sumAbsPy = 0;
            if (group.Count < 2)
                return;
            var p0 = group[0];
            if (!p0.CumulativePixelX.HasValue || !p0.CumulativePixelY.HasValue)
                return;
            var prevX = p0.CumulativePixelX!.Value;
            var prevY = p0.CumulativePixelY!.Value;
            for (var i = 1; i < group.Count; i++) {
                var s = group[i];
                if (!s.CumulativePixelX.HasValue || !s.CumulativePixelY.HasValue)
                    return;
                sumAbsPx += Math.Abs(s.CumulativePixelX!.Value - prevX);
                sumAbsPy += Math.Abs(s.CumulativePixelY!.Value - prevY);
                prevX = s.CumulativePixelX.Value;
                prevY = s.CumulativePixelY.Value;
            }
        }

        /// <summary>Median nominal arcsec/px among samples that have FITS scale (for pixel-equivalent caption).</summary>
        private static bool TryMedianNominalPlateScale(IReadOnlyList<DriftSample> group, out double medianArcSecPerPx) {
            medianArcSecPerPx = 0;
            var list = new List<double>();
            foreach (var s in group) {
                if (s.NominalPlateScaleArcSecPerPx is double v && v > 0)
                    list.Add(v);
            }

            if (list.Count == 0)
                return false;
            list.Sort();
            medianArcSecPerPx = list[list.Count / 2];
            return true;
        }

        /// <summary>
        /// HTML body (already safe: generated labels + numeric formatting only).
        /// Lists plate-solved segment ΔRA/ΔDec for each frame pair whose logs include a dither trigger on that edge,
        /// then Σ|Δ| totals across those intervals only.
        /// </summary>
        private static string FormatDitherIntervalMovementHtml(IReadOnlyList<DriftSample> group) {
            if (group.Count < 2)
                return "";
            var rows = new List<(string Label, string DeltaRa, string DeltaDec, string Pixel, string Note)>();
            var pixelPath = group.All(s => s.IsPixelPath);
            var suspectFloor = CalculateSuspectDitherFloor(group);
            double sumAbsRa = 0;
            double sumAbsDec = 0;
            double sumAbsPx = 0;
            double sumAbsPy = 0;
            double discountedAbsRa = 0;
            double discountedAbsDec = 0;
            double discountedAbsPx = 0;
            double discountedAbsPy = 0;
            var suspectCount = 0;
            var anyPxSegment = false;
            var assessedMovesArc = new List<double>();
            var assessedMovesPx = new List<double>();

            for (var i = 1; i < group.Count; i++) {
                var markers = group[i].EdgeSequencerMarkers;
                if (markers == null || !markers.Any(m => m.IsDither))
                    continue;

                GetAnchoredPlotPoint(group, i - 1, out var x0, out var y0);
                GetAnchoredPlotPoint(group, i, out var x1, out var y1);
                var dRa = x1 - x0;
                var dDec = y1 - y0;
                var move = Math.Sqrt(dRa * dRa + dDec * dDec);
                var suspect = move >= suspectFloor;
                var note = suspect
                    ? "Excluded from dither assessment: likely tracking issue / suspect jump."
                    : "";
                if (suspect) {
                    suspectCount++;
                    discountedAbsRa += Math.Abs(dRa);
                    discountedAbsDec += Math.Abs(dDec);
                } else {
                    sumAbsRa += Math.Abs(dRa);
                    sumAbsDec += Math.Abs(dDec);
                    assessedMovesArc.Add(move);
                }
                var prev = group[i - 1];
                var cur = group[i];
                var label = FormattableString.Invariant(
                    $"Frames {prev.FrameIndex + 1}→{cur.FrameIndex + 1}");
                var pxText = "";

                if (pixelPath
                    && prev.CumulativePixelX is double px0 && prev.CumulativePixelY is double py0
                    && cur.CumulativePixelX is double px1 && cur.CumulativePixelY is double py1) {
                    var adx = Math.Abs(px1 - px0);
                    var ady = Math.Abs(py1 - py0);
                    if (suspect) {
                        discountedAbsPx += adx;
                        discountedAbsPy += ady;
                    } else {
                        sumAbsPx += adx;
                        sumAbsPy += ady;
                        assessedMovesPx.Add(Math.Sqrt(adx * adx + ady * ady));
                    }
                    anyPxSegment = true;
                    pxText = FormattableString.Invariant($"|Δx| {adx:0.##} px · |Δy| {ady:0.##} px");
                }

                rows.Add((label, FmtSignedArcSec(dRa) + "″", FmtSignedArcSec(dDec) + "″", pxText, note));
            }

            if (rows.Count == 0)
                return "";

            var assessedCount = rows.Count - suspectCount;
            var totalArc = FormattableString.Invariant(
                $"Logged dither intervals total (assessed) — Σ|ΔRA| {sumAbsRa:0.###}″ · Σ|ΔDec| {sumAbsDec:0.###}″");
            if (anyPxSegment)
                totalArc += FormattableString.Invariant(
                    $" · detector Σ|Δx| {sumAbsPx:0.##} px · Σ|Δy| {sumAbsPy:0.##} px");

            string? discountedLine = null;
            if (suspectCount > 0) {
                var disc = FormattableString.Invariant(
                    $"{suspectCount} suspect interval{(suspectCount == 1 ? "" : "s")} excluded — discounted Σ|ΔRA| {discountedAbsRa:0.###}″ · Σ|ΔDec| {discountedAbsDec:0.###}″");
                if (anyPxSegment && (discountedAbsPx > 0 || discountedAbsPy > 0))
                    disc += FormattableString.Invariant(
                        $" · detector Σ|Δx| {discountedAbsPx:0.##} px · Σ|Δy| {discountedAbsPy:0.##} px");
                discountedLine = disc + ".";
            }

            string? typicalLine = null;
            if (assessedMovesArc.Count > 0) {
                var sorted = assessedMovesArc.OrderBy(v => v).ToList();
                var medianArc = sorted[(sorted.Count - 1) / 2];
                var typical = FormattableString.Invariant(
                    $"Typical dither — median |Δ| {medianArc:0.##}″ across {assessedCount} assessed dither{(assessedCount == 1 ? "" : "s")}");
                if (assessedMovesPx.Count > 0) {
                    var sortedPx = assessedMovesPx.OrderBy(v => v).ToList();
                    var medianPx = sortedPx[(sortedPx.Count - 1) / 2];
                    typical += FormattableString.Invariant($" (~{medianPx:0.#} px)");
                }
                typicalLine = typical + ".";
            }

            var sb = new StringBuilder();
            sb.AppendLine("    <div class=\"mt-4 rounded-lg border border-purple-900/40 bg-slate-900/30 p-3 text-xs text-slate-400\">");
            sb.AppendLine($"      <p class=\"font-semibold text-purple-200\">{totalArc}</p>");
            if (discountedLine != null)
                sb.AppendLine($"      <p class=\"mt-1 text-amber-200\">{Escape(discountedLine)}</p>");
            if (typicalLine != null)
                sb.AppendLine($"      <p class=\"mt-1 text-slate-300\">{Escape(typicalLine)}</p>");
            sb.AppendLine("      <details class=\"mt-3\">");
            sb.AppendLine(
                $"        <summary class=\"cursor-pointer text-slate-300 marker:text-purple-300 hover:text-white\">Exposures with logged dithers between them — {rows.Count} result{(rows.Count == 1 ? "" : "s")} (click to expand)</summary>");
            sb.AppendLine("        <div class=\"mt-3 overflow-x-auto\">");
            sb.AppendLine("          <table class=\"min-w-full table-fixed divide-y divide-slate-700 text-left text-xs\">");
            sb.AppendLine("            <thead class=\"bg-slate-900/80 text-purple-200\"><tr>");
            sb.AppendLine("              <th class=\"w-[28%] px-3 py-2 font-medium\">Frames</th>");
            sb.AppendLine("              <th class=\"w-[24%] px-3 py-2 font-medium\">ΔRA</th>");
            sb.AppendLine("              <th class=\"w-[24%] px-3 py-2 font-medium\">ΔDec</th>");
            if (anyPxSegment)
                sb.AppendLine("              <th class=\"w-[18%] px-3 py-2 font-medium\">Detector pixels</th>");
            sb.AppendLine("              <th class=\"w-[20%] px-3 py-2 font-medium\">Exclusion note</th>");
            sb.AppendLine("            </tr></thead>");
            sb.AppendLine("            <tbody class=\"divide-y divide-slate-800 bg-slate-950/30 text-slate-300\">");
            foreach (var row in rows) {
                sb.AppendLine("              <tr>");
                sb.AppendLine($"                <td class=\"px-3 py-2 font-mono text-[11px]\">{row.Label}</td>");
                sb.AppendLine($"                <td class=\"px-3 py-2\">{row.DeltaRa}</td>");
                sb.AppendLine($"                <td class=\"px-3 py-2\">{row.DeltaDec}</td>");
                if (anyPxSegment)
                    sb.AppendLine($"                <td class=\"px-3 py-2\">{row.Pixel}</td>");
                var note = string.IsNullOrWhiteSpace(row.Note) ? "-" : Escape(row.Note);
                var noteClass = string.IsNullOrWhiteSpace(row.Note) ? "text-slate-500" : "text-amber-200";
                sb.AppendLine($"                <td class=\"px-3 py-2 {noteClass}\">{note}</td>");
                sb.AppendLine("              </tr>");
            }
            sb.AppendLine("            </tbody>");
            sb.AppendLine("          </table>");
            sb.AppendLine("        </div>");
            sb.AppendLine("      </details>");
            sb.AppendLine("    </div>");
            return sb.ToString();
        }

        private static string FmtSignedArcSec(double v) =>
            string.Format(CultureInfo.InvariantCulture, "{0:+0.###;-0.###;0}", v);

        private static double CalculateSuspectDitherFloor(IReadOnlyList<DriftSample> group) {
            var moves = group
                .Skip(1)
                .SelectMany(s => s.EdgeSequencerMarkers ?? new List<SequencerEdgeMarker>())
                .Where(m => m.IsDither)
                .Select(m => Math.Sqrt(m.DeltaRaArcSec * m.DeltaRaArcSec + m.DeltaDecArcSec * m.DeltaDecArcSec))
                .OrderBy(v => v)
                .ToList();
            if (moves.Count == 0)
                return double.PositiveInfinity;
            var median = moves[(moves.Count - 1) / 2];
            return moves.Count > 1
                ? Math.Max(300.0, median * 5.0)
                : 600.0;
        }

        private static string FormatAnalysisSummaryHtml(TargetAnalysis analysis) {
            var sb = new StringBuilder();
            sb.Append(FormatDriftRiskHtml(analysis.DriftRisk));
            sb.AppendLine("    <div class=\"mt-4 grid gap-3 sm:grid-cols-3\">");
            sb.AppendLine("      <div class=\"rounded-lg border border-slate-700 bg-slate-900/40 p-3\">");
            sb.AppendLine("        <p class=\"text-[10px] font-semibold uppercase tracking-wide text-sky-400\">Drift rate</p>");
            var rate = analysis.DriftRate;
            var px = rate.TotalPixelsPerMinute.HasValue
                ? FormattableString.Invariant($" · ≈ {rate.TotalPixelsPerMinute.Value:0.##} px/min")
                : "";
            sb.AppendLine(
                $"        <p class=\"mt-1 text-sm text-slate-200\">{rate.TotalArcSecPerMinute:0.###}″/min{Escape(px)}</p>");
            sb.AppendLine(
                $"        <p class=\"mt-1 text-xs text-slate-500\">ΔRA {FmtSignedArcSec(rate.DeltaRaArcSecPerMinute)}″/min · ΔDec {FmtSignedArcSec(rate.DeltaDecArcSecPerMinute)}″/min</p>");
            sb.AppendLine("      </div>");
            sb.AppendLine("      <div class=\"rounded-lg border border-slate-700 bg-slate-900/40 p-3\">");
            sb.AppendLine("        <p class=\"text-[10px] font-semibold uppercase tracking-wide text-purple-400\">Dither effectiveness</p>");
            if (analysis.Dithers.Count == 0) {
                sb.AppendLine("        <p class=\"mt-1 text-sm text-slate-300\">No logged dithers on this target.</p>");
            } else {
                var assessed = analysis.Dithers.Where(d => !d.IsSuspect).ToList();
                var weak = assessed.Count(d => d.Assessment == "Weak");
                var repeated = assessed.Count(d => d.Assessment == "Repeated direction");
                var med = assessed.Count == 0
                    ? 0
                    : assessed.Select(d => d.MoveArcSec).OrderBy(v => v).ElementAt(assessed.Count / 2);
                sb.AppendLine(
                    $"        <p class=\"mt-1 text-sm text-slate-200\">{assessed.Count} assessed dither{(assessed.Count == 1 ? "" : "s")} · median {med:0.##}″</p>");
                sb.AppendLine(
                    $"        <p class=\"mt-1 text-xs text-slate-500\">Weak {weak} · repeated direction {repeated}</p>");
                if (analysis.SuspectDitherCount > 0) {
                    sb.AppendLine(
                        $"        <p class=\"mt-1 text-xs text-amber-300\">Excluded suspect tracking intervals: {analysis.SuspectDitherCount}; discounted RA {analysis.SuspectDitherDiscountedAbsRaArcSec:0.#}″ · Dec {analysis.SuspectDitherDiscountedAbsDecArcSec:0.#}″.</p>");
                }
            }
            sb.AppendLine("      </div>");
            sb.AppendLine("      <div class=\"rounded-lg border border-slate-700 bg-slate-900/40 p-3\">");
            sb.AppendLine("        <p class=\"text-[10px] font-semibold uppercase tracking-wide text-pink-400\">Center-after-drift</p>");
            if (analysis.Centers.Count == 0) {
                sb.AppendLine("        <p class=\"mt-1 text-sm text-slate-300\">No correlated center events.</p>");
            } else {
                var helpful = analysis.Centers.Count(c => c.Assessment == "Helpful");
                var partial = analysis.Centers.Count(c => c.Assessment == "Partial");
                var ineffective = analysis.Centers.Count(c => c.Assessment == "Ineffective");
                sb.AppendLine(
                    $"        <p class=\"mt-1 text-sm text-slate-200\">{analysis.Centers.Count} center event{(analysis.Centers.Count == 1 ? "" : "s")}</p>");
                sb.AppendLine(
                    $"        <p class=\"mt-1 text-xs text-slate-500\">Helpful {helpful} · partial {partial} · ineffective {ineffective}</p>");
            }
            sb.AppendLine("      </div>");
            sb.AppendLine("    </div>");

            if (analysis.Recommendations.Count > 0) {
                sb.AppendLine("    <div class=\"mt-3 rounded-lg border border-slate-700 bg-slate-900/30 p-3\">");
                sb.AppendLine("      <p class=\"text-[10px] font-semibold uppercase tracking-wide text-amber-300\">Settings hints from this session</p>");
                sb.AppendLine("      <ul class=\"mt-2 list-disc space-y-1 pl-5 text-xs text-slate-300\">");
                foreach (var rec in analysis.Recommendations.Take(4))
                    sb.AppendLine($"        <li>{Escape(rec.Text)}</li>");
                sb.AppendLine("      </ul>");
                sb.AppendLine("    </div>");
            }

            return sb.ToString();
        }

        private static string FormatDriftRiskHtml(DriftRiskSummary risk) {
            if (string.IsNullOrWhiteSpace(risk.Status))
                return "";

            var tone = ToneTriple(risk.Tone, risk.Status);
            var px = risk.NaturalDriftPixelsPerMinute.HasValue
                ? FormattableString.Invariant($" · ≈ {risk.NaturalDriftPixelsPerMinute.Value:0.##} px/min")
                : "";
            var detail = string.IsNullOrWhiteSpace(risk.Detail)
                ? "Advisory only. This is a walking-noise risk hint, not an image-quality verdict."
                : risk.Detail;

            var sb = new StringBuilder();
            sb.AppendLine($"    <div class=\"mt-4 rounded-lg border {tone.Item1} {tone.Item2} p-3\">");
            sb.AppendLine("      <div class=\"flex flex-wrap items-center justify-between gap-2\">");
            sb.AppendLine("        <p class=\"text-[10px] font-semibold uppercase tracking-wide text-slate-300\">Drift / walking-noise risk</p>");
            sb.AppendLine($"        <span class=\"rounded-full border {tone.Item1} px-2 py-0.5 text-xs font-semibold {tone.Item3}\">{Escape(tone.Item4)}</span>");
            sb.AppendLine("      </div>");

            // Sub-tier chips: star shape + walking noise so the headline label never surprises.
            if (!string.IsNullOrWhiteSpace(risk.StarShapeStatus) || !string.IsNullOrWhiteSpace(risk.WalkingNoiseStatus)) {
                sb.AppendLine("      <div class=\"mt-2 flex flex-wrap gap-2\">");
                if (!string.IsNullOrWhiteSpace(risk.StarShapeStatus))
                    sb.AppendLine($"        {FormatSubTierChip("Star shape", risk.StarShapeStatus, risk.StarShapeTone)}");
                if (!string.IsNullOrWhiteSpace(risk.WalkingNoiseStatus))
                    sb.AppendLine($"        {FormatSubTierChip("Walking noise", risk.WalkingNoiseStatus, risk.WalkingNoiseTone)}");
                sb.AppendLine("      </div>");
            }

            if (risk.IntervalCount > 0) {
                sb.AppendLine(
                    $"      <p class=\"mt-2 text-sm {tone.Item3}\">{risk.NaturalDriftArcSecPerMinute:0.##}″/min natural drift{Escape(px)} · {risk.DirectionConsistency:P0} directional</p>");
                sb.AppendLine(
                    $"      <p class=\"mt-1 text-xs text-slate-400\">Net drift without logged dither/center intervals: {risk.NetNaturalDriftArcSec:0.#}″ across {risk.IntervalCount} interval{(risk.IntervalCount == 1 ? "" : "s")}.</p>");
                if (risk.EstimatedDriftPerExposureArcSec.HasValue) {
                    var perSubPx = risk.EstimatedDriftPerExposurePixels.HasValue
                        ? FormattableString.Invariant($" · ≈ {risk.EstimatedDriftPerExposurePixels.Value:0.##} px")
                        : "";
                    sb.AppendLine(
                        $"      <p class=\"mt-1 text-xs text-slate-400\">Star-shape hint: estimated {risk.EstimatedDriftPerExposureArcSec.Value:0.#}″ drift during a typical exposure{Escape(perSubPx)}. Round stars typically tolerate &lt; 1–2 px of motion per exposure.</p>");
                }
                if (risk.DitherHeadroomRatio.HasValue && risk.BetweenDitherDriftArcSec.HasValue) {
                    var ditherPxBit = risk.BetweenDitherDriftPixels.HasValue
                        ? FormattableString.Invariant($" · ≈ {risk.BetweenDitherDriftPixels.Value:0.##} px between dithers")
                        : "";
                    sb.AppendLine(
                        $"      <p class=\"mt-1 text-xs text-slate-400\">Walking-noise hint: dither headroom <span class=\"font-semibold text-slate-200\">{risk.DitherHeadroomRatio.Value:0.0}×</span> (median dither magnitude vs cumulative drift between dithers — {risk.BetweenDitherDriftArcSec.Value:0.##}″ between dithers{Escape(ditherPxBit)}). Higher is safer.</p>");
                } else if (risk.WorstWindowDriftArcSec.HasValue && risk.WorstWindowFrameCount > 0) {
                    var windowPx = risk.WorstWindowDriftPixels.HasValue
                        ? FormattableString.Invariant($" · ≈ {risk.WorstWindowDriftPixels.Value:0.##} px")
                        : "";
                    sb.AppendLine(
                        $"      <p class=\"mt-1 text-xs text-slate-400\">Walking-noise hint (no dither headroom data): worst short-window drift was {risk.WorstWindowDriftArcSec.Value:0.#}″ over {risk.WorstWindowFrameCount} frame{(risk.WorstWindowFrameCount == 1 ? "" : "s")}{Escape(windowPx)}.</p>");
                }
            }
            sb.AppendLine($"      <p class=\"mt-1 text-xs text-slate-500\">{Escape(detail)} Risk is advisory: star-shape is about single-exposure round stars (&lt; 1–2 px is typically acceptable), walking-noise is about cumulative drift between dithers vs the dither magnitude.</p>");
            sb.AppendLine("    </div>");
            return sb.ToString();
        }

        private static (string, string, string, string) ToneTriple(string tone, string statusText) =>
            tone == "good"
                ? ("border-emerald-500/40", "bg-emerald-500/10", "text-emerald-200", "Low")
                : tone == "warn"
                    ? ("border-amber-500/50", "bg-amber-500/10", "text-amber-200", statusText)
                    : tone == "ok"
                        ? ("border-sky-500/40", "bg-sky-500/10", "text-sky-200", "Moderate")
                        : ("border-slate-600", "bg-slate-900/40", "text-slate-300", statusText);

        private static string FormatSubTierChip(string label, string status, string tone) {
            var t = ToneTriple(tone, status);
            return FormattableString.Invariant(
                $"<span class=\"rounded-full border {t.Item1} {t.Item2} px-2 py-0.5 text-[11px] {t.Item3}\"><span class=\"font-semibold uppercase tracking-wide\">{Escape(label)}</span>: {Escape(status)}</span>");
        }

        private static string FormatTimelineHtml(TargetAnalysis analysis) {
            if (analysis.Timeline.Count == 0)
                return "";
            var sb = new StringBuilder();
            sb.AppendLine("    <div class=\"mt-3 rounded-lg border border-slate-700 bg-slate-900/30 p-3\">");
            sb.AppendLine("      <p class=\"text-[10px] font-semibold uppercase tracking-wide text-sky-400\">Session quality timeline</p>");
            sb.AppendLine("      <div class=\"mt-2 flex h-3 overflow-hidden rounded bg-slate-800\">");
            var totalSeconds = analysis.Timeline.Sum(s => Math.Max(1.0, (s.EndUtc - s.StartUtc).TotalSeconds));
            foreach (var seg in analysis.Timeline) {
                var pct = 100.0 * Math.Max(1.0, (seg.EndUtc - seg.StartUtc).TotalSeconds) / totalSeconds;
                var cls = seg.Tone == "good"
                    ? "bg-emerald-500/80"
                    : seg.Tone == "warn"
                        ? "bg-amber-500/80"
                        : "bg-sky-500/80";
                sb.AppendLine(
                    $"        <span class=\"{cls}\" style=\"width:{pct.ToString("0.###", CultureInfo.InvariantCulture)}%\" title=\"{Escape(seg.Label)}: {Escape(seg.Detail)}\"></span>");
            }
            sb.AppendLine("      </div>");
            sb.AppendLine("      <div class=\"mt-2 flex flex-wrap gap-2 text-xs\">");
            foreach (var g in analysis.Timeline.GroupBy(s => s.Label)) {
                var tone = g.First().Tone;
                var cls = tone == "good"
                    ? "border-emerald-500/40 bg-emerald-500/10 text-emerald-200"
                    : tone == "warn"
                        ? "border-amber-500/40 bg-amber-500/10 text-amber-200"
                        : "border-sky-500/40 bg-sky-500/10 text-sky-200";
                sb.AppendLine(
                    $"        <span class=\"rounded-full border px-2 py-0.5 {cls}\">{Escape(g.Key)} {g.Count()}</span>");
            }
            sb.AppendLine("      </div>");
            sb.AppendLine("    </div>");
            return sb.ToString();
        }

        private static string FormatReportPayloadJson(
                IReadOnlyList<DriftTrackingService.CompletedTarget> targets,
                int minExposuresPerTarget,
                string sessionLogLabel) {
            var payload = SessionAnalysisService.BuildReportPayload(targets, minExposuresPerTarget, sessionLogLabel);
            return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = false });
        }

        private static SeestarDeviceInfo ResolveReportDevice(IReadOnlyList<DriftTrackingService.CompletedTarget> targets) {
            var devices = targets
                .Select(t => t.SeestarDevice)
                .Where(d => d is { IsKnown: true } && !string.IsNullOrWhiteSpace(d.DisplayName))
                .Select(d => d.DisplayName.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (devices.Count == 0)
                return SeestarDeviceInfo.Unknown;
            if (devices.Count > 1 || devices.Any(d => d.Equals(SeestarDeviceInfo.Mixed.DisplayName, StringComparison.OrdinalIgnoreCase)))
                return SeestarDeviceInfo.Mixed;
            return SeestarDeviceInfo.FromId(devices[0]);
        }

        private static string FormatMovementTotalsLine(IReadOnlyList<DriftSample> group) {
            if (group.Count < 2)
                return "Single frame — no frame-to-frame movement to sum.";
            SumAbsoluteSegmentMovementAlongTrace(group, out var sumRa, out var sumDec);
            var arc = FormattableString.Invariant($"ΔRA {sumRa:0.###}″ · ΔDec {sumDec:0.###}″");

            if (group.All(s => s.IsPixelPath)) {
                SumAbsoluteSegmentMovementInPixels(group, out var spx, out var spy);
                var px = FormattableString.Invariant($" · detector Σ|Δx| {spx:0.##} px · Σ|Δy| {spy:0.##} px");
                return arc + px;
            }

            if (TryMedianNominalPlateScale(group, out var scale)) {
                var eqRa = sumRa / scale;
                var eqDec = sumDec / scale;
                var approx = FormattableString.Invariant(
                    $" · ≈ Σ|Δx| {eqRa:0.##} px · Σ|Δy| {eqDec:0.##} px equivalent (÷ median {scale:0.###}″/px from FITS XPIXSZ+FOCALLEN)");
                return arc + approx;
            }

            return arc;
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
                        x0 = px,
                        y0 = py,
                        x1 = cx,
                        y1 = cy,
                        isDither = markers[j].IsDither,
                        tooltip = markers[j].Tooltip ?? ""
                    });
                }
            }

            return JsonSerializer.Serialize(list);
        }

        private static string FormatPossibleGapMarkersJson(IReadOnlyList<DriftSample> group) {
            if (group.Count < 2)
                return "[]";
            var list = new List<object>();
            for (var i = 1; i < group.Count; i++) {
                var prev = group[i - 1];
                var cur = group[i];
                if (!FitsFolderImport.TryExposureSequenceFromFileName(prev.FileName, out var a)
                    || !FitsFolderImport.TryExposureSequenceFromFileName(cur.FileName, out var b))
                    continue;
                var missing = b - a - 1;
                if (missing <= 0)
                    continue;

                GetAnchoredPlotPoint(group, i - 1, out var px, out var py);
                GetAnchoredPlotPoint(group, i, out var cx, out var cy);
                var label = $"Possibly missing/unsolved frames ({missing})";
                var tooltip =
                    $"Possibly missing or unsolved frames: {missing} logged exposure(s) fall between {prev.FileName} and {cur.FileName} but are not in the solved trace. This jump may span more than one exposure.";
                list.Add(new {
                    x = Math.Round((px + cx) / 2.0, 4),
                    y = Math.Round((py + cy) / 2.0, 4),
                    isGap = true,
                    label,
                    tooltip
                });
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

            if (rows.Count == 0) {
                sb.AppendLine("      <h4 class=\"text-sm font-semibold uppercase tracking-wide text-sky-400\">Sequencer events (NINA logs)</h4>");
                sb.AppendLine("      <div class=\"mt-3 rounded-lg border border-slate-700 bg-slate-900/40 p-4\">");
                sb.AppendLine($"        <p class=\"text-sm text-slate-300\">No correlated dither or center-after-drift events between consecutive frames for target <span class=\"text-sky-300\">{Escape(sectionTargetName)}</span>.</p>");
                sb.AppendLine("        <p class=\"mt-2 text-xs leading-relaxed text-slate-500\">SeeDrift reads the same NINA log(s) used for this run. Events attach only to intervals between two lights of the same target.</p>");
                sb.AppendLine("      </div>");
                sb.AppendLine("    </div>");
                return;
            }

            sb.AppendLine("      <details class=\"mt-3 rounded-lg border border-slate-700 bg-slate-900/30\">");
            sb.AppendLine(
                $"        <summary class=\"cursor-pointer px-3 py-2.5 text-sm font-semibold uppercase tracking-wide text-sky-400 marker:text-sky-400 hover:bg-slate-900/50\">"
                + $"Sequencer events (NINA logs) — {rows.Count} row{(rows.Count == 1 ? "" : "s")} (click to expand)</summary>");
            sb.AppendLine("        <div class=\"border-t border-slate-700\">");
            sb.AppendLine("          <p class=\"px-3 pt-3 text-xs text-slate-500\">Between-frame triggers for this target (same-target consecutive frames only).</p>");
            sb.AppendLine("          <div class=\"mt-2 overflow-x-auto px-3 pb-3\">");
            sb.AppendLine("            <table class=\"seedrift-seq-table min-w-full table-fixed divide-y divide-slate-700 text-left text-xs\">");
            sb.AppendLine("              <thead class=\"bg-slate-900/80 text-sky-300\"><tr>");
            sb.AppendLine("                <th class=\"w-[22%] px-3 py-2 font-medium\">From frame</th>");
            sb.AppendLine("                <th class=\"w-[22%] px-3 py-2 font-medium\">To frame</th>");
            sb.AppendLine("                <th class=\"w-[14%] whitespace-nowrap px-3 py-2 font-medium\">Kind</th>");
            sb.AppendLine("                <th class=\"w-[42%] px-3 py-2 font-medium\">Detail</th>");
            sb.AppendLine("              </tr></thead>");
            sb.AppendLine("              <tbody class=\"divide-y divide-slate-800 bg-slate-950/40 text-slate-300\">");
            foreach (var r in rows) {
                sb.AppendLine("              <tr class=\"align-top\">");
                sb.AppendLine($"                <td class=\"break-all px-3 py-2 font-mono text-[11px] leading-snug\">{Escape(r.fromFn)}</td>");
                sb.AppendLine($"                <td class=\"break-all px-3 py-2 font-mono text-[11px] leading-snug\">{Escape(r.toFn)}</td>");
                sb.AppendLine($"                <td class=\"whitespace-normal px-3 py-2 align-top\">{Escape(r.kind)}</td>");
                sb.AppendLine($"                <td class=\"break-words px-3 py-2 text-[11px] leading-snug text-slate-400\">{Escape(r.detail)}</td>");
                sb.AppendLine("              </tr>");
            }
            sb.AppendLine("              </tbody>");
            sb.AppendLine("            </table>");
            sb.AppendLine("          </div>");
            sb.AppendLine("        </div>");
            sb.AppendLine("      </details>");
            sb.AppendLine("    </div>");
        }

        /// <summary>
        /// Run-wide "Session settings used" card showing values observed from NINA logs (CenterAfterDrift threshold,
        /// CenterAfterDrift evaluate cadence, DitherAfterExposures cadence, Mount Dither Pixels via DirectGuider
        /// pulse magnitudes, and observed dither pulse guide durations as a Seestar Alpaca guide-rate proxy).
        /// </summary>
        private static string FormatSessionSettingsHtml(SessionSequencerSettings s) {
            var rows = new List<string>();

            if (s.DitherPixelsMedian.HasValue && s.DitherPulseCount > 0) {
                var px = s.DitherPixelsMedian.Value;
                var pulses = s.DitherPulseCount;
                // Realized line is shown only when we have ≥3 commanded/measured pairs (otherwise the median is too noisy);
                // a value well below ~85–90% is most often a settle-time problem, not a wrong commanded magnitude.
                string realizedExtra = "";
                if (s.RealizedSampleCount >= 3
                    && s.RealizedMeasuredDitherPixels.HasValue
                    && s.RealizedDitherRatio.HasValue) {
                    realizedExtra = string.Format(CultureInfo.InvariantCulture,
                        "Realized {0:0.#} px ({1:0.#}%) median across {2} pulse{3}",
                        s.RealizedMeasuredDitherPixels.Value,
                        s.RealizedDitherRatio.Value * 100.0,
                        s.RealizedSampleCount,
                        s.RealizedSampleCount == 1 ? "" : "s");
                }
                rows.Add(FormatSettingsRow(
                    "Mount Dither Device — Dither Pixels",
                    string.Format(CultureInfo.InvariantCulture, "~{0:0.#} px", px),
                    $"median of {pulses} pulse{(pulses == 1 ? "" : "s")} · commanded |Δ| from DirectGuider log",
                    false,
                    realizedExtra));
            }

            if (s.CenterMaxArcMin.HasValue) {
                var v = s.CenterMaxArcMin.Value;
                var detail = "configured threshold (CenterAfterDriftTrigger evaluation lines)";
                var variance = (s.CenterMaxArcMinMin.HasValue && s.CenterMaxArcMinMax.HasValue
                                && Math.Abs(s.CenterMaxArcMinMax.Value - s.CenterMaxArcMinMin.Value) > 0.05)
                    ? string.Format(CultureInfo.InvariantCulture, " · varied: {0:0.#}′–{1:0.#}′",
                        s.CenterMaxArcMinMin!.Value, s.CenterMaxArcMinMax!.Value)
                    : "";
                rows.Add(FormatSettingsRow(
                    "Center After Drift — Max arc-minutes",
                    string.Format(CultureInfo.InvariantCulture, "{0:0.#}′ threshold", v),
                    detail + variance,
                    false));
            }

            if (s.CenterEvaluateAfterExposures.HasValue && s.ObservedCenterEvaluationCount >= 2) {
                var n = s.CenterEvaluateAfterExposures.Value;
                var detail = "inferred from frame gaps between CenterAfterDrift evaluation lines";
                var variance = (s.CenterEvaluateAfterExposuresMin.HasValue && s.CenterEvaluateAfterExposuresMax.HasValue
                                && s.CenterEvaluateAfterExposuresMin.Value != s.CenterEvaluateAfterExposuresMax.Value)
                    ? $" · varied: every {s.CenterEvaluateAfterExposuresMin}–{s.CenterEvaluateAfterExposuresMax} exposures"
                    : "";
                rows.Add(FormatSettingsRow(
                    "Center After Drift — Evaluate after exposures",
                    $"every ~{n} exposure{(n == 1 ? "" : "s")}",
                    detail + variance,
                    true));
            }

            if (s.DitherAfterExposuresN.HasValue && s.ObservedDitherTriggerCount > 0) {
                var n = s.DitherAfterExposuresN.Value;
                var detail = s.DitherCadenceInferred
                    ? "inferred from frame gaps between DitherAfterExposures triggers"
                    : "from NINA Starting Trigger log line";
                var variance = (s.DitherAfterExposuresNMin.HasValue && s.DitherAfterExposuresNMax.HasValue
                                && s.DitherAfterExposuresNMin.Value != s.DitherAfterExposuresNMax.Value)
                    ? $" · varied: every {s.DitherAfterExposuresNMin}–{s.DitherAfterExposuresNMax} exposures"
                    : "";
                var label = s.DitherCadenceInferred
                    ? $"every ~{n} exposure{(n == 1 ? "" : "s")}"
                    : $"every {n} exposure{(n == 1 ? "" : "s")}";
                rows.Add(FormatSettingsRow(
                    "Dither After Exposures — Cadence",
                    label,
                    detail + variance,
                    s.DitherCadenceInferred));
            }

            if (s.DitherGuideDurationFirstSecMedian.HasValue && s.DitherGuideDurationSecondSecMedian.HasValue
                && s.GuideDurationSampleCount > 0) {
                var ra = s.DitherGuideDurationFirstSecMedian.Value;
                var dec = s.DitherGuideDurationSecondSecMedian.Value;
                var count = s.GuideDurationSampleCount;
                rows.Add(FormatSettingsRow(
                    "Dither pulse guide durations",
                    string.Format(CultureInfo.InvariantCulture, "{0:0.00} s RA · {1:0.00} s Dec", ra, dec),
                    $"median across {count} pulse{(count == 1 ? "" : "s")} · proxy for Seestar Alpaca RA/Dec guide rate",
                    false));
            }

            if (rows.Count == 0)
                return "";

            var sb = new StringBuilder();
            sb.AppendLine("<section class=\"mb-10\">");
            sb.AppendLine("  <details class=\"rounded-lg border border-slate-800 bg-slate-900/40\">");
            sb.AppendLine("    <summary class=\"flex cursor-pointer items-baseline justify-between gap-3 p-4 marker:text-sky-300 hover:bg-slate-900/60\">");
            sb.AppendLine("      <h2 class=\"text-base font-semibold text-sky-200\">Session settings used <span class=\"ml-2 text-[11px] font-normal normal-case text-slate-400\">(click to expand)</span></h2>");
            sb.AppendLine("      <span class=\"text-[10px] uppercase tracking-wide text-slate-500\">observed from NINA logs</span>");
            sb.AppendLine("    </summary>");
            sb.AppendLine("    <div class=\"border-t border-slate-800 px-4 pb-4 pt-3\">");
            sb.AppendLine("      <p class=\"text-xs text-slate-500\">Values reflect how NINA was configured during this run. Effectiveness of each event is in the per-target panels below. RA/Dec guide rate is a Seestar Alpaca setting and only visible here as observed dither pulse durations.</p>");
            sb.AppendLine("      <dl class=\"mt-3 grid gap-3 sm:grid-cols-2\">");
            foreach (var row in rows)
                sb.AppendLine(row);
            sb.AppendLine("      </dl>");
            sb.AppendLine("    </div>");
            sb.AppendLine("  </details>");
            sb.AppendLine("</section>");
            return sb.ToString();
        }

        private static string FormatSettingsRow(string label, string value, string detail, bool inferred, string? secondaryLine = null) {
            var inferredBadge = inferred
                ? " <span class=\"ml-1 rounded border border-slate-700 bg-slate-800/60 px-1.5 py-0.5 text-[9px] uppercase tracking-wide text-slate-400\">inferred</span>"
                : "";
            var secondary = string.IsNullOrEmpty(secondaryLine)
                ? ""
                : $"<dd class=\"mt-1 text-xs text-slate-300\">{Escape(secondaryLine!)}</dd>";
            return
                "      <div class=\"rounded-md border border-slate-800 bg-slate-950/40 p-3\">" +
                $"<dt class=\"text-[11px] font-semibold uppercase tracking-wide text-sky-400\">{Escape(label)}</dt>" +
                $"<dd class=\"mt-1 text-sm text-slate-100\">{Escape(value)}{inferredBadge}</dd>" +
                secondary +
                $"<dd class=\"mt-1 text-[11px] text-slate-500\">{Escape(detail)}</dd>" +
                "</div>";
        }

        private static string FormatPageHeaderLogsHtml(IReadOnlyList<DriftTrackingService.CompletedTarget> targets) {
            var paths = targets
                .SelectMany(t => t.SourceLogPaths ?? Array.Empty<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var sb = new StringBuilder();
            if (paths.Count == 0) {
                sb.AppendLine("      <div class=\"mt-3 text-sm text-slate-500\">");
                sb.AppendLine("        <p class=\"font-medium text-slate-400\">NINA log files</p>");
                sb.AppendLine("        <p class=\"mt-1 text-xs\">Paths were not recorded for this export (older SeeDrift build).</p>");
                sb.AppendLine("      </div>");
                return sb.ToString();
            }

            sb.AppendLine("      <div class=\"mt-3 text-sm\">");
            sb.AppendLine("        <p class=\"font-medium text-slate-300\">NINA log files on this page</p>");
            if (paths.Count == 1) {
                sb.AppendLine($"        <p class=\"mt-1 break-all text-slate-400\">{Escape(paths[0])}</p>");
            } else {
                sb.AppendLine($"        <p class=\"mt-1 text-xs text-slate-500\">{paths.Count} files — NINA logs that contributed <span class=\"font-mono\">Saved image to …</span> lines for each <strong class=\"font-medium text-slate-400\">run</strong> below (<strong class=\"font-medium text-slate-400\">Stop</strong>: arm/disarm window + matching log names; <strong class=\"font-medium text-slate-400\">Previous session report</strong>: the file you chose).</p>");
                sb.AppendLine("        <ul class=\"mt-2 max-h-48 list-disc space-y-1 overflow-y-auto pl-5 text-slate-400\">");
                const int max = 16;
                foreach (var p in paths.Take(max))
                    sb.AppendLine($"          <li class=\"break-all\">{Escape(p)}</li>");
                if (paths.Count > max)
                    sb.AppendLine($"          <li class=\"text-slate-500\">… and {paths.Count - max} more</li>");
                sb.AppendLine("        </ul>");
            }

            sb.AppendLine("      </div>");
            return sb.ToString();
        }

        private static string FormatBatchFramesAndDuration(int sampleCount, TimeSpan runDuration) {
            var frames = $"{sampleCount} frame{(sampleCount == 1 ? "" : "s")}";
            if (runDuration.TotalMilliseconds < 0.5)
                return frames;
            return $"{frames} · Processing time {RunDurationFormatter.ToReadable(runDuration)}";
        }

        /// <summary>Frame count plus FITS integration time when <c>EXPTIME</c> (or related keywords) is available.</summary>
        private static string FormatTargetFramesSubtitle(IReadOnlyList<DriftSample> grp) {
            var frames = $"{grp.Count} frame{(grp.Count == 1 ? "" : "s")}";
            var integration = FormatTargetIntegrationTime(grp);
            return string.IsNullOrEmpty(integration) ? frames : $"{frames} · {integration}";
        }

        private static string FormatTargetIntegrationTime(IReadOnlyList<DriftSample> grp) {
            var values = grp
                .Select(s => s.ExposureDurationSeconds)
                .Where(v => v.HasValue && v.Value > 0)
                .Select(v => v!.Value)
                .ToList();
            if (values.Count == 0)
                return "";
            var min = values.Min();
            var max = values.Max();
            if (Math.Abs(min - max) < 0.05)
                return FormatIntegrationSeconds(min);
            return $"{FormatIntegrationSeconds(min)}–{FormatIntegrationSeconds(max)}";
        }

        private static string FormatIntegrationSeconds(double seconds) {
            if (seconds >= 60)
                return string.Format(CultureInfo.InvariantCulture, "{0:0.#}m", seconds / 60.0);
            if (seconds >= 10)
                return string.Format(CultureInfo.InvariantCulture, "{0:0}s", seconds);
            if (seconds >= 1)
                return string.Format(CultureInfo.InvariantCulture, "{0:0.#}s", seconds);
            return string.Format(CultureInfo.InvariantCulture, "{0:0.##}s", seconds);
        }

        /// <summary>First/last exposure start from solved frames (DATE-OBS / log timing), shown in local wall time.</summary>
        private static string FormatTargetExposureRangeHtml(IReadOnlyList<DriftSample> grp) {
            if (grp.Count == 0)
                return "";
            var startUtc = grp.Min(s => s.ExposureStartUtc);
            var endUtc = grp.Max(s => s.ExposureStartUtc);
            var fmt = "yyyy-MM-dd HH:mm";
            var sl = startUtc.ToLocalTime().ToString(fmt, CultureInfo.InvariantCulture);
            if (startUtc == endUtc)
                return $"<span class=\"text-slate-500\">Exposure</span> {Escape(sl)} <span class=\"text-slate-500\">(local)</span>";
            var el = endUtc.ToLocalTime().ToString(fmt, CultureInfo.InvariantCulture);
            return $"<span class=\"text-slate-500\">Start</span> {Escape(sl)} <span class=\"text-slate-500\">·</span> <span class=\"text-slate-500\">End</span> {Escape(el)} <span class=\"text-slate-500\">(local)</span>";
        }

        private static string Escape(string s) {
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }
    }
}
