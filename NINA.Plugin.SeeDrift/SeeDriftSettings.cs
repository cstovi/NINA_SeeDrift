using System;
using System.IO;
using Newtonsoft.Json;

using NINA.Plugin.SeeDrift.Models;

namespace NINA.Plugin.SeeDrift {

    public class SeeDriftSettings {
        /// <summary>Folder for HTML exports (Save dialog default).</summary>
        public string HtmlExportFolder { get; set; } = "";

        /// <summary>How offline folder import builds the trace.</summary>
        public FolderPlotMode FolderImportPlotMode { get; set; } = FolderPlotMode.PixelRegistration;

        /// <summary>Central crop edge length for phase correlation (pixels).</summary>
        public int RegistrationCropSize { get; set; } = 800;

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
