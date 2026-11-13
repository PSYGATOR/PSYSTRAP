using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

using CommunityToolkit.Mvvm.Input;

using Bloxstrap.Integrations;

namespace Bloxstrap.UI.ViewModels.Settings
{
    /// <summary>A game you played recently, as a card on the home page.</summary>
    public class RecentGameCard : NotifyPropertyChangedViewModel
    {
        private readonly HomeViewModel _owner;

        public long PlaceId { get; }
        public long UniverseId { get; }
        public string Name { get; }
        public DateTime LastPlayed { get; }
        public TimeSpan TotalPlayed { get; }

        /// <summary>The last public or private server you were in, if it's recent enough that it may still be running.</summary>
        public RecentServers.Entry? LastServer { get; }

        public string When => HomeViewModel.Ago(LastPlayed);

        public string Played => TotalPlayed.TotalMinutes >= 1 ? $"{HomeViewModel.Span(TotalPlayed)} played" : "";

        public ImageSource? Icon { get; private set; }

        public bool IsFavourite => App.Settings.Prop.Launching.Favourites.Any(f => f.PlaceId == PlaceId && f.PrivateServerLinkCode == "");

        public string FavouriteTip => IsFavourite ? "In your favourites" : "Add to favourites";

        public Wpf.Ui.Common.SymbolRegular FavouriteIcon => IsFavourite ? Wpf.Ui.Common.SymbolRegular.StarEmphasis24 : Wpf.Ui.Common.SymbolRegular.StarAdd24;

        public Visibility RejoinVisibility => LastServer is not null ? Visibility.Visible : Visibility.Collapsed;

        public ICommand PlayCommand { get; }
        public ICommand RejoinCommand { get; }
        public ICommand FavouriteCommand { get; }
        public ICommand ServersCommand { get; }

        public RecentGameCard(HomeViewModel owner, long placeId, long universeId, string name, DateTime lastPlayed, TimeSpan totalPlayed, RecentServers.Entry? lastServer)
        {
            _owner = owner;
            PlaceId = placeId;
            UniverseId = universeId;
            Name = name;
            LastPlayed = lastPlayed;
            TotalPlayed = totalPlayed;
            LastServer = lastServer;

            PlayCommand = new RelayCommand(() => _owner.Play(PlaceId, Name));
            RejoinCommand = new RelayCommand(() =>
            {
                if (LastServer is null)
                    return;

                RecentServers.Rejoin(LastServer);
                _owner.SetStatus($"Rejoining your last server in {Name.TrimEnd('.')}...");
            });
            FavouriteCommand = new RelayCommand(() =>
            {
                _owner.AddFavourite(PlaceId, Name);
                RefreshFavourite();
            });
            ServersCommand = new RelayCommand(() => Process.Start(Paths.Process, $"-servers {PlaceId}"));

            _ = LoadIconAsync();
        }

        internal void RefreshFavourite()
        {
            OnPropertyChanged(nameof(IsFavourite));
            OnPropertyChanged(nameof(FavouriteTip));
            OnPropertyChanged(nameof(FavouriteIcon));
        }

        private async Task LoadIconAsync()
        {
            Icon = RobloxImages.Load(await RobloxImages.GameIconAsync(PlaceId), 256);
            OnPropertyChanged(nameof(Icon));
        }
    }

    /// <summary>A favourite on the home page.</summary>
    public class FavouriteCard : NotifyPropertyChangedViewModel
    {
        public FavouriteGame Favourite { get; }

        public string Name => Favourite.Name;

        public string Detail { get; }

        public ImageSource? Icon { get; private set; }

        public ICommand PlayCommand { get; }

        public FavouriteCard(HomeViewModel owner, FavouriteGame favourite)
        {
            Favourite = favourite;

            var parts = new List<string>();

            if (favourite.PrivateServerLinkCode != "")
                parts.Add("private server");

            if (favourite.AccountUserId != 0)
                parts.Add("as " + (Accounts.AccountStore.Load().FirstOrDefault(a => a.UserId == favourite.AccountUserId)?.Username ?? "a saved account"));

            Detail = parts.Count > 0 ? String.Join(", ", parts) : "public game";

            PlayCommand = new RelayCommand(() =>
            {
                _ = Launching.LaunchAsync(favourite);
                owner.SetStatus($"Opening {favourite.Name.TrimEnd('.')}...");
            });

            _ = LoadIconAsync();
        }

        private async Task LoadIconAsync()
        {
            Icon = RobloxImages.Load(await RobloxImages.GameIconAsync(Favourite.PlaceId), 128);
            OnPropertyChanged(nameof(Icon));
        }
    }

    /// <summary>A Roblox window that's open right now.</summary>
    public class RunningCard : NotifyPropertyChangedViewModel
    {
        public int ProcessId { get; }

        public string Who { get; private set; }

        public string What { get; private set; } = "In the menu";

        public string Uptime { get; private set; } = "";

        public ImageSource? Avatar { get; private set; }

        public ImageSource? GameIcon { get; private set; }

        public Visibility GameIconVisibility => GameIcon is null ? Visibility.Collapsed : Visibility.Visible;

        public ICommand FocusCommand { get; }

        private long _userId = -1;
        private long _placeId = -1;

        public RunningCard(int processId)
        {
            ProcessId = processId;
            Who = $"Window {MultiInstance.GetSlot(processId)}";
            FocusCommand = new RelayCommand(() => RobloxWindows.Focus(ProcessId));
        }

        internal void Update(Accounts.LiveSessions.Session? session)
        {
            var started = MultiInstance.GetStartTime(ProcessId);
            Uptime = started is null ? "" : $"open {HomeViewModel.Span(DateTime.Now - started.Value)}";
            OnPropertyChanged(nameof(Uptime));

            long userId = session?.UserId ?? 0;
            long placeId = session?.PlaceId ?? 0;

            if (userId != _userId)
            {
                _userId = userId;

                if (userId != 0)
                    _ = LoadPersonAsync(userId);
            }

            if (placeId != _placeId)
            {
                _placeId = placeId;

                if (placeId == 0)
                {
                    What = "In the menu";
                    GameIcon = null;
                    OnPropertyChanged(nameof(What));
                    OnPropertyChanged(nameof(GameIcon));
                    OnPropertyChanged(nameof(GameIconVisibility));
                }
                else
                {
                    _ = LoadGameAsync(placeId);
                }
            }
        }

        private async Task LoadPersonAsync(long userId)
        {
            string? name = await RobloxImages.UsernameAsync(userId);
            var avatar = RobloxImages.Load(await RobloxImages.AvatarAsync(userId), 96);

            if (userId != _userId)
                return;

            if (name is not null)
            {
                Who = name;
                OnPropertyChanged(nameof(Who));
            }

            Avatar = avatar;
            OnPropertyChanged(nameof(Avatar));
        }

        private async Task LoadGameAsync(long placeId)
        {
            var iconTask = RobloxImages.GameIconAsync(placeId);
            string? name = await GameProfiles.GetGameNameAsync(placeId);
            var icon = RobloxImages.Load(await iconTask, 64);

            if (placeId != _placeId)
                return;

            What = name ?? $"Place {placeId}";
            GameIcon = icon;
            OnPropertyChanged(nameof(What));
            OnPropertyChanged(nameof(GameIcon));
            OnPropertyChanged(nameof(GameIconVisibility));
        }
    }

    /// <summary>A game in the "most played this week" list.</summary>
    public class MostPlayedRow
    {
        public string Name { get; init; } = "";
        public string Time { get; init; } = "";
        public double BarWidth { get; init; }
    }

    /// <summary>
    /// PSYstrap home page: play buttons, today's numbers, games you played recently (from the recent servers list and
    /// playtime), favourites, the windows open right now and what you played most this week.
    /// </summary>
    public class HomeViewModel : NotifyPropertyChangedViewModel
    {
        private const double BarMax = 220;

        private readonly DispatcherTimer _timer;
        private DateTime _recentStamp;

        public ObservableCollection<RecentGameCard> RecentGames { get; } = new();

        public ObservableCollection<FavouriteCard> Favourites { get; } = new();

        public ObservableCollection<RunningCard> Running { get; } = new();

        public List<MostPlayedRow> MostPlayed { get; private set; } = new();

        public string Greeting
        {
            get
            {
                int hour = DateTime.Now.Hour;
                return hour < 5 ? "Up late?" : hour < 12 ? "Good morning" : hour < 18 ? "Good afternoon" : "Good evening";
            }
        }

        public string Subline { get; private set; } = "";

        public string PlayedToday { get; private set; } = "0m";
        public string PlayedWeek { get; private set; } = "0m";
        public string WindowsOpen { get; private set; } = "0";
        public string Dropped { get; private set; } = "0";

        public string Status { get; private set; } = "";

        public Visibility RecentEmptyVisibility => RecentGames.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility FavouritesEmptyVisibility => Favourites.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility RunningEmptyVisibility => Running.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility MostPlayedEmptyVisibility => MostPlayed.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility AfkSetupVisibility => App.Settings.Prop.Launching.Favourites.Any(f => f.OpenAtStartup) ? Visibility.Visible : Visibility.Collapsed;

        public ICommand PlayRobloxCommand => new RelayCommand(() =>
        {
            Process.Start(Paths.Process, "-player");
            SetStatus("Opening Roblox...");
        });

        public ICommand InstancesCommand => new RelayCommand(() => Process.Start(Paths.Process, "-instances"));

        public ICommand ServerBrowserCommand => new RelayCommand(() => Process.Start(Paths.Process, "-servers"));

        public ICommand AfkSetupCommand => new RelayCommand(() =>
        {
            App.Settings.Save();
            Process.Start(Paths.Process, "-afksetup");
            SetStatus("Opening your AFK setup, about 12 seconds apart.");
        });

        public HomeViewModel()
        {
            LoadRecent();
            LoadFavourites();
            LoadMostPlayed();
            RefreshLive();

            _timer = new DispatcherTimer(TimeSpan.FromSeconds(4), DispatcherPriority.Background, (_, _) => Tick(), Application.Current.Dispatcher);
        }

        public void Start() => _timer.Start();

        public void Stop() => _timer.Stop();

        private void Tick()
        {
            try
            {
                RefreshLive();

                // new games joined since the page opened
                if (RecentStamp() != _recentStamp)
                {
                    LoadRecent();
                    LoadMostPlayed();
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("HomeViewModel::Tick", ex);
            }
        }

        internal void SetStatus(string status)
        {
            Status = status;
            OnPropertyChanged(nameof(Status));
        }

        internal void Play(long placeId, string name)
        {
            Process.Start(Paths.Process, $"-player \"roblox://experiences/start?placeId={placeId}\"");
            SetStatus($"Opening {name.TrimEnd('.')}...");
        }

        internal void AddFavourite(long placeId, string name)
        {
            var favourites = App.Settings.Prop.Launching.Favourites;

            if (favourites.Any(f => f.PlaceId == placeId && f.PrivateServerLinkCode == ""))
            {
                SetStatus($"{name} is already in your favourites.");
                return;
            }

            favourites.Add(new FavouriteGame { Name = name, PlaceId = placeId });
            App.Settings.Save();
            LoadFavourites();
            SetStatus($"Added {name} to your favourites.");
        }

        #region Loading
        private static DateTime RecentStamp()
        {
            try
            {
                string path = Path.Combine(Paths.Base, "RecentServers.json");
                return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        private void LoadRecent()
        {
            _recentStamp = RecentStamp();

            var servers = RecentServers.Load();
            var playtime = PlaytimeTracker.Load();
            var games = new List<RecentGameCard>();
            var seen = new HashSet<long>();

            // servers you were in, newest first, one card per game
            foreach (var group in servers.Where(s => s.PlaceId != 0)
                         .GroupBy(s => s.UniverseId != 0 ? s.UniverseId : s.PlaceId)
                         .OrderByDescending(g => g.Max(s => s.Joined)))
            {
                var last = group.OrderByDescending(s => s.Joined).First();
                var entry = playtime.GetValueOrDefault(last.UniverseId.ToString());
                var total = TimeSpan.FromSeconds(entry?.Days.Values.Sum() ?? 0);

                // a server from more than 12 hours ago has almost certainly shut down
                var rejoin = DateTime.Now - last.Joined < TimeSpan.FromHours(12) ? last : null;

                games.Add(new RecentGameCard(this, last.PlaceId, last.UniverseId, last.Name, last.Joined, total, rejoin));
                seen.Add(group.Key);
            }

            // games with playtime that dropped off the server list
            foreach (var (key, entry) in playtime)
            {
                if (!long.TryParse(key, out long universeId) || seen.Contains(universeId) || entry.PlaceId == 0 || entry.Days.Count == 0)
                    continue;

                // only the day is known; call it midday (or an hour ago, for today)
                var lastDay = entry.Days.Keys.Select(d => DateTime.TryParse(d, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : DateTime.MinValue).Max();
                var when = lastDay.Date == DateTime.Today ? DateTime.Now.AddHours(-1) : lastDay.AddHours(12);

                games.Add(new RecentGameCard(this, entry.PlaceId, universeId, entry.Name != "" ? entry.Name : $"Place {entry.PlaceId}",
                    when, TimeSpan.FromSeconds(entry.Days.Values.Sum()), null));
            }

            RecentGames.Clear();

            foreach (var game in games.OrderByDescending(g => g.LastPlayed).Take(10))
                RecentGames.Add(game);

            OnPropertyChanged(nameof(RecentEmptyVisibility));
        }

        private void LoadFavourites()
        {
            Favourites.Clear();

            foreach (var favourite in App.Settings.Prop.Launching.Favourites)
                Favourites.Add(new FavouriteCard(this, favourite));

            foreach (var game in RecentGames)
                game.RefreshFavourite();

            OnPropertyChanged(nameof(FavouritesEmptyVisibility));
            OnPropertyChanged(nameof(AfkSetupVisibility));
        }

        private void LoadMostPlayed()
        {
            var playtime = PlaytimeTracker.Load();
            var week = Enumerable.Range(0, 7).Select(i => DateTime.Today.AddDays(-i).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).ToHashSet();
            string today = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            var totals = playtime
                .Select(pair => (Entry: pair.Value, Week: pair.Value.Days.Where(d => week.Contains(d.Key)).Sum(d => d.Value)))
                .Where(x => x.Week >= 60)
                .OrderByDescending(x => x.Week)
                .ToList();

            long top = totals.Count > 0 ? totals[0].Week : 1;

            MostPlayed = totals.Take(5).Select(x => new MostPlayedRow
            {
                Name = x.Entry.Name != "" ? x.Entry.Name : $"Place {x.Entry.PlaceId}",
                Time = Span(TimeSpan.FromSeconds(x.Week)),
                BarWidth = Math.Max(6, BarMax * x.Week / top)
            }).ToList();

            long todaySeconds = playtime.Values.Sum(e => e.Days.GetValueOrDefault(today));
            long weekSeconds = totals.Sum(x => x.Week);

            PlayedToday = Span(TimeSpan.FromSeconds(todaySeconds));
            PlayedWeek = Span(TimeSpan.FromSeconds(weekSeconds));

            OnPropertyChanged(nameof(MostPlayed));
            OnPropertyChanged(nameof(MostPlayedEmptyVisibility));
            OnPropertyChanged(nameof(PlayedToday));
            OnPropertyChanged(nameof(PlayedWeek));
        }

        private void RefreshLive()
        {
            var clients = MultiInstance.GetRobloxProcesses();
            var sessions = Accounts.LiveSessions.List().ToDictionary(s => s.ProcessId);

            foreach (var gone in Running.Where(r => clients.All(c => c.Id != r.ProcessId)).ToList())
                Running.Remove(gone);

            foreach (var client in clients.Where(c => Running.All(r => r.ProcessId != c.Id)))
                Running.Add(new RunningCard(client.Id));

            foreach (var card in Running)
                card.Update(sessions.GetValueOrDefault(card.ProcessId));

            var afk = AfkLog.Summarise(DateTime.Today);

            WindowsOpen = clients.Count.ToString();
            Dropped = (afk.Disconnects + afk.Crashes).ToString();

            Subline = clients.Count switch
            {
                0 => "Nothing open right now. Pick something to play.",
                1 => sessions.Count == 1 ? "1 window open and in a game." : "1 window open.",
                _ => $"{clients.Count} windows open, {sessions.Count} in a game."
            };

            OnPropertyChanged(nameof(WindowsOpen));
            OnPropertyChanged(nameof(Dropped));
            OnPropertyChanged(nameof(Subline));
            OnPropertyChanged(nameof(RunningEmptyVisibility));
        }
        #endregion

        #region Text
        internal static string Span(TimeSpan time) =>
            time.TotalHours >= 1 ? $"{(int)time.TotalHours}h {time.Minutes}m" : $"{Math.Max(0, (int)time.TotalMinutes)}m";

        internal static string Ago(DateTime when)
        {
            var ago = DateTime.Now - when;

            if (ago < TimeSpan.FromMinutes(1))
                return "just now";
            if (ago < TimeSpan.FromHours(1))
                return $"{(int)ago.TotalMinutes}m ago";
            if (ago < TimeSpan.FromHours(24) && when.Date == DateTime.Today)
                return $"{(int)ago.TotalHours}h ago";
            if (when.Date == DateTime.Today.AddDays(-1))
                return "yesterday";
            if (ago < TimeSpan.FromDays(7))
                return $"{(int)Math.Ceiling(ago.TotalDays)} days ago";

            return when.ToString("d MMM", CultureInfo.CurrentCulture);
        }
        #endregion
    }
}
