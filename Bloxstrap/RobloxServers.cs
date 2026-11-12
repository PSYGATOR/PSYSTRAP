namespace Bloxstrap
{
    /// <summary>
    /// PSYstrap: reads a game's public server list - the same list Roblox's own "Servers" tab shows - to find the
    /// emptiest server, and builds links that join a specific server.
    /// </summary>
    public static class RobloxServers
    {
        public record ServerInfo(string Id, int Playing, int MaxPlayers);

        private class ServerPage
        {
            [JsonPropertyName("data")]
            public List<ServerEntry> Data { get; set; } = new();
        }

        private class ServerEntry
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = "";

            [JsonPropertyName("playing")]
            public int Playing { get; set; }

            [JsonPropertyName("maxPlayers")]
            public int MaxPlayers { get; set; }

            [JsonPropertyName("ping")]
            public double? Ping { get; set; }

            [JsonPropertyName("fps")]
            public double? Fps { get; set; }
        }

        /// <summary>A server for the server browser. Ping is the players' average (what Roblox reports).</summary>
        public record ListedServer(string Id, int Playing, int MaxPlayers, int? AveragePing, double? Fps);

        /// <summary>Up to pages × 100 public servers, biggest first.</summary>
        public static async Task<List<ListedServer>> ListAsync(long placeId, int pages = 3)
        {
            var servers = new List<ListedServer>();
            string? cursor = null;

            for (int page = 0; page < pages; page++)
            {
                try
                {
                    string url = $"https://games.roblox.com/v1/games/{placeId}/servers/Public?sortOrder=Desc&limit=100" + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
                    var result = await Http.GetJson<ServerPageWithCursor>(url);

                    if (result is null)
                        break;

                    servers.AddRange(result.Data
                        .Where(s => !String.IsNullOrEmpty(s.Id))
                        .Select(s => new ListedServer(s.Id, s.Playing, s.MaxPlayers, s.Ping is double p ? (int)Math.Round(p) : null, s.Fps)));

                    cursor = result.NextPageCursor;

                    if (String.IsNullOrEmpty(cursor))
                        break;
                }
                catch (Exception ex)
                {
                    App.Logger.WriteException("RobloxServers::ListAsync", ex);
                    break;
                }
            }

            return servers;
        }

        /// <summary>The public server with the fewest players (but not full), optionally skipping the one you're in.</summary>
        public static async Task<ServerInfo?> FindSmallestAsync(long placeId, string? excludeServerId = null)
        {
            const string LOG_IDENT = "RobloxServers::FindSmallestAsync";

            string url = $"https://games.roblox.com/v1/games/{placeId}/servers/Public?sortOrder=Asc&excludeFullGames=true&limit=100";

            try
            {
                var page = await Http.GetJson<ServerPage>(url);

                var best = page?.Data
                    .Where(server => !String.IsNullOrEmpty(server.Id) && server.Id != excludeServerId && server.Playing < server.MaxPlayers)
                    .OrderBy(server => server.Playing)
                    .FirstOrDefault();

                if (best is null)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"No joinable public servers found for place {placeId}");
                    return null;
                }

                App.Logger.WriteLine(LOG_IDENT, $"Smallest server for {placeId}: {best.Id} ({best.Playing}/{best.MaxPlayers})");
                return new ServerInfo(best.Id, best.Playing, best.MaxPlayers);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                return null;
            }
        }

        private class ServerPageWithCursor : ServerPage
        {
            [JsonPropertyName("nextPageCursor")]
            public string? NextPageCursor { get; set; }
        }

        /// <summary>
        /// Player count of one public server, found by paging through the game's server list (a few pages at most,
        /// so huge games may not be found). Private/reserved servers aren't listed.
        /// </summary>
        // the overlay and Discord both ask every minute or two; share the answer and back off if Roblox says slow down
        private static readonly Dictionary<(long, string), (Task<ServerInfo?> Task, DateTime At)> _serverCache = new();
        private static DateTime _backoffUntil = DateTime.MinValue;

        public static Task<ServerInfo?> FindServerAsync(long placeId, string serverId, int maxPages = 5)
        {
            lock (_serverCache)
            {
                var key = (placeId, serverId);

                if (_serverCache.TryGetValue(key, out var cached) && DateTime.Now - cached.At < TimeSpan.FromSeconds(90))
                    return cached.Task;

                if (DateTime.Now < _backoffUntil)
                    return cached.Task ?? Task.FromResult<ServerInfo?>(null);

                var task = LookUpServerAsync(placeId, serverId, maxPages);
                _serverCache[key] = (task, DateTime.Now);
                return task;
            }
        }

        private static async Task<ServerInfo?> LookUpServerAsync(long placeId, string serverId, int maxPages)
        {
            string? cursor = null;

            for (int page = 0; page < maxPages; page++)
            {
                try
                {
                    string url = $"https://games.roblox.com/v1/games/{placeId}/servers/Public?sortOrder=Desc&limit=100" + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
                    var result = await Http.GetJson<ServerPageWithCursor>(url);
                    var match = result?.Data.FirstOrDefault(server => server.Id == serverId);

                    if (match is not null)
                        return new ServerInfo(match.Id, match.Playing, match.MaxPlayers);

                    cursor = result?.NextPageCursor;

                    if (String.IsNullOrEmpty(cursor))
                        break;
                }
                catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    App.Logger.WriteLine("RobloxServers::FindServerAsync", "Roblox says too many requests, waiting 3 minutes");
                    _backoffUntil = DateTime.Now.AddMinutes(3);
                    break;
                }
                catch (Exception ex)
                {
                    App.Logger.WriteException("RobloxServers::FindServerAsync", ex);
                    break;
                }
            }

            return null;
        }

        public static string GetJoinLink(long placeId, string serverId) =>
            String.IsNullOrEmpty(serverId)
                ? $"roblox://experiences/start?placeId={placeId}"
                : $"roblox://experiences/start?placeId={placeId}&gameInstanceId={serverId}";
    }
}
