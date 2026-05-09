using System;
using System.IO;
using Newtonsoft.Json;

namespace NINA.Plugin.SeeDrift {

    public class SeeDriftSettings {

        /// <summary>Folder for rolling night HTML exports.</summary>
        public string HtmlExportFolder { get; set; } = "";

        /// <summary>Scratch folder for plate-solve intermediates (reserved for future use).</summary>
        public string TempWorkingFolder { get; set; } = "";

        /// <summary>UTC ISO 8601 text for Test report observation window start (e.g. <c>2025-05-09T22:00:00Z</c>).</summary>
        public string TestObservationStartUtcIso { get; set; } = "";

        /// <summary>UTC ISO 8601 text for Test report observation window end.</summary>
        public string TestObservationEndUtcIso { get; set; } = "";

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
            try {
                s.TempWorkingFolder = Path.Combine(Path.GetTempPath(), "SeeDrift");
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
