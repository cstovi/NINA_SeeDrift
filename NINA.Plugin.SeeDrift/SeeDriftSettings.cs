using System;
using System.IO;
using Newtonsoft.Json;
using NINA.Plugin.SeeDrift.Utility;

namespace NINA.Plugin.SeeDrift {

    public class SeeDriftSettings {

        /// <summary>Folder for rolling night HTML exports.</summary>
        public string HtmlExportFolder { get; set; } = "";

        /// <summary>Previous session report: path to a NINA .log file (Saved image to … lines).</summary>
        public string TestReportLogFilePath { get; set; } = "";

        /// <summary>Max concurrent plate solves (1 … <see cref="CpuTopology.MaxPlateSolveParallelism"/>). Default on fresh settings matches physical CPU cores (clamped to that ceiling).</summary>
        public int PlateSolveParallelism { get; set; } =
            Math.Clamp(CpuTopology.PhysicalCoreCount, 1, CpuTopology.MaxPlateSolveParallelism);

        /// <summary>
        /// Night HTML lists only targets with at least this many solved frames in the batch (default 50).
        /// </summary>
        public int MinExposuresPerTarget { get; set; } = 50;

        /// <summary>Optional Discord Execute Webhook URL (<c>https://discord.com/api/webhooks/...</c>). Empty = disabled. Token is secret — never log.</summary>
        public string DiscordWebhookUrl { get; set; } = "";

        /// <summary>Before/after comparison: first saved SeeDrift HTML report.</summary>
        public string CompareBeforeReportPath { get; set; } = "";

        /// <summary>Before/after comparison: second saved SeeDrift HTML report.</summary>
        public string CompareAfterReportPath { get; set; } = "";

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
