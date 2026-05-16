using System;
using System.IO;
using NINA.Plugin.SeeDrift.Utility;
using Xunit;

namespace NINA.Plugin.SeeDrift.Tests.Utility {

    public sealed class FitsPathResolverTests {

        [Fact]
        public void TryResolveExistingFile_prefers_log_path_when_it_exists() {
            var dir = CreateTempDir();
            try {
                var file = Path.Combine(dir, "img.fits");
                File.WriteAllText(file, " ");
                var alt = Path.Combine(dir, "alt");
                Directory.CreateDirectory(alt);

                Assert.True(FitsPathResolver.TryResolveExistingFile(file, dir, alt, out var resolved));
                Assert.Equal(Path.GetFullPath(file), Path.GetFullPath(resolved));
            } finally {
                TryDeleteDir(dir);
            }
        }

        [Fact]
        public void TryResolveExistingFile_uses_alternative_when_log_missing() {
            var original = CreateTempDir();
            var alternative = CreateTempDir();
            try {
                var rel = Path.Combine("M42", "lights", "2026-05-15", "img.fits");
                var altFile = Path.Combine(alternative, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(altFile)!);
                File.WriteAllText(altFile, " ");

                var logPath = Path.Combine(original, rel);
                Assert.True(FitsPathResolver.TryResolveExistingFile(logPath, original, alternative, out var resolved));
                Assert.Equal(Path.GetFullPath(altFile), Path.GetFullPath(resolved));
            } finally {
                TryDeleteDir(original);
                TryDeleteDir(alternative);
            }
        }

        [Fact]
        public void TryResolveExistingFile_false_when_log_missing_and_not_under_original_root() {
            var original = CreateTempDir();
            var alternative = CreateTempDir();
            try {
                var other = CreateTempDir();
                var logPath = Path.Combine(other, "img.fits");
                Assert.False(FitsPathResolver.TryResolveExistingFile(logPath, original, alternative, out _));
            } finally {
                TryDeleteDir(original);
                TryDeleteDir(alternative);
            }
        }

        [Fact]
        public void TryResolveExistingFile_false_when_mapping_incomplete() {
            var dir = CreateTempDir();
            try {
                var logPath = Path.Combine(dir, "img.fits");
                Assert.False(FitsPathResolver.TryResolveExistingFile(logPath, "", dir, out _));
                Assert.False(FitsPathResolver.TryResolveExistingFile(logPath, dir, "", out _));
            } finally {
                TryDeleteDir(dir);
            }
        }

        [Fact]
        public void TryBuildAlternativePath_normalizes_trailing_slashes() {
            var original = CreateTempDir();
            var alternative = CreateTempDir();
            try {
                var logPath = Path.Combine(original, "Target", "img.fits");
                Assert.True(FitsPathResolver.TryBuildAlternativePath(
                    logPath,
                    original + Path.DirectorySeparatorChar,
                    alternative + Path.DirectorySeparatorChar,
                    out var built));
                Assert.Equal(
                    Path.GetFullPath(Path.Combine(alternative, "Target", "img.fits")),
                    Path.GetFullPath(built));
            } finally {
                TryDeleteDir(original);
                TryDeleteDir(alternative);
            }
        }

        private static string CreateTempDir() {
            var path = Path.Combine(Path.GetTempPath(), "SeeDriftTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void TryDeleteDir(string path) {
            try {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
            } catch {
                // best effort
            }
        }
    }
}
