using System.Runtime.InteropServices;

namespace Bloxstrap.Integrations
{
    /// <summary>
    /// PSYstrap: the AFK jobs that are about the whole PC rather than one window:
    /// - keep the PC awake while Roblox is open (every watcher asks; Windows lets the PC sleep once none do)
    /// - away mode: minimize every Roblox window when you've left the PC, bring them back when you return
    /// - the status report to your phone every so often
    /// Only the watcher holding the "AfkCoordinator" lock does away mode and the report, so they happen once, not per window.
    /// </summary>
    public sealed class AfkCoordinator : IDisposable
    {
        private const string LOG_IDENT = "AfkCoordinator";

        private readonly CancellationTokenSource _cancel = new();
        private readonly Thread _thread;

        private List<IntPtr> _awayMinimized = new();
        private DateTime _nextReport = DateTime.MinValue;
        private DateTime _lastReport = DateTime.Now;
        private bool _awake;

        public AfkCoordinator()
        {
            _thread = new Thread(Loop) { IsBackground = true, Name = "PSYstrap AFK coordinator" };
            _thread.Start();
        }

        private void Loop()
        {
            InterProcessLock? owner = null;
            var token = _cancel.Token;

            try
            {
                while (!token.IsCancellationRequested)
                {
                    var afk = App.Settings.Prop.Afk;

                    try
                    {
                        UpdateKeepAwake(afk.KeepAwake);

                        if (owner is null)
                        {
                            var candidate = new InterProcessLock("AfkCoordinator");

                            if (candidate.IsAcquired)
                                owner = candidate;
                            else
                                candidate.Mutex.Dispose();
                        }

                        if (owner is not null)
                        {
                            AwayMode(afk.AwayMinimizeMinutes);
                            StatusReport(afk.StatusReportMinutes);
                        }
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteException(LOG_IDENT, ex);
                    }

                    token.WaitHandle.WaitOne(TimeSpan.FromSeconds(3));
                }
            }
            finally
            {
                if (_awayMinimized.Count > 0)
                    RobloxWindows.Restore(_awayMinimized);

                UpdateKeepAwake(false);
                owner?.Dispose(); // released by the thread that took it
            }
        }

        #region Keep awake
        private void UpdateKeepAwake(bool wanted)
        {
            if (wanted == _awake)
                return;

            // ES_SYSTEM_REQUIRED only: the screen can still turn off, the PC just doesn't sleep
            SetThreadExecutionState(wanted ? ES_CONTINUOUS | ES_SYSTEM_REQUIRED : ES_CONTINUOUS);
            _awake = wanted;
            App.Logger.WriteLine(LOG_IDENT, wanted ? "Keeping the PC awake" : "Letting the PC sleep again");
        }
        #endregion

        #region Away mode
        private void AwayMode(int minutes)
        {
            double idle = SecondsSinceLastInput();

            if (_awayMinimized.Count > 0)
            {
                // you're back - unless that "input" was the anti-AFK nudge, which Windows counts as input too
                if ((idle < 2 && !AntiAfk.NudgedRecently()) || minutes <= 0)
                {
                    App.Logger.WriteLine(LOG_IDENT, "You're back, restoring the Roblox windows");
                    RobloxWindows.Restore(_awayMinimized);
                    _awayMinimized.Clear();
                }

                return;
            }

            if (minutes <= 0 || idle < minutes * 60)
                return;

            _awayMinimized = RobloxWindows.MinimizeAll();

            if (_awayMinimized.Count > 0)
                App.Logger.WriteLine(LOG_IDENT, $"Away for {minutes} minutes, minimized {_awayMinimized.Count} Roblox windows");
        }
        #endregion

        #region Status report
        private void StatusReport(int minutes)
        {
            if (minutes <= 0 || !Webhook.IsValidUrl(App.Settings.Prop.Gameplay.Webhook.Url))
            {
                _nextReport = DateTime.MinValue;
                return;
            }

            if (_nextReport == DateTime.MinValue)
                _nextReport = DateTime.Now.AddMinutes(minutes);

            if (DateTime.Now < _nextReport)
                return;

            _nextReport = DateTime.Now.AddMinutes(minutes);
            var since = _lastReport;
            _lastReport = DateTime.Now;

            Webhook.Send("PSYstrap status", BuildStatus(since), Webhook.Grey);
        }

        /// <summary>A short summary of every Roblox window, plus what happened since a point in time.</summary>
        public static string BuildStatus(DateTime since)
        {
            var clients = MultiInstance.GetRobloxProcesses();
            var sessions = Accounts.LiveSessions.List();
            var accounts = Accounts.AccountStore.Load();
            var summary = AfkLog.Summarise(since);
            var lines = new List<string>();

            int inGame = sessions.Count(s => s.PlaceId != 0 && clients.Any(c => c.Id == s.ProcessId));
            DateTime? oldest = clients.Select(c => MultiInstance.GetStartTime(c.Id)).Where(t => t is not null).Min();

            lines.Add($"{clients.Count} Roblox window{(clients.Count == 1 ? "" : "s")}, {inGame} in a game" + (oldest is DateTime start ? $", up {Format(DateTime.Now - start)}" : "") + ".");

            var log = AfkLog.Load(400);

            foreach (var session in sessions.Where(s => clients.Any(c => c.Id == s.ProcessId)))
            {
                string who = accounts.FirstOrDefault(a => a.UserId == session.UserId)?.Username ?? (session.UserId != 0 ? session.UserId.ToString() : "?");
                string game = log.FirstOrDefault(e => e.PlaceId == session.PlaceId && e.Game.Length > 0)?.Game ?? $"place {session.PlaceId}";
                lines.Add(session.PlaceId == 0 ? $"- {who}: not in a game" : $"- {who}: {game}, joined {Format(DateTime.Now - session.Joined)} ago");
            }

            lines.Add($"Since the last report: {summary.Disconnects} disconnects, {summary.Rejoins} rejoins, {summary.Crashes} crashes/freezes.");
            return String.Join("\n", lines);
        }

        private static string Format(TimeSpan time) =>
            time.TotalHours >= 1 ? $"{(int)time.TotalHours}h {time.Minutes}m" : $"{Math.Max(0, time.Minutes)}m";
        #endregion

        public static double SecondsSinceLastInput()
        {
            var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
            return GetLastInputInfo(ref info) ? (Environment.TickCount - (int)info.dwTime) / 1000.0 : 0;
        }

        public void Dispose()
        {
            _cancel.Cancel();
            _thread.Join(5000);
        }

        #region Native
        private const uint ES_CONTINUOUS = 0x80000000, ES_SYSTEM_REQUIRED = 0x00000001;

        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

        [DllImport("kernel32.dll")] private static extern uint SetThreadExecutionState(uint flags);
        [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
        #endregion
    }
}
