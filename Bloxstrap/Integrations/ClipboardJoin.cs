using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Bloxstrap.Integrations
{
    /// <summary>
    /// PSYstrap: when you copy a Roblox game, private server or share link while Roblox is open, a popup offers to
    /// join it. Uses Windows' clipboard change notification (no polling). Only one watcher listens at a time.
    /// </summary>
    public sealed class ClipboardJoin : IDisposable
    {
        private const string LOG_IDENT = "ClipboardJoin";
        private const int WM_CLIPBOARDUPDATE = 0x031D;

        private readonly Action<string, string, EventHandler?> _notify;
        private readonly InterProcessLock _lock;
        private readonly HwndSource? _window;
        private string _last = "";

        public ClipboardJoin(Action<string, string, EventHandler?> notify)
        {
            _notify = notify;
            _lock = new InterProcessLock("ClipboardJoin");

            if (!_lock.IsAcquired)
                return;

            // a message-only window to receive clipboard notifications on the UI thread
            _window = new HwndSource(new HwndSourceParameters("PSYstrap clipboard") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0 });
            _window.AddHook(WndProc);
            AddClipboardFormatListener(_window.Handle);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_CLIPBOARDUPDATE && App.Settings.Prop.Gameplay.ClipboardJoin)
            {
                try { OnClipboardChanged(); }
                catch (Exception ex) { App.Logger.WriteLine(LOG_IDENT, ex.Message); }
            }

            return IntPtr.Zero;
        }

        private void OnClipboardChanged()
        {
            if (!Clipboard.ContainsText())
                return;

            string text = Clipboard.GetText().Trim();

            if (text.Length > 500 || text == _last || !text.Contains("roblox.com", StringComparison.OrdinalIgnoreCase))
                return;

            _last = text;

            var share = Launching.SharePattern.Match(text);
            var parsed = Launching.ParseGameLink(text);

            if (share.Success)
            {
                string code = share.Groups[1].Value;
                _notify("Join this server?", "You copied a Roblox server link. Click to join it.",
                    (_, _) => Process.Start(Paths.Process, $"-player \"roblox://navigation/share_links?code={code}&type=Server\""));
            }
            else if (parsed is (long placeId, string linkCode))
            {
                bool isPrivate = linkCode.Length > 0;
                _notify(isPrivate ? "Join this private server?" : "Play this game?", "You copied a Roblox link. Click to open it.",
                    (_, _) => _ = Launching.LaunchAsync(new Models.Persistable.FavouriteGame { PlaceId = placeId, PrivateServerLinkCode = linkCode, Name = $"Place {placeId}" }));
            }
        }

        public void Dispose()
        {
            if (_window is not null)
            {
                RemoveClipboardFormatListener(_window.Handle);
                _window.Dispose();
            }

            // the lock has to be released on the thread that took it; if not, Windows frees it when the process ends
            try { _lock.Dispose(); } catch (ApplicationException) { }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool AddClipboardFormatListener(IntPtr window);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RemoveClipboardFormatListener(IntPtr window);
    }
}
