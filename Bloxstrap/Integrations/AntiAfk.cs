using System.Runtime.InteropServices;

namespace Bloxstrap.Integrations
{
    /// <summary>
    /// PSYstrap anti-AFK: Roblox disconnects you after 20 minutes without input. Before that happens this gives the
    /// window a tiny nudge - a 1 pixel mouse wiggle and a right-click tap, which does nothing in almost every game.
    /// If the window isn't focused it only steps in when you haven't touched the PC for a minute, and puts focus back.
    /// </summary>
    public sealed class AntiAfk
    {
        private const string LOG_IDENT = "AntiAfk";

        private readonly Func<IntPtr> _getWindow;
        private readonly Func<bool> _isInGame;

        private DateTime _lastActivity = DateTime.Now;

        public AntiAfk(Func<IntPtr> getWindow, Func<bool> isInGame)
        {
            _getWindow = getWindow;
            _isInGame = isInGame;
        }

        /// <summary>Call for any real input that reached this Roblox window.</summary>
        public void NoteActivity() => _lastActivity = DateTime.Now;

        /// <summary>Call regularly (from the overlay's timer).</summary>
        public void Tick()
        {
            var settings = App.Settings.Prop.Gameplay;

            if (!settings.AntiAfk || !_isInGame())
            {
                _lastActivity = DateTime.Now;
                return;
            }

            if ((DateTime.Now - _lastActivity).TotalMinutes < Math.Clamp(settings.AntiAfkMinutes, 1, 19))
                return;

            IntPtr window = _getWindow();

            if (window == IntPtr.Zero)
                return;

            IntPtr previous = GetForegroundWindow();
            bool needsFocus = previous != window;
            bool wasMinimized = IsIconic(window);

            // don't yank focus away from someone who's using the PC - try again later
            if ((needsFocus || wasMinimized) && SecondsSinceLastInput() < 60)
                return;

            // minimized (away mode, tray): pop it up for the nudge, then put it back
            if (wasMinimized)
            {
                ShowWindow(window, SW_RESTORE);
                Thread.Sleep(400);
            }

            if (needsFocus && !Focus(window))
            {
                if (wasMinimized)
                    ShowWindow(window, SW_SHOWMINNOACTIVE);
                return;
            }

            Thread.Sleep(150);
            MarkNudge();
            Nudge();
            _lastActivity = DateTime.Now;
            App.Logger.WriteLine(LOG_IDENT, "Nudged an idle Roblox window" + (wasMinimized ? " (was minimized)" : ""));

            if (wasMinimized)
            {
                Thread.Sleep(100);
                ShowWindow(window, SW_SHOWMINNOACTIVE);
            }

            if (needsFocus && previous != IntPtr.Zero)
            {
                Thread.Sleep(100);
                Focus(previous);
            }
        }

        private static string NudgeFile => Path.Combine(Paths.Base, "LastNudge.txt");

        private static void MarkNudge()
        {
            try { File.WriteAllText(NudgeFile, DateTime.Now.ToString("o")); } catch { }
        }

        /// <summary>True for a few seconds after any window's anti-AFK nudge (it shows up as input to Windows).</summary>
        public static bool NudgedRecently()
        {
            try { return File.Exists(NudgeFile) && DateTime.Now - File.GetLastWriteTime(NudgeFile) < TimeSpan.FromSeconds(5); }
            catch { return false; }
        }

        private static void Nudge()
        {
            SendMouse(MOUSEEVENTF_MOVE, 1, 0);
            Thread.Sleep(30);
            SendMouse(MOUSEEVENTF_MOVE, -1, 0);
            Thread.Sleep(30);
            SendMouse(MOUSEEVENTF_RIGHTDOWN, 0, 0);
            Thread.Sleep(40);
            SendMouse(MOUSEEVENTF_RIGHTUP, 0, 0);
        }

        private static double SecondsSinceLastInput()
        {
            var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
            return GetLastInputInfo(ref info) ? (Environment.TickCount - (int)info.dwTime) / 1000.0 : 0;
        }

        private static bool Focus(IntPtr window)
        {
            IntPtr foreground = GetForegroundWindow();
            uint foregroundThread = GetWindowThreadProcessId(foreground, out _);
            uint thisThread = GetCurrentThreadId();
            bool attached = foregroundThread != thisThread && AttachThreadInput(thisThread, foregroundThread, true);

            try
            {
                BringWindowToTop(window);
                SetForegroundWindow(window);
            }
            finally
            {
                if (attached)
                    AttachThreadInput(thisThread, foregroundThread, false);
            }

            return GetForegroundWindow() == window;
        }

        #region Native
        private const uint MOUSEEVENTF_MOVE = 0x0001, MOUSEEVENTF_RIGHTDOWN = 0x0008, MOUSEEVENTF_RIGHTUP = 0x0010;

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT { public uint type; public MOUSEINPUT mi; } // 40 bytes on x64, as SendInput expects

        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

        private static void SendMouse(uint flags, int dx, int dy)
        {
            var input = new INPUT { type = 0, mi = new MOUSEINPUT { dx = dx, dy = dy, dwFlags = flags } };
            SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        }

        [DllImport("user32.dll")] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
        [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
        private const int SW_RESTORE = 9, SW_SHOWMINNOACTIVE = 7;
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint attach, uint attachTo, bool doAttach);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        #endregion
    }
}
