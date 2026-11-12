using Bloxstrap.Accounts;

namespace Bloxstrap.Integrations
{
    /// <summary>
    /// PSYstrap: pops up a notification when one of your friends starts playing a game, with a click to join them.
    /// Reads friends and presence with one of your saved accounts, once a minute. With several Roblox windows open only
    /// one of them does the checking (whoever holds the lock), so you don't get the same alert three times.
    /// </summary>
    public sealed class FriendAlerts : IDisposable
    {
        private const string LOG_IDENT = "FriendAlerts";

        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan FriendListRefresh = TimeSpan.FromMinutes(10);

        private readonly Action<string, string, EventHandler?> _showAlert;
        private readonly CancellationTokenSource _cancel = new();
        private readonly Thread _thread;

        // what each friend was last seen doing: universe (or place) ID when in a game, 0 otherwise
        private readonly Dictionary<long, long> _lastGame = new();
        private Dictionary<long, string> _names = new();
        private List<long> _friends = new();
        private DateTime _friendsLoaded = DateTime.MinValue;
        private long _accountId;

        public FriendAlerts(Action<string, string, EventHandler?> showAlert)
        {
            _showAlert = showAlert;
            _thread = new Thread(Loop) { IsBackground = true, Name = "PSYstrap friend alerts" };
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
                    if (App.Settings.Prop.Gameplay.FriendAlerts)
                    {
                        owner ??= new InterProcessLock("FriendAlerts");

                        if (owner.IsAcquired)
                        {
                            try { Poll().GetAwaiter().GetResult(); }
                            catch (Exception ex) { App.Logger.WriteException(LOG_IDENT, ex); }
                        }
                        else
                        {
                            owner.Mutex.Dispose();
                            owner = null; // another window is doing it; try again next time
                        }
                    }
                    else if (owner is not null)
                    {
                        owner.Dispose();
                        owner = null;
                        _lastGame.Clear();
                    }

                    token.WaitHandle.WaitOne(PollInterval);
                }
            }
            finally
            {
                owner?.Dispose(); // the mutex has to be released by the thread that took it
            }
        }

        private async Task Poll()
        {
            var accounts = AccountStore.Load();
            long wanted = App.Settings.Prop.Gameplay.FriendAlertsUserId;
            var account = accounts.FirstOrDefault(a => a.UserId == wanted) ?? accounts.FirstOrDefault();

            if (account is null || AccountStore.GetSession(account) is not string session)
                return;

            if (account.UserId != _accountId)
            {
                // different account: start over so its friends aren't all "just joined"
                _accountId = account.UserId;
                _friendsLoaded = DateTime.MinValue;
                _lastGame.Clear();
            }

            if (DateTime.Now - _friendsLoaded > FriendListRefresh)
            {
                var ids = await RobloxAuth.GetFriendIdsAsync(session, account.UserId);

                if (ids is null)
                {
                    App.Logger.WriteLine(LOG_IDENT, "Couldn't read the friends list (session expired?)");
                    return;
                }

                // your own alts on the list aren't worth an alert
                var own = accounts.Select(a => a.UserId).ToHashSet();
                _friends = ids.Where(id => !own.Contains(id)).ToList();
                _names = await RobloxAuth.GetDisplayNamesAsync(session, _friends);
                _friendsLoaded = DateTime.Now;

                App.Logger.WriteLine(LOG_IDENT, $"Watching {_friends.Count} friends of account {account.UserId}");
            }

            if (_friends.Count == 0)
                return;

            var presences = await RobloxAuth.GetPresenceAsync(session, _friends);

            if (presences is null)
                return;

            var joined = new List<RobloxAuth.FriendPresence>();

            foreach (var presence in presences)
            {
                long game = presence.Type == 2 ? (presence.UniverseId != 0 ? presence.UniverseId : Math.Max(presence.PlaceId, 1)) : 0;
                bool seen = _lastGame.TryGetValue(presence.UserId, out long before);
                _lastGame[presence.UserId] = game;

                // the first check only learns who's already playing
                if (seen && game != 0 && game != before)
                    joined.Add(presence);
            }

            if (joined.Count == 0)
                return;

            var first = joined[0];
            string name = _names.GetValueOrDefault(first.UserId, "A friend");
            string where = String.IsNullOrEmpty(first.Location) ? "a game" : first.Location;
            string caption = joined.Count == 1 ? $"{name} is playing {where}" : $"{name} and {joined.Count - 1} more friends just joined games";

            string message = first.PlaceId != 0
                ? $"Click to join {name}."
                : $"{name}'s joins are private, so you can't follow them in.";

            EventHandler? onClick = first.PlaceId == 0 ? null : (_, _) => JoinFriend(account, first);

            _showAlert(caption, message, onClick);
        }

        private static void JoinFriend(SavedAccount account, RobloxAuth.FriendPresence friend)
        {
            Task.Run(async () =>
            {
                try
                {
                    string? link = null;

                    if (AccountStore.GetSession(account) is string session && await RobloxAuth.GetLaunchTicketAsync(session) is string ticket)
                        link = RobloxAuth.BuildFollowLink(ticket, friend.UserId);

                    link ??= String.IsNullOrEmpty(friend.ServerId)
                        ? $"roblox://experiences/start?placeId={friend.PlaceId}"
                        : RobloxServers.GetJoinLink(friend.PlaceId, friend.ServerId);

                    App.Logger.WriteLine(LOG_IDENT, $"Joining friend {friend.UserId} in place {friend.PlaceId}");
                    Process.Start(Paths.Process, $"-player \"{link}\"");
                }
                catch (Exception ex)
                {
                    App.Logger.WriteException(LOG_IDENT, ex);
                }
            });
        }

        public void Dispose()
        {
            _cancel.Cancel();
            _thread.Join(3000);
        }
    }
}
