namespace Bloxstrap
{
    /// <summary>
    /// PSYstrap: fast flag cleanup (remove what Roblox ignores) and fast flag profiles (save/load whole flag sets).
    /// </summary>
    public static class FlagTools
    {
        /// <summary>
        /// The flags Roblox still reads from ClientAppSettings.json, from Roblox's own announcement:
        /// https://devforum.roblox.com/t/allowlist-for-local-client-configuration-via-fast-flags/3966569
        /// Everything else is ignored by the player (Studio still reads everything).
        /// </summary>
        public static readonly HashSet<string> Allowlist = new(StringComparer.Ordinal)
        {
            // geometry
            "DFIntCSGLevelOfDetailSwitchingDistance",
            "DFIntCSGLevelOfDetailSwitchingDistanceL12",
            "DFIntCSGLevelOfDetailSwitchingDistanceL23",
            "DFIntCSGLevelOfDetailSwitchingDistanceL34",
            // rendering
            "FFlagHandleAltEnterFullscreenManually",
            "DFFlagTextureQualityOverrideEnabled",
            "DFIntTextureQualityOverride",
            "FIntDebugForceMSAASamples",
            "DFFlagDisableDPIScale",
            "FFlagDebugGraphicsPreferD3D11",
            "FFlagDebugSkyGray",
            "DFFlagDebugPauseVoxelizer",
            "DFIntDebugFRMQualityLevelOverride",
            "FIntFRMMaxGrassDistance",
            "FIntFRMMinGrassDistance",
            "FFlagDebugGraphicsPreferVulkan",
            "FFlagDebugGraphicsPreferOpenGL",
            // user interface
            "FIntGrassMovementReducedMotionFactor"
        };

        /// <summary>Flags set right now that Roblox will ignore.</summary>
        public static List<string> IgnoredFlags() => App.FastFlags.Prop.Keys.Where(key => !Allowlist.Contains(key)).OrderBy(key => key).ToList();

        /// <summary>Removes the ignored flags (saved with the Settings window's Save button). Returns how many.</summary>
        public static int RemoveIgnored()
        {
            var ignored = IgnoredFlags();

            foreach (string key in ignored)
                App.FastFlags.Prop.Remove(key);

            return ignored.Count;
        }

        #region Profiles
        private static string Folder => Path.Combine(Paths.Base, "FlagProfiles");

        public static List<string> ListProfiles() =>
            Directory.Exists(Folder)
                ? Directory.GetFiles(Folder, "*.json").Select(Path.GetFileNameWithoutExtension).OfType<string>().OrderBy(n => n).ToList()
                : new();

        public static string SafeName(string name) => String.Concat(name.Split(Path.GetInvalidFileNameChars())).Trim();

        /// <summary>Saves the current flags (as they are in the editor) under a name.</summary>
        public static void SaveProfile(string name)
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Path.Combine(Folder, $"{SafeName(name)}.json"),
                JsonSerializer.Serialize(App.FastFlags.Prop, new JsonSerializerOptions { WriteIndented = true }));
        }

        /// <summary>Replaces the current flags with a saved profile (saved with the Save button).</summary>
        public static bool LoadProfile(string name)
        {
            string path = Path.Combine(Folder, $"{SafeName(name)}.json");

            if (!File.Exists(path))
                return false;

            var flags = JsonSerializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(path));

            if (flags is null)
                return false;

            App.FastFlags.Prop.Clear();

            foreach (var (key, value) in flags)
                App.FastFlags.Prop[key] = value.ToString() ?? "";

            return true;
        }

        public static void DeleteProfile(string name)
        {
            try { File.Delete(Path.Combine(Folder, $"{SafeName(name)}.json")); } catch { }
        }
        #endregion
    }
}
