using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Bloxstrap.Utility
{
    /// <summary>
    /// PSYstrap: game icons and account pictures from Roblox's public thumbnail API, cached in %LocalAppData%\PSYstrap\Icons.
    /// Game icons are kept for a week, account pictures for a day (people change avatars).
    /// </summary>
    public static class RobloxImages
    {
        private const string LOG_IDENT = "RobloxImages";

        private static string Folder => Path.Combine(Paths.Base, "Icons");

        private static readonly Regex PlaceIdPattern = new(@"placeId=(\d+)", RegexOptions.IgnoreCase);

        // one download per picture even when several things ask at once
        private static readonly Dictionary<string, Task<string?>> _pending = new();

        public static Task<string?> GameIconAsync(long placeId) =>
            GetAsync($"{placeId}.png", TimeSpan.FromDays(7),
                $"https://thumbnails.roblox.com/v1/places/gameicons?placeIds={placeId}&returnPolicy=PlaceHolder&size=256x256&format=Png&isCircular=false");

        public static Task<string?> AvatarAsync(long userId) =>
            GetAsync($"user-{userId}.png", TimeSpan.FromDays(1),
                $"https://thumbnails.roblox.com/v1/users/avatar-headshot?userIds={userId}&size=150x150&format=Png&isCircular=false");

        /// <summary>The game icon as an .ico (the 256px PNG inside an ICO container), for shortcuts and window icons.</summary>
        public static async Task<string?> GameIconIcoAsync(long placeId)
        {
            string? png = await GameIconAsync(placeId);

            if (png is null)
                return null;

            string ico = Path.ChangeExtension(png, ".ico");

            try
            {
                if (File.Exists(ico) && File.GetLastWriteTimeUtc(ico) >= File.GetLastWriteTimeUtc(png))
                    return ico;

                byte[] data = await File.ReadAllBytesAsync(png);

                using var stream = File.Create(ico);
                using var writer = new BinaryWriter(stream);
                writer.Write((ushort)0);            // reserved
                writer.Write((ushort)1);            // icon
                writer.Write((ushort)1);            // one image
                writer.Write((byte)0);              // 256 wide
                writer.Write((byte)0);              // 256 high
                writer.Write((byte)0);              // no palette
                writer.Write((byte)0);
                writer.Write((ushort)1);            // planes
                writer.Write((ushort)32);           // bits per pixel
                writer.Write((uint)data.Length);
                writer.Write((uint)22);             // image data right after the 6 + 16 byte header
                writer.Write(data);

                return ico;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Couldn't make {ico}: {ex.Message}");
                return File.Exists(ico) ? ico : null;
            }
        }

        private static readonly Dictionary<long, string> _usernames = new();

        /// <summary>A Roblox username from the public profile (saved accounts first, so no request for those).</summary>
        public static async Task<string?> UsernameAsync(long userId)
        {
            if (userId <= 0)
                return null;

            lock (_usernames)
                if (_usernames.TryGetValue(userId, out string? known))
                    return known;

            string? name = Accounts.AccountStore.Load().FirstOrDefault(a => a.UserId == userId)?.Username;

            if (name is null)
            {
                try
                {
                    using var user = JsonDocument.Parse(await App.HttpClient.GetStringAsync($"https://users.roblox.com/v1/users/{userId}"));
                    name = user.RootElement.GetProperty("name").GetString();
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"No name for user {userId}: {ex.Message}");
                }
            }

            if (name is not null)
                lock (_usernames)
                    _usernames[userId] = name;

            return name;
        }

        /// <summary>Place ID in a Roblox launch (roblox-player: or roblox:// link), or 0.</summary>
        public static long PlaceIdFromLaunchArgs(string? args)
        {
            if (String.IsNullOrEmpty(args))
                return 0;

            try { args = Uri.UnescapeDataString(args); } catch { }

            var match = PlaceIdPattern.Match(args);
            return match.Success && long.TryParse(match.Groups[1].Value, out long placeId) ? placeId : 0;
        }

        /// <summary>A frozen image for WPF, loaded fully so the file isn't kept open. Null if it can't be read.</summary>
        public static ImageSource? Load(string? path, int decodeWidth = 0)
        {
            if (path is null || !File.Exists(path))
                return null;

            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                image.UriSource = new Uri(path);

                if (decodeWidth > 0)
                    image.DecodePixelWidth = decodeWidth;

                image.EndInit();
                image.Freeze();
                return image;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Couldn't load {path}: {ex.Message}");
                return null;
            }
        }

        private static Task<string?> GetAsync(string fileName, TimeSpan keepFor, string apiUrl)
        {
            lock (_pending)
            {
                if (_pending.TryGetValue(fileName, out var running))
                    return running;

                var task = DownloadAsync(fileName, keepFor, apiUrl);
                _pending[fileName] = task;
                task.ContinueWith(_ => { lock (_pending) _pending.Remove(fileName); });
                return task;
            }
        }

        private static async Task<string?> DownloadAsync(string fileName, TimeSpan keepFor, string apiUrl)
        {
            string path = Path.Combine(Folder, fileName);

            try
            {
                if (File.Exists(path) && DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < keepFor)
                    return path;

                using var info = JsonDocument.Parse(await App.HttpClient.GetStringAsync(apiUrl));
                var entry = info.RootElement.GetProperty("data")[0];

                // still being generated on Roblox's side: use the old copy if there is one
                if (entry.TryGetProperty("state", out var state) && state.GetString() != "Completed")
                    return File.Exists(path) ? path : null;

                string? url = entry.GetProperty("imageUrl").GetString();

                if (String.IsNullOrEmpty(url))
                    return File.Exists(path) ? path : null;

                byte[] png = await App.HttpClient.GetByteArrayAsync(url);
                Directory.CreateDirectory(Folder);

                // write next to it first, so a half-written file never gets used
                string temp = path + ".tmp";
                await File.WriteAllBytesAsync(temp, png);
                File.Move(temp, path, true);

                return path;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"No picture for {fileName}: {ex.Message}");
                return File.Exists(path) ? path : null;
            }
        }
    }
}
