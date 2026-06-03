using System;
using System.Diagnostics;
using System.IO;
using NINA.Plugin.SeeDrift.Utility;

namespace NINA.Plugin.SeeDrift.Services {

    /// <summary>
    /// Manages the FFmpeg executable lifecycle: extraction from embedded resource,
    /// validation, and path resolution.
    /// </summary>
    internal class FFmpegManager {

        private static readonly string FFmpegPath = Path.Combine(SeeDriftPaths.ToolsDirectory, "ffmpeg.exe");
        private static readonly string LicensePath = Path.Combine(SeeDriftPaths.ToolsDirectory, "ffmpeg-LICENSE");

        /// <summary>Embedded resource path (assembly manifest resource name).</summary>
        private const string EmbeddedResourceName = "NINA.Plugin.SeeDrift.Resources.ffmpeg.exe";
        private const string EmbeddedLicenseName = "NINA.Plugin.SeeDrift.Resources.LICENSE";

        private bool _extracted;
        private readonly object _lock = new();

        /// <summary>
        /// Returns the validated path to ffmpeg.exe, extracting from embedded resource on first call.
        /// </summary>
        /// <exception cref="FFmpegNotFoundException">If the executable is missing or fails validation.</exception>
        public string GetFFmpegPath() {
            if (!_extracted) {
                lock (_lock) {
                    if (!_extracted)
                        ExtractAndValidate();
                }
            }
            return FFmpegPath;
        }

        /// <summary>
        /// Returns true when ffmpeg.exe exists at the expected path and passes validation.
        /// Does not throw — callers can check availability without handling exceptions.
        /// </summary>
        public bool IsAvailable() {
            try {
                GetFFmpegPath();
                return true;
            } catch {
                return false;
            }
        }

        /// <summary>Path to the extracted FFmpeg license file (may be missing).</summary>
        public string GetLicensePath() => LicensePath;

        private void ExtractAndValidate() {
            // Already on disk?
            if (File.Exists(FFmpegPath)) {
                var fi = new FileInfo(FFmpegPath);
                if (fi.Length > 0 && ValidateExecutable()) {
                    _extracted = true;
                    SeeDriftLog.Debug($"FFmpeg found at {FFmpegPath} ({fi.Length} bytes)");
                    return;
                }
                SeeDriftLog.Warning($"FFmpeg at {FFmpegPath} appears corrupted ({new FileInfo(FFmpegPath).Length} bytes), re-extracting...");
            }

            ExtractFromResource();

            if (!File.Exists(FFmpegPath))
                throw new FFmpegNotFoundException($"FFmpeg extraction failed: {FFmpegPath} not found after extraction");

            if (!ValidateExecutable())
                throw new FFmpegNotFoundException($"FFmpeg at {FFmpegPath} failed validation (ffmpeg -version returned non-zero or unexpected output)");

            _extracted = true;
            SeeDriftLog.Info($"FFmpeg extracted to {FFmpegPath} ({new FileInfo(FFmpegPath).Length} bytes)");
        }

        private void ExtractFromResource() {
            Directory.CreateDirectory(SeeDriftPaths.ToolsDirectory);
            ExtractEmbeddedResource(EmbeddedResourceName, FFmpegPath);
            ExtractEmbeddedResource(EmbeddedLicenseName, LicensePath);
        }

        private static void ExtractEmbeddedResource(string resourceName, string outputPath) {
            using var stream = typeof(FFmpegManager).Assembly.GetManifestResourceStream(resourceName);
            if (stream == null) {
                // License is optional; ffmpeg.exe is required.
                if (resourceName == EmbeddedResourceName)
                    throw new FFmpegNotFoundException($"Embedded resource '{resourceName}' not found in assembly. Ensure ffmpeg.exe is added as EmbeddedResource in the .csproj.");
                return;
            }
            using var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
            stream.CopyTo(fileStream);
        }

        private static bool ValidateExecutable() {
            try {
                var psi = new ProcessStartInfo(FFmpegPath, "-version") {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc == null) {
                    SeeDriftLog.Warning($"FFmpeg validation: Process.Start returned null for {FFmpegPath}");
                    return false;
                }
                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(5000);
                var ok = proc.ExitCode == 0 && output.Contains("ffmpeg", StringComparison.OrdinalIgnoreCase);
                if (!ok)
                    SeeDriftLog.Warning($"FFmpeg validation: exit code {proc.ExitCode}, output starts with: {Truncate(output, 200)}");
                return ok;
            } catch (Exception ex) {
                SeeDriftLog.Warning($"FFmpeg validation threw: {ex.Message}");
                return false;
            }
        }

        private static string Truncate(string s, int maxLen) =>
            s == null ? "" : (s.Length <= maxLen ? s : s.Substring(0, maxLen) + "...");
    }

    /// <summary>Thrown when FFmpeg is not found, extraction fails, or validation fails.</summary>
    internal class FFmpegNotFoundException : Exception {
        public FFmpegNotFoundException(string message) : base(message) { }
        public FFmpegNotFoundException(string message, Exception inner) : base(message, inner) { }
    }
}
