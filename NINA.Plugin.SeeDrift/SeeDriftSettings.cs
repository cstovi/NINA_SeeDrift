using System;
using System.IO;
using System.Threading;
using Newtonsoft.Json;
using NINA.Core.Utility;
using NINA.Plugin.SeeDrift.Utility;

namespace NINA.Plugin.SeeDrift {

    /// <summary>Where a <see cref="SeeDriftSettings"/> instance's values came from — controls whether it may be persisted.</summary>
    internal enum SettingsLoadState {

        /// <summary>No settings file on disk; fresh defaults that may be saved (creates the file).</summary>
        MissingFile,

        /// <summary>Successfully read/deserialized from the existing settings file; normal saves allowed.</summary>
        Loaded,

        /// <summary>settings.json exists but could not be read/deserialized; defaults must never overwrite it.</summary>
        LoadFailed
    }

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
        public string VideoResolution { get; set; } = "480p";

        /// <summary>
        /// If true, generates video preview automatically when a report is created
        /// (default false — user clicks button to trigger).
        /// </summary>
        public bool AutoGenerateVideo { get; set; } = false;

        /// <summary>
        /// If true, overlays a drift reticle (+) on the preview video showing the FOV center
        /// movement across frames.
        /// </summary>
        public bool ShowDriftReticle { get; set; } = true;

        /// <summary>Log path prefix for capture location (e.g. C:\Users\…\N.I.N.A). Used with <see cref="AlternativeImageMappingAlternativeRoot"/>.</summary>
        public string AlternativeImageMappingOriginalRoot { get; set; } = "";

        /// <summary>Secondary root when FITS were moved (e.g. P:\Astro\Home).</summary>
        public string AlternativeImageMappingAlternativeRoot { get; set; } = "";

        private SettingsLoadState _loadState = SettingsLoadState.Loaded;

        /// <summary>True when saving this instance cannot clobber real settings (no file yet, or a successful load).</summary>
        internal bool CanPersist => _loadState != SettingsLoadState.LoadFailed;

        /// <summary>Where this instance's values came from (diagnostics / tests).</summary>
        internal SettingsLoadState LoadState => _loadState;

        private void SetLoadState(SettingsLoadState state) => _loadState = state;

        private static string SettingsPath => SettingsPathOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NINA", "SeeDrift", "settings.json");

        /// <summary>Test hook: overrides the settings file location (null in production).</summary>
        internal static string? SettingsPathOverride { get; set; }

        private static readonly object SettingsFileLock = new();
        private const int SettingsIoAttempts = 5;
        private const int SettingsIoRetryDelayMs = 50;

        public static SeeDriftSettings Load() {
            lock (SettingsFileLock) {
                try {
                    if (File.Exists(SettingsPath)) {
                        var settings = JsonConvert.DeserializeObject<SeeDriftSettings>(ReadAllTextWithRetry(SettingsPath));
                        if (settings != null) {
                            settings.SetLoadState(SettingsLoadState.Loaded);
                            return settings;
                        }

                        // The file exists but holds no usable settings document — defaults must not overwrite it.
                        return UnpersistableDefaults();
                    }
                } catch (Exception ex) {
                    Logger.Warning($"[SeeDrift] Settings load failed: {ex.Message}");
                    return UnpersistableDefaults();
                }

                // No settings file yet — fresh defaults may be saved to create it on first use.
                var fresh = new SeeDriftSettings();
                fresh.SetLoadState(SettingsLoadState.MissingFile);
                return fresh;
            }
        }

        /// <summary>Defaults for an existing-but-unreadable settings file — marked so <see cref="Save"/> refuses to overwrite it.</summary>
        private static SeeDriftSettings UnpersistableDefaults() {
            var settings = new SeeDriftSettings();
            settings.SetLoadState(SettingsLoadState.LoadFailed);
            return settings;
        }

        public void Save() {
            if (!CanPersist) {
                Logger.Warning(
                    "[SeeDrift] Settings save skipped: settings.json exists but could not be loaded, so defaults were not written over it. Repair or delete the file to re-enable saving.");
                return;
            }

            lock (SettingsFileLock) {
                string? tempPath = null;
                try {
                    var settingsDirectory = Path.GetDirectoryName(SettingsPath)!;
                    Directory.CreateDirectory(settingsDirectory);

                    var json = JsonConvert.SerializeObject(this, Formatting.Indented);
                    tempPath = Path.Combine(settingsDirectory, $"settings.{Guid.NewGuid():N}.tmp");
                    File.WriteAllText(tempPath, json);

                    ReplaceSettingsFileWithRetry(tempPath);
                    tempPath = null;
                } catch (Exception ex) {
                    Logger.Warning($"[SeeDrift] Settings save failed: {ex.Message}");
                } finally {
                    TryDeleteTempFile(tempPath);
                }
            }
        }

        private static string ReadAllTextWithRetry(string path) => RetrySettingsIo(() => File.ReadAllText(path));

        private static void ReplaceSettingsFileWithRetry(string tempPath) => RetrySettingsIo(() => {
            if (File.Exists(SettingsPath)) {
                File.Replace(tempPath, SettingsPath, null);
            } else {
                File.Move(tempPath, SettingsPath);
            }
        });

        private static T RetrySettingsIo<T>(Func<T> action) {
            for (var attempt = 1; ; attempt++) {
                try {
                    return action();
                } catch (IOException) when (attempt < SettingsIoAttempts) {
                    Thread.Sleep(SettingsIoRetryDelayMs);
                } catch (UnauthorizedAccessException) when (attempt < SettingsIoAttempts) {
                    Thread.Sleep(SettingsIoRetryDelayMs);
                }
            }
        }

        private static void RetrySettingsIo(Action action) => RetrySettingsIo(() => {
            action();
            return true;
        });

        private static void TryDeleteTempFile(string? tempPath) {
            if (string.IsNullOrEmpty(tempPath) || !File.Exists(tempPath)) {
                return;
            }

            try {
                File.Delete(tempPath);
            } catch {
                // Best effort cleanup only. A failed save must not mask the original settings I/O warning.
            }
        }
    }
}
