using System.IO.Compression;
using System.Reflection;
using System.Text.Json.Nodes;

namespace Bloxstrap
{
    /// <summary>
    /// PSYstrap: bring settings over from another strap (Bloxstrap, Fishstrap, Froststrap...), and back up / restore
    /// PSYstrap's own settings as one zip. Saved accounts are never part of either: they're encrypted to this PC.
    /// </summary>
    public static class SettingsTransfer
    {
        private const string LOG_IDENT = "SettingsTransfer";

        public static readonly string[] KnownStraps = { "Bloxstrap", "Fishstrap", "Froststrap", "Voidstrap", "Nyxstrap" };

        // look and feel stays PSYstrap's own
        private static readonly HashSet<string> SkipKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "Theme", "BootstrapperStyle", "BootstrapperIcon", "BootstrapperTitle", "BootstrapperIconCustomLocation",
            "SelectedCustomTheme", "Channel", "AllowCookieAccess", "EnableAnalytics", "UpdateCheckingEnabled"
        };

        /// <summary>Straps that are installed on this PC (they have a Settings.json in %LocalAppData%).</summary>
        public static List<string> FindInstalledStraps() =>
            KnownStraps.Where(name => File.Exists(Path.Combine(Paths.LocalAppData, name, "Settings.json"))).ToList();

        /// <summary>
        /// Copies another strap's settings, fast flags and mods into PSYstrap. Settings and flags go into memory (hit
        /// Save to keep them); mod files are copied straight away. Returns a short summary.
        /// </summary>
        public static string ImportFrom(string strap)
        {
            string root = Path.Combine(Paths.LocalAppData, strap);
            int settings = 0, flags = 0, mods = 0;

            // settings: same-named simple values only
            try
            {
                if (JsonNode.Parse(File.ReadAllText(Path.Combine(root, "Settings.json"))) is JsonObject theirs)
                {
                    foreach (var property in typeof(Models.Persistable.Settings).GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    {
                        if (!property.CanWrite || SkipKeys.Contains(property.Name) || theirs[property.Name] is not JsonValue value)
                            continue;

                        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

                        if (!(type.IsPrimitive || type.IsEnum || type == typeof(string)))
                            continue;

                        try
                        {
                            object? converted = type.IsEnum
                                ? (value.TryGetValue(out int number) ? Enum.ToObject(type, number) : Enum.Parse(type, value.ToString(), true))
                                : value.Deserialize(property.PropertyType);

                            property.SetValue(App.Settings.Prop, converted);
                            settings++;
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Couldn't read {strap}'s settings: {ex.Message}");
            }

            // fast flags
            try
            {
                string flagFile = Path.Combine(root, "Modifications", "ClientSettings", "ClientAppSettings.json");

                if (File.Exists(flagFile) && JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(flagFile)) is { } theirFlags)
                {
                    foreach (var (key, value) in theirFlags)
                    {
                        App.FastFlags.Prop[key] = value.ValueKind == JsonValueKind.String ? value.GetString()! : value.ToString();
                        flags++;
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Couldn't read {strap}'s fast flags: {ex.Message}");
            }

            // mod files (fonts, sounds, textures...)
            string modFolder = Path.Combine(root, "Modifications");

            if (Directory.Exists(modFolder))
            {
                foreach (string file in Directory.GetFiles(modFolder, "*", SearchOption.AllDirectories))
                {
                    string relative = Path.GetRelativePath(modFolder, file);

                    if (relative.StartsWith("ClientSettings", StringComparison.OrdinalIgnoreCase))
                        continue;

                    try
                    {
                        string target = Path.Combine(Paths.Modifications, relative);
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        File.Copy(file, target, true);
                        mods++;
                    }
                    catch { }
                }
            }

            App.Logger.WriteLine(LOG_IDENT, $"Imported from {strap}: {settings} settings, {flags} flags, {mods} mod files");
            return $"Got {settings} settings, {flags} fast flags and {mods} mod files from {strap}. Press Save to keep the settings and flags.";
        }

        #region Backup
        // what goes in a backup (relative to the PSYstrap folder); accounts stay out on purpose
        private static readonly string[] BackupFiles = { "Settings.json", "Playtime.json", "RecentServers.json" };
        private static readonly string[] BackupFolders = { "Macros", "Modifications", "FlagProfiles", "ModProfiles" };

        public static void ExportBackup(string zipPath)
        {
            if (File.Exists(zipPath))
                File.Delete(zipPath);

            using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

            foreach (string name in BackupFiles)
            {
                string path = Path.Combine(Paths.Base, name);
                if (File.Exists(path))
                    zip.CreateEntryFromFile(path, name);
            }

            foreach (string folder in BackupFolders)
            {
                string path = Path.Combine(Paths.Base, folder);
                if (!Directory.Exists(path))
                    continue;

                foreach (string file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                    zip.CreateEntryFromFile(file, Path.GetRelativePath(Paths.Base, file).Replace('\\', '/'));
            }

            App.Logger.WriteLine(LOG_IDENT, $"Backed up to {zipPath}");
        }

        /// <summary>Restores a backup made by ExportBackup. Only known files and folders are written.</summary>
        public static int ImportBackup(string zipPath)
        {
            using var zip = ZipFile.OpenRead(zipPath);
            string root = Path.GetFullPath(Paths.Base) + Path.DirectorySeparatorChar;
            int restored = 0;

            foreach (var entry in zip.Entries)
            {
                if (entry.FullName.EndsWith('/'))
                    continue;

                string relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                string top = relative.Split(Path.DirectorySeparatorChar)[0];

                bool allowed = BackupFiles.Contains(relative, StringComparer.OrdinalIgnoreCase)
                    || BackupFolders.Contains(top, StringComparer.OrdinalIgnoreCase);

                string target = Path.GetFullPath(Path.Combine(Paths.Base, relative));

                // nothing outside the PSYstrap folder, whatever the zip says
                if (!allowed || !target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    continue;

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, true);
                restored++;
            }

            App.Settings.Load(false);
            App.FastFlags.Load(false);

            App.Logger.WriteLine(LOG_IDENT, $"Restored {restored} files from {zipPath}");
            return restored;
        }
        #endregion
    }
}
