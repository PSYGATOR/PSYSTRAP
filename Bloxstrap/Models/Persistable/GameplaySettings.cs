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
    }
}
