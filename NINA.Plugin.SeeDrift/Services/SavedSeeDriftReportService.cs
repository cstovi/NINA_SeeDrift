using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using NINA.Plugin.SeeDrift.Models;
using NINA.Plugin.SeeDrift.Utility;

namespace NINA.Plugin.SeeDrift.Services {

    internal static class SavedSeeDriftReportService {
        private static readonly Regex RxKind = new(
            "<meta\\s+name=\"seedrift-report-kind\"\\s+content=\"([^\"]+)\"",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex RxVersion = new(
            "<meta\\s+name=\"seedrift-generator-version\"\\s+content=\"([^\"]+)\"",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex RxReportPayload = new(
            "<script\\s+type=\"application/json\"\\s+id=\"seedrift-report-data\"",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex RxPayload = new(
            "<script\\s+type=\"application/json\"\\s+id=\"seedrift-report-data\"[^>]*>(.*?)</script>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex RxSessionDateFromFileName = new(
            "_sess(\\d{8})",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static IReadOnlyList<SavedSeeDriftReport> LoadReports() {
            var reports = new List<SavedSeeDriftReport>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var folder in new[] { SeeDriftPaths.ReportLibrary }) {
                if (!Directory.Exists(folder))
                    continue;
                foreach (var path in Directory.EnumerateFiles(folder, "*.html", SearchOption.TopDirectoryOnly)) {
                    if (!seen.Add(path))
                        continue;
                    if (!TryReadReportInfo(path, out var kind, out var version, out var sessionDate, out var device, out var targetCount, out var frameCount))
                        continue;
                    reports.Add(new SavedSeeDriftReport {
                        Path = path,
                        Kind = kind,
                        Version = version,
                        SessionDate = sessionDate,
                        SeestarDevice = device,
                        TargetCount = targetCount,
                        FrameCount = frameCount,
                        LastWriteLocal = File.GetLastWriteTime(path)
                    });
                }
            }

            return reports
                .OrderByDescending(r => r.LastWriteLocal)
                .ThenBy(r => r.FileName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool TryReadReportInfo(
                string path,
                out string kind,
                out string version,
                out string sessionDate,
                out SeestarDeviceInfo device,
                out int targetCount,
                out int frameCount) {
            kind = "";
            version = "";
            sessionDate = "";
            device = SeestarDeviceInfo.Unknown;
            targetCount = 0;
            frameCount = 0;
            try {
                var html = File.ReadAllText(path);
                var kindMatch = RxKind.Match(html);
                if (!kindMatch.Success && !RxReportPayload.IsMatch(html))
                    return false;
                kind = kindMatch.Success
                    ? kindMatch.Groups[1].Value.Trim()
                    : "night";
                var versionMatch = RxVersion.Match(html);
                version = versionMatch.Success ? versionMatch.Groups[1].Value.Trim() : "";
                TryReadPayloadSummary(html, out sessionDate, out device, out targetCount, out frameCount);
                if (string.IsNullOrWhiteSpace(sessionDate))
                    sessionDate = TrySessionDateFromFileName(path);
                return kind.Equals("night", StringComparison.OrdinalIgnoreCase)
                    || kind.Equals("comparison", StringComparison.OrdinalIgnoreCase);
            } catch {
                return false;
            }
        }

        private static void TryReadPayloadSummary(string html, out string sessionDate, out SeestarDeviceInfo device, out int targetCount, out int frameCount) {
            sessionDate = "";
            device = SeestarDeviceInfo.Unknown;
            targetCount = 0;
            frameCount = 0;
            try {
                var match = RxPayload.Match(html);
                if (!match.Success)
                    return;
                var payload = JsonSerializer.Deserialize<SeeDriftReportPayload>(
                    match.Groups[1].Value,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (payload?.Targets == null)
                    return;
                sessionDate = payload.SessionDate ?? "";
                device = payload.SeestarDevice ?? SeestarDeviceInfo.Unknown;
                targetCount = payload.Targets.Count;
                frameCount = payload.Targets.Sum(t => t.FrameCount);
            } catch {
                sessionDate = "";
                device = SeestarDeviceInfo.Unknown;
                targetCount = 0;
                frameCount = 0;
            }
        }

        private static string TrySessionDateFromFileName(string path) {
            var name = Path.GetFileNameWithoutExtension(path) ?? "";
            var match = RxSessionDateFromFileName.Match(name);
            if (!match.Success)
                return "";
            var raw = match.Groups[1].Value;
            return $"{raw[..4]}-{raw.Substring(4, 2)}-{raw.Substring(6, 2)}";
        }
    }
}
