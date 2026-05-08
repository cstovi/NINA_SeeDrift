using System;
using System.IO;
using Newtonsoft.Json;

namespace NINA.Plugin.SeeDrift {

    public class SeeDriftSettings {
        public bool OnlySeestarCameras { get; set; } = true;
        public bool AutoResetOnTargetChange { get; set; } = true;

        /// <summary>Folder for HTML exports (Save dialog default).</summary>
        public string HtmlExportFolder { get; set; } = "";

        private static string SettingsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NINA", "SeeDrift", "settings.json");

        public static SeeDriftSettings Load() {
            try {
                if (File.Exists(SettingsPath))
                    return JsonConvert.DeserializeObject<SeeDriftSettings>(File.ReadAllText(SettingsPath)) ?? new SeeDriftSettings();
            } catch { }
            var s = new SeeDriftSettings();
            try {
                var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (!string.IsNullOrEmpty(docs))
                    s.HtmlExportFolder = Path.Combine(docs, "SeeDrift");
            } catch { }
            return s;
        }

        public void Save() {
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
                File.WriteAllText(SettingsPath, JsonConvert.SerializeObject(this, Formatting.Indented));
            } catch { }
        }
    }
}
