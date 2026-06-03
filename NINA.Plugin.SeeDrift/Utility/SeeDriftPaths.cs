using System;
using System.IO;

namespace NINA.Plugin.SeeDrift.Utility {

    internal static class SeeDriftPaths {
        public static string AppDataRoot =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NINA", "SeeDrift");

        public static string ReportLibrary =>
            Path.Combine(AppDataRoot, "Reports");

        public static string ToolsDirectory =>
            Path.Combine(AppDataRoot, "Tools");

        public static string ResolveReportFolder() {
            return Path.GetFullPath(ReportLibrary);
        }
    }
}
