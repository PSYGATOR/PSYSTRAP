using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

using CommunityToolkit.Mvvm.Input;

using Bloxstrap.Accounts;
using Bloxstrap.Integrations;
using Bloxstrap.Models.Persistable;

namespace Bloxstrap.UI.ViewModels.Settings
{
    /// <summary>PSYstrap: Settings > Gameplay. Changes go into App.Settings and are saved with the Save button.</summary>
    public class GameplayViewModel : NotifyPropertyChangedViewModel
    {
        private static GameplaySettings Gameplay => App.Settings.Prop.Gameplay;

        public GameplayViewModel()
        {
            foreach (var remap in Gameplay.Remaps)
                Remaps.Add(remap);

            foreach (var (placeId, profile) in Gameplay.GameProfiles)
                Profiles.Add(new ProfileRow(placeId, profile));

            foreach (long universeId in Gameplay.DiscordHiddenGames)
                HiddenGames.Add(new HiddenGameRow(universeId) { Name = $"Game {universeId}" });

            FriendAccounts.Add(new(0, "First saved account"));

            foreach (var account in AccountStore.Load())
                FriendAccounts.Add(new(account.UserId, account.Label));

            LoadPlaytime();
            _ = LoadHiddenGameNames();
        }

        #region Staying in game
        public bool AutoRejoin
        {
            get => Gameplay.AutoRejoin;
            set => Gameplay.AutoRejoin = value;
        }

        public bool AntiAfk
        {
            get => Gameplay.AntiAfk;
            set => Gameplay.AntiAfk = value;
        }

        public IReadOnlyList<int> AntiAfkChoices { get; } = new[] { 5, 10, 15, 18 };

        public int AntiAfkMinutes
        {
            get => Gameplay.AntiAfkMinutes;
            set => Gameplay.AntiAfkMinutes = value;
        }

        public bool MuteBackgroundWindows
        {
            get => Gameplay.MuteBackgroundWindows;
            set => Gameplay.MuteBackgroundWindows = value;
        }

        public bool CrashWatchdog { get => Gameplay.CrashWatchdog; set => Gameplay.CrashWatchdog = value; }

        public bool DisableCrashHandler { get => Gameplay.DisableCrashHandler; set => Gameplay.DisableCrashHandler = value; }

        public bool MinimizeToTray { get => Gameplay.MinimizeToTray; set => Gameplay.MinimizeToTray = value; }

        public bool SessionRecap { get => Gameplay.SessionRecap; set => Gameplay.SessionRecap = value; }

        public bool GameIconOnWindow { get => Gameplay.GameIconOnWindow; set => Gameplay.GameIconOnWindow = value; }

        public bool ClipboardJoin { get => Gameplay.ClipboardJoin; set => Gameplay.ClipboardJoin = value; }

        public bool OrganiseScreenshots { get => Gameplay.OrganiseScreenshots; set => Gameplay.OrganiseScreenshots = value; }

        public bool BoosterEnabled { get => Gameplay.Booster.Enabled; set => Gameplay.Booster.Enabled = value; }

        public bool BoosterHighPerformance { get => Gameplay.Booster.HighPerformancePower; set => Gameplay.Booster.HighPerformancePower = value; }

        /// <summary>Comma separated process names, like "chrome, Spotify".</summary>
        public string BoosterApps
        {
            get => String.Join(", ", Gameplay.Booster.CloseApps);
            set => Gameplay.Booster.CloseApps = (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }
        #endregion

        #region Remaps
        public ObservableCollection<KeyRemap> Remaps { get; } = new();

        /// <summary>Suggestions for the two boxes; anything Parse understands can be typed too.</summary>
        public IReadOnlyList<string> InputNames { get; } = KeyRemapper.MouseButtonNames.Concat(new[]
        {
            "Space", "LeftShift", "LeftCtrl", "LeftAlt", "Tab", "CapsLock", "Enter", "Escape", "Back",
            "Q", "E", "R", "F", "G", "T", "Z", "X", "C", "V", "W", "A", "S", "D",
            "1", "2", "3", "4", "5", "6", "7", "8", "9", "0",
            "F1", "F2", "F3", "F4", "F5", "F9", "F10", "F11", "F12",
            "Up", "Down", "Left", "Right"
        }).ToList();

        public string NewRemapFrom { get; set; } = "";

        public string NewRemapTo { get; set; } = "";

        public string RemapStatus { get; private set; } = "";

        public ICommand AddRemapCommand => new RelayCommand(AddRemap);

        public ICommand DeleteRemapCommand => new RelayCommand<KeyRemap>(remap =>
        {
            if (remap is null)
                return;

            Remaps.Remove(remap);
            Gameplay.Remaps.Remove(remap);
        });

        private void AddRemap()
        {
            string from = NewRemapFrom.Trim(), to = NewRemapTo.Trim();

            if (KeyRemapper.Parse(from) is not KeyRemapper.InputId fromId)
                RemapStatus = $"\"{from}\" isn't a key or mouse button I know.";
            else if (KeyRemapper.Parse(to) is not KeyRemapper.InputId toId)
                RemapStatus = $"\"{to}\" isn't a key or mouse button I know.";
            else if (fromId == toId)
                RemapStatus = "That's the same thing twice.";
            else
            {
                // one remap per input: a newer one replaces the old
                foreach (var old in Remaps.Where(r => KeyRemapper.Parse(r.From) == fromId).ToList())
                {
                    Remaps.Remove(old);
                    Gameplay.Remaps.Remove(old);
                }

                var remap = new KeyRemap { From = from, To = to };
                Remaps.Add(remap);
                Gameplay.Remaps.Add(remap);

                RemapStatus = "";
                NewRemapFrom = NewRemapTo = "";
                OnPropertyChanged(nameof(NewRemapFrom));
                OnPropertyChanged(nameof(NewRemapTo));
            }

            OnPropertyChanged(nameof(RemapStatus));
        }
        #endregion

        #region Game profiles
        public class ProfileRow : NotifyPropertyChangedViewModel
        {
            public long PlaceId { get; }

            public GameProfile Profile { get; }

            // the number "Custom" uses for this game until one is typed
            private int _custom;

            public ProfileRow(long placeId, GameProfile profile)
            {
                PlaceId = placeId;
                Profile = profile;
                _custom = RobloxUserSettings.ChoiceFor(profile.FramerateCap) == RobloxUserSettings.CustomFramerate
                    ? profile.FramerateCap
                    : App.Settings.Prop.CustomFramerateCap;
            }

            public string Title => String.IsNullOrEmpty(Profile.Name) ? $"Place {PlaceId}" : Profile.Name;

            /// <summary>The list entry: "Normal setting", a preset, or "Custom" for any other number.</summary>
            public int FramerateCap
            {
                get => RobloxUserSettings.ChoiceFor(Profile.FramerateCap);
                set
                {
                    Profile.FramerateCap = value == RobloxUserSettings.CustomFramerate ? _custom : value;
                    OnPropertyChanged(nameof(CustomFramerateVisibility));
                }
            }

            public Visibility CustomFramerateVisibility =>
                FramerateCap == RobloxUserSettings.CustomFramerate ? Visibility.Visible : Visibility.Collapsed;

            public string CustomFramerate
            {
                get => _custom.ToString(CultureInfo.InvariantCulture);
                set
                {
                    if (RobloxUserSettings.ParseCustomFramerate(value) is int fps)
                    {
                        _custom = fps;

                        if (FramerateCap == RobloxUserSettings.CustomFramerate)
                            Profile.FramerateCap = fps;
                    }

                    OnPropertyChanged(nameof(CustomFramerate));
                }
            }

            public string Potato
            {
                get => Profile.PotatoMode switch { true => "On", false => "Off", null => "Normal setting" };
                set => Profile.PotatoMode = value switch { "On" => true, "Off" => false, _ => null };
            }

            public string ModProfile
            {
                get => String.IsNullOrEmpty(Profile.ModProfile) ? NormalMods : Profile.ModProfile;
                set => Profile.ModProfile = value == NormalMods ? "" : value ?? "";
            }
        }

        public const string NormalMods = "Normal mods";

        /// <summary>"Normal mods" plus the mod profiles saved on the Mods page.</summary>
        public IReadOnlyList<string> ModProfileChoices { get; } = new[] { NormalMods }.Concat(ModProfiles.List()).ToList();

        public ObservableCollection<ProfileRow> Profiles { get; } = new();

        public IReadOnlyList<KeyValuePair<int, string>> ProfileFramerateCaps { get; } =
            new[] { new KeyValuePair<int, string>(0, "Normal setting") }.Concat(RobloxUserSettings.FramerateCapChoices.Skip(1)).ToList();

        public IReadOnlyList<string> PotatoChoices { get; } = new[] { "Normal setting", "On", "Off" };

        public string NewProfilePlace { get; set; } = "";

        public string ProfileStatus { get; private set; } = "";

        public ICommand AddProfileCommand => new AsyncRelayCommand(() => AddProfile(NewProfilePlace));

        public ICommand AddCurrentGameProfileCommand => new AsyncRelayCommand(async () =>
        {
            if (CurrentPlaceId() is long placeId)
                await AddProfile(placeId.ToString());
            else
                SetProfileStatus("You're not in a game right now.");
        });

        public ICommand DeleteProfileCommand => new RelayCommand<ProfileRow>(row =>
        {
            if (row is null)
                return;

            Profiles.Remove(row);
            Gameplay.GameProfiles.Remove(row.PlaceId);
        });

        private async Task AddProfile(string text)
        {
            // a game link works too
            long? placeId = GameProfiles.GetPlaceId(text) ?? (long.TryParse(System.Text.RegularExpressions.Regex.Match(text, @"\d+").Value, out long id) ? id : null);

            if (placeId is not long place || place <= 0)
            {
                SetProfileStatus("Paste a place ID or a roblox.com/games/... link.");
                return;
            }

            if (Gameplay.GameProfiles.ContainsKey(place))
            {
                SetProfileStatus("That game already has a profile.");
                return;
            }

            SetProfileStatus("Looking up the game...");

            string name = await GameProfiles.GetGameNameAsync(place) ?? "";
            var profile = new GameProfile { Name = name };

            Gameplay.GameProfiles[place] = profile;
            Profiles.Add(new ProfileRow(place, profile));

            NewProfilePlace = "";
            OnPropertyChanged(nameof(NewProfilePlace));
            SetProfileStatus(name.Length > 0 ? "" : "Added, but couldn't get the game's name.");
        }

        private void SetProfileStatus(string text)
        {
            ProfileStatus = text;
            OnPropertyChanged(nameof(ProfileStatus));
        }

        private static long? CurrentPlaceId() =>
            LiveSessions.List().OrderByDescending(s => s.Joined).FirstOrDefault(s => s.PlaceId != 0)?.PlaceId;
        #endregion

        #region Playtime
        public record PlaytimeRow(string Name, string Today, string Week, string Total, long TotalSeconds);

        public ObservableCollection<PlaytimeRow> Playtime { get; } = new();

        public string PlaytimeSummary { get; private set; } = "";

        public ICommand RefreshPlaytimeCommand => new RelayCommand(LoadPlaytime);

        private void LoadPlaytime()
        {
            Playtime.Clear();

            var today = DateTime.Today;
            string todayKey = today.ToString("yyyy-MM-dd");
            var weekKeys = Enumerable.Range(0, 7).Select(i => today.AddDays(-i).ToString("yyyy-MM-dd")).ToHashSet();

            long allToday = 0, allWeek = 0, allTotal = 0;

            var rows = PlaytimeTracker.Load().Values.Select(game =>
            {
                long t = game.Days.GetValueOrDefault(todayKey);
                long w = game.Days.Where(day => weekKeys.Contains(day.Key)).Sum(day => day.Value);
                long total = game.Days.Values.Sum();

                allToday += t; allWeek += w; allTotal += total;

                return new PlaytimeRow(String.IsNullOrEmpty(game.Name) ? $"Place {game.PlaceId}" : game.Name, Format(t), Format(w), Format(total), total);
            }).OrderByDescending(row => row.TotalSeconds).Take(15).ToList();

            foreach (var row in rows)
                Playtime.Add(row);

            PlaytimeSummary = rows.Count == 0
                ? "Nothing yet. It starts counting the next time you play."
                : $"Today {Format(allToday)}, this week {Format(allWeek)}, all time {Format(allTotal)}.";

            OnPropertyChanged(nameof(PlaytimeSummary));
            OnPropertyChanged(nameof(PlaytimeHeaderVisibility));
        }

        public System.Windows.Visibility PlaytimeHeaderVisibility =>
            Playtime.Count > 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

        private static string Format(long seconds)
        {
            if (seconds < 60)
                return seconds == 0 ? "-" : "<1m";

            var time = TimeSpan.FromSeconds(seconds);
            return time.TotalHours >= 1 ? $"{(int)time.TotalHours}h {time.Minutes}m" : $"{time.Minutes}m";
        }
        #endregion

        #region Friend alerts
        public record AccountChoice(long UserId, string Label);

        public ObservableCollection<AccountChoice> FriendAccounts { get; } = new();

        public bool FriendAlerts
        {
            get => Gameplay.FriendAlerts;
            set => Gameplay.FriendAlerts = value;
        }

        public long FriendAlertsUserId
        {
            get => FriendAccounts.Any(a => a.UserId == Gameplay.FriendAlertsUserId) ? Gameplay.FriendAlertsUserId : 0;
            set => Gameplay.FriendAlertsUserId = value;
        }

        public bool HasSavedAccounts => FriendAccounts.Count > 1;

        public string FriendAlertsNote => HasSavedAccounts
            ? "Checks once a minute while Roblox is open."
            : "Needs a saved account. Add one in the Instances window first.";
        #endregion

        #region Discord
        public class HiddenGameRow
        {
            public long UniverseId { get; }

            public string Name { get; set; } = "";

            public HiddenGameRow(long universeId) => UniverseId = universeId;
        }

        public ObservableCollection<HiddenGameRow> HiddenGames { get; } = new();

        public string DiscordAppId
        {
            get => Gameplay.DiscordAppId;
            set => Gameplay.DiscordAppId = new string((value ?? "").Where(Char.IsDigit).ToArray());
        }

        public bool DiscordShowServerInfo
        {
            get => Gameplay.DiscordShowServerInfo;
            set => Gameplay.DiscordShowServerInfo = value;
        }

        public bool DiscordFollowFocusedWindow
        {
            get => Gameplay.DiscordFollowFocusedWindow;
            set => Gameplay.DiscordFollowFocusedWindow = value;
        }

        public string NewHiddenGame { get; set; } = "";

        public string HiddenStatus { get; private set; } = "";

        public ICommand HideGameCommand => new AsyncRelayCommand(() => HideGame(NewHiddenGame));

        public ICommand HideCurrentGameCommand => new AsyncRelayCommand(async () =>
        {
            if (CurrentPlaceId() is long placeId)
                await HideGame(placeId.ToString());
            else
                SetHiddenStatus("You're not in a game right now.");
        });

        public ICommand UnhideGameCommand => new RelayCommand<HiddenGameRow>(row =>
        {
            if (row is null)
                return;

            HiddenGames.Remove(row);
            Gameplay.DiscordHiddenGames.Remove(row.UniverseId);
        });

        private async Task HideGame(string text)
        {
            long? placeId = GameProfiles.GetPlaceId(text) ?? (long.TryParse(System.Text.RegularExpressions.Regex.Match(text, @"\d+").Value, out long id) ? id : null);

            if (placeId is not long place || place <= 0)
            {
                SetHiddenStatus("Paste a place ID or a roblox.com/games/... link.");
                return;
            }

            SetHiddenStatus("Looking up the game...");

            if (await GameProfiles.GetUniverseIdAsync(place) is not long universeId)
            {
                SetHiddenStatus("Couldn't find that game.");
                return;
            }

            if (!Gameplay.DiscordHiddenGames.Contains(universeId))
            {
                Gameplay.DiscordHiddenGames.Add(universeId);

                string name = (await GameProfiles.GetUniverseNamesAsync(new[] { universeId })).GetValueOrDefault(universeId) ?? $"Game {universeId}";
                HiddenGames.Add(new HiddenGameRow(universeId) { Name = name });
            }

            NewHiddenGame = "";
            OnPropertyChanged(nameof(NewHiddenGame));
            SetHiddenStatus("");
        }

        private void SetHiddenStatus(string text)
        {
            HiddenStatus = text;
            OnPropertyChanged(nameof(HiddenStatus));
        }

        private async Task LoadHiddenGameNames()
        {
            if (HiddenGames.Count == 0)
                return;

            var names = await GameProfiles.GetUniverseNamesAsync(HiddenGames.Select(row => row.UniverseId));

            // rebuild the rows so the list redraws with names
            var rows = HiddenGames.ToList();
            HiddenGames.Clear();

            foreach (var row in rows)
                HiddenGames.Add(new HiddenGameRow(row.UniverseId) { Name = names.GetValueOrDefault(row.UniverseId) ?? row.Name });
        }
        #endregion
    }
}
