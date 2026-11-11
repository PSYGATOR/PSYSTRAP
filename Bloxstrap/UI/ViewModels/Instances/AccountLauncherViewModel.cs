using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

using CommunityToolkit.Mvvm.Input;

using Bloxstrap.Accounts;

namespace Bloxstrap.UI.ViewModels.Instances
{
    public class AccountRowViewModel : NotifyPropertyChangedViewModel
    {
        public SavedAccount Account { get; }

        public bool IsSelected { get; set; }

        public string Label => Account.Label;

        public string Details { get; }

        public ICommand RemoveCommand { get; }

        public AccountRowViewModel(SavedAccount account, Action<AccountRowViewModel> remove)
        {
            Account = account;
            Details = $"User ID {account.UserId}  ·  saved {account.Added:d}";
            RemoveCommand = new RelayCommand(() => remove(this));
        }
    }

    public class LaunchTargetViewModel
    {
        public enum TargetKind { Server, SmallestServer, AnyServer }

        public TargetKind Kind { get; init; }

        public LiveSessions.Session? Session { get; init; }

        public string Label { get; init; } = "";

        public bool NeedsPlaceId => Kind != TargetKind.Server;
    }

    /// <summary>
    /// PSYstrap saved accounts: add them by signing in on Roblox's page, then open several at once - each in its own
    /// client - straight into a running client's server, the emptiest server of a game, or any server of a game.
    /// </summary>
    public class AccountLauncherViewModel : NotifyPropertyChangedViewModel
    {
        private const string LOG_IDENT = "AccountLauncher";

        // gap between launches so each client is up before the next starts
        private static readonly TimeSpan LaunchGap = TimeSpan.FromSeconds(12);

        private bool _launching;

        public ObservableCollection<AccountRowViewModel> Accounts { get; } = new();

        public ObservableCollection<LaunchTargetViewModel> Targets { get; } = new();

        private LaunchTargetViewModel? _selectedTarget;

        public LaunchTargetViewModel? SelectedTarget
        {
            get => _selectedTarget;
            set
            {
                _selectedTarget = value;
                OnPropertyChanged(nameof(SelectedTarget));
                OnPropertyChanged(nameof(PlaceIdVisibility));
            }
        }

        public string PlaceIdText { get; set; } = "";

        public Visibility PlaceIdVisibility => SelectedTarget?.NeedsPlaceId == true ? Visibility.Visible : Visibility.Collapsed;

        public Visibility EmptyVisibility => Accounts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        public string Status { get; private set; } = "";

        public ICommand AddAccountCommand => new RelayCommand(AddAccount);

        public ICommand LaunchCommand => new AsyncRelayCommand(LaunchSelectedAsync);

        public ICommand SelectAllCommand => new RelayCommand(() =>
        {
            bool select = Accounts.Any(row => !row.IsSelected);

            foreach (var row in Accounts)
            {
                row.IsSelected = select;
                row.OnPropertyChanged(nameof(AccountRowViewModel.IsSelected));
            }
        });

        public AccountLauncherViewModel()
        {
            ReloadAccounts();
            RefreshTargets();
        }

        public void ReloadAccounts()
        {
            Accounts.Clear();

            foreach (var account in AccountStore.Load().OrderBy(account => account.Username, StringComparer.OrdinalIgnoreCase))
                Accounts.Add(new AccountRowViewModel(account, RemoveAccount));

            OnPropertyChanged(nameof(EmptyVisibility));
        }

        /// <summary>Called periodically: running clients' servers come and go.</summary>
        public void RefreshTargets()
        {
            var sessions = LiveSessions.List();
            var names = AccountStore.Load().ToDictionary(account => account.UserId, account => account.Username);
            string? previous = SelectedTarget?.Label;

            var targets = new List<LaunchTargetViewModel>();

            foreach (var session in sessions.OrderBy(session => MultiInstance.GetSlot(session.ProcessId)))
            {
                string who = names.TryGetValue(session.UserId, out string? name) ? name : $"instance {MultiInstance.GetSlot(session.ProcessId)}";
                targets.Add(new LaunchTargetViewModel
                {
                    Kind = LaunchTargetViewModel.TargetKind.Server,
                    Session = session,
                    Label = $"Join {who}'s server (game {session.PlaceId}{(session.IsPrivate ? ", private" : "")})"
                });
            }

            targets.Add(new LaunchTargetViewModel { Kind = LaunchTargetViewModel.TargetKind.SmallestServer, Label = "Smallest server of a game" });
            targets.Add(new LaunchTargetViewModel { Kind = LaunchTargetViewModel.TargetKind.AnyServer, Label = "Any server of a game" });

            // only rebuild when something changed, so an open dropdown isn't disturbed
            if (targets.Select(t => t.Label).SequenceEqual(Targets.Select(t => t.Label)))
                return;

            Targets.Clear();

            foreach (var target in targets)
                Targets.Add(target);

            SelectedTarget = Targets.FirstOrDefault(t => t.Label == previous) ?? Targets.First();
        }

        private void SetStatus(string text)
        {
            Status = text;
            OnPropertyChanged(nameof(Status));
        }

        private void AddAccount()
        {
            var window = new Elements.Accounts.AccountSignInWindow { Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) };

            if (window.ShowDialog() == true && window.SavedAccount is not null)
            {
                ReloadAccounts();
                RefreshTargets();
                SetStatus($"Saved {window.SavedAccount.Name}.");
            }
        }

        private void RemoveAccount(AccountRowViewModel row)
        {
            var answer = Frontend.ShowMessageBox($"Remove {row.Label} from PSYstrap?\n\nThis only deletes PSYstrap's saved sign-in; the Roblox account itself isn't touched.",
                MessageBoxImage.Question, MessageBoxButton.YesNo);

            if (answer != MessageBoxResult.Yes)
                return;

            AccountStore.Remove(row.Account.UserId);
            ReloadAccounts();
        }

        private async Task LaunchSelectedAsync()
        {
            if (_launching)
                return;

            var selected = Accounts.Where(row => row.IsSelected).Select(row => row.Account).ToList();
            var target = SelectedTarget;

            if (selected.Count == 0)
            {
                SetStatus("Tick the accounts to open first.");
                return;
            }

            if (target is null)
                return;

            long placeId = target.Session?.PlaceId ?? 0;

            if (target.NeedsPlaceId && (!long.TryParse(PlaceIdText.Trim(), out placeId) || placeId <= 0))
            {
                SetStatus("Enter the game's place ID (the number in its roblox.com/games/ link).");
                return;
            }

            // several accounts at once needs multi-instance
            if (!App.Settings.Prop.MultiInstance && (selected.Count > 1 || MultiInstance.GetRobloxProcesses().Any()))
            {
                App.Settings.Load(false);
                App.Settings.Prop.MultiInstance = true;
                App.Settings.Save();
            }

            _launching = true;

            try
            {
                string? serverId = target.Session?.ServerId;
                string? accessCode = target.Session is { IsPrivate: true } ? target.Session.AccessCode : null;

                if (target.Kind == LaunchTargetViewModel.TargetKind.SmallestServer)
                {
                    SetStatus("Finding the smallest server...");
                    var server = await RobloxServers.FindSmallestAsync(placeId);

                    if (server is null)
                    {
                        SetStatus("Couldn't find a public server with space for that game.");
                        return;
                    }

                    if (server.MaxPlayers - server.Playing < selected.Count)
                        SetStatus($"Note: that server only has {server.MaxPlayers - server.Playing} free slots.");

                    serverId = server.Id;
                }

                for (int i = 0; i < selected.Count; i++)
                {
                    var account = selected[i];
                    SetStatus($"Opening {account.Username} ({i + 1}/{selected.Count})...");

                    string? session = AccountStore.GetSession(account);

                    if (session is null)
                    {
                        SetStatus($"{account.Username}'s saved sign-in can't be read on this PC - remove it and add it again.");
                        continue;
                    }

                    string? ticket = await RobloxAuth.GetLaunchTicketAsync(session);

                    if (ticket is null)
                    {
                        SetStatus($"Roblox refused {account.Username}'s saved sign-in (it may have expired) - remove it and add it again.");
                        continue;
                    }

                    string link = RobloxAuth.BuildLaunchLink(ticket, placeId, serverId, accessCode);
                    Process.Start(Paths.Process, $"-player \"{link}\"");
                    App.Logger.WriteLine(LOG_IDENT, $"Launched account {account.UserId} into place {placeId}");

                    if (i < selected.Count - 1)
                        await Task.Delay(LaunchGap);
                }

                SetStatus($"Opened {selected.Count} account{(selected.Count == 1 ? "" : "s")}.");
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                SetStatus($"Something went wrong: {ex.Message}");
            }
            finally
            {
                _launching = false;
            }
        }
    }
}
