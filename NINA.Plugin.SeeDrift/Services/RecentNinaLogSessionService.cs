using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NINA.Plugin.SeeDrift.Models;
using NINA.Plugin.SeeDrift.Utility;

namespace NINA.Plugin.SeeDrift.Services {

    internal static class RecentNinaLogSessionService {
        private static readonly TimeSpan RecentWindow = TimeSpan.FromDays(14);

        public static IReadOnlyList<RecentNinaLogSession> LoadRecentSessions() {
            var cutoff = DateTime.Now.Subtract(RecentWindow).Date;
            var logs = NinaLogCorrelator.GetAllNinaLogFiles()
                .Select(p => new {
                    Path = p,
                    Start = NinaLogCorrelator.TryParseNinaLogFileNameLocalStart(p, out var start)
                        ? start
                        : File.GetLastWriteTime(p)
                })
                .Where(x => x.Start >= cutoff)
                .OrderByDescending(x => x.Start)
                .ToList();

            var sessions = new List<RecentNinaLogSession>();
            foreach (var log in logs) {
                sessions.Add(BuildSummary(log.Path, log.Start));
            }

            return sessions;
        }

        private static RecentNinaLogSession BuildSummary(string logPath, DateTime localStart) {
            var device = SeestarDeviceInfoService.FromLogFiles(new[] { logPath });
            if (!NinaLogCorrelator.TryCollectSavedImagePathsFromLogs(
                    new[] { logPath },
                    null,
                    null,
                    out var saves,
                    out _,
                    out _)) {
                return Empty(logPath, localStart);
            }

            var entries = FitsFolderImport.BuildEntriesFromLogSaveOrder(saves);
            if (entries.Count == 0) {
                return new RecentNinaLogSession {
                    LogPath = logPath,
                    LocalStart = localStart,
                    Scope = "NINA log",
                    SeestarDevice = device,
                    ImageCount = 0,
                    TargetCount = 0,
                    Duration = TimeSpan.Zero
                };
            }

            var targets = entries
                .Select(e => string.IsNullOrWhiteSpace(e.TargetLabel) ? "Unknown" : e.TargetLabel.Trim())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var first = entries.Min(e => e.ExposureUtc);
            var last = entries.Max(e => e.ExposureUtc);
            var duration = last > first ? last - first : TimeSpan.Zero;

            return new RecentNinaLogSession {
                LogPath = logPath,
                LocalStart = localStart,
                Scope = FormatScope(targets),
                SeestarDevice = device,
                ImageCount = entries.Count,
                TargetCount = targets.Count,
                Duration = duration
            };
        }

        private static RecentNinaLogSession Empty(string logPath, DateTime localStart) =>
            new() {
                LogPath = logPath,
                LocalStart = localStart,
                Scope = "NINA log",
                SeestarDevice = SeestarDeviceInfoService.FromLogFiles(new[] { logPath }),
                ImageCount = 0,
                TargetCount = 0,
                Duration = TimeSpan.Zero
            };

        private static string FormatScope(IReadOnlyList<string> targets) {
            if (targets.Count == 0)
                return "NINA log";
            if (targets.Count == 1)
                return targets[0];
            if (targets.Count <= 3)
                return string.Join(", ", targets);
            return $"{targets[0]}, {targets[1]}, {targets[2]} +{targets.Count - 3}";
        }
    }
}
