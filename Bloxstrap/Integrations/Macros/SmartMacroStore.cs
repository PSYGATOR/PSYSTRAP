using Bloxstrap.Models.Macros;

namespace Bloxstrap.Integrations.Macros
{
    /// <summary>Smart macros live in Macros\Smart\{name}\ as smart.json plus one PNG per image detector.</summary>
    public static class SmartMacroStore
    {
        public static string Directory => Path.Combine(MacroStore.Directory, "Smart");

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public static string FolderFor(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');

            return Path.Combine(Directory, name.Trim());
        }

        public static List<string> ListNames()
        {
            if (!System.IO.Directory.Exists(Directory))
                return new();

            return System.IO.Directory.GetDirectories(Directory)
                .Where(dir => File.Exists(Path.Combine(dir, "smart.json")))
                .Select(dir => Load(Path.GetFileName(dir))?.Name ?? Path.GetFileName(dir))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static SmartMacro? Load(string name)
        {
            try
            {
                string path = Path.Combine(FolderFor(name), "smart.json");
                return File.Exists(path) ? JsonSerializer.Deserialize<SmartMacro>(File.ReadAllText(path), JsonOptions) : null;
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("SmartMacroStore::Load", ex);
                return null;
            }
        }

        public static void Save(SmartMacro macro)
        {
            string folder = FolderFor(macro.Name);
            System.IO.Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "smart.json"), JsonSerializer.Serialize(macro, JsonOptions));

            // drop template images no detector uses any more
            var used = macro.Detectors.Select(d => d.TemplateFile).Where(f => !String.IsNullOrEmpty(f)).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (string png in System.IO.Directory.GetFiles(folder, "*.png"))
            {
                if (!used.Contains(Path.GetFileName(png)))
                    File.Delete(png);
            }
        }

        public static void Delete(string name)
        {
            string folder = FolderFor(name);

            if (System.IO.Directory.Exists(folder))
                System.IO.Directory.Delete(folder, true);
        }

        public static string NextFreeName()
        {
            var existing = ListNames().ToHashSet(StringComparer.OrdinalIgnoreCase);

            for (int i = 1; ; i++)
            {
                string name = $"Smart macro {i}";

                if (!existing.Contains(name))
                    return name;
            }
        }
    }
}
