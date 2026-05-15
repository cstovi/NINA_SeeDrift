using System;
using System.IO;

namespace NINA.Plugin.SeeDrift.Models {

    public sealed class RecentNinaLogSession {
        public string LogPath { get; init; } = "";
        public DateTime LocalStart { get; init; }
        public int TargetCount { get; init; }
        public int ImageCount { get; init; }
        public TimeSpan Duration { get; init; }
        public string Scope { get; init; } = "";
        public SeestarDeviceInfo SeestarDevice { get; init; } = SeestarDeviceInfo.Unknown;

        public string DisplayLabel {
            get {
                var duration = FormatDuration(Duration);
                var scope = string.IsNullOrWhiteSpace(SeestarDevice.DisplayName)
                    ? "Unknown scope"
                    : SeestarDevice.DisplayName.Trim();
                return $"{LocalStart:yyyy-MM-dd HH:mm} — {scope} — {TargetCount} target{(TargetCount == 1 ? "" : "s")}, {ImageCount} image{(ImageCount == 1 ? "" : "s")}, {duration}";
            }
        }

        public override string ToString() => DisplayLabel;

        /// <summary>Multi-line summary shown under the recent-log dropdown.</summary>
        public string DetailText {
            get {
                var scope = string.IsNullOrWhiteSpace(SeestarDevice.DisplayName)
                    ? "Unknown scope"
                    : SeestarDevice.DisplayName.Trim();
                var file = string.IsNullOrWhiteSpace(LogPath) ? "—" : Path.GetFileName(LogPath);
                return string.Join(Environment.NewLine, new[] {
                    $"Log file: {file}",
                    $"Started: {LocalStart:yyyy-MM-dd HH:mm}",
                    $"Scope: {scope}",
                    $"Session: {TargetCount} target{(TargetCount == 1 ? "" : "s")}, {ImageCount} image{(ImageCount == 1 ? "" : "s")}, {FormatDuration(Duration)}"
                });
            }
        }

        private static string FormatDuration(TimeSpan duration) {
            if (duration <= TimeSpan.Zero)
                return "0m";
            if (duration.TotalHours >= 1)
                return $"{(int)duration.TotalHours}h{duration.Minutes:00}m";
            return $"{Math.Max(1, (int)Math.Round(duration.TotalMinutes))}m";
        }
    }
}
