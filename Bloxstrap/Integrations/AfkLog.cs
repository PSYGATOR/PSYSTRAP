namespace Bloxstrap.Integrations
{
    /// <summary>
    /// PSYstrap: a running log of what every Roblox window did (joined, left, disconnected, rejoined, crashed...), kept in
    /// AfkLog.jsonl so the AFK page, the status report and the phone remote can show it. One line per event; it trims
    /// itself to the last 3000 events.
    /// </summary>
    public static class AfkLog
    {
        public enum Kind
        {
            Joined,
            Left,
            Disconnected,
            Rejoined,
            Crashed,
            Froze,
            Stuck,
            RamRestart,
            InternetDown,
            InternetBack,
            ScheduledOpen,
            ScheduledClose
        }

        public class Entry
        {
            public DateTime At { get; set; }
            public Kind Kind { get; set; }
            public long UserId { get; set; }
            public long PlaceId { get; set; }
            public string Game { get; set; } = "";
            public string Detail { get; set; } = "";
        }

        private const int Keep = 3000;
        private static string FilePath => Path.Combine(Paths.Base, "AfkLog.jsonl");

        private static readonly Dictionary<long, string> NameCache = new();

        /// <summary>Logs an event in the background, looking the game's name up if Roblox hasn't said it yet.</summary>
        public static void Add(Kind kind, ActivityData? game = null, string detail = "")
        {
            var entry = new Entry
            {
                At = DateTime.Now,
                Kind = kind,
                UserId = game?.UserId ?? 0,
                PlaceId = game?.PlaceId ?? 0,
                Game = game?.UniverseDetails?.Data.Name ?? "",
                Detail = detail
            };

            long universeId = game?.UniverseId ?? 0;

            Task.Run(async () =>
            {
                if (entry.Game.Length == 0 && universeId != 0)
                {
                    lock (NameCache)
                        entry.Game = NameCache.GetValueOrDefault(universeId) ?? "";

                    if (entry.Game.Length == 0)
                    {
                        entry.Game = (await GameProfiles.GetUniverseNamesAsync(new[] { universeId })).GetValueOrDefault(universeId) ?? "";

                        lock (NameCache)
                            NameCache[universeId] = entry.Game;
                    }
                }

                Add(entry);
            });
        }

        public static void Add(Entry entry)
        {
            string line = JsonSerializer.Serialize(entry) + "\n";

            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    File.AppendAllText(FilePath, line);

                    if (new FileInfo(FilePath).Length > 900_000)
                        Trim();

                    return;
                }
                catch (IOException)
                {
                    Thread.Sleep(100);
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine("AfkLog", ex.Message);
                    return;
                }
            }
        }

        private static void Trim()
        {
            var lines = File.ReadAllLines(FilePath);

            if (lines.Length > Keep)
                File.WriteAllLines(FilePath, lines[^Keep..]);
        }

        /// <summary>Newest first.</summary>
        public static List<Entry> Load(int max = 200)
        {
            var entries = new List<Entry>();

            try
            {
                if (!File.Exists(FilePath))
                    return entries;

                using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                var lines = new List<string>();

                while (reader.ReadLine() is string line)
                    lines.Add(line);

                for (int i = lines.Count - 1; i >= 0 && entries.Count < max; i--)
                {
                    try
                    {
                        if (JsonSerializer.Deserialize<Entry>(lines[i]) is Entry entry)
                            entries.Add(entry);
                    }
                    catch (JsonException) { }
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("AfkLog", ex.Message);
            }

            return entries;
        }

        public record Summary(int Disconnects, int Rejoins, int Crashes, Dictionary<long, TimeSpan> InGameByAccount);

        /// <summary>Counts since a point in time, plus time spent in game per account (from join/leave pairs).</summary>
        public static Summary Summarise(DateTime since)
        {
            var entries = Load(Keep).Where(e => e.At >= since).OrderBy(e => e.At).ToList();
            var inGame = new Dictionary<long, TimeSpan>();
            var joinedAt = new Dictionary<long, DateTime>();

            foreach (var entry in entries)
            {
                if (entry.Kind == Kind.Joined)
                {
                    joinedAt[entry.UserId] = entry.At;
                }
                else if (entry.Kind is Kind.Left && joinedAt.Remove(entry.UserId, out var start))
                {
                    inGame[entry.UserId] = inGame.GetValueOrDefault(entry.UserId) + (entry.At - start);
                }
            }

            // still in game
            foreach (var (user, start) in joinedAt)
                inGame[user] = inGame.GetValueOrDefault(user) + (DateTime.Now - start);

            return new Summary(
                entries.Count(e => e.Kind == Kind.Disconnected),
                entries.Count(e => e.Kind == Kind.Rejoined),
                entries.Count(e => e.Kind is Kind.Crashed or Kind.Froze),
                inGame);
        }

        public static string Describe(Kind kind) => kind switch
        {
            Kind.Joined => "joined",
            Kind.Left => "left",
            Kind.Disconnected => "disconnected",
            Kind.Rejoined => "rejoined",
            Kind.Crashed => "Roblox crashed",
            Kind.Froze => "Roblox froze",
            Kind.Stuck => "stuck joining",
            Kind.RamRestart => "restarted (RAM)",
            Kind.InternetDown => "internet down",
            Kind.InternetBack => "internet back",
            Kind.ScheduledOpen => "opened (schedule)",
            Kind.ScheduledClose => "closed (schedule)",
            _ => kind.ToString()
        };
    }
}
