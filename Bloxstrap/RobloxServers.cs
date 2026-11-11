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

        public static string GetJoinLink(long placeId, string serverId) =>
            $"roblox://experiences/start?placeId={placeId}&gameInstanceId={serverId}";
    }
}
