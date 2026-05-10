using System;
using System.IO;

namespace NINA.Plugin.SeeDrift.Models {

    public sealed class SavedSeeDriftReport {
        public string Path { get; init; } = "";
        public string Kind { get; init; } = "";
        public string Version { get; init; } = "";
        public DateTime LastWriteLocal { get; init; }

        public string DisplayLabel {
            get {
                var kind = string.IsNullOrWhiteSpace(Kind) ? "report" : Kind.Trim();
                var version = string.IsNullOrWhiteSpace(Version) ? "" : $" · v{Version.Trim()}";
                return $"{LastWriteLocal:yyyy-MM-dd HH:mm} — {kind}{version} — {FileName}";
            }
        }

        public string FileName => string.IsNullOrWhiteSpace(Path) ? "" : System.IO.Path.GetFileName(Path);

        public override string ToString() => DisplayLabel;
    }
}
