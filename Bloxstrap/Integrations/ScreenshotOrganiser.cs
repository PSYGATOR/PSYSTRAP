using System.Runtime.InteropServices;

namespace Bloxstrap.Integrations
{
    /// <summary>
    /// PSYstrap: moves new Roblox screenshots (Pictures\Roblox) into a folder named after the game you were playing.
    /// With several windows open, the game is the one in the window that had focus when the picture was taken.
    /// Only one watcher does this at a time.
    /// </summary>
    public sealed class ScreenshotOrganiser : IDisposable
    {
        private const string LOG_IDENT = "ScreenshotOrganiser";

        private readonly InterProcessLock _lock;
        private readonly FileSystemWatcher? _watcher;
        private readonly Dictionary<long, string> _names = new();

        public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Roblox");

        public ScreenshotOrganiser()
        {
            _lock = new InterProcessLock("Screenshots");

            if (!_lock.IsAcquired || !Directory.Exists(Folder))
                return;

            _watcher = new FileSystemWatcher(Folder) { IncludeSubdirectories = false, EnableRaisingEvents = true };
            _watcher.Created += (_, e) => _ = MoveAsync(e.FullPath);
        }

        private async Task MoveAsync(string path)
        {
            if (!App.Settings.Prop.Gameplay.OrganiseScreenshots)
                return;

            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is not (".png" or ".jpg" or ".jpeg"))
                return;

            // which game: the focused Roblox window's, from the sessions every watcher publishes
            GetWindowThreadProcessId(GetForegroundWindow(), out uint focusedPid);
            var sessions = Accounts.LiveSessions.List();
            var session = sessions.FirstOrDefault(s => s.ProcessId == focusedPid) ?? (sessions.Count == 1 ? sessions[0] : null);

            if (session is null || session.PlaceId == 0)
                return;

            if (!_names.TryGetValue(session.PlaceId, out string? name))
            {
                name = await GameProfiles.GetGameNameAsync(session.PlaceId) ?? $"Place {session.PlaceId}";
                name = String.Concat(name.Split(Path.GetInvalidFileNameChars())).Trim().TrimEnd('.');
                _names[session.PlaceId] = name;
            }

            string targetFolder = Path.Combine(Folder, name);
            Directory.CreateDirectory(targetFolder);
            string target = Path.Combine(targetFolder, Path.GetFileName(path));

            // Roblox may still be writing it
            for (int attempt = 0; attempt < 10; attempt++)
            {
                await Task.Delay(500);

                try
                {
                    File.Move(path, target, overwrite: false);
                    App.Logger.WriteLine(LOG_IDENT, $"Moved a screenshot into '{name}'");
                    return;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        public void Dispose()
        {
            _watcher?.Dispose();
            // the lock has to be released on the thread that took it; if not, Windows frees it when the process ends
            try { _lock.Dispose(); } catch (ApplicationException) { }
        }

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    }
}
