namespace Bloxstrap
{
    /// <summary>
    /// PSYstrap: mod profiles. A profile is a saved copy of the Modifications folder (fonts, sounds, sky, cursors...),
    /// not counting fast flags. A game profile can pick one; PSYstrap swaps it in right before that game opens and puts
    /// your normal mods back when you open anything else.
    ///
    /// All Roblox windows share one Roblox folder, so with several windows open the last one launched decides.
    /// </summary>
    public static class ModProfiles
    {
        private const string LOG_IDENT = "ModProfiles";
        private const string NormalName = "_normal";

        private static string Folder => Path.Combine(Paths.Base, "ModProfiles");
        private static string CurrentFile => Path.Combine(Folder, "current.txt");

        public static List<string> List() =>
            Directory.Exists(Folder)
                ? Directory.GetDirectories(Folder).Select(Path.GetFileName).OfType<string>().Where(n => n != NormalName).OrderBy(n => n).ToList()
                : new();

        /// <summary>The profile that's in the Modifications folder now ("" = your normal mods).</summary>
        public static string Current
        {
            get { try { return File.Exists(CurrentFile) ? File.ReadAllText(CurrentFile).Trim() : ""; } catch { return ""; } }
            private set { Directory.CreateDirectory(Folder); File.WriteAllText(CurrentFile, value); }
        }

        /// <summary>Saves what's in the Modifications folder now as a profile.</summary>
        public static void Save(string name)
        {
            name = FlagTools.SafeName(name);
            if (name.Length == 0 || name == NormalName)
                return;

            CopyMods(Paths.Modifications, Path.Combine(Folder, name));
        }

        public static void Delete(string name)
        {
            try { Directory.Delete(Path.Combine(Folder, FlagTools.SafeName(name)), true); } catch { }
        }

        /// <summary>
        /// Called by the bootstrapper before it copies mods into Roblox. Swaps in the game's profile, or puts the normal
        /// mods back if the last launch used a profile and this one doesn't.
        /// </summary>
        public static void PrepareForLaunch(string? profile)
        {
            try
            {
                profile = String.IsNullOrWhiteSpace(profile) ? "" : FlagTools.SafeName(profile);
                string current = Current;

                if (profile == current)
                    return;

                if (profile.Length > 0 && !Directory.Exists(Path.Combine(Folder, profile)))
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Mod profile '{profile}' doesn't exist, leaving mods alone");
                    return;
                }

                // keep your normal mods safe the first time a profile takes over
                if (current.Length == 0)
                    CopyMods(Paths.Modifications, Path.Combine(Folder, NormalName));

                string source = Path.Combine(Folder, profile.Length > 0 ? profile : NormalName);

                if (!Directory.Exists(source))
                    return;

                CopyMods(source, Paths.Modifications);
                Current = profile;
                App.Logger.WriteLine(LOG_IDENT, profile.Length > 0 ? $"Switched to mod profile '{profile}'" : "Put the normal mods back");
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
            }
        }

        /// <summary>Makes target an exact copy of source, leaving the ClientSettings (fast flags) folder alone.</summary>
        private static void CopyMods(string source, string target)
        {
            Directory.CreateDirectory(target);

            foreach (string dir in Directory.GetDirectories(target))
            {
                if (!Path.GetFileName(dir).Equals("ClientSettings", StringComparison.OrdinalIgnoreCase))
                    Directory.Delete(dir, true);
            }

            foreach (string file in Directory.GetFiles(target))
                File.Delete(file);

            if (!Directory.Exists(source))
                return;

            foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(source, file);

                if (relative.StartsWith("ClientSettings", StringComparison.OrdinalIgnoreCase))
                    continue;

                string destination = Path.Combine(target, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination, true);
            }
        }
    }
}
