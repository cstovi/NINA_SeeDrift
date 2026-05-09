using System;
using System.IO;
using NINA.Core.Utility;

namespace NINA.Plugin.SeeDrift.Utility {

    /// <summary>
    /// Writes SeeDrift messages to <c>%LocalAppData%\NINA\SeeDrift\SeeDrift.log</c> and forwards to NINA’s application log.
    /// </summary>
    internal static class SeeDriftLog {

        private static readonly object FileLock = new();

        public static string LogFilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NINA", "SeeDrift", "SeeDrift.log");

        public static void Debug(string message) {
            Logger.Debug(message);
            AppendFile("DEBUG", message);
        }

        public static void Info(string message) {
            Logger.Info(message);
            AppendFile("INFO", message);
        }

        public static void Warning(string message) {
            Logger.Warning(message);
            AppendFile("WARN", message);
        }

        public static void Error(string message) {
            Logger.Error(message);
            AppendFile("ERROR", message);
        }

        private static void AppendFile(string level, string message) {
            try {
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
                lock (FileLock) {
                    var dir = Path.GetDirectoryName(LogFilePath);
                    if (!string.IsNullOrEmpty(dir))
                        Directory.CreateDirectory(dir);
                    File.AppendAllText(LogFilePath, line);
                }
            } catch {
                // never break NINA for logging failures
            }
        }
    }
}
