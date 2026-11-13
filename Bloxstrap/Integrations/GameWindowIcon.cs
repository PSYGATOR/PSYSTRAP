using System.Runtime.InteropServices;

namespace Bloxstrap.Integrations
{
    /// <summary>
    /// PSYstrap: puts the icon of the game you're in on the Roblox window (title bar, taskbar, Alt+Tab) instead of
    /// the Roblox logo, and puts the logo back when you leave. Only sends the window a WM_SETICON, the same message
    /// Windows uses for any app's icon; nothing in Roblox is touched. The icons belong to this watcher, which
    /// lives as long as the Roblox window, and get freed when they're swapped out.
    /// </summary>
    public sealed class GameWindowIcon : IDisposable
    {
        private const string LOG_IDENT = "GameWindowIcon";

        private readonly ActivityWatcher _activity;
        private readonly int _robloxPid;
        private readonly System.Threading.Timer _timer;

        private IntPtr _big;
        private IntPtr _small;
        private long _placeId;
        private bool _disposed;

        public GameWindowIcon(ActivityWatcher activity, int robloxPid)
        {
            _activity = activity;
            _robloxPid = robloxPid;

            activity.OnGameJoin += (_, _) => _ = ApplyAsync(activity.Data.RootActivity?.PlaceId ?? activity.Data.PlaceId);
            activity.OnGameLeave += (_, _) => _ = LeaveAsync();

            // Roblox can recreate its window (fullscreen etc.), which brings its own icon back
            _timer = new System.Threading.Timer(_ => Reapply(), null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
        }

        private async Task ApplyAsync(long placeId)
        {
            if (!App.Settings.Prop.Gameplay.GameIconOnWindow || placeId == 0)
                return;

            try
            {
                string? ico = await RobloxImages.GameIconIcoAsync(placeId);

                if (ico is null || _disposed)
                    return;

                IntPtr big = LoadImage(IntPtr.Zero, ico, IMAGE_ICON, GetSystemMetrics(SM_CXICON), GetSystemMetrics(SM_CYICON), LR_LOADFROMFILE);
                IntPtr small = LoadImage(IntPtr.Zero, ico, IMAGE_ICON, GetSystemMetrics(SM_CXSMICON), GetSystemMetrics(SM_CYSMICON), LR_LOADFROMFILE);

                if (big == IntPtr.Zero || small == IntPtr.Zero)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Couldn't load {ico} ({Marshal.GetLastWin32Error()})");
                    Destroy(big);
                    Destroy(small);
                    return;
                }

                IntPtr oldBig, oldSmall;

                lock (this)
                {
                    oldBig = _big;
                    oldSmall = _small;
                    _big = big;
                    _small = small;
                    _placeId = placeId;
                }

                Send(big, small);

                // only free the old ones once the window has the new ones
                Destroy(oldBig);
                Destroy(oldSmall);

                App.Logger.WriteLine(LOG_IDENT, $"Window icon set to place {placeId}");
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
            }
        }

        private async Task LeaveAsync()
        {
            // a teleport is a leave and a join straight away, so wait a moment before going back to the Roblox logo
            await Task.Delay(TimeSpan.FromSeconds(8));

            if (_activity.InGame || _disposed)
                return;

            Reset();
        }

        private void Reset()
        {
            IntPtr big, small;

            lock (this)
            {
                big = _big;
                small = _small;
                _big = _small = IntPtr.Zero;
                _placeId = 0;
            }

            if (big == IntPtr.Zero)
                return;

            // no icon of its own means the window goes back to the Roblox logo from its window class
            Send(IntPtr.Zero, IntPtr.Zero);
            Destroy(big);
            Destroy(small);
        }

        private void Reapply()
        {
            try
            {
                if (!App.Settings.Prop.Gameplay.GameIconOnWindow)
                {
                    Reset();
                    return;
                }

                IntPtr window = RobloxWindows.GetWindow(_robloxPid);

                if (window == IntPtr.Zero || _big == IntPtr.Zero)
                    return;

                if (SendMessageTimeout(window, WM_GETICON, (IntPtr)ICON_BIG, IntPtr.Zero, SMTO_ABORTIFHUNG, 2000, out IntPtr current) != IntPtr.Zero && current != _big)
                    Send(_big, _small);
            }
            catch { }
        }

        private void Send(IntPtr big, IntPtr small)
        {
            IntPtr window = RobloxWindows.GetWindow(_robloxPid);

            if (window == IntPtr.Zero)
                return;

            SendMessageTimeout(window, WM_SETICON, (IntPtr)ICON_BIG, big, SMTO_ABORTIFHUNG, 2000, out _);
            SendMessageTimeout(window, WM_SETICON, (IntPtr)ICON_SMALL, small, SMTO_ABORTIFHUNG, 2000, out _);
        }

        private static void Destroy(IntPtr icon)
        {
            if (icon != IntPtr.Zero)
                DestroyIcon(icon);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _timer.Dispose();
            Reset();
        }

        #region Native
        private const uint WM_SETICON = 0x0080;
        private const uint WM_GETICON = 0x007F;
        private const int ICON_SMALL = 0;
        private const int ICON_BIG = 1;
        private const uint IMAGE_ICON = 1;
        private const uint LR_LOADFROMFILE = 0x10;
        private const uint SMTO_ABORTIFHUNG = 0x2;
        private const int SM_CXICON = 11;
        private const int SM_CYICON = 12;
        private const int SM_CXSMICON = 49;
        private const int SM_CYSMICON = 50;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadImage(IntPtr instance, string name, uint type, int width, int height, uint flags);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr icon);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
        #endregion
    }
}
