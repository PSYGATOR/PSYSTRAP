namespace Bloxstrap.Integrations
{
    /// <summary>
    /// PSYstrap playtime: how long you play each game, per day, kept only on this PC in Playtime.json.
    /// Each Roblox window's watcher records its own sessions; writes are short and retried, so several windows can
    /// share the file.
    /// </summary>
    public sealed class PlaytimeTracker : IDisposable
    {
        private const string LOG_IDENT = "PlaytimeTracker";

        public class GameEntry
        {
            public string Name { get; set; } = "";

            public long PlaceId { get; set; }

            /// <summary>Seconds played, keyed by date (yyyy-MM-dd).</summary>
            public Dictionary<string, long> Days { get; set; } = new();
        }

        public static string FilePath => Path.Combine(Paths.Base, "Playtime.json");

        private readonly ActivityWatcher _activity;
        private readonly System.Threading.Timer _timer;
        private readonly object _lock = new();

        private long _universeId;
        private DateTime _countedUntil;
        private bool _inGame;

        public PlaytimeTracker(ActivityWatcher activity)
        {
            _activity = activity;

            activity.OnGameJoin += (_, _) => StartSession();
            activity.OnGameLeave += (_, _) => Flush(endSession: true);

            // save progress every minute, so a crash loses at most a minute
            _timer = new System.Threading.Timer(_ => Flush(endSession: false), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }

        private void StartSession()
        {
            lock (_lock)
            {
                Flush(endSession: true);

                _universeId = _activity.Data.UniverseId;
                _countedUntil = DateTime.Now;
                _inGame = _universeId != 0;
            }

            if (_inGame)
                _ = FillInNameAsync(_universeId, _activity.Data.PlaceId);
        }

        /// <summary>Adds the time since the last save to today's total.</summary>
        private void Flush(bool endSession)
        {
            lock (_lock)
            {
                if (!_inGame)
                    return;

                var now = DateTime.Now;
                long seconds = (long)(now - _countedUntil).TotalSeconds;

                if (seconds > 0)
                {
                    Update(entries =>
                    {
                        var entry = GetOrAdd(entries, _universeId);
                        entry.PlaceId = entry.PlaceId == 0 ? _activity.Data.PlaceId : entry.PlaceId;
                        string day = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                        entry.Days[day] = entry.Days.GetValueOrDefault(day) + seconds;
                    });

                    _countedUntil = now;
                }

                if (endSession)
                    _inGame = false;
            }
        }

        /// <summary>Time played today in the game this window is in, including the part not saved yet.</summary>
        public TimeSpan? TodayInCurrentGame()
        {
            lock (_lock)
            {
                if (!_inGame)
                    return null;

                string day = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                long saved = Load().TryGetValue(_universeId.ToString(), out var entry) ? entry.Days.GetValueOrDefault(day) : 0;
                return TimeSpan.FromSeconds(saved) + (DateTime.Now - _countedUntil);
            }
        }

        private async Task FillInNameAsync(long universeId, long placeId)
        {
            if (Load().TryGetValue(universeId.ToString(), out var existing) && !String.IsNullOrEmpty(existing.Name))
                return;

            string? name = await GameProfiles.GetGameNameAsync(placeId);

            if (name is not null)
                Update(entries => GetOrAdd(entries, universeId).Name = name);
        }

        private static GameEntry GetOrAdd(Dictionary<string, GameEntry> entries, long universeId)
        {
            string key = universeId.ToString();

            if (!entries.TryGetValue(key, out var entry))
                entries[key] = entry = new GameEntry();

            return entry;
        }

        #region File
        public static Dictionary<string, GameEntry> Load()
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    if (!File.Exists(FilePath))
                        return new();

                    return JsonSerializer.Deserialize<Dictionary<string, GameEntry>>(File.ReadAllText(FilePath)) ?? new();
                }
                catch (IOException)
                {
                    Thread.Sleep(50);
                }
                catch (JsonException)
                {
                    return new();
                }
            }

            return new();
        }

        /// <summary>Read-modify-write under an exclusive file lock, so two windows can't overwrite each other.</summary>
        private static void Update(Action<Dictionary<string, GameEntry>> change)
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    using var stream = new FileStream(FilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                    using var reader = new StreamReader(stream, leaveOpen: true);
                    string json = reader.ReadToEnd();

                    var entries = String.IsNullOrWhiteSpace(json)
                        ? new Dictionary<string, GameEntry>()
                        : JsonSerializer.Deserialize<Dictionary<string, GameEntry>>(json) ?? new();

                    change(entries);

                    stream.SetLength(0);
                    using var writer = new StreamWriter(stream);
                    writer.Write(JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
                    return;
                }
                catch (IOException)
                {
                    Thread.Sleep(50 + attempt * 20);
                }
                catch (Exception ex)
                {
                    App.Logger.WriteException(LOG_IDENT, ex);
                    return;
                }
            }
        }
        #endregion

        public void Dispose()
        {
            _timer.Dispose();
            Flush(endSession: true);
        }
    }
}
