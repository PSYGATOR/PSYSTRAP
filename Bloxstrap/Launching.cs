using Microsoft.Win32;

using Bloxstrap.Accounts;
using Bloxstrap.Integrations;
using Bloxstrap.Models.Persistable;

namespace Bloxstrap
{
    /// <summary>
    /// PSYstrap: favourite games and private servers, desktop shortcuts for them, opening them when Windows starts, and
    /// the scheduler (Windows Task Scheduler tasks that open a favourite at a time of day and close it later).
    /// </summary>
    public static class Launching
    {
        private const string LOG_IDENT = "Launching";
        private const string TaskFolder = "PSYstrap";
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        private static LaunchingSettings Settings => App.Settings.Prop.Launching;

        public static FavouriteGame? Find(string id) => Settings.Favourites.FirstOrDefault(f => f.Id == id);

        #region Links
        private static readonly Regex PrivateCodePattern = new(@"privateServerLinkCode=([\w-]+)", RegexOptions.IgnoreCase);
        private static readonly Regex LinkCodePattern = new(@"[?&]linkCode=([\w-]+)", RegexOptions.IgnoreCase);
        private static readonly Regex GamesPattern = new(@"roblox\.com/(?:[a-z]{2}/)?games/(\d+)", RegexOptions.IgnoreCase);
        private static readonly Regex PlaceIdPattern = new(@"placeId=(\d+)", RegexOptions.IgnoreCase);
        public static readonly Regex SharePattern = new(@"roblox\.com/share\?code=([\w]+)&type=Server", RegexOptions.IgnoreCase);

        /// <summary>Place ID and private server link code from a game or private server link (or a bare place ID).</summary>
        public static (long PlaceId, string LinkCode)? ParseGameLink(string text)
        {
            text = text.Trim();

            if (long.TryParse(text, out long bare) && bare > 0)
                return (bare, "");

            var games = GamesPattern.Match(text);
            var placeParam = PlaceIdPattern.Match(text);
            long placeId = games.Success ? long.Parse(games.Groups[1].Value) : placeParam.Success ? long.Parse(placeParam.Groups[1].Value) : 0;

            if (placeId == 0)
                return null;

            var code = PrivateCodePattern.Match(text);
            if (!code.Success)
                code = LinkCodePattern.Match(text);

            return (placeId, code.Success ? code.Groups[1].Value : "");
        }

        /// <summary>Works out a favourite from any Roblox link, including share links (those need a saved account).</summary>
        public static async Task<FavouriteGame?> FavouriteFromLinkAsync(string text)
        {
            (long PlaceId, string LinkCode)? parsed = ParseGameLink(text);

            var share = SharePattern.Match(text);
            if (parsed is null && share.Success)
            {
                var account = AccountStore.Load().FirstOrDefault();
                if (account is not null && AccountStore.GetSession(account) is string session)
                    parsed = await RobloxAuth.ResolveShareLinkAsync(session, share.Groups[1].Value);
            }

            if (parsed is not (long placeId, string linkCode))
                return null;

            string name = await GameProfiles.GetGameNameAsync(placeId) ?? $"Place {placeId}";

            return new FavouriteGame
            {
                PlaceId = placeId,
                PrivateServerLinkCode = linkCode,
                Name = linkCode.Length > 0 ? $"{name} (private server)" : name
            };
        }
        #endregion

        #region Opening
        public static async Task LaunchByIdAsync(string id, int stopAfterMinutes, bool scheduled)
        {
            var favourite = Find(id);

            if (favourite is null)
            {
                App.Logger.WriteLine(LOG_IDENT, $"No favourite with id '{id}'");

                if (!scheduled)
                    Frontend.ShowMessageBox("That favourite isn't in PSYstrap anymore. Add it again in Settings > AFK & launching and make a new shortcut.", System.Windows.MessageBoxImage.Information);

                return;
            }

            await LaunchAsync(favourite, stopAfterMinutes, scheduled);
        }

        /// <summary>Opens Roblox in this game/private server, as the favourite's saved account if it has one.</summary>
        public static async Task<bool> LaunchAsync(FavouriteGame favourite, int stopAfterMinutes = 0, bool scheduled = false)
        {
            string? link = null;

            if (favourite.AccountUserId != 0)
            {
                var account = AccountStore.Load().FirstOrDefault(a => a.UserId == favourite.AccountUserId);

                if (account is not null && AccountStore.GetSession(account) is string session && await RobloxAuth.GetLaunchTicketAsync(session) is string ticket)
                    link = RobloxAuth.BuildLaunchLink(ticket, favourite.PlaceId, linkCode: favourite.PrivateServerLinkCode);
                else
                    App.Logger.WriteLine(LOG_IDENT, "Couldn't get a launch ticket for the saved account, opening normally");
            }

            link ??= favourite.PrivateServerLinkCode.Length > 0
                ? $"roblox://experiences/start?placeId={favourite.PlaceId}&linkCode={favourite.PrivateServerLinkCode}"
                : $"roblox://experiences/start?placeId={favourite.PlaceId}";

            App.Logger.WriteLine(LOG_IDENT, $"Opening '{favourite.Name}' (place {favourite.PlaceId}, stop after {stopAfterMinutes} min)");

            // log files are named by the second they start in; a launch in this process's first second would get the
            // same name, think it's a duplicate launch and quit
            var age = DateTime.Now - Process.GetCurrentProcess().StartTime;
            if (age < TimeSpan.FromSeconds(1.5))
                await Task.Delay(TimeSpan.FromSeconds(1.5) - age);

            try
            {
                Process.Start(Paths.Process, $"-player \"{link}\"");
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                return false;
            }

            if (stopAfterMinutes > 0)
                AddStopTimer(favourite, DateTime.Now.AddMinutes(stopAfterMinutes));

            if (scheduled)
                Integrations.AfkLog.Add(new Integrations.AfkLog.Entry { At = DateTime.Now, Kind = Integrations.AfkLog.Kind.ScheduledOpen, UserId = favourite.AccountUserId, PlaceId = favourite.PlaceId, Game = favourite.Name });

            if (scheduled && App.Settings.Prop.Gameplay.Webhook.OnSchedule)
                await Webhook.SendAsync(App.Settings.Prop.Gameplay.Webhook.Url, $"Opened {favourite.Name}",
                    $"Scheduled launch{Webhook.AccountSuffix(favourite.AccountUserId)}" + (stopAfterMinutes > 0 ? $", closing in {FormatMinutes(stopAfterMinutes)}." : "."));

            return true;
        }

        /// <summary>"-startup": open the AFK setup once Windows has settled.</summary>
        public static async Task OpenStartupFavouritesAsync()
        {
            // give Windows a moment to finish starting up (network etc.)
            await Task.Delay(TimeSpan.FromSeconds(20));
            await Integrations.InternetCheck.WaitUntilOnlineAsync(TimeSpan.FromMinutes(5));
            await OpenAfkSetupAsync();
        }

        /// <summary>
        /// "-afksetup": opens every favourite ticked "part of my AFK setup", a few seconds apart, then tiles the windows.
        /// </summary>
        public static async Task OpenAfkSetupAsync()
        {
            var favourites = Settings.Favourites.Where(f => f.OpenAtStartup).ToList();

            if (favourites.Count == 0)
            {
                App.Logger.WriteLine(LOG_IDENT, "AFK setup is empty");
                return;
            }

            App.Logger.WriteLine(LOG_IDENT, $"Opening the AFK setup ({favourites.Count} games)");

            for (int i = 0; i < favourites.Count; i++)
            {
                if (i > 0)
                    await Task.Delay(TimeSpan.FromSeconds(12));

                await LaunchAsync(favourites[i]);
            }

            // wait for the last window to show up, then lay them all out
            if (favourites.Count > 1)
            {
                await Task.Delay(TimeSpan.FromSeconds(40));
                Integrations.RobloxWindows.ArrangeAll();
            }
        }

        /// <summary>A desktop shortcut that opens the whole AFK setup.</summary>
        public static string? CreateAfkSetupShortcut()
        {
            try
            {
                string lnk = Path.Combine(Paths.Desktop, "PSYstrap AFK setup.lnk");
                dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
                dynamic shortcut = shell.CreateShortcut(lnk);
                shortcut.TargetPath = Paths.Application;
                shortcut.Arguments = "-afksetup";
                shortcut.WorkingDirectory = Paths.Base;
                shortcut.IconLocation = $"{Paths.Application},0";
                shortcut.Description = "Open every game in your PSYstrap AFK setup";
                shortcut.Save();
                return lnk;
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                return null;
            }
        }

        public static string FormatMinutes(int minutes) =>
            minutes >= 60 ? $"{minutes / 60}h" + (minutes % 60 > 0 ? $" {minutes % 60}m" : "") : $"{minutes}m";
        #endregion

        #region Stop timers (close a scheduled launch later)
        public class StopTimer
        {
            public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
            public string Name { get; set; } = "";
            public long PlaceId { get; set; }
            public long UserId { get; set; }
            public DateTime StopAt { get; set; }
        }

        private static string TimerFolder => Path.Combine(Paths.Base, "StopTimers");

        private static void AddStopTimer(FavouriteGame favourite, DateTime stopAt)
        {
            Directory.CreateDirectory(TimerFolder);
            var timer = new StopTimer { Name = favourite.Name, PlaceId = favourite.PlaceId, UserId = favourite.AccountUserId, StopAt = stopAt };
            File.WriteAllText(Path.Combine(TimerFolder, $"{timer.Id}.json"), JsonSerializer.Serialize(timer));
        }

        /// <summary>Timers still waiting; ones that never found their window within 12 hours are dropped.</summary>
        public static List<StopTimer> LoadStopTimers()
        {
            var timers = new List<StopTimer>();

            if (!Directory.Exists(TimerFolder))
                return timers;

            foreach (string file in Directory.GetFiles(TimerFolder, "*.json"))
            {
                try
                {
                    var timer = JsonSerializer.Deserialize<StopTimer>(File.ReadAllText(file));

                    if (timer is null || DateTime.Now - timer.StopAt > TimeSpan.FromHours(12))
                        File.Delete(file);
                    else
                        timers.Add(timer);
                }
                catch (IOException) { }
                catch (JsonException) { try { File.Delete(file); } catch { } }
            }

            return timers;
        }

        public static void DeleteStopTimer(string id)
        {
            try { File.Delete(Path.Combine(TimerFolder, $"{id}.json")); } catch { }
        }
        #endregion

        #region Desktop shortcuts
        /// <summary>Puts a shortcut on the desktop that opens this favourite, with the game's icon.</summary>
        public static async Task<string?> CreateShortcutAsync(FavouriteGame favourite)
        {
            try
            {
                string? icon = await RobloxImages.GameIconIcoAsync(favourite.PlaceId);
                string name = String.Concat(favourite.Name.Split(Path.GetInvalidFileNameChars())).Trim().TrimEnd('.', ' ');
                string lnk = Path.Combine(Paths.Desktop, $"{(name.Length > 0 ? name : "Roblox game")}.lnk");

                // Windows' own shortcut maker (WScript.Shell), so the target is stored the way Explorer expects
                dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
                dynamic shortcut = shell.CreateShortcut(lnk);
                shortcut.TargetPath = Paths.Application;
                shortcut.Arguments = $"-favourite {favourite.Id}";
                shortcut.WorkingDirectory = Paths.Base;
                shortcut.IconLocation = $"{icon ?? Paths.Application},0";
                shortcut.Description = $"Play {favourite.Name} with {App.ProjectName}";
                shortcut.Save();
                App.Logger.WriteLine(LOG_IDENT, $"Made a shortcut for '{favourite.Name}'");
                return lnk;
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                return null;
            }
        }

        #endregion

        #region Windows: start with Windows + scheduled tasks
        /// <summary>
        /// Makes Windows match the settings: the Run entry for "start with Windows", and one Task Scheduler task per
        /// scheduled launch (in a "PSYstrap" folder, so they're easy to find). Called when settings are saved.
        /// </summary>
        public static void SyncWithWindows()
        {
            try
            {
                using var run = Registry.CurrentUser.CreateSubKey(RunKey);

                bool wantStartup = Settings.StartWithWindows && Settings.Favourites.Any(f => f.OpenAtStartup);

                if (wantStartup)
                    run.SetValue(App.ProjectName, $"\"{Paths.Application}\" -startup");
                else if (run.GetValue(App.ProjectName) is not null)
                    run.DeleteValue(App.ProjectName);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
            }

            try
            {
                var wanted = Settings.Schedule
                    .Where(s => s.Enabled && Find(s.FavouriteId) is not null && TimeSpan.TryParse(s.Time, out _))
                    .ToDictionary(s => $"{TaskFolder}\\{s.Id}");

                foreach (string existing in ListTasks().Where(name => !wanted.ContainsKey(name)))
                    RunSchtasks("/Delete", "/TN", existing, "/F");

                foreach (var (name, entry) in wanted)
                {
                    string time = TimeSpan.Parse(entry.Time).ToString(@"hh\:mm");
                    string command = $"\"{Paths.Application}\" -favourite {entry.FavouriteId} -scheduled"
                        + (entry.StopAfterMinutes > 0 ? $" -stopafter {entry.StopAfterMinutes}" : "");

                    RunSchtasks("/Create", "/F", "/SC", "DAILY", "/ST", time, "/TN", name, "/TR", command);
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
            }
        }

        private static List<string> ListTasks()
        {
            string output = RunSchtasks("/Query", "/FO", "CSV", "/NH");
            var names = new List<string>();

            foreach (string line in output.Split('\n'))
            {
                var match = Regex.Match(line, $@"^""\\({TaskFolder}\\[^""]+)""");
                if (match.Success && !names.Contains(match.Groups[1].Value))
                    names.Add(match.Groups[1].Value);
            }

            return names;
        }

        private static string RunSchtasks(params string[] args)
        {
            var info = new ProcessStartInfo("schtasks.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            foreach (string arg in args)
                info.ArgumentList.Add(arg);

            using var process = Process.Start(info)!;
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit(15000);

            if (process.ExitCode != 0 && args[0] != "/Query")
                App.Logger.WriteLine(LOG_IDENT, $"schtasks {args[0]} failed: {error.Trim()}");

            return output;
        }
        #endregion
    }
}
