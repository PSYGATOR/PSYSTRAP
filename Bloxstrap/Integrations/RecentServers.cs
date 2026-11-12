namespace Bloxstrap.Integrations
{
    /// <summary>
    /// PSYstrap: the last 30 servers you were in (any window, any account), kept in RecentServers.json so you can jump
    /// back into one from Settings > AFK &amp; launching.
    /// </summary>
    public static class RecentServers
    {
        private const int Keep = 30;

        public class Entry
        {
            public long PlaceId { get; set; }
            public long UniverseId { get; set; }
            public string Name { get; set; } = "";
            public string ServerId { get; set; } = "";
            public string AccessCode { get; set; } = "";
            public bool IsPrivate { get; set; }
            public long UserId { get; set; }
            public DateTime Joined { get; set; }
        }

        private static string FilePath => Path.Combine(Paths.Base, "RecentServers.json");

        public static List<Entry> Load()
        {
            try
            {
                return File.Exists(FilePath) ? JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(FilePath)) ?? new() : new();
            }
            catch
            {
                return new();
            }
        }

        /// <summary>Hooks a watcher's activity so every game join is remembered.</summary>
        public static void Track(ActivityWatcher activity)
        {
            activity.OnGameJoin += async (_, _) =>
            {
                var data = activity.Data;

                if (data.PlaceId == 0 || data.ServerType == ServerType.Reserved)
                    return; // reserved servers (teleport-only) can't be rejoined

                string name = data.UniverseDetails?.Data.Name
                    ?? (await GameProfiles.GetUniverseNamesAsync(new[] { data.UniverseId })).GetValueOrDefault(data.UniverseId)
                    ?? $"Place {data.PlaceId}";

                Add(new Entry
                {
                    PlaceId = data.PlaceId,
                    UniverseId = data.UniverseId,
                    Name = name,
                    ServerId = data.JobId,
                    AccessCode = data.AccessCode,
                    IsPrivate = data.ServerType == ServerType.Private,
                    UserId = data.UserId,
                    Joined = DateTime.Now
                });
            };
        }

        private static void Add(Entry entry)
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    // read-modify-write under an exclusive handle, since several watchers can join at once
                    using var stream = new FileStream(FilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                    List<Entry> list;

                    try { list = stream.Length > 0 ? JsonSerializer.Deserialize<List<Entry>>(stream) ?? new() : new(); }
                    catch (JsonException) { list = new(); }

                    list.RemoveAll(e => e.ServerId == entry.ServerId && e.UserId == entry.UserId);
                    list.Insert(0, entry);

                    stream.SetLength(0);
                    JsonSerializer.Serialize(stream, list.Take(Keep).ToList());
                    return;
                }
                catch (IOException)
                {
                    Thread.Sleep(200);
                }
            }
        }

        /// <summary>Opens Roblox in that server again (as the same saved account if it was one).</summary>
        public static void Rejoin(Entry entry)
        {
            Task.Run(async () =>
            {
                string? link = null;
                var account = Accounts.AccountStore.Load().FirstOrDefault(a => a.UserId == entry.UserId);

                if (account is not null && Accounts.AccountStore.GetSession(account) is string session
                    && await Accounts.RobloxAuth.GetLaunchTicketAsync(session) is string ticket)
                {
                    link = Accounts.RobloxAuth.BuildLaunchLink(ticket, entry.PlaceId,
                        entry.IsPrivate ? null : entry.ServerId, entry.IsPrivate ? entry.AccessCode : null);
                }

                link ??= entry.IsPrivate
                    ? $"roblox://experiences/start?placeId={entry.PlaceId}&accessCode={entry.AccessCode}"
                    : RobloxServers.GetJoinLink(entry.PlaceId, entry.ServerId);

                Process.Start(Paths.Process, $"-player \"{link}\"");
            });
        }
    }
}
