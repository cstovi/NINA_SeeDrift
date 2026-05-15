using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NINA.Plugin.SeeDrift.Utility;

namespace NINA.Plugin.SeeDrift.Services {

    /// <summary>Posts completed night HTML to Discord Execute Webhook (multipart). Errors go to SeeDrift.log only.</summary>
    internal static class DiscordWebhookNotifier {

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(2) };

        /// <summary>Discord attachment limit is 25 MiB for standard uploads.</summary>
        internal const long MaxAttachmentBytes = 25L * 1024 * 1024;

        /// <summary>Fire-and-forget upload; returns immediately. Does not log the webhook URL.</summary>
        public static void EnqueueUpload(string? webhookUrlRaw, string htmlAbsolutePath) {
            if (string.IsNullOrWhiteSpace(webhookUrlRaw)) return;
            var trimmed = webhookUrlRaw.Trim();
            if (!IsAllowedDiscordWebhookUrl(trimmed, out _)) {
                SeeDriftLog.Warning(
                    "SeeDrift: Discord webhook URL ignored (must be https://discord.com/api/webhooks/… or https://discordapp.com/api/webhooks/…).");
                return;
            }

            if (string.IsNullOrWhiteSpace(htmlAbsolutePath) || !File.Exists(htmlAbsolutePath)) {
                SeeDriftLog.Warning("SeeDrift: Discord webhook skipped (night HTML file missing).");
                return;
            }

            var url = trimmed;
            var path = htmlAbsolutePath;
            _ = Task.Run(() => UploadAsync(url, path));
        }

        /// <summary>Fire-and-forget text-only webhook (e.g. Stop with nothing to attach). Does not log the webhook URL.</summary>
        public static void EnqueueTextMessage(string? webhookUrlRaw, string content) {
            if (string.IsNullOrWhiteSpace(webhookUrlRaw) || string.IsNullOrWhiteSpace(content)) return;
            var trimmed = webhookUrlRaw.Trim();
            if (!IsAllowedDiscordWebhookUrl(trimmed, out _)) {
                SeeDriftLog.Warning(
                    "SeeDrift: Discord webhook URL ignored (must be https://discord.com/api/webhooks/… or https://discordapp.com/api/webhooks/…).");
                return;
            }

            var url = trimmed;
            var body = content.Trim();
            _ = Task.Run(() => SendTextOnlyAsync(url, body));
        }

        internal static bool IsAllowedDiscordWebhookUrl(string trimmed, out string normalizedUrl) {
            normalizedUrl = trimmed;
            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)) return false;
            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                return false;
            var host = uri.IdnHost;
            if (!string.Equals(host, "discord.com", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(host, "discordapp.com", StringComparison.OrdinalIgnoreCase))
                return false;
            return uri.AbsolutePath.StartsWith("/api/webhooks/", StringComparison.OrdinalIgnoreCase);
        }

        private static async Task UploadAsync(string webhookUrl, string htmlPath) {
            try {
                var len = new FileInfo(htmlPath).Length;
                var fileName = Path.GetFileName(htmlPath);
                if (len > MaxAttachmentBytes) {
                    SeeDriftLog.Info($"SeeDrift: Discord skipped large night HTML ({len} bytes) — {htmlPath}");
                    await SendTextOnlyAsync(
                            webhookUrl,
                            $"SeeDrift — report **{fileName}** is too large to attach (~25 MiB Discord limit). Open it from Plugins → SeeDrift or `%LocalAppData%\\NINA\\SeeDrift\\Reports`.")
                        .ConfigureAwait(false);
                    return;
                }

                var payloadObj = new {
                    content = $"SeeDrift — night report **{fileName}** attached.",
                    attachments = new[] {
                        new { id = 0, filename = fileName }
                    }
                };
                var json = JsonConvert.SerializeObject(payloadObj);

                using var multipart = new MultipartFormDataContent();
                multipart.Add(new StringContent(json, Encoding.UTF8), "payload_json");

                await using var fs = File.OpenRead(htmlPath);
                var fileContent = new StreamContent(fs);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/html");
                multipart.Add(fileContent, "files[0]", fileName);

                using var resp = await Http.PostAsync(webhookUrl, multipart).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) {
                    var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    SeeDriftLog.Warning($"SeeDrift: Discord webhook POST failed — HTTP {(int)resp.StatusCode}.");
                    if (body.Length > 0 && body.Length < 600)
                        SeeDriftLog.Warning($"SeeDrift: Discord response: {body}");
                } else {
                    SeeDriftLog.Info("SeeDrift: Discord webhook upload completed.");
                }
            } catch (Exception ex) {
                SeeDriftLog.Warning($"SeeDrift: Discord webhook upload failed — {ex.Message}");
            }
        }

        private static async Task SendTextOnlyAsync(string webhookUrl, string content) {
            try {
                var payloadObj = new { content };
                var json = JsonConvert.SerializeObject(payloadObj);
                using var multipart = new MultipartFormDataContent();
                multipart.Add(new StringContent(json, Encoding.UTF8), "payload_json");
                using var resp = await Http.PostAsync(webhookUrl, multipart).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                    SeeDriftLog.Warning($"SeeDrift: Discord webhook (text-only) failed — HTTP {(int)resp.StatusCode}.");
            } catch (Exception ex) {
                SeeDriftLog.Warning($"SeeDrift: Discord webhook (text-only) failed — {ex.Message}");
            }
        }
    }
}
