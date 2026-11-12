using System.Runtime.InteropServices;
using System.Windows;

namespace Bloxstrap.Integrations
{
    /// <summary>
    /// PSYstrap: small jobs each watcher does for its own Roblox window, every couple of seconds:
    /// - closes RobloxCrashHandler.exe (one less process per window)
    /// - hides a minimized window from the taskbar (minimize to tray)
    /// - reopens Roblox in the same game if it freezes for a minute, or crashes
    /// - closes Roblox when a scheduled launch's time is up
    /// </summary>
    public sealed class ClientCare : IDisposable
    {
        private const string LOG_IDENT = "ClientCare";
        private static readonly TimeSpan HangLimit = TimeSpan.FromSeconds(60);

        private readonly int _pid;
        private readonly Process? _process;
        private readonly IntPtr _exitHandle;
        private readonly Func<ActivityWatcher?> _getActivity;
        private readonly Action<string, string, EventHandler?> _notify;
        private readonly System.Threading.Timer _timer;

        private IntPtr _window;
        private bool _tabHidden;
        private bool _toldAboutTray;
        private DateTime? _hungSince;
        private int _tick;

        /// <summary>Set when PSYstrap closes this Roblox on purpose, so the watchdog leaves it closed.</summary>
        public bool ClosingOnPurpose { get; set; }

        public ClientCare(int robloxPid, Func<ActivityWatcher?> getActivity, Action<string, string, EventHandler?> notify)
        {
            _pid = robloxPid;
            _getActivity = getActivity;
            _notify = notify;

            try { _process = Process.GetProcessById(robloxPid); } catch { }

            // a handle kept open from the start is the only way to read the exit code once Roblox is gone
            _exitHandle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | SYNCHRONIZE, false, robloxPid);

            _timer = new System.Threading.Timer(_ => SafeTick(), null, 1000, 2000);
        }

        private void SafeTick()
        {
            try { Tick(); }
            catch (Exception ex) { App.Logger.WriteException(LOG_IDENT, ex); }
        }

        private void Tick()
        {
            _tick++;
            var options = App.Settings.Prop.Gameplay;

            if (_process is null || _process.HasExited)
                return;

            if (_window == IntPtr.Zero || !IsWindow(_window))
            {
                _process.Refresh();
                _window = _process.MainWindowHandle;
            }

            if (options.DisableCrashHandler && _tick % 3 == 1)
                CloseCrashHandlers();

            UpdateTray(options.MinimizeToTray);

            if (options.CrashWatchdog)
                CheckHang();
            else
                _hungSince = null;

            if (_tick % 15 == 0)
                CheckStopTimers();
        }

        #region Crash handler
        private static void CloseCrashHandlers()
        {
            foreach (var handler in Process.GetProcessesByName("RobloxCrashHandler"))
            {
                using (handler)
                {
                    try
                    {
                        // only the ones from PSYstrap's own Roblox copies, not Studio's
                        string? path = MultiInstance.GetProcessPath(handler.Id);

                        if (path is null || !path.StartsWith(Paths.Base, StringComparison.OrdinalIgnoreCase))
                            continue;

                        handler.Kill();
                        App.Logger.WriteLine(LOG_IDENT, $"Closed RobloxCrashHandler ({handler.Id})");
                    }
                    catch { }
                }
            }
        }
        #endregion

        #region Minimize to tray
        private void UpdateTray(bool enabled)
        {
            if (_window == IntPtr.Zero)
                return;

            bool minimized = IsIconic(_window);

            if (enabled && minimized && !_tabHidden)
            {
                SetTaskbarButton(false);

                if (!_toldAboutTray)
                {
                    _toldAboutTray = true;
                    _notify("Roblox is in the tray", "Click the PSYstrap icon in the corner of the taskbar to bring it back.", (_, _) => ShowWindow());
                }
            }
            else if (_tabHidden && (!minimized || !enabled))
            {
                SetTaskbarButton(true);
            }
        }

        private void SetTaskbarButton(bool visible)
        {
            IntPtr window = _window;

            // the taskbar COM object wants the UI (STA) thread
            Application.Current?.Dispatcher.Invoke(() =>
            {
                try
                {
                    var taskbar = (ITaskbarList)new TaskbarListComObject();
                    taskbar.HrInit();

                    if (visible)
                        taskbar.AddTab(window);
                    else
                        taskbar.DeleteTab(window);

                    Marshal.ReleaseComObject(taskbar);
                    _tabHidden = !visible;
                }
                catch (Exception ex)
                {
                    App.Logger.WriteException(LOG_IDENT, ex);
                }
            });
        }

        /// <summary>Brings the Roblox window back (tray icon click).</summary>
        public void ShowWindow()
        {
            if (_window == IntPtr.Zero)
                return;

            if (_tabHidden)
                SetTaskbarButton(true);

            ShowWindowNative(_window, IsIconic(_window) ? SW_RESTORE : SW_SHOW);
            SetForegroundWindow(_window);
        }
        #endregion

        #region Watchdog
        private void CheckHang()
        {
            if (_window == IntPtr.Zero || !IsHungAppWindow(_window))
            {
                _hungSince = null;
                return;
            }

            _hungSince ??= DateTime.Now;

            if (DateTime.Now - _hungSince < HangLimit)
                return;

            _hungSince = null;
            var game = CurrentOrLastGame();

            App.Logger.WriteLine(LOG_IDENT, $"Roblox has been frozen for a minute (game {game?.PlaceId.ToString() ?? "none"})");

            if (game is null)
                return; // not in a game; nothing to put you back into

            Recover(game, "froze");
        }

        /// <summary>Called by the watcher once Roblox has closed. Reopens it if it crashed.</summary>
        public void OnRobloxExited()
        {
            if (_exitHandle == IntPtr.Zero || ClosingOnPurpose)
                return;

            if (!GetExitCodeProcess(_exitHandle, out uint exitCode) || exitCode == STILL_ACTIVE)
                return;

            int code = unchecked((int)exitCode);

            App.Logger.WriteLine(LOG_IDENT, $"Roblox exited with code 0x{code:X8}");

            // NTSTATUS errors (access violation, stack overflow...) mean a crash. Closing the window gives 0, and
            // PSYstrap's own restarts end it with -1, so neither of those count.
            bool crashed = (uint)code >= 0xC0000000 && code != -1;

            if (!crashed || !App.Settings.Prop.Gameplay.CrashWatchdog)
                return;

            var game = CurrentOrLastGame();

            if (game is null)
                return;

            Recover(game, "crashed");
        }

        private void Recover(ActivityData game, string what)
        {
            string name = game.UniverseDetails?.Data.Name ?? $"place {game.PlaceId}";
            string who = Webhook.AccountSuffix(game.UserId);

            if (!AutoRejoin.RecordAttempt())
            {
                App.Logger.WriteLine(LOG_IDENT, "Already reopened 3 times in 10 minutes, leaving it");

                if (App.Settings.Prop.Gameplay.Webhook.OnCrash)
                    Webhook.Send($"Roblox {what}", $"Roblox {what}{who} in {name}. It already reopened 3 times in 10 minutes, so it's staying closed.", Webhook.Red);

                return;
            }

            if (App.Settings.Prop.Gameplay.Webhook.OnCrash)
                Webhook.Send($"Roblox {what}", $"Roblox {what}{who} in {name}. Opening it again.", Webhook.Gold);

            ClosingOnPurpose = true;
            bool isPrivate = game.ServerType == ServerType.Private;
            Task.Run(() => Accounts.ClientRejoin.RejoinServer(_pid, game.PlaceId, game.JobId, game.AccessCode, isPrivate, game.UserId));
        }

        private ActivityData? CurrentOrLastGame()
        {
            var activity = _getActivity();

            if (activity is null)
                return null;

            if (activity.InGame && activity.Data.PlaceId != 0)
                return activity.Data;

            // left within the last few minutes counts too (a crash usually ends the game first)
            var last = activity.History.FirstOrDefault();
            return last is not null && last.TimeLeft is DateTime left && DateTime.Now - left < TimeSpan.FromMinutes(3) ? last : null;
        }
        #endregion

        #region Scheduled close
        private void CheckStopTimers()
        {
            var activity = _getActivity();

            if (activity is not { InGame: true })
                return;

            var game = activity.Data;

            foreach (var timer in Launching.LoadStopTimers())
            {
                bool mine = timer.UserId != 0
                    ? timer.UserId == game.UserId
                    : timer.PlaceId == game.PlaceId || timer.PlaceId == game.RootActivity?.PlaceId;

                if (!mine || DateTime.Now < timer.StopAt)
                    continue;

                Launching.DeleteStopTimer(timer.Id);
                App.Logger.WriteLine(LOG_IDENT, $"Scheduled time is up for '{timer.Name}', closing Roblox");

                if (App.Settings.Prop.Gameplay.Webhook.OnSchedule)
                    Webhook.Send($"Closed {timer.Name}", $"Scheduled time is up{Webhook.AccountSuffix(game.UserId)}, closed Roblox.", Webhook.Grey);

                ClosingOnPurpose = true;

                try
                {
                    _process?.CloseMainWindow();

                    if (_process is not null && !_process.WaitForExit(10000))
                        _process.Kill();
                }
                catch { }

                return;
            }
        }
        #endregion

        public void Dispose()
        {
            _timer.Dispose();

            if (_exitHandle != IntPtr.Zero)
                CloseHandle(_exitHandle);

            if (_tabHidden && _window != IntPtr.Zero && IsWindow(_window))
                SetTaskbarButton(true);
        }

        #region Native
        private const int SW_SHOW = 5, SW_RESTORE = 9;
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000, SYNCHRONIZE = 0x00100000, STILL_ACTIVE = 259;

        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int processId);
        [DllImport("kernel32.dll")] private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);

        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsHungAppWindow(IntPtr window);
        [DllImport("user32.dll", EntryPoint = "ShowWindow")] private static extern bool ShowWindowNative(IntPtr window, int command);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);

        [ComImport, Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
        private class TaskbarListComObject { }

        [ComImport, Guid("56FDF342-FD6D-11d0-958A-006097C9A090"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ITaskbarList
        {
            void HrInit();
            void AddTab(IntPtr window);
            void DeleteTab(IntPtr window);
            void ActivateTab(IntPtr window);
            void SetActiveAlt(IntPtr window);
        }
        #endregion
    }
}
