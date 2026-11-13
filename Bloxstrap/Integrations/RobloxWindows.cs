using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Bloxstrap.Integrations
{
    /// <summary>PSYstrap: things done to Roblox windows from outside: screenshots, arranging, minimizing, screen off.</summary>
    public static class RobloxWindows
    {
        public static IntPtr GetWindow(int processId)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                return process.MainWindowHandle;
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        /// <summary>
        /// A JPEG of the window (at most 960 px wide), drawn by the window itself so it works when it's covered.
        /// Null when minimized or gone.
        /// </summary>
        public static byte[]? Capture(int processId)
        {
            IntPtr window = GetWindow(processId);

            if (window == IntPtr.Zero || IsIconic(window) || !GetWindowRect(window, out var rect))
                return null;

            int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;

            if (width < 50 || height < 50)
                return null;

            try
            {
                using var full = new Bitmap(width, height, PixelFormat.Format32bppArgb);

                using (var graphics = Graphics.FromImage(full))
                {
                    IntPtr hdc = graphics.GetHdc();
                    try { PrintWindow(window, hdc, PW_RENDERFULLCONTENT); }
                    finally { graphics.ReleaseHdc(hdc); }
                }

                double scale = Math.Min(1, 960.0 / width);
                using var small = new Bitmap(full, Math.Max(1, (int)(width * scale)), Math.Max(1, (int)(height * scale)));
                using var stream = new MemoryStream();

                var jpeg = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                var quality = new EncoderParameters(1) { Param = { [0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 80L) } };
                small.Save(stream, jpeg, quality);

                return stream.ToArray();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("RobloxWindows::Capture", ex.Message);
                return null;
            }
        }

        /// <summary>Tiles every Roblox window over the main screen.</summary>
        public static void ArrangeAll()
        {
            var windows = MultiInstance.GetRobloxProcesses()
                .OrderBy(process => MultiInstance.GetSlot(process.Id))
                .Select(process => process.MainWindowHandle)
                .Where(handle => handle != IntPtr.Zero)
                .ToList();

            if (windows.Count == 0)
                return;

            var area = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
            int columns = (int)Math.Ceiling(Math.Sqrt(windows.Count));
            int rows = (int)Math.Ceiling(windows.Count / (double)columns);
            int width = area.Width / columns;
            int height = area.Height / rows;

            for (int i = 0; i < windows.Count; i++)
            {
                ShowWindow(windows[i], SW_RESTORE);
                MoveWindow(windows[i], area.Left + (i % columns) * width, area.Top + (i / columns) * height, width, height, true);
            }
        }

        /// <summary>Minimizes every Roblox window that isn't already; returns the ones it minimized.</summary>
        public static List<IntPtr> MinimizeAll()
        {
            var minimized = new List<IntPtr>();

            foreach (var process in MultiInstance.GetRobloxProcesses())
            {
                IntPtr window = process.MainWindowHandle;

                if (window != IntPtr.Zero && !IsIconic(window))
                {
                    ShowWindow(window, SW_SHOWMINNOACTIVE);
                    minimized.Add(window);
                }
            }

            return minimized;
        }

        /// <summary>Restores windows without stealing focus from what you're doing.</summary>
        public static void Restore(IEnumerable<IntPtr> windows)
        {
            foreach (var window in windows)
            {
                if (IsWindow(window) && IsIconic(window))
                    ShowWindow(window, SW_SHOWNOACTIVATE);
            }
        }

        public static bool IsMinimized(IntPtr window) => window != IntPtr.Zero && IsIconic(window);

        /// <summary>Brings a Roblox window to the front (restoring it if it's minimized).</summary>
        public static void Focus(int processId)
        {
            IntPtr window = GetWindow(processId);

            if (window == IntPtr.Zero)
                return;

            if (IsIconic(window))
                ShowWindow(window, SW_RESTORE);

            SetForegroundWindow(window);
        }

        /// <summary>Turns the screen off (moving the mouse turns it back on). Roblox keeps running.</summary>
        public static void ScreenOff() => PostMessage(new IntPtr(0xFFFF), WM_SYSCOMMAND, new IntPtr(SC_MONITORPOWER), new IntPtr(2));

        #region Native
        private const uint PW_RENDERFULLCONTENT = 2;
        private const int SW_RESTORE = 9, SW_SHOWMINNOACTIVE = 7, SW_SHOWNOACTIVATE = 4;
        private const uint WM_SYSCOMMAND = 0x0112;
        private const int SC_MONITORPOWER = 0xF170;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr window, IntPtr hdc, uint flags);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out RECT rect);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] private static extern bool MoveWindow(IntPtr window, int x, int y, int width, int height, bool repaint);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        #endregion
    }
}
