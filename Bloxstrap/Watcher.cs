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
            }

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
            _friendAlerts = new FriendAlerts((caption, message, onClick) =>
                System.Windows.Application.Current.Dispatcher.BeginInvoke(() => _notifyIcon?.ShowAlert(caption, message, 10, onClick)));
        }

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

            if (_watcherData is not null)
                Accounts.LiveSessions.Clear(_watcherData.ProcessId);

            _notifyIcon?.Dispose();
            RichPresence?.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}
