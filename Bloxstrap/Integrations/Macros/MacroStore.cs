using Bloxstrap.Models.Macros;

namespace Bloxstrap.Integrations.Macros
{
    /// <summary>Loads and saves macros as JSON files in %LocalAppData%\PSYstrap\Macros.</summary>
    public static class MacroStore
    {
        public static string Directory => Path.Combine(Paths.Base, "Macros");

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private static string GetPath(string name) => Path.Combine(Directory, MakeFileName(name) + ".json");

        private static string MakeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');

            return name.Trim();
        }

        public static List<string> ListNames()
        {
            if (!System.IO.Directory.Exists(Directory))
                return new();

            return System.IO.Directory.GetFiles(Directory, "*.json")
                .Select(path => Load(Path.GetFileNameWithoutExtension(path))?.Name ?? Path.GetFileNameWithoutExtension(path))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static Macro? Load(string name)
        {
            try
            {
                string path = GetPath(name);
                return File.Exists(path) ? JsonSerializer.Deserialize<Macro>(File.ReadAllText(path), JsonOptions) : null;
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("MacroStore::Load", ex);
                return null;
            }
        }

        public static void Save(Macro macro)
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(GetPath(macro.Name), JsonSerializer.Serialize(macro, JsonOptions));
        }

        public static void Delete(string name)
        {
            string path = GetPath(name);

            if (File.Exists(path))
                File.Delete(path);
        }

        public static string NextFreeName()
        {
            var existing = ListNames().ToHashSet(StringComparer.OrdinalIgnoreCase);

            for (int i = 1; ; i++)
            {
                string name = $"Macro {i}";

                if (!existing.Contains(name))
                    return name;
            }
        }
    }
}
