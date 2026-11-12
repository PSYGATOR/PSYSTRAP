using Bloxstrap.AppData;
using Bloxstrap.Integrations;
using Bloxstrap.Models;

namespace Bloxstrap
{
    public class Watcher : IDisposable
    {
        // per Roblox client, so multi-instance clients each get their own watcher and overlay
        private readonly InterProcessLock _lock = null!;

        private readonly WatcherData? _watcherData;
        
        private readonly NotifyIconWrapper? _notifyIcon;

        public readonly ActivityWatcher? ActivityWatcher;

        public readonly DiscordRichPresence? RichPresence;

        public readonly OverlayController? Overlay;

        public readonly AutoRejoin? AutoRejoin;

        public readonly PlaytimeTracker? Playtime;

        private readonly FriendAlerts? _friendAlerts;

        public readonly ClientCare? Care;

        private readonly GameBooster? _booster;

        private readonly ScreenshotOrganiser? _screenshots;

        private readonly ClipboardJoin? _clipboard;

        public Watcher()
        {
            const string LOG_IDENT = "Watcher";

            string? watcherDataArg = App.LaunchSettings.WatcherFlag.Data;

            if (String.IsNullOrEmpty(watcherDataArg))
            {
#if DEBUG
                string path = new RobloxPlayerData().ExecutablePath;
                if (!File.Exists(path))
                    throw new ApplicationException("Roblox player is not been installed");

                using var gameClientProcess = Process.Start(path);

                _watcherData = new() { ProcessId = gameClientProcess.Id };
#else
                throw new Exception("Watcher data not specified");
#endif
            }
            else
            {
                _watcherData = JsonSerializer.Deserialize<WatcherData>(Encoding.UTF8.GetString(Convert.FromBase64String(watcherDataArg)));
            }

            if (_watcherData is null)
                throw new Exception("Watcher data is invalid");

            _lock = new InterProcessLock($"Watcher-{_watcherData.ProcessId}");

            if (!_lock.IsAcquired)
            {
                App.Logger.WriteLine(LOG_IDENT, "Watcher instance already exists for this client");
                return;
            }
            // PSYstrap: Windows remembers a mute per app, so a window closed while muted in the background would open
            // muted next time. A lone client always gets its sound back; with more, the multi-instance host decides.
            int clientPid = _watcherData.ProcessId;

            Task.Run(async () =>
            {
                for (int attempt = 0; attempt < 30; attempt++)
                {
                    if (MultiInstance.GetRobloxProcesses().Any(process => process.Id != clientPid))
                        return;

                    var (found, changed) = AudioMute.SetMute(clientPid, false);

                    if (changed > 0)
                        App.Logger.WriteLine(LOG_IDENT, "Unmuted Roblox (it was left muted from last time)");

                    if (found > 0)
                        return;

                    await Task.Delay(2000);
                }
            });

            if (App.Settings.Prop.EnableActivityTracking)
            {
                ActivityWatcher = new(_watcherData.LogFile);

                if (App.Settings.Prop.UseDisableAppPatch)
                {
                    ActivityWatcher.OnAppClose += delegate
                    {
                        App.Logger.WriteLine(LOG_IDENT, "Received desktop app exit, closing Roblox");
                        using var process = Process.GetProcessById(_watcherData.ProcessId);
                        process.CloseMainWindow();
                    };
                }

                // PSYstrap: rejoin the same server if Roblox drops you
                AutoRejoin = new AutoRejoin(ActivityWatcher, _watcherData.ProcessId);

                // PSYstrap: time played per game per day
                Playtime = new PlaytimeTracker(ActivityWatcher);

                // PSYstrap: let the account launcher see which server this client is in
                int robloxPid = _watcherData.ProcessId;
                var activity = ActivityWatcher;
                activity.OnGameJoin += (_, _) => Accounts.LiveSessions.Publish(robloxPid, activity.Data);
                activity.OnGameLeave += (_, _) => Accounts.LiveSessions.Clear(robloxPid);

                // every window gets presence, but only one shows it at a time (see DiscordOwner)
                if (App.Settings.Prop.UseDiscordRichPresence)
                    RichPresence = new(ActivityWatcher, _watcherData.ProcessId);

                // PSYstrap: remember servers, and a little recap when you leave a game
                RecentServers.Track(activity);
                activity.OnGameLeave += (_, _) => _ = ShowSessionRecapAsync(activity);
            }

            // PSYstrap: crash handler, tray, watchdog, scheduled close
            Care = new ClientCare(_watcherData.ProcessId, () => ActivityWatcher, Notify);
            _booster = new GameBooster(_watcherData.ProcessId);
            _screenshots = new ScreenshotOrganiser();

            // PSYstrap in-game overlay. Created on the UI thread (the watcher is constructed there).
            // Always runs so the menu key works; "Show overlay" only hides the counters.
            try
            {
                Overlay = new OverlayController(_watcherData.ProcessId, () =>
                    ActivityWatcher is { InGame: true } activity ? activity.Data : null)
                {
                    GetPlaytimeToday = () => Playtime?.TodayInCurrentGame()
                };
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, "Couldn't start the overlay");
                App.Logger.WriteException(LOG_IDENT, ex);
            }

            _notifyIcon = new(this);

            // PSYstrap: friend alerts (only one watcher actually checks; see FriendAlerts)
            _friendAlerts = new FriendAlerts(Notify);

            _clipboard = new ClipboardJoin(Notify);
        }

        /// <summary>A popup from this window's tray icon. Safe from any thread.</summary>
        public void Notify(string caption, string message, EventHandler? onClick) =>
            System.Windows.Application.Current.Dispatcher.BeginInvoke(() => _notifyIcon?.ShowAlert(caption, message, 10, onClick));

        private async Task ShowSessionRecapAsync(ActivityWatcher activity)
        {
            try
            {
                if (!App.Settings.Prop.Gameplay.SessionRecap || activity.History.FirstOrDefault() is not ActivityData game)
                    return;

                // a teleport is a leave and a join straight away; only recap real leaves
                await Task.Delay(TimeSpan.FromSeconds(8));

                if (activity.InGame && activity.Data.UniverseId == game.UniverseId)
                    return;

                var played = (game.TimeLeft ?? DateTime.Now) - (game.RootActivity?.TimeJoined ?? game.TimeJoined);

                if (played < TimeSpan.FromMinutes(1))
                    return;

                string name = game.UniverseDetails?.Data.Name
                    ?? (await GameProfiles.GetUniverseNamesAsync(new[] { game.UniverseId })).GetValueOrDefault(game.UniverseId)
                    ?? "that game";

                long todaySeconds = PlaytimeTracker.Load().GetValueOrDefault(game.UniverseId.ToString())?.Days.GetValueOrDefault(DateTime.Today.ToString("yyyy-MM-dd")) ?? 0;

                string message = $"You played for {FormatSpan(played)}.";
                if (todaySeconds > played.TotalSeconds + 60)
                    message += $" {FormatSpan(TimeSpan.FromSeconds(todaySeconds))} today in total.";

                Notify($"Left {name}", message, null);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("Watcher::SessionRecap", ex);
            }
        }

        private static string FormatSpan(TimeSpan time) =>
            time.TotalHours >= 1 ? $"{(int)time.TotalHours}h {time.Minutes}m" : $"{Math.Max(1, time.Minutes)}m";

        public void KillRobloxProcess() => CloseProcess(_watcherData!.ProcessId, true);

        public void CloseProcess(int pid, bool force = false)
        {
            const string LOG_IDENT = "Watcher::CloseProcess";

            try
            {
                using var process = Process.GetProcessById(pid);

                App.Logger.WriteLine(LOG_IDENT, $"Killing process '{process.ProcessName}' (pid={pid}, force={force})");

                if (process.HasExited)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"PID {pid} has already exited");
                    return;
                }

                if (force)
                    process.Kill();
                else
                    process.CloseMainWindow();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"PID {pid} could not be closed");
                App.Logger.WriteException(LOG_IDENT, ex);
            }
        }

        public async Task Run()
        {
            if (!_lock.IsAcquired || _watcherData is null)
                return;

            ActivityWatcher?.Start();

            while (Utilities.GetProcessesSafe().Any(x => x.Id == _watcherData.ProcessId))
                await Task.Delay(1000);

            // PSYstrap: reopen it if it crashed
            Care?.OnRobloxExited();

            if (_watcherData.AutoclosePids is not null)
            {
                foreach (int pid in _watcherData.AutoclosePids)
                    CloseProcess(pid);
            }

            if (App.LaunchSettings.TestModeFlag.Active)
                Process.Start(Paths.Process, "-settings -testmode");
        }

        public void Dispose()
        {
            App.Logger.WriteLine("Watcher::Dispose", "Disposing Watcher");

            Overlay?.Dispose();
            Playtime?.Dispose();
            _friendAlerts?.Dispose();
            Care?.Dispose();
            _booster?.Dispose();
            _screenshots?.Dispose();
            _clipboard?.Dispose();

            if (_watcherData is not null)
                Accounts.LiveSessions.Clear(_watcherData.ProcessId);

            _notifyIcon?.Dispose();
            RichPresence?.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}
