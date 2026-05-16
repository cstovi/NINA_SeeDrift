using System;
using System.IO;

namespace NINA.Plugin.SeeDrift.Utility {

    /// <summary>Resolves FITS paths from NINA logs, with optional original→alternative root mapping when files moved.</summary>
    internal static class FitsPathResolver {

        public static bool TryResolveExistingFile(
                string logPath,
                string? originalRoot,
                string? alternativeRoot,
                out string resolvedPath) {
            resolvedPath = logPath ?? "";
            if (string.IsNullOrWhiteSpace(logPath))
                return false;

            string normalizedLog;
            try {
                normalizedLog = Path.GetFullPath(logPath.Trim());
            } catch {
                return false;
            }

            try {
                if (File.Exists(normalizedLog)) {
                    resolvedPath = normalizedLog;
                    return true;
                }
            } catch {
                return false;
            }

            if (!TryBuildAlternativePath(normalizedLog, originalRoot, alternativeRoot, out var alternativePath))
                return false;

            try {
                if (File.Exists(alternativePath)) {
                    resolvedPath = alternativePath;
                    return true;
                }
            } catch {
                // ignore
            }

            return false;
        }

        public static bool TryBuildAlternativePath(
                string logPath,
                string? originalRoot,
                string? alternativeRoot,
                out string alternativePath) {
            alternativePath = "";
            if (string.IsNullOrWhiteSpace(originalRoot) || string.IsNullOrWhiteSpace(alternativeRoot))
                return false;

            string normLog;
            string normOriginal;
            string normAlternative;
            try {
                normLog = Path.GetFullPath(logPath.Trim());
                normOriginal = NormalizeRoot(originalRoot);
                normAlternative = NormalizeRoot(alternativeRoot);
            } catch {
                return false;
            }

            if (!IsUnderRoot(normLog, normOriginal, out var relativeSuffix))
                return false;

            if (string.IsNullOrEmpty(relativeSuffix)
                || relativeSuffix.Contains("..", StringComparison.Ordinal))
                return false;

            try {
                alternativePath = Path.GetFullPath(Path.Combine(normAlternative, relativeSuffix));
            } catch {
                return false;
            }

            if (!IsUnderRoot(alternativePath, normAlternative, out _))
                return false;

            return true;
        }

        private static string NormalizeRoot(string root) {
            var trimmed = root.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return Path.GetFullPath(trimmed);
        }

        private static bool IsUnderRoot(string fullPath, string root, out string relativeSuffix) {
            relativeSuffix = "";
            if (string.IsNullOrEmpty(fullPath) || string.IsNullOrEmpty(root))
                return false;

            if (fullPath.Length < root.Length)
                return false;

            if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return false;

            if (fullPath.Length == root.Length) {
                relativeSuffix = "";
                return true;
            }

            var next = fullPath[root.Length];
            if (next != Path.DirectorySeparatorChar && next != Path.AltDirectorySeparatorChar)
                return false;

            relativeSuffix = fullPath.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return true;
        }
    }
}
