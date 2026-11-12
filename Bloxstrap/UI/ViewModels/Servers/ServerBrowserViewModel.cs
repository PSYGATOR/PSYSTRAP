using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;

using CommunityToolkit.Mvvm.Input;

using Bloxstrap.Accounts;

namespace Bloxstrap.UI.ViewModels.Servers
{
    /// <summary>PSYstrap: a game's public servers, and joining any of them as any saved account.</summary>
    public class ServerBrowserViewModel : NotifyPropertyChangedViewModel
    {
        public class ServerRow
        {
            public string Id { get; init; } = "";
            public int Playing { get; init; }
            public int MaxPlayers { get; init; }
            public int? AveragePing { get; init; }

            public string Players => $"{Playing}/{MaxPlayers}";
            public string AveragePingText => AveragePing is int ping ? $"{ping} ms" : "-";
            public bool Full => Playing >= MaxPlayers;
        }

        public record AccountChoice(long UserId, string Label);

        public ObservableCollection<ServerRow> Servers { get; } = new();

        public ICollectionView ServersView { get; }

        public ObservableCollection<AccountChoice> Accounts { get; } = new();

        public IReadOnlyList<string> SortChoices { get; } = new[] { "Most players", "Fewest players", "Lowest players' ping" };

        public ServerBrowserViewModel(long placeId)
        {
            ServersView = CollectionViewSource.GetDefaultView(Servers);
            ServersView.Filter = item => item is ServerRow row && (!HideFull || !row.Full);

            Accounts.Add(new(0, "Whoever is signed in to Roblox"));
            foreach (var account in AccountStore.Load())
                Accounts.Add(new(account.UserId, account.Label));

            _placeText = placeId > 0 ? placeId.ToString() : "";

            if (placeId > 0)
                _ = LoadAsync();
        }

        #region Bound properties
        private string _placeText;
        public string PlaceText { get => _placeText; set { _placeText = value; OnPropertyChanged(nameof(PlaceText)); } }

        private string _gameName = "";
        public string GameName { get => _gameName; private set { _gameName = value; OnPropertyChanged(nameof(GameName)); } }

        private string _status = "Paste a game link or place ID and press Load.";
        public string Status { get => _status; private set { _status = value; OnPropertyChanged(nameof(Status)); } }

        private long _accountUserId;
        public long AccountUserId { get => _accountUserId; set { _accountUserId = value; OnPropertyChanged(nameof(AccountUserId)); } }

        private bool _hideFull = true;
        public bool HideFull { get => _hideFull; set { _hideFull = value; OnPropertyChanged(nameof(HideFull)); ServersView.Refresh(); } }

        private string _sort = "Most players";
        public string Sort { get => _sort; set { _sort = value; OnPropertyChanged(nameof(Sort)); ApplySort(); } }

        private bool _busy;
        public bool Busy { get => _busy; private set { _busy = value; OnPropertyChanged(nameof(Busy)); OnPropertyChanged(nameof(NotBusy)); } }
        public bool NotBusy => !Busy;

        private long _placeId;
        #endregion

        public ICommand LoadCommand => new AsyncRelayCommand(LoadAsync);

        public ICommand JoinCommand => new RelayCommand<ServerRow>(row => { if (row is not null) _ = JoinAsync(row); });

        private void ApplySort()
        {
            ServersView.SortDescriptions.Clear();

            switch (Sort)
            {
                case "Fewest players":
                    ServersView.SortDescriptions.Add(new SortDescription(nameof(ServerRow.Playing), ListSortDirection.Ascending));
                    break;
                case "Lowest players' ping":
                    ServersView.SortDescriptions.Add(new SortDescription(nameof(ServerRow.AveragePing), ListSortDirection.Ascending));
                    break;
                default:
                    ServersView.SortDescriptions.Add(new SortDescription(nameof(ServerRow.Playing), ListSortDirection.Descending));
                    break;
            }
        }

        private async Task LoadAsync()
        {
            if (Launching.ParseGameLink(PlaceText) is not (long placeId, _))
            {
                Status = "That doesn't look like a place ID or game link.";
                return;
            }

            _placeId = placeId;
            Busy = true;
            Status = "Getting the server list...";
            Servers.Clear();

            GameName = await GameProfiles.GetGameNameAsync(placeId) ?? $"Place {placeId}";
            var servers = await RobloxServers.ListAsync(placeId);

            foreach (var server in servers)
                Servers.Add(new ServerRow { Id = server.Id, Playing = server.Playing, MaxPlayers = server.MaxPlayers, AveragePing = server.AveragePing });

            ApplySort();
            Busy = false;
            Status = servers.Count == 0
                ? "No public servers found (or Roblox didn't answer)."
                : $"{servers.Count} servers. \"Players' ping\" is the average ping of the people already in that server, so a low one usually means it's close to where most of them live.";
        }

        private async Task JoinAsync(ServerRow row)
        {
            string? link = null;

            if (AccountUserId != 0)
            {
                var account = AccountStore.Load().FirstOrDefault(a => a.UserId == AccountUserId);

                if (account is not null && AccountStore.GetSession(account) is string session && await RobloxAuth.GetLaunchTicketAsync(session) is string ticket)
                    link = RobloxAuth.BuildLaunchLink(ticket, _placeId, row.Id);
            }

            link ??= RobloxServers.GetJoinLink(_placeId, row.Id);
            Process.Start(Paths.Process, $"-player \"{link}\"");
            Status = $"Joining a server with {row.Players} players...";
        }
    }
}
