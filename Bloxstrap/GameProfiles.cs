namespace Bloxstrap
{
    /// <summary>
    /// PSYstrap per-game profiles: an FPS cap and potato on/off for one game, applied when that game is launched.
    /// The game is read from the launch link (website Play button, join links, rejoins, the account launcher).
    /// Picking a game inside the Roblox app happens after launch, so profiles can't apply there until a restart.
    /// </summary>
    public static class GameProfiles
    {
        private static readonly Regex PlaceIdPattern = new(@"placeId(?:=|%3D)(\d+)", RegexOptions.IgnoreCase);

        public static long? GetPlaceId(string launchCommandLine)
        {
            var match = PlaceIdPattern.Match(launchCommandLine ?? "");
            return match.Success && long.TryParse(match.Groups[1].Value, out long id) ? id : null;
        }

        public static GameProfile? ForLaunch(string launchCommandLine)
        {
            long? placeId = GetPlaceId(launchCommandLine);

            if (placeId is null || !App.Settings.Prop.Gameplay.GameProfiles.TryGetValue(placeId.Value, out var profile))
                return null;

            App.Logger.WriteLine("GameProfiles", $"Using the profile for {profile.Name} ({placeId})");
            return profile;
        }

        /// <summary>Universe (the whole game) a place belongs to.</summary>
        public static async Task<long?> GetUniverseIdAsync(long placeId)
        {
            try
            {
                using var universe = JsonDocument.Parse(await App.HttpClient.GetStringAsync($"https://apis.roblox.com/universes/v1/places/{placeId}/universe"));
                return universe.RootElement.GetProperty("universeId").GetInt64();
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("GameProfiles::GetUniverseIdAsync", ex);
                return null;
            }
        }

        /// <summary>Game names by universe ID (one request for all of them).</summary>
        public static async Task<Dictionary<long, string>> GetUniverseNamesAsync(IEnumerable<long> universeIds)
        {
            var names = new Dictionary<long, string>();
            var ids = universeIds.Distinct().Take(50).ToList();

            if (ids.Count == 0)
                return names;

            try
            {
                using var games = JsonDocument.Parse(await App.HttpClient.GetStringAsync($"https://games.roblox.com/v1/games?universeIds={String.Join(",", ids)}"));

                foreach (var game in games.RootElement.GetProperty("data").EnumerateArray())
                    names[game.GetProperty("id").GetInt64()] = game.GetProperty("name").GetString() ?? "";
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("GameProfiles::GetUniverseNamesAsync", ex);
            }

            return names;
        }

        /// <summary>Game name for a place ID, from Roblox's public API (no sign-in needed).</summary>
        public static async Task<string?> GetGameNameAsync(long placeId)
        {
            if (await GetUniverseIdAsync(placeId) is not long universeId)
                return null;

            return (await GetUniverseNamesAsync(new[] { universeId })).GetValueOrDefault(universeId);
        }
    }
}