namespace Bloxstrap
{
    /// <summary>
    /// PSYstrap: edits Roblox's own per-user settings file (%LocalAppData%\Roblox\GlobalBasicSettings_N.xml),
    /// the same file Roblox's in-game settings menu saves to. Roblox reads it at startup and rewrites it on exit,
    /// so changes have to be made before Roblox launches.
    /// </summary>
    public static class RobloxUserSettings
    {
        /// <summary>
        /// Frame rate caps offered in the UI - the same values as Roblox's own "Maximum Frame Rate" setting,
        /// since Roblox ignores anything else (it fell back to 60 when given 30). 0 = leave Roblox's setting alone.
        /// </summary>
        public static IReadOnlyList<KeyValuePair<int, string>> FramerateCapChoices { get; } = new List<KeyValuePair<int, string>>
        {
            new(0, "Don't change"),
            new(-1, "Roblox default (60)"),
            new(60, "60 FPS"),
            new(120, "120 FPS"),
            new(144, "144 FPS"),
            new(240, "240 FPS")
        };

        public static string? FindSettingsFile()
        {
            string dir = Path.Combine(Paths.LocalAppData, "Roblox");

            if (!Directory.Exists(dir))
                return null;

            // GlobalBasicSettings_13.xml - pick the highest version, ignore the Studio copy
            return Directory.GetFiles(dir, "GlobalBasicSettings_*.xml")
                .Where(path => !Path.GetFileName(path).Contains("Studio", StringComparison.OrdinalIgnoreCase))
                .Select(path => (path, version: int.TryParse(Regex.Match(Path.GetFileName(path), @"_(\d+)\.xml$").Groups[1].Value, out int v) ? v : -1))
                .OrderByDescending(x => x.version)
                .Select(x => x.path)
                .FirstOrDefault();
        }

        public static int? ReadFramerateCap()
        {
            string? path = FindSettingsFile();

            if (path is null)
                return null;

            var match = Regex.Match(File.ReadAllText(path), "<int name=\"FramerateCap\">(-?\\d+)</int>");
            return match.Success ? int.Parse(match.Groups[1].Value) : null;
        }

        /// <summary>Replaces an existing &lt;type name="name"&gt;value&lt;/type&gt; entry. Missing entries are left alone.</summary>
        public static void SetValue(string type, string name, string value)
        {
            const string LOG_IDENT = "RobloxUserSettings::SetValue";

            try
            {
                string? path = FindSettingsFile();

                if (path is null)
                    return;

                string text = File.ReadAllText(path);
                var pattern = new Regex($"<{type} name=\"{Regex.Escape(name)}\">[^<]*</{type}>");

                if (!pattern.IsMatch(text))
                {
                    App.Logger.WriteLine(LOG_IDENT, $"{name} not found, skipping");
                    return;
                }

                string updated = pattern.Replace(text, $"<{type} name=\"{name}\">{value}</{type}>", 1);

                if (updated == text)
                    return;

                byte[] original = File.ReadAllBytes(path);
                bool bom = original.Length >= 3 && original[0] == 0xEF && original[1] == 0xBB && original[2] == 0xBF;
                File.WriteAllText(path, updated, new UTF8Encoding(bom));
                App.Logger.WriteLine(LOG_IDENT, $"Set {name} to {value}");
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
            }
        }

        /// <summary>Writes App.Settings.Prop.FramerateCap into Roblox's settings file, if one is chosen.</summary>
        public static void ApplyFramerateCap()
        {
            const string LOG_IDENT = "RobloxUserSettings::ApplyFramerateCap";

            int cap = App.Settings.Prop.FramerateCap;

            if (cap == 0)
                return;

            try
            {
                string? path = FindSettingsFile();

                if (path is null)
                {
                    App.Logger.WriteLine(LOG_IDENT, "No GlobalBasicSettings file yet (Roblox hasn't been run), skipping");
                    return;
                }

                string text = File.ReadAllText(path);
                var pattern = new Regex("<int name=\"FramerateCap\">-?\\d+</int>");

                if (!pattern.IsMatch(text))
                {
                    App.Logger.WriteLine(LOG_IDENT, "FramerateCap entry not found, skipping");
                    return;
                }

                string updated = pattern.Replace(text, $"<int name=\"FramerateCap\">{cap}</int>", 1);

                if (updated != text)
                {
                    byte[] original = File.ReadAllBytes(path);
                    bool bom = original.Length >= 3 && original[0] == 0xEF && original[1] == 0xBB && original[2] == 0xBF;
                    File.WriteAllText(path, updated, new UTF8Encoding(bom));
                    App.Logger.WriteLine(LOG_IDENT, $"Set FramerateCap to {cap}");
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
            }
        }
    }
}
