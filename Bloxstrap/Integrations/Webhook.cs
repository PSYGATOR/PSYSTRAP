namespace Bloxstrap.Integrations
{
    /// <summary>
    /// PSYstrap: posts short messages to a Discord webhook you set up (Settings > AFK &amp; launching), so you get them on
    /// your phone. Only ever sends to the URL you typed in, and only for the events you ticked.
    /// </summary>
    public static class Webhook
    {
        public const int Green = 0x3CF02A, Red = 0xF0203F, Gold = 0xE9B632, Grey = 0x7D9E80;

        private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(15) };
        private static readonly SemaphoreSlim Gate = new(1, 1);
        private static DateTime _lastSent = DateTime.MinValue;

        public static bool IsValidUrl(string? url) =>
            !String.IsNullOrWhiteSpace(url)
            && Regex.IsMatch(url.Trim(), @"^https://(?:(?:canary|ptb)\.)?discord(?:app)?\.com/api/webhooks/\d+/[\w-]+/?$");

        /// <summary>Sends one message in the background, optionally with a picture. Does nothing if no (valid) webhook is set.</summary>
        public static void Send(string title, string text, int color = Green, byte[]? jpeg = null)
        {
            string url = App.Settings.Prop.Gameplay.Webhook.Url.Trim();

            if (!IsValidUrl(url))
                return;

            _ = SendAsync(url, title, text, color, jpeg);
        }

        /// <summary>Sends straight away and says whether Discord accepted it (for the Test button).</summary>
        public static async Task<bool> SendAsync(string url, string title, string text, int color = Green, byte[]? jpeg = null)
        {
            await Gate.WaitAsync();

            try
            {
                // Discord allows ~5 messages per 2 seconds per webhook; stay well under that
                var wait = _lastSent.AddSeconds(1.5) - DateTime.Now;
                if (wait > TimeSpan.Zero)
                    await Task.Delay(wait);

                var body = new
                {
                    username = App.ProjectName,
                    embeds = new[]
                    {
                        new
                        {
                            title = Truncate(title, 250),
                            description = Truncate(text, 2000),
                            color,
                            timestamp = DateTime.UtcNow.ToString("o"),
                            footer = new { text = Environment.MachineName },
                            image = jpeg is null ? null : new { url = "attachment://roblox.jpg" }
                        }
                    }
                };

                var json = JsonSerializer.Serialize(body, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
                HttpContent content;

                if (jpeg is null)
                {
                    content = new StringContent(json, Encoding.UTF8, "application/json");
                }
                else
                {
                    // the picture goes along as a file and the embed points at it
                    var form = new MultipartFormDataContent();
                    form.Add(new StringContent(json, Encoding.UTF8, "application/json"), "payload_json");
                    var file = new ByteArrayContent(jpeg);
                    file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
                    form.Add(file, "files[0]", "roblox.jpg");
                    content = form;
                }

                using (content)
                using (var response = await Client.PostAsync(url, content))
                {
                    _lastSent = DateTime.Now;

                    if (!response.IsSuccessStatusCode)
                        App.Logger.WriteLine("Webhook", $"Discord answered {(int)response.StatusCode}");

                    return response.IsSuccessStatusCode;
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("Webhook", $"Couldn't send: {ex.Message}");
                return false;
            }
            finally
            {
                Gate.Release();
            }
        }

        /// <summary>"on Name" for a saved account, so you can tell your alts apart in the messages.</summary>
        public static string AccountSuffix(long userId)
        {
            if (userId == 0)
                return "";

            var account = Accounts.AccountStore.Load().FirstOrDefault(a => a.UserId == userId);
            return account is null ? "" : $" on {account.Username}";
        }

        private static string Truncate(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
    }
}
