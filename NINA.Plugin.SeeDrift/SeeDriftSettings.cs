using System;
using System.IO;
using Newtonsoft.Json;
using NINA.Core.Utility;
using NINA.Plugin.SeeDrift.Utility;

namespace NINA.Plugin.SeeDrift {

    public class SeeDriftSettings {

        /// <summary>Run report: path to a NINA .log file (Saved image to … lines).</summary>
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

        // --- Video Preview Generation ---

        /// <summary>Video frame rate in fps (1-60, default 10).</summary>
        public int VideoFrameRate { get; set; } = 10;

        /// <summary>FFmpeg encoder preset: "ultrafast", "fast", "medium", "slow" (default "fast").</summary>
        public string VideoEncoderPreset { get; set; } = "fast";

        /// <summary>Output resolution: "native", "1080p", "720p" (default "native").</summary>
        public string VideoResolution { get; set; } = "native";

        /// <summary>
        /// If true, generates video preview automatically when a report is created
        /// (default false — user clicks button to trigger).
        /// </summary>
        public bool AutoGenerateVideo { get; set; } = false;

        /// <summary>Log path prefix for capture location (e.g. C:\Users\…\N.I.N.A). Used with <see cref="AlternativeImageMappingAlternativeRoot"/>.</summary>
        public string AlternativeImageMappingOriginalRoot { get; set; } = "";

        /// <summary>Secondary root when FITS were moved (e.g. P:\Astro\Home).</summary>
        public string AlternativeImageMappingAlternativeRoot { get; set; } = "";

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
                    } catch (Exception ex) { Logger.Warning($"[SeeDrift] Settings rewrite failed: {ex.Message}"); }
                    return s;
                }
            } catch (Exception ex) { Logger.Warning($"[SeeDrift] Settings load failed: {ex.Message}"); }
            return new SeeDriftSettings();
        }

        public void Save() {
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
                File.WriteAllText(SettingsPath, JsonConvert.SerializeObject(this, Formatting.Indented));
            } catch (Exception ex) { Logger.Warning($"[SeeDrift] Settings save failed: {ex.Message}"); }
        }
    }
}
