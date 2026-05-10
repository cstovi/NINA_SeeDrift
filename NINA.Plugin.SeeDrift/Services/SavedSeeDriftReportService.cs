using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        public static IReadOnlyList<SavedSeeDriftReport> LoadReports() {
            var reports = new List<SavedSeeDriftReport>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var folder in new[] { SeeDriftPaths.ReportLibrary }) {
                if (!Directory.Exists(folder))
                    continue;
                foreach (var path in Directory.EnumerateFiles(folder, "*.html", SearchOption.TopDirectoryOnly)) {
                    if (!seen.Add(path))
                        continue;
                    if (!TryReadReportInfo(path, out var kind, out var version))
                        continue;
                    reports.Add(new SavedSeeDriftReport {
                        Path = path,
                        Kind = kind,
                        Version = version,
                        LastWriteLocal = File.GetLastWriteTime(path)
                    });
                }
            }

            return reports
                .OrderByDescending(r => r.LastWriteLocal)
                .ThenBy(r => r.FileName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool TryReadReportInfo(string path, out string kind, out string version) {
            kind = "";
            version = "";
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
                return kind.Equals("night", StringComparison.OrdinalIgnoreCase)
                    || kind.Equals("comparison", StringComparison.OrdinalIgnoreCase);
            } catch {
                return false;
            }
        }
    }
}
