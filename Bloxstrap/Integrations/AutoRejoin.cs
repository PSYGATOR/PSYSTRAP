namespace Bloxstrap.Integrations
{
    /// <summary>
    /// PSYstrap: when Roblox drops you from a server (connection lost, server shut down...), opens it again in the same
    /// server. It reads Roblox's log to tell that apart from you leaving, closing Roblox, teleporting, or being kicked or
    /// banned (it never rejoins those). At most 3 rejoins every 10 minutes, so it can't loop.
    /// </summary>
    public sealed class AutoRejoin
    {
        private const string LOG_IDENT = "AutoRejoin";

        // disconnect reasons that mean "don't come back": you left (285), kicked (267), kicked for unexpected
        // client behaviour (268), same account joined elsewhere (264, 273), banned (600)
        private static readonly HashSet<int> NoRejoinReasons = new() { 264, 267, 268, 273, 285, 600 };

        private static readonly Regex[] ReasonPatterns =
        {
            new(@"Sending disconnect with reason: (\d+)"),
            new(@"Disconnection Notification\.? Reason: ?(\d+)", RegexOptions.IgnoreCase),
            new(@"\(ID = (\d+)\)"),
            new(@"Error Code: ?(\d+)", RegexOptions.IgnoreCase)
        };

        // Roblox mostly logs the reason as text ("Client has been disconnected with reason: ...")
        private static readonly Regex[] TextReasonPatterns =
        {
            new(@"disconnected with reason: ?(.+)$", RegexOptions.IgnoreCase),
            new(@"Lost connection with reason ?: ?(.+)$", RegexOptions.IgnoreCase)
        };

        private static readonly Regex NoRejoinText = new(@"another device|different device|kick|bann?ed|unexpected client|exploit|you left", RegexOptions.IgnoreCase);

        private static readonly Regex ServerGoneText = new(@"shut ?down|restart|server is full|no longer available", RegexOptions.IgnoreCase);

        private static string HistoryFile => Path.Combine(Paths.Base, "Rejoins.txt");

        private readonly ActivityWatcher _activity;
        private readonly int _robloxPid;

        private DateTime _userLeftAt = DateTime.MinValue;
        private int? _lastReason;
        private string? _lastReasonText;
        private DateTime _lastReasonAt = DateTime.MinValue;

        public AutoRejoin(ActivityWatcher activity, int robloxPid)
        {
            _activity = activity;
            _robloxPid = robloxPid;

            activity.OnLogEntry += (_, line) => Inspect(line);
            activity.OnGameJoin += (_, _) => { _lastReason = null; _lastReasonText = null; };
            activity.OnGameLeave += (_, _) => _ = OnGameLeaveAsync();
            activity.OnAppClose += (_, _) => _userLeftAt = DateTime.Now;
        }

        private void Inspect(string line)
        {
            if (line.Contains("leaveUGCGameInternal", StringComparison.Ordinal))
            {
                _userLeftAt = DateTime.Now;
                return;
            }

            foreach (var pattern in TextReasonPatterns)
            {
                var match = pattern.Match(line);

                if (!match.Success)
                    continue;

                string text = match.Groups[1].Value.Trim();

                if (text != _lastReasonText)
                    App.Logger.WriteLine(LOG_IDENT, $"Disconnect reason: {text.Substring(0, Math.Min(200, text.Length))}");

                _lastReasonText = text;
                _lastReasonAt = DateTime.Now;
                return;
            }

            foreach (var pattern in ReasonPatterns)
            {
                var match = pattern.Match(line);

                if (!match.Success || !int.TryParse(match.Groups[1].Value, out int reason))
                    continue;

                _lastReason = reason;
                _lastReasonAt = DateTime.Now;

                // keep the raw line - these formats aren't documented, so the log is how to tune this later
                App.Logger.WriteLine(LOG_IDENT, $"Disconnect reason {reason}: {line.Substring(0, Math.Min(200, line.Length))}");

                if (reason == 285)
                    _userLeftAt = DateTime.Now;

                return;
            }
        }

        private async Task OnGameLeaveAsync()
        {
            if (!App.Settings.Prop.Gameplay.AutoRejoin)
                return;

            // the watcher has already cleared Data by now; the game we just left is the newest history entry
            var data = _activity.History.FirstOrDefault();

            if (data is null)
                return;

            long placeId = data.PlaceId;
            string serverId = data.JobId;
            string accessCode = data.AccessCode;
            bool isPrivate = data.ServerType == ServerType.Private;
            long userId = data.UserId;

            if (placeId == 0)
                return;

            // give Roblox a moment: closing the window, a teleport or leaving to the menu all show up in this time
            await Task.Delay(TimeSpan.FromSeconds(6));

            if ((DateTime.Now - _userLeftAt).TotalSeconds < 20)
            {
                App.Logger.WriteLine(LOG_IDENT, "You left the game yourself, not rejoining");
                return;
            }

            if (_lastReason is int reason && (DateTime.Now - _lastReasonAt).TotalSeconds < 30 && NoRejoinReasons.Contains(reason))
            {
                App.Logger.WriteLine(LOG_IDENT, $"Disconnect reason {reason} means kicked/left/banned, not rejoining");
                return;
            }

            bool recentText = _lastReasonText is not null && (DateTime.Now - _lastReasonAt).TotalSeconds < 30;

            if (recentText && NoRejoinText.IsMatch(_lastReasonText!))
            {
                App.Logger.WriteLine(LOG_IDENT, $"Not rejoining: {_lastReasonText}");
                return;
            }

            // that server is gone, so any server of the same game
            if (recentText && ServerGoneText.IsMatch(_lastReasonText!) && !isPrivate)
                serverId = "";

            if (_activity.InGame)
            {
                App.Logger.WriteLine(LOG_IDENT, "Back in a game already (teleport), not rejoining");
                return;
            }

            if (!Utilities.GetProcessesSafe().Any(process => process.Id == _robloxPid))
            {
                App.Logger.WriteLine(LOG_IDENT, "Roblox was closed, not rejoining");
                return;
            }

            if (!RecordAttempt())
            {
                App.Logger.WriteLine(LOG_IDENT, "Already rejoined 3 times in 10 minutes, giving up");
                return;
            }

            string why = _lastReasonText ?? _lastReason?.ToString() ?? "unknown";
            App.Logger.WriteLine(LOG_IDENT, $"Lost the server ({why}), rejoining {placeId}/{(serverId.Length > 0 ? serverId : "any server")}");
            Accounts.ClientRejoin.RejoinServer(_robloxPid, placeId, serverId, accessCode, isPrivate, userId);
        }

        /// <summary>Keeps a small history across watcher restarts (each rejoin starts a new watcher).</summary>
        private static bool RecordAttempt()
        {
            try
            {
                var cutoff = DateTime.Now.AddMinutes(-10);
                var recent = File.Exists(HistoryFile)
                    ? File.ReadAllLines(HistoryFile)
                        .Select(line => DateTime.TryParse(line, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time) ? time : DateTime.MinValue)
                        .Where(time => time > cutoff)
                        .ToList()
                    : new List<DateTime>();

                if (recent.Count >= 3)
                    return false;

                recent.Add(DateTime.Now);
                File.WriteAllLines(HistoryFile, recent.Select(time => time.ToString("o", CultureInfo.InvariantCulture)));
                return true;
            }
            catch
            {
                return true;
            }
        }
    }
}
