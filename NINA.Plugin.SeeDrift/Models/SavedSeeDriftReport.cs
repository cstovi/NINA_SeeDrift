using System;
using System.IO;

namespace NINA.Plugin.SeeDrift.Models {

    public sealed class SavedSeeDriftReport {
        public string Path { get; init; } = "";
        public string Kind { get; init; } = "";
        public string Version { get; init; } = "";
        public string SessionDate { get; init; } = "";
        public SeestarDeviceInfo SeestarDevice { get; init; } = SeestarDeviceInfo.Unknown;
        public int TargetCount { get; init; }
        public int FrameCount { get; init; }
        public DateTime LastWriteLocal { get; init; }

        public string DisplayLabel {
            get {
                var kind = string.IsNullOrWhiteSpace(Kind) ? "report" : Kind.Trim();
                var version = string.IsNullOrWhiteSpace(Version) ? "" : $" · v{Version.Trim()}";
                var date = string.IsNullOrWhiteSpace(SessionDate)
                    ? LastWriteLocal.ToString("yyyy-MM-dd HH:mm")
                    : SessionDate.Trim();
                var device = string.IsNullOrWhiteSpace(SeestarDevice.DisplayName)
                    ? "Unknown scope"
                    : SeestarDevice.DisplayName.Trim();
                var scope = TargetCount > 0 || FrameCount > 0
                    ? $" · {TargetCount} target{(TargetCount == 1 ? "" : "s")} · {FrameCount} frame{(FrameCount == 1 ? "" : "s")}"
                    : "";
                return $"{date} — {device} — {kind}{version}{scope} — {FileName}";
            }
        }

        public string FileName => string.IsNullOrWhiteSpace(Path) ? "" : System.IO.Path.GetFileName(Path);

        public override string ToString() => DisplayLabel;
    }
}
