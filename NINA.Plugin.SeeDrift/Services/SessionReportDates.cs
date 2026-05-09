using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace NINA.Plugin.SeeDrift.Services {

    /// <summary>
    /// Session calendar day for HTML heading and <c>sess</c> filename stamp: prefers dates embedded in standard NINA log filenames,
    /// then earliest solved exposure, then log file timestamps.
    /// </summary>
    internal static class SessionReportDates {

        /// <summary>NINA log files are typically named <c>yyyyMMdd-HHmmss-....log</c>.</summary>
        internal static bool TryParseLeadingDateFromLogFileName(string path, out DateOnly date) {
            date = default;
            var fn = Path.GetFileName(path);
            if (fn.Length < 10 || fn[8] != '-')
                return false;
            for (var i = 0; i < 8; i++) {
                if (!char.IsDigit(fn[i]))
                    return false;
            }

            return DateOnly.TryParseExact(
                fn.Substring(0, 8),
                "yyyyMMdd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out date);
        }

        internal static DateOnly? MinLeadingDateFromLogPaths(IEnumerable<string?> paths) {
            DateOnly? min = null;
            foreach (var p in paths) {
                if (string.IsNullOrWhiteSpace(p))
                    continue;
                if (!TryParseLeadingDateFromLogFileName(p.Trim(), out var d))
                    continue;
                min = min.HasValue ? (d < min.Value ? d : min.Value) : d;
            }

            return min;
        }

        /// <summary>Local calendar day describing the session for filenames and report header.</summary>
        internal static DateOnly ResolveSessionCalendarDay(IReadOnlyList<DriftTrackingService.CompletedTarget> targets) {
            var paths = targets.SelectMany(t => t.SourceLogPaths ?? Array.Empty<string>());
            var fromNames = MinLeadingDateFromLogPaths(paths);
            if (fromNames.HasValue)
                return fromNames.Value;

            DateTime? minUtc = null;
            foreach (var t in targets) {
                foreach (var s in t.Samples) {
                    var u = s.ExposureStartUtc;
                    u = u.Kind == DateTimeKind.Utc ? u : u.ToUniversalTime();
                    minUtc = minUtc.HasValue ? (u < minUtc.Value ? u : minUtc.Value) : u;
                }
            }

            if (minUtc.HasValue)
                return DateOnly.FromDateTime(minUtc.Value.ToLocalTime());

            long? minLogTicks = null;
            foreach (var t in targets) {
                if (t.SourceLogPaths == null)
                    continue;
                foreach (var p in t.SourceLogPaths) {
                    try {
                        if (string.IsNullOrWhiteSpace(p) || !File.Exists(p))
                            continue;
                        var w = File.GetLastWriteTimeUtc(p);
                        var ticks = w.Ticks;
                        minLogTicks = minLogTicks.HasValue ? Math.Min(minLogTicks.Value, ticks) : ticks;
                    } catch {
                        // ignore
                    }
                }
            }

            if (minLogTicks.HasValue)
                return DateOnly.FromDateTime(new DateTime(minLogTicks.Value, DateTimeKind.Utc).ToLocalTime());

            return DateOnly.FromDateTime(DateTime.Now);
        }
    }
}
