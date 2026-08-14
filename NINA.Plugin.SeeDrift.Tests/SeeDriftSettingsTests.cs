using System;
using System.IO;
using NINA.Plugin.SeeDrift;
using Xunit;

namespace NINA.Plugin.SeeDrift.Tests {

    /// <summary>
    /// Verifies that an existing-but-unreadable settings.json is never overwritten with defaults
    /// by automatic saves, while missing files and successfully loaded settings still save normally.
    /// </summary>
    public sealed class SeeDriftSettingsTests : IDisposable {

        private readonly string _settingsDirectory;
        private readonly string _settingsPath;

        public SeeDriftSettingsTests() {
            _settingsDirectory = Path.Combine(Path.GetTempPath(), "SeeDriftSettingsTests", Guid.NewGuid().ToString("N"));
            _settingsPath = Path.Combine(_settingsDirectory, "settings.json");
            SeeDriftSettings.SettingsPathOverride = _settingsPath;
        }

        public void Dispose() {
            SeeDriftSettings.SettingsPathOverride = null;
            try {
                if (Directory.Exists(_settingsDirectory))
                    Directory.Delete(_settingsDirectory, recursive: true);
            } catch {
                // Best-effort cleanup only — never fail a test over temp file removal.
            }
        }

        [Fact]
        public void Load_with_missing_file_returns_saveable_defaults_that_create_the_file() {
            var settings = SeeDriftSettings.Load();

            Assert.True(settings.CanPersist);
            Assert.False(File.Exists(_settingsPath));

            // A fresh instance may be saved, which creates the settings file.
            settings.Save();
            Assert.True(File.Exists(_settingsPath));
        }

        [Fact]
        public void Load_with_valid_file_returns_persisted_values_and_saves_changes() {
            Directory.CreateDirectory(_settingsDirectory);
            File.WriteAllText(_settingsPath, """{"DiscordWebhookUrl":"https://discord.com/api/webhooks/secret","MinExposuresPerTarget":77}""");

            var settings = SeeDriftSettings.Load();

            Assert.True(settings.CanPersist);
            Assert.Equal("https://discord.com/api/webhooks/secret", settings.DiscordWebhookUrl);
            Assert.Equal(77, settings.MinExposuresPerTarget);

            settings.DiscordWebhookUrl = "https://discord.com/api/webhooks/new";
            settings.Save();

            var saved = File.ReadAllText(_settingsPath);
            Assert.Contains("https://discord.com/api/webhooks/new", saved);
        }

        [Fact]
        public void Load_with_corrupt_file_returns_defaults_that_never_overwrite_the_file() {
            Directory.CreateDirectory(_settingsDirectory);
            File.WriteAllText(_settingsPath, "{ not valid json !!");

            var settings = SeeDriftSettings.Load();

            Assert.False(settings.CanPersist);
            Assert.Equal("", settings.DiscordWebhookUrl);

            // Automatic/unrelated saves must leave the existing (unreadable) file untouched.
            settings.DiscordWebhookUrl = "https://discord.com/api/webhooks/hijacked";
            settings.Save();

            Assert.Equal("{ not valid json !!", File.ReadAllText(_settingsPath));
        }

        [Fact]
        public void Load_with_file_containing_no_settings_document_never_overwrites_the_file() {
            Directory.CreateDirectory(_settingsDirectory);
            File.WriteAllText(_settingsPath, "null");

            var settings = SeeDriftSettings.Load();

            Assert.False(settings.CanPersist);
            Assert.Equal("", settings.DiscordWebhookUrl);

            settings.DiscordWebhookUrl = "https://discord.com/api/webhooks/hijacked";
            settings.Save();

            Assert.Equal("null", File.ReadAllText(_settingsPath));
        }

        [Fact]
        public void Load_marks_state_appropriately() {
            // Missing file
            Assert.Equal(SettingsLoadState.MissingFile, SeeDriftSettings.Load().LoadState);

            // Valid file
            Directory.CreateDirectory(_settingsDirectory);
            File.WriteAllText(_settingsPath, """{"VideoFrameRate":12}""");
            Assert.Equal(SettingsLoadState.Loaded, SeeDriftSettings.Load().LoadState);

            // Existing file that cannot be deserialized
            File.WriteAllText(_settingsPath, "{ nope");
            Assert.Equal(SettingsLoadState.LoadFailed, SeeDriftSettings.Load().LoadState);
        }
    }
}
