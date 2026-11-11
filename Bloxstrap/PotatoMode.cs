namespace Bloxstrap
{
    /// <summary>
    /// PSYstrap "potato mode": the lightest Roblox can run, for weak PCs, weak Wi-Fi and background clients.
    /// Uses only flags on Roblox's client allowlist (others are ignored by Roblox) plus Roblox's own graphics quality
    /// setting. Applies from the next launch, and to every client, since all clients share one settings folder.
    /// </summary>
    public static class PotatoMode
    {
        private static readonly IReadOnlyDictionary<string, string> Flags = new Dictionary<string, string>
        {
            // lowest texture resolution - also means far less to download when joining a game
            ["DFFlagTextureQualityOverrideEnabled"] = "True",
            ["DFIntTextureQualityOverride"] = "0",

            // no grass
            ["FIntFRMMinGrassDistance"] = "0",
            ["FIntFRMMaxGrassDistance"] = "0",

            // lowest render quality and level of detail
            ["DFIntDebugFRMQualityLevelOverride"] = "1",
            ["DFIntCSGLevelOfDetailSwitchingDistance"] = "0",
            ["DFIntCSGLevelOfDetailSwitchingDistanceL12"] = "0",
            ["DFIntCSGLevelOfDetailSwitchingDistanceL23"] = "0",
            ["DFIntCSGLevelOfDetailSwitchingDistanceL34"] = "0",

            // stop recalculating lighting voxels (saves CPU)
            ["DFFlagDebugPauseVoxelizer"] = "True"
        };

        /// <summary>Adds or removes the potato flags. Saved with the rest of the fast flags.</summary>
        public static void SetFlags(bool enabled)
        {
            foreach (var pair in Flags)
                App.FastFlags.SetValue(pair.Key, enabled ? pair.Value : null);
        }

        /// <summary>At launch: Roblox's graphics slider to its lowest manual level.</summary>
        public static void ApplyGraphicsQuality()
        {
            if (!App.Settings.Prop.PotatoMode)
                return;

            RobloxUserSettings.SetValue("int", "GraphicsQualityLevel", "1");
            RobloxUserSettings.SetValue("token", "SavedQualityLevel", "1");
        }
    }
}
