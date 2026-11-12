namespace Bloxstrap.Models.Persistable
{
    /// <summary>One key/mouse button remapped to a key, only while Roblox is focused.</summary>
    public class KeyRemap
    {
        /// <summary>A key name (E, Space, F1...) or a mouse button (MouseLeft, MouseRight, MouseMiddle, Mouse4, Mouse5).</summary>
        public string From { get; set; } = "";

        /// <summary>Same names as From.</summary>
        public string To { get; set; } = "";
    }

    /// <summary>Settings applied when a specific game is launched. Null/0 = use the normal setting.</summary>
    public class GameProfile
    {
        public string Name { get; set; } = "";

        public int FramerateCap { get; set; } = 0;

        public bool? PotatoMode { get; set; } = null;

        /// <summary>Mod profile to switch to for this game; empty = leave mods as they are.</summary>
        public string ModProfile { get; set; } = "";
    }

    /// <summary>PSYstrap everyday-gameplay options, stored in Settings.json under "Gameplay".</summary>
    public class GameplaySettings
    {
        public bool AutoRejoin { get; set; } = true;

        public bool AntiAfk { get; set; } = false;

        public int AntiAfkMinutes { get; set; } = 15;

        public bool MuteBackgroundWindows { get; set; } = true;

        public List<KeyRemap> Remaps { get; set; } = new();

        /// <summary>Per-game profiles, keyed by place ID.</summary>
        public Dictionary<long, GameProfile> GameProfiles { get; set; } = new();

        public bool FriendAlerts { get; set; } = false;

        /// <summary>Saved account whose friends are watched. 0 = first saved account.</summary>
        public long FriendAlertsUserId { get; set; } = 0;

        /// <summary>Your own Discord application ID. Empty = Bloxstrap's.</summary>
        public string DiscordAppId { get; set; } = "";

        public bool DiscordShowServerInfo { get; set; } = true;

        /// <summary>Games shown on Discord as just "Roblox" (universe IDs).</summary>
        public List<long> DiscordHiddenGames { get; set; } = new();

        /// <summary>With several windows, Discord shows the one you used last (instead of the first one opened).</summary>
        public bool DiscordFollowFocusedWindow { get; set; } = true;

        // ---- added in 2.14 ----

        /// <summary>Close RobloxCrashHandler.exe as soon as it starts (one less process per window).</summary>
        public bool DisableCrashHandler { get; set; } = true;

        /// <summary>Minimizing Roblox hides it from the taskbar; click the PSYstrap tray icon to get it back.</summary>
        public bool MinimizeToTray { get; set; } = false;

        /// <summary>Reopen Roblox in the same game if it crashes or freezes.</summary>
        public bool CrashWatchdog { get; set; } = true;

        /// <summary>Popup with time played etc. when you leave a game.</summary>
        public bool SessionRecap { get; set; } = true;

        /// <summary>Offer to join when you copy a Roblox game or private server link.</summary>
        public bool ClipboardJoin { get; set; } = false;

        /// <summary>Move Roblox screenshots into a folder per game.</summary>
        public bool OrganiseScreenshots { get; set; } = false;

        public BoosterSettings Booster { get; set; } = new();

        public WebhookSettings Webhook { get; set; } = new();
    }

    /// <summary>High performance power plan (and closing apps you pick) while Roblox runs.</summary>
    public class BoosterSettings
    {
        public bool Enabled { get; set; } = false;

        public bool HighPerformancePower { get; set; } = true;

        /// <summary>Process names (no .exe) to close while Roblox runs.</summary>
        public List<string> CloseApps { get; set; } = new();
    }

    /// <summary>Messages to a Discord webhook (shows up on your phone).</summary>
    public class WebhookSettings
    {
        public string Url { get; set; } = "";

        public bool OnDisconnect { get; set; } = true;

        public bool OnRejoin { get; set; } = true;

        public bool OnCrash { get; set; } = true;

        public bool OnSmartMacro { get; set; } = false;

        public bool OnFriendJoin { get; set; } = false;

        public bool OnSchedule { get; set; } = true;
    }

    /// <summary>A pinned game or private server, optionally opened as one of the saved accounts.</summary>
    public class FavouriteGame
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];

        public string Name { get; set; } = "";

        public long PlaceId { get; set; }

        /// <summary>Private server link code (from a ...?privateServerLinkCode= link), empty for a public game.</summary>
        public string PrivateServerLinkCode { get; set; } = "";

        /// <summary>Saved account to open it as; 0 = whoever is signed in to Roblox.</summary>
        public long AccountUserId { get; set; }

        /// <summary>Open this when PSYstrap starts with Windows.</summary>
        public bool OpenAtStartup { get; set; }
    }

    /// <summary>Open a favourite at a time of day, and optionally close it after a while.</summary>
    public class ScheduledLaunch
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];

        public string FavouriteId { get; set; } = "";

        /// <summary>"HH:mm", local time.</summary>
        public string Time { get; set; } = "08:00";

        /// <summary>Close it after this many minutes; 0 = leave it open.</summary>
        public int StopAfterMinutes { get; set; }

        public bool Enabled { get; set; } = true;
    }

    /// <summary>PSYstrap launching options, stored in Settings.json under "Launching".</summary>
    public class LaunchingSettings
    {
        public List<FavouriteGame> Favourites { get; set; } = new();

        public List<ScheduledLaunch> Schedule { get; set; } = new();

        public bool StartWithWindows { get; set; } = false;
    }
}
