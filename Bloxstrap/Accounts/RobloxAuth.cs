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

        #region Friends (friend alerts)
        public record FriendPresence(long UserId, int Type, string Location, long PlaceId, long UniverseId, string? ServerId);

        /// <summary>GET with the session, as JSON. Null on any failure.</summary>
        private static async Task<JsonDocument?> GetJsonAsync(string url, string session)
        {
            using var request = NewRequest(HttpMethod.Get, url, session);
            using var response = await Client.SendAsync(request);

            return response.IsSuccessStatusCode ? JsonDocument.Parse(await response.Content.ReadAsStringAsync()) : null;
        }

        /// <summary>POST JSON with the session, doing the CSRF token dance. Null on any failure.</summary>
        private static async Task<JsonDocument?> PostJsonAsync(string url, string session, object body)
        {
            string json = JsonSerializer.Serialize(body);
            string? csrf = null;

            for (int attempt = 0; attempt < 2; attempt++)
            {
                using var request = NewRequest(HttpMethod.Post, url, session);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                if (csrf is not null)
                    request.Headers.Add("x-csrf-token", csrf);

                using var response = await Client.SendAsync(request);

                if (response.IsSuccessStatusCode)
                    return JsonDocument.Parse(await response.Content.ReadAsStringAsync());

                if (csrf is null && response.Headers.TryGetValues("x-csrf-token", out var tokens))
                {
                    csrf = tokens.FirstOrDefault();
                    continue;
                }

                App.Logger.WriteLine("RobloxAuth::PostJsonAsync", $"{new Uri(url).Host} answered {(int)response.StatusCode}");
                return null;
            }

            return null;
        }

        /// <summary>User IDs of everyone on the account's friends list. Null if it couldn't be read.</summary>
        public static async Task<List<long>?> GetFriendIdsAsync(string session, long userId)
        {
            using var doc = await GetJsonAsync($"https://friends.roblox.com/v1/users/{userId}/friends", session);

            if (doc is null || !doc.RootElement.TryGetProperty("data", out var data))
                return null;

            return data.EnumerateArray()
                .Select(friend => friend.TryGetProperty("id", out var id) ? id.GetInt64() : 0)
                .Where(id => id != 0)
                .ToList();
        }

        /// <summary>Presence of each user (type: 0 offline, 1 online, 2 in game, 3 studio). Asked 50 at a time.</summary>
        public static async Task<List<FriendPresence>?> GetPresenceAsync(string session, IReadOnlyList<long> userIds)
        {
            var result = new List<FriendPresence>();

            for (int start = 0; start < userIds.Count; start += 50)
            {
                var chunk = userIds.Skip(start).Take(50).ToArray();
                using var doc = await PostJsonAsync("https://presence.roblox.com/v1/presence/users", session, new { userIds = chunk });

                if (doc is null || !doc.RootElement.TryGetProperty("userPresences", out var presences))
                    return null;

                foreach (var p in presences.EnumerateArray())
                {
                    static long Long(JsonElement e, string name) =>
                        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;

                    static string? Str(JsonElement e, string name) =>
                        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

                    result.Add(new FriendPresence(Long(p, "userId"), (int)Long(p, "userPresenceType"), Str(p, "lastLocation") ?? "",
                        Long(p, "rootPlaceId") is long root and not 0 ? root : Long(p, "placeId"), Long(p, "universeId"), Str(p, "gameId")));
                }
            }

            return result;
        }

        /// <summary>Display names (or usernames) by user ID.</summary>
        public static async Task<Dictionary<long, string>> GetDisplayNamesAsync(string session, IReadOnlyList<long> userIds)
        {
            var names = new Dictionary<long, string>();

            for (int start = 0; start < userIds.Count; start += 100)
            {
                var chunk = userIds.Skip(start).Take(100).ToArray();
                using var doc = await PostJsonAsync("https://users.roblox.com/v1/users", session, new { userIds = chunk, excludeBannedUsers = false });

                if (doc is null || !doc.RootElement.TryGetProperty("data", out var data))
                    continue;

                foreach (var user in data.EnumerateArray())
                {
                    string name = user.TryGetProperty("displayName", out var d) && d.GetString() is { Length: > 0 } display ? display
                        : user.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";

                    if (user.TryGetProperty("id", out var id))
                        names[id.GetInt64()] = name;
                }
            }

            return names;
        }

        /// <summary>The roblox-player: link for joining whatever server a friend is in (the website's "Join" button).</summary>
        public static string BuildFollowLink(string ticket, long friendUserId)
        {
            long browserTrackerId = Random.Shared.NextInt64(100_000_000_000, 999_999_999_999);
            string placeLauncher = $"https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestFollowUser&browserTrackerId={browserTrackerId}&userId={friendUserId}&isPlayTogetherGame=false";
            long launchTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            return $"roblox-player:1+launchmode:play+gameinfo:{ticket}+launchtime:{launchTime}" +
                   $"+placelauncherurl:{Uri.EscapeDataString(placeLauncher)}+browsertrackerid:{browserTrackerId}" +
                   "+robloxLocale:en_us+gameLocale:en_us+channel:+LaunchExp:InApp";
        }
        #endregion

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
