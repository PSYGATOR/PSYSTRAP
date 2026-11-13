using System.Collections.ObjectModel;
using System.Windows.Input;

using CommunityToolkit.Mvvm.Input;

using Bloxstrap.Accounts;
using Bloxstrap.Integrations;
using Bloxstrap.Models.Persistable;

namespace Bloxstrap.UI.ViewModels.Settings
{
    /// <summary>PSYstrap: Settings > AFK &amp; launching. Saved with the Save button (which also updates Windows).</summary>
    public class LaunchingViewModel : NotifyPropertyChangedViewModel
    {
        private static LaunchingSettings Launch => App.Settings.Prop.Launching;
        private static WebhookSettings Hook => App.Settings.Prop.Gameplay.Webhook;

        public record AccountChoice(long UserId, string Label);

        public ObservableCollection<AccountChoice> Accounts { get; } = new();

        public LaunchingViewModel()
        {
            Accounts.Add(new(0, "Whoever is signed in"));
            foreach (var account in AccountStore.Load())
                Accounts.Add(new(account.UserId, account.Label));

            foreach (var favourite in Launch.Favourites)
                Favourites.Add(favourite);

            foreach (var entry in Launch.Schedule)
                Schedule.Add(new ScheduleRow(entry));

            LoadRecent();
            LoadHistory();
        }

        #region Favourites
        public ObservableCollection<FavouriteGame> Favourites { get; } = new();

        public string NewFavourite { get; set; } = "";

        private string _favouriteStatus = "";
        public string FavouriteStatus { get => _favouriteStatus; private set { _favouriteStatus = value; OnPropertyChanged(nameof(FavouriteStatus)); } }

        public ICommand AddFavouriteCommand => new AsyncRelayCommand(() => AddFavouriteAsync(NewFavourite));

        public ICommand AddCurrentGameCommand => new AsyncRelayCommand(async () =>
        {
            var session = LiveSessions.List().OrderByDescending(s => s.Joined).FirstOrDefault(s => s.PlaceId != 0);

            if (session is null)
                FavouriteStatus = "You're not in a game right now.";
            else
                await AddFavouriteAsync(session.PlaceId.ToString(), session.UserId);
        });

        public ICommand PlayFavouriteCommand => new RelayCommand<FavouriteGame>(f => { if (f is not null) _ = Launching.LaunchAsync(f); });

        public ICommand ShortcutCommand => new AsyncRelayCommand<FavouriteGame>(async f =>
        {
            if (f is null)
                return;

            // the shortcut points at the favourite by ID, so it has to be saved for the shortcut to work
            App.Settings.Save();
            FavouriteStatus = await Launching.CreateShortcutAsync(f) is string path
                ? $"Made \"{Path.GetFileName(path)}\" on your desktop."
                : "Couldn't make the shortcut (check the log).";
        });

        public ICommand ServersCommand => new RelayCommand<FavouriteGame>(f => { if (f is not null) Process.Start(Paths.Process, $"-servers {f.PlaceId}"); });

        public ICommand OpenServerBrowserCommand => new RelayCommand(() => Process.Start(Paths.Process, "-servers"));

        public ICommand DeleteFavouriteCommand => new RelayCommand<FavouriteGame>(f =>
        {
            if (f is null)
                return;

            Favourites.Remove(f);
            Launch.Favourites.Remove(f);

            // scheduled launches of it go too
            foreach (var row in Schedule.Where(r => r.Entry.FavouriteId == f.Id).ToList())
            {
                Schedule.Remove(row);
                Launch.Schedule.Remove(row.Entry);
            }

            OnPropertyChanged(nameof(FavouriteChoices));
        });

        private async Task AddFavouriteAsync(string text, long userId = 0)
        {
            FavouriteStatus = "Looking it up...";
            var favourite = await Launching.FavouriteFromLinkAsync(text);

            if (favourite is null)
            {
                FavouriteStatus = SharePatternLooksLike(text)
                    ? "Share links need a saved account to read. Add one in the Instances window, or paste the game link instead."
                    : "Paste a game link, a private server link or a place ID.";
                return;
            }

            if (AccountStore.Load().Any(a => a.UserId == userId))
                favourite.AccountUserId = userId;

            Favourites.Add(favourite);
            Launch.Favourites.Add(favourite);
            OnPropertyChanged(nameof(FavouriteChoices));

            NewFavourite = "";
            OnPropertyChanged(nameof(NewFavourite));
            FavouriteStatus = "";
        }

        private static bool SharePatternLooksLike(string text) => text.Contains("roblox.com/share", StringComparison.OrdinalIgnoreCase);

        public bool StartWithWindows
        {
            get => Launch.StartWithWindows;
            set => Launch.StartWithWindows = value;
        }
        #endregion

        #region Schedule
        public class ScheduleRow : NotifyPropertyChangedViewModel
        {
            public ScheduledLaunch Entry { get; }

            public ScheduleRow(ScheduledLaunch entry) => Entry = entry;

            public string FavouriteId { get => Entry.FavouriteId; set => Entry.FavouriteId = value; }

            public string Time
            {
                get => Entry.Time;
                set
                {
                    Entry.Time = value;
                    OnPropertyChanged(nameof(TimeValid));
                }
            }

            public bool TimeValid => TimeSpan.TryParse(Entry.Time, out var t) && t < TimeSpan.FromDays(1);

            public int StopAfterMinutes { get => Entry.StopAfterMinutes; set => Entry.StopAfterMinutes = value; }

            public bool Enabled { get => Entry.Enabled; set => Entry.Enabled = value; }
        }

        public ObservableCollection<ScheduleRow> Schedule { get; } = new();

        public IEnumerable<FavouriteGame> FavouriteChoices => Favourites.ToList();

        public IReadOnlyList<KeyValuePair<int, string>> StopChoices { get; } = new List<KeyValuePair<int, string>>
        {
            new(0, "Leave it open"),
            new(30, "Close after 30m"),
            new(60, "Close after 1h"),
            new(120, "Close after 2h"),
            new(240, "Close after 4h"),
            new(480, "Close after 8h"),
            new(720, "Close after 12h")
        };

        public string ScheduleStatus => Favourites.Count == 0 ? "Add a favourite first, then schedule it here." : "Times are your PC's time. Save to put them in Windows.";

        public ICommand AddScheduleCommand => new RelayCommand(() =>
        {
            if (Favourites.Count == 0)
                return;

            var entry = new ScheduledLaunch { FavouriteId = Favourites[0].Id, Time = DateTime.Now.AddHours(1).ToString("HH:00") };
            Launch.Schedule.Add(entry);
            Schedule.Add(new ScheduleRow(entry));
        });

        public ICommand DeleteScheduleCommand => new RelayCommand<ScheduleRow>(row =>
        {
            if (row is null)
                return;

            Schedule.Remove(row);
            Launch.Schedule.Remove(row.Entry);
        });
        #endregion

        #region Recent servers
        public class RecentRow
        {
            public RecentServers.Entry Entry { get; init; } = null!;
            public string Name => Entry.Name;
            public string Detail { get; init; } = "";
        }

        public ObservableCollection<RecentRow> Recent { get; } = new();

        public ICommand RefreshRecentCommand => new RelayCommand(LoadRecent);

        public ICommand RejoinRecentCommand => new RelayCommand<RecentRow>(row => { if (row is not null) RecentServers.Rejoin(row.Entry); });

        private void LoadRecent()
        {
            Recent.Clear();
            var accounts = AccountStore.Load();

            foreach (var entry in RecentServers.Load().Take(15))
            {
                string who = accounts.FirstOrDefault(a => a.UserId == entry.UserId)?.Username ?? "";
                string ago = FormatAgo(DateTime.Now - entry.Joined);

                Recent.Add(new RecentRow
                {
                    Entry = entry,
                    Detail = String.Join("  ·  ", new[] { ago, entry.IsPrivate ? "private server" : null, who.Length > 0 ? who : null }.Where(s => s is not null))
                });
            }

            OnPropertyChanged(nameof(RecentEmptyVisibility));
        }

        public System.Windows.Visibility RecentEmptyVisibility => Recent.Count == 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

        private static string FormatAgo(TimeSpan ago) =>
            ago.TotalMinutes < 1 ? "just now"
            : ago.TotalHours < 1 ? $"{(int)ago.TotalMinutes}m ago"
            : ago.TotalDays < 1 ? $"{(int)ago.TotalHours}h ago"
            : $"{(int)ago.TotalDays}d ago";
        #endregion

        #region AFK setup
        private string _setupStatus = "";
        public string SetupStatus { get => _setupStatus; private set { _setupStatus = value; OnPropertyChanged(nameof(SetupStatus)); } }

        public ICommand OpenAfkSetupCommand => new RelayCommand(() =>
        {
            if (!Favourites.Any(f => f.OpenAtStartup))
            {
                SetupStatus = "Tick \"Part of my AFK setup\" on some favourites first.";
                return;
            }

            // saved first, so the setup that opens is the one on screen
            App.Settings.Save();
            Process.Start(Paths.Process, "-afksetup");
            SetupStatus = "Opening them, about 12 seconds apart. The windows get tiled once they're all open.";
        });

        public ICommand AfkSetupShortcutCommand => new RelayCommand(() =>
        {
            SetupStatus = Launching.CreateAfkSetupShortcut() is string path
                ? $"Made \"{Path.GetFileName(path)}\" on your desktop."
                : "Couldn't make the shortcut (check the log).";
        });
        #endregion

        #region Keeping windows alive
        private static Models.Persistable.AfkSettings Afk => App.Settings.Prop.Afk;

        public bool WaitForInternet { get => Afk.WaitForInternet; set => Afk.WaitForInternet = value; }
        public bool FallbackServer { get => Afk.FallbackServer; set => Afk.FallbackServer = value; }
        public bool RetryLater { get => Afk.RetryLater; set => Afk.RetryLater = value; }

        public IReadOnlyList<KeyValuePair<int, string>> StuckChoices { get; } = new List<KeyValuePair<int, string>>
        {
            new(0, "Off"), new(2, "2 minutes"), new(3, "3 minutes"), new(5, "5 minutes"), new(10, "10 minutes")
        };

        public int StuckJoinMinutes { get => Afk.StuckJoinMinutes; set => Afk.StuckJoinMinutes = value; }

        public IReadOnlyList<KeyValuePair<int, string>> RamChoices { get; } = new List<KeyValuePair<int, string>>
        {
            new(0, "Off"), new(1536, "Over 1.5 GB"), new(2048, "Over 2 GB"), new(2560, "Over 2.5 GB"), new(3072, "Over 3 GB"), new(4096, "Over 4 GB")
        };

        public int RamLimitMB { get => Afk.RamLimitMB; set => Afk.RamLimitMB = value; }
        #endregion

        #region While you're away
        public bool KeepAwake { get => Afk.KeepAwake; set => Afk.KeepAwake = value; }

        public IReadOnlyList<KeyValuePair<int, string>> AwayChoices { get; } = new List<KeyValuePair<int, string>>
        {
            new(0, "Off"), new(5, "After 5 minutes"), new(10, "After 10 minutes"), new(15, "After 15 minutes"), new(30, "After 30 minutes"), new(60, "After 1 hour")
        };

        public int AwayMinimizeMinutes { get => Afk.AwayMinimizeMinutes; set => Afk.AwayMinimizeMinutes = value; }

        public ICommand ScreenOffCommand => new RelayCommand(() => Task.Run(async () =>
        {
            // a moment to let go of the mouse, or the click itself wakes it up again
            await Task.Delay(1000);
            Integrations.RobloxWindows.ScreenOff();
        }));
        #endregion

        #region History
        public record HistoryRow(string When, string What, string Detail);

        public ObservableCollection<HistoryRow> History { get; } = new();

        private string _historySummary = "";
        public string HistorySummary { get => _historySummary; private set { _historySummary = value; OnPropertyChanged(nameof(HistorySummary)); } }

        public ICommand RefreshHistoryCommand => new RelayCommand(LoadHistory);

        private void LoadHistory()
        {
            History.Clear();
            var accounts = AccountStore.Load();

            foreach (var entry in AfkLog.Load(40))
            {
                string who = accounts.FirstOrDefault(a => a.UserId == entry.UserId)?.Username ?? "";
                string when = entry.At.Date == DateTime.Today ? entry.At.ToString("HH:mm") : entry.At.ToString("ddd HH:mm");
                string what = (who.Length > 0 ? who + " " : "") + AfkLog.Describe(entry.Kind) + (entry.Game.Length > 0 ? $" ({entry.Game})" : "");
                History.Add(new HistoryRow(when, what, entry.Detail));
            }

            var today = AfkLog.Summarise(DateTime.Today);
            string uptime = String.Join(", ", today.InGameByAccount
                .Where(pair => pair.Value.TotalMinutes >= 1)
                .Select(pair => $"{accounts.FirstOrDefault(a => a.UserId == pair.Key)?.Username ?? (pair.Key == 0 ? "?" : pair.Key.ToString())} {FormatSpan(pair.Value)}"));

            HistorySummary = $"Today: {today.Disconnects} disconnects, {today.Rejoins} rejoins, {today.Crashes} crashes or freezes."
                + (uptime.Length > 0 ? $" In game: {uptime}." : "");

            OnPropertyChanged(nameof(HistoryEmptyVisibility));
        }

        public System.Windows.Visibility HistoryEmptyVisibility => History.Count == 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

        private static string FormatSpan(TimeSpan time) =>
            time.TotalHours >= 1 ? $"{(int)time.TotalHours}h {time.Minutes}m" : $"{time.Minutes}m";
        #endregion

        #region Phone remote
        private static Models.Persistable.RemoteSettings Remote => App.Settings.Prop.Afk.Remote;

        public bool RemoteEnabled
        {
            get => Remote.Enabled;
            set
            {
                Remote.Enabled = value;

                if (value && Remote.Pin.Length < 6)
                    Remote.Pin = PhoneRemote.NewPin();

                OnPropertyChanged(nameof(RemotePin));
                OnPropertyChanged(nameof(RemoteAddresses));
            }
        }

        public string RemotePort
        {
            get => Remote.Port.ToString();
            set
            {
                if (int.TryParse(value, out int port) && port is >= 1024 and <= 65535)
                    Remote.Port = port;

                OnPropertyChanged(nameof(RemoteAddresses));
            }
        }

        public string RemotePin => Remote.Pin.Length >= 6 ? Remote.Pin : "(turn it on to get one)";

        public string RemoteAddresses
        {
            get
            {
                var addresses = PhoneRemote.Addresses(Remote.Port);
                return addresses.Count == 0 ? "No network found." : "On your phone (same Wi-Fi): " + String.Join("  or  ", addresses);
            }
        }

        public ICommand NewPinCommand => new RelayCommand(() =>
        {
            Remote.Pin = PhoneRemote.NewPin();
            OnPropertyChanged(nameof(RemotePin));
        });
        #endregion

        #region Phone alerts (webhook)
        public bool AlertScreenshots { get => Afk.AlertScreenshots; set => Afk.AlertScreenshots = value; }

        public IReadOnlyList<KeyValuePair<int, string>> ReportChoices { get; } = new List<KeyValuePair<int, string>>
        {
            new(0, "Off"), new(30, "Every 30 minutes"), new(60, "Every hour"), new(120, "Every 2 hours"), new(240, "Every 4 hours"), new(480, "Every 8 hours")
        };

        public int StatusReportMinutes { get => Afk.StatusReportMinutes; set => Afk.StatusReportMinutes = value; }

        public string WebhookUrl
        {
            get => Hook.Url;
            set { Hook.Url = value?.Trim() ?? ""; OnPropertyChanged(nameof(WebhookStatus)); }
        }

        private string? _webhookTestResult;

        public string WebhookStatus => _webhookTestResult
            ?? (String.IsNullOrWhiteSpace(Hook.Url) ? "Off. Paste a Discord webhook link to turn it on."
                : Webhook.IsValidUrl(Hook.Url) ? "Looks good. Press Test to check." : "That isn't a Discord webhook link.");

        public ICommand TestWebhookCommand => new AsyncRelayCommand(async () =>
        {
            _webhookTestResult = "Sending...";
            OnPropertyChanged(nameof(WebhookStatus));

            bool ok = Webhook.IsValidUrl(Hook.Url) && await Webhook.SendAsync(Hook.Url.Trim(), "PSYstrap test", "If you can read this, phone alerts work.");

            _webhookTestResult = ok ? "Sent. Check Discord." : "Didn't work. Check the link (and that the webhook wasn't deleted).";
            OnPropertyChanged(nameof(WebhookStatus));
            _webhookTestResult = null;
        });

        public bool OnDisconnect { get => Hook.OnDisconnect; set => Hook.OnDisconnect = value; }
        public bool OnRejoin { get => Hook.OnRejoin; set => Hook.OnRejoin = value; }
        public bool OnCrash { get => Hook.OnCrash; set => Hook.OnCrash = value; }
        public bool OnSmartMacro { get => Hook.OnSmartMacro; set => Hook.OnSmartMacro = value; }
        public bool OnFriendJoin { get => Hook.OnFriendJoin; set => Hook.OnFriendJoin = value; }
        public bool OnSchedule { get => Hook.OnSchedule; set => Hook.OnSchedule = value; }
        #endregion
    }
}
