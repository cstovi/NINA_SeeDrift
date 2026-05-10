using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using NINA.Plugin.SeeDrift.Models;
using NINA.Plugin.SeeDrift.Utility;

namespace NINA.Plugin.SeeDrift.Services {

    /// <summary>
    /// Finds the newest night HTML in the report library whose embedded data references a given NINA log path.
    /// </summary>
    internal static class ReportLibraryLookup {
        private static readonly Regex RxPayload = new(
            "<script\\s+type=\"application/json\"\\s+id=\"seedrift-report-data\"[^>]*>(.*?)</script>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        /// <summary>
        /// Returns the newest matching report file and a short summary line (frames, processing time).
        /// </summary>
        public static bool TryFindNightReportForNinaLog(string ninaLogPath, out string reportHtmlPath, out string summaryLine) {
            reportHtmlPath = "";
            summaryLine = "";
            if (string.IsNullOrWhiteSpace(ninaLogPath))
                return false;

            string normalizedLog;
            try {
                normalizedLog = Path.GetFullPath(ninaLogPath.Trim());
            } catch {
                normalizedLog = ninaLogPath.Trim();
            }

            var folder = SeeDriftPaths.ReportLibrary;
            if (!Directory.Exists(folder))
                return false;

            var candidates = Directory.EnumerateFiles(folder, "*.html", SearchOption.TopDirectoryOnly)
                .Select(p => new FileInfo(p))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ThenByDescending(f => f.FullName, StringComparer.OrdinalIgnoreCase);

            foreach (var fi in candidates) {
                string html;
                try {
                    html = File.ReadAllText(fi.FullName);
                } catch {
                    continue;
                }

                if (!TryDeserializePayload(html, out var payload) || payload == null)
                    continue;

                var kindMeta = TryReadReportKind(html);
                if (kindMeta != null && kindMeta.Equals("comparison", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (payload.SourceLogPaths is { Count: > 0 }) {
                    foreach (var p in payload.SourceLogPaths) {
                        if (PathsEqual(p, normalizedLog)) {
                            reportHtmlPath = fi.FullName;
                            summaryLine = FormatSummary(payload);
                            return true;
                        }
                    }
                }

                if (LegacyHtmlContainsLogPath(html, normalizedLog)) {
                    reportHtmlPath = fi.FullName;
                    summaryLine = FormatSummary(payload);
                    return true;
                }
            }

            return false;
        }

        private static string? TryReadReportKind(string html) {
            var rx = new Regex(
                "<meta\\s+name=\"seedrift-report-kind\"\\s+content=\"([^\"]+)\"",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);
            var m = rx.Match(html);
            return m.Success ? m.Groups[1].Value.Trim() : null;
        }

        private static bool LegacyHtmlContainsLogPath(string html, string normalizedLog) {
            if (string.IsNullOrWhiteSpace(normalizedLog))
                return false;
            if (html.IndexOf(normalizedLog, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            var escaped = HtmlEscapeMinimal(normalizedLog);
            if (!ReferenceEquals(escaped, normalizedLog) && html.IndexOf(escaped, StringComparison.Ordinal) >= 0)
                return true;
            return false;
        }

        private static string HtmlEscapeMinimal(string s) =>
            s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

        private static bool TryDeserializePayload(string html, out SeeDriftReportPayload? payload) {
            payload = null;
            try {
                var match = RxPayload.Match(html);
                if (!match.Success)
                    return false;
                payload = JsonSerializer.Deserialize<SeeDriftReportPayload>(
                    match.Groups[1].Value,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return payload != null;
            } catch {
                payload = null;
                return false;
            }
        }

        private static bool PathsEqual(string a, string b) {
            try {
                var fa = Path.GetFullPath(a.Trim());
                var fb = Path.GetFullPath(b.Trim());
                return string.Equals(fa, fb, StringComparison.OrdinalIgnoreCase);
            } catch {
                return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
            }
        }

        private static string FormatSummary(SeeDriftReportPayload payload) {
            var frames = payload.Targets?.Sum(t => t.FrameCount) ?? 0;
            var parts = new List<string>();
            if (frames > 0)
                parts.Add($"{frames} frame{(frames == 1 ? "" : "s")}");
            if (payload.RunProcessingSeconds > 0.5) {
                var readable = RunDurationFormatter.ToReadable(TimeSpan.FromSeconds(payload.RunProcessingSeconds));
                parts.Add($"~{readable} processing");
            }

            return parts.Count > 0
                ? string.Join(" · ", parts) + " (saved on disk)"
                : "Saved on disk";
        }
    }
}
