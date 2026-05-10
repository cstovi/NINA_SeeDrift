using System;

namespace NINA.Plugin.SeeDrift.Models {

    public sealed class RecentNinaLogSession {
        public string LogPath { get; init; } = "";
        public DateTime LocalStart { get; init; }
        public int TargetCount { get; init; }
        public int ImageCount { get; init; }
        public TimeSpan Duration { get; init; }
        public string Scope { get; init; } = "";

        public string DisplayLabel {
            get {
                var duration = FormatDuration(Duration);
                return $"{LocalStart:yyyy-MM-dd HH:mm} — {TargetCount} target{(TargetCount == 1 ? "" : "s")}, {ImageCount} image{(ImageCount == 1 ? "" : "s")}, {duration}";
            }
        }

        public override string ToString() => DisplayLabel;

        private static string FormatDuration(TimeSpan duration) {
            if (duration <= TimeSpan.Zero)
                return "0m";
            if (duration.TotalHours >= 1)
                return $"{(int)duration.TotalHours}h{duration.Minutes:00}m";
            return $"{Math.Max(1, (int)Math.Round(duration.TotalMinutes))}m";
        }
    }
}
