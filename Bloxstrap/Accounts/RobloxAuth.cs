using System.Net.Http.Headers;

namespace Bloxstrap.Accounts
{
    /// <summary>
    /// PSYstrap: talks to roblox.com for saved accounts - who a session belongs to, and the one-time launch ticket the
    /// Roblox website itself uses when you press Play.
    ///
    /// Uses its own HttpClient without PSYstrap's request logging, so session cookies can never end up in a log file.
    /// </summary>
    public static class RobloxAuth
    {
        private const string CookieName = ".ROBLOSECURITY";

        private static readonly HttpClient Client = new(new HttpClientHandler { UseCookies = false, AutomaticDecompression = DecompressionMethods.All });

        public record AccountInfo(long Id, string Name, string DisplayName);

        private class AuthenticatedUser
        {
            [JsonPropertyName("id")] public long Id { get; set; }
            [JsonPropertyName("name")] public string Name { get; set; } = "";
            [JsonPropertyName("displayName")] public string DisplayName { get; set; } = "";
        }

        private static HttpRequestMessage NewRequest(HttpMethod method, string url, string session)
        {
            var request = new HttpRequestMessage(method, url);
            request.Headers.Add("Cookie", $"{CookieName}={session}");
            request.Headers.Referrer = new Uri("https://www.roblox.com/");
            request.Headers.UserAgent.ParseAdd($"{App.ProjectName}/{App.Version}");
            return request;
        }

        /// <summary>Who the session belongs to, or null if it's expired/invalid.</summary>
        public static async Task<AccountInfo?> GetAccountAsync(string session)
        {
            using var request = NewRequest(HttpMethod.Get, "https://users.roblox.com/v1/users/authenticated", session);
            using var response = await Client.SendAsync(request);

            if (!response.IsSuccessStatusCode)
                return null;

            var user = JsonSerializer.Deserialize<AuthenticatedUser>(await response.Content.ReadAsStringAsync());
            return user is null || user.Id == 0 ? null : new AccountInfo(user.Id, user.Name, user.DisplayName);
        }

        /// <summary>
        /// One-time ticket that starts a Roblox client signed in as this account (what roblox.com does for "Play").
        /// Roblox requires a CSRF token: the first POST is refused with one, the second uses it.
        /// </summary>
        public static async Task<string?> GetLaunchTicketAsync(string session)
        {
            const string url = "https://auth.roblox.com/v1/authentication-ticket/";
            string? csrf = null;

            for (int attempt = 0; attempt < 2; attempt++)
            {
                using var request = NewRequest(HttpMethod.Post, url, session);
                request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

                if (csrf is not null)
                    request.Headers.Add("x-csrf-token", csrf);

                using var response = await Client.SendAsync(request);

                if (response.Headers.TryGetValues("rbx-authentication-ticket", out var tickets))
                    return tickets.FirstOrDefault();

                if (response.Headers.TryGetValues("x-csrf-token", out var tokens))
                {
                    csrf = tokens.FirstOrDefault();
                    continue;
                }

                App.Logger.WriteLine("RobloxAuth::GetLaunchTicketAsync", $"Ticket request failed ({(int)response.StatusCode})");
                return null;
            }

            return null;
        }

        /// <summary>
        /// The roblox-player: link a browser launch uses. serverId joins that exact server; accessCode a private server;
        /// neither joins any public server of the place.
        /// </summary>
        public static string BuildLaunchLink(string ticket, long placeId, string? serverId = null, string? accessCode = null)
        {
            long browserTrackerId = Random.Shared.NextInt64(100_000_000_000, 999_999_999_999);

            string placeLauncher = !String.IsNullOrEmpty(accessCode)
                ? $"https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestPrivateGame&browserTrackerId={browserTrackerId}&placeId={placeId}&accessCode={accessCode}&isPlayTogetherGame=false"
                : !String.IsNullOrEmpty(serverId)
                    ? $"https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestGameJob&browserTrackerId={browserTrackerId}&placeId={placeId}&gameId={serverId}&isPlayTogetherGame=false"
                    : $"https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestGame&browserTrackerId={browserTrackerId}&placeId={placeId}&isPlayTogetherGame=false";

            long launchTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            return $"roblox-player:1+launchmode:play+gameinfo:{ticket}+launchtime:{launchTime}" +
                   $"+placelauncherurl:{Uri.EscapeDataString(placeLauncher)}+browsertrackerid:{browserTrackerId}" +
                   "+robloxLocale:en_us+gameLocale:en_us+channel:+LaunchExp:InApp";
        }
    }
}
