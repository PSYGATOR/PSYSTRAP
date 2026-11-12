namespace Bloxstrap
{
    /// <summary>
    /// PSYstrap "potato mode": the lightest Roblox can run, for weak PCs, weak Wi-Fi and lots of clients at once.
    /// Uses only flags on Roblox's client allowlist (others are ignored by Roblox) plus Roblox's own graphics and
    /// frame-rate settings. Applies from the next launch, and to every client, since all clients share one settings folder.
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

            // no anti-aliasing (1 sample)
            ["FIntDebugForceMSAASamples"] = "1",

            // stop recalculating lighting voxels (saves CPU)
            ["DFFlagDebugPauseVoxelizer"] = "True"
        };

        /// <summary>Potato mode's frame-rate cap. 60 is the lowest value Roblox accepts.</summary>
        public const int FramerateCap = 60;

        /// <summary>Adds or removes the potato flags. Saved with the rest of the fast flags.</summary>
        public static void SetFlags(bool enabled)
        {
            foreach (var pair in Flags)
                App.FastFlags.SetValue(pair.Key, enabled ? pair.Value : null);
        }

        /// <summary>
        /// Before mods are applied at launch: makes sure every potato flag is set, so flags added in newer versions of
        /// PSYstrap also reach people who switched potato mode on earlier.
        /// </summary>
        /// <param name="enabled">Whether potato mode applies to this launch (a game profile can override the setting).</param>
        public static void EnsureFlags(bool enabled)
        {
            bool allSet = Flags.All(pair => App.FastFlags.GetValue(pair.Key) == pair.Value);
            bool noneSet = Flags.All(pair => App.FastFlags.GetValue(pair.Key) is null);

            if (enabled ? allSet : noneSet)
                return;

            SetFlags(enabled);
            App.FastFlags.Save();
            App.Logger.WriteLine("PotatoMode::EnsureFlags", enabled ? "Added potato flags" : "Removed potato flags");
        }

        /// <summary>At launch: Roblox's graphics slider to its lowest manual level, and the 60 FPS cap if chosen.</summary>
        /// <param name="allowCap">False when a game profile sets its own FPS cap, which then wins over potato's 60.</param>
        public static void ApplyGraphicsQuality(bool enabled, bool allowCap = true)
        {
            if (!enabled)
                return;

            RobloxUserSettings.SetValue("int", "GraphicsQualityLevel", "1");
            RobloxUserSettings.SetValue("token", "SavedQualityLevel", "1");

            // overrides the normal frame-rate cap, which is written just before this
            if (allowCap && App.Settings.Prop.PotatoCapFramerate)
                RobloxUserSettings.SetValue("int", "FramerateCap", FramerateCap.ToString());
        }
    }
}
