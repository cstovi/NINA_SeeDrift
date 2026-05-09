using System;
using System.IO;
using Newtonsoft.Json;

namespace NINA.Plugin.SeeDrift {

    public class SeeDriftSettings {

        /// <summary>Folder for rolling night HTML exports.</summary>
        public string HtmlExportFolder { get; set; } = "";

        /// <summary>Historic Test report: path to a NINA .log file (Saved image to … lines).</summary>
        public string TestReportLogFilePath { get; set; } = "";

        /// <summary>Max concurrent plate solves (1–16). Each slot uses its own solver instance; higher uses more RAM/CPU.</summary>
        public int PlateSolveParallelism { get; set; } = 4;

        /// <summary>
        /// Night HTML lists only targets with at least this many solved frames in the batch (default 1 = include all).
        /// </summary>
        public int MinExposuresPerTarget { get; set; } = 1;

        private static string SettingsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NINA", "SeeDrift", "settings.json");

        public static SeeDriftSettings Load() {
            try {
                if (File.Exists(SettingsPath)) {
                    var s = JsonConvert.DeserializeObject<SeeDriftSettings>(File.ReadAllText(SettingsPath)) ?? new SeeDriftSettings();
                    try {
                        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
                        File.WriteAllText(SettingsPath, JsonConvert.SerializeObject(s, Formatting.Indented));
                    } catch { }
                    return s;
                }
            } catch { }
            var defaults = new SeeDriftSettings();
            try {
                var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (!string.IsNullOrEmpty(docs))
                    defaults.HtmlExportFolder = Path.Combine(docs, "SeeDrift");
            } catch { }
            return defaults;
        }

        public void Save() {
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
                File.WriteAllText(SettingsPath, JsonConvert.SerializeObject(this, Formatting.Indented));
            } catch { }
        }
    }
}
