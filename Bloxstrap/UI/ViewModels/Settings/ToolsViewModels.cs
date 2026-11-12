using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace Bloxstrap.UI.ViewModels.Settings
{
    /// <summary>PSYstrap: Engine Settings page - remove ignored fast flags, and fast flag profiles.</summary>
    public class FlagToolsViewModel : NotifyPropertyChangedViewModel
    {
        public ObservableCollection<string> Profiles { get; } = new(FlagTools.ListProfiles());

        public string? SelectedProfile { get; set; }

        public string NewProfileName { get; set; } = "";

        private string _status = "";
        public string Status { get => _status; private set { _status = value; OnPropertyChanged(nameof(Status)); } }

        public string IgnoredSummary
        {
            get
            {
                int ignored = FlagTools.IgnoredFlags().Count, total = App.FastFlags.Prop.Count;
                return ignored == 0
                    ? $"All {total} of your flags still work."
                    : $"{ignored} of your {total} flags aren't on Roblox's allowlist, so Roblox ignores them.";
            }
        }

        public ICommand CleanCommand => new RelayCommand(() =>
        {
            var ignored = FlagTools.IgnoredFlags();

            if (ignored.Count == 0)
            {
                Status = "Nothing to remove.";
                return;
            }

            string list = String.Join("\n", ignored.Take(15)) + (ignored.Count > 15 ? $"\n...and {ignored.Count - 15} more" : "");
            var answer = Frontend.ShowMessageBox($"Remove these {ignored.Count} flags? Roblox ignores them anyway.\n\n{list}\n\nTip: save a flag profile first if you want them back later.",
                MessageBoxImage.Question, MessageBoxButton.YesNo);

            if (answer != MessageBoxResult.Yes)
                return;

            int removed = FlagTools.RemoveIgnored();
            Status = $"Removed {removed} flags. Hit Save to keep it.";
            OnPropertyChanged(nameof(IgnoredSummary));
        });

        public ICommand SaveProfileCommand => new RelayCommand(() =>
        {
            string name = FlagTools.SafeName(NewProfileName);

            if (name.Length == 0)
            {
                Status = "Type a name for the profile first.";
                return;
            }

            FlagTools.SaveProfile(name);

            if (!Profiles.Contains(name))
                Profiles.Add(name);

            NewProfileName = "";
            OnPropertyChanged(nameof(NewProfileName));
            Status = $"Saved your current {App.FastFlags.Prop.Count} flags as \"{name}\".";
        });

        public ICommand LoadProfileCommand => new RelayCommand(() =>
        {
            if (SelectedProfile is null)
                return;

            Status = FlagTools.LoadProfile(SelectedProfile)
                ? $"Loaded \"{SelectedProfile}\" ({App.FastFlags.Prop.Count} flags). Hit Save to use it."
                : "Couldn't read that profile.";

            OnPropertyChanged(nameof(IgnoredSummary));
        });

        public ICommand DeleteProfileCommand => new RelayCommand(() =>
        {
            if (SelectedProfile is null)
                return;

            FlagTools.DeleteProfile(SelectedProfile);
            Profiles.Remove(SelectedProfile);
            Status = "Deleted.";
        });
    }

    /// <summary>PSYstrap: Mods page - mod profiles.</summary>
    public class ModProfilesViewModel : NotifyPropertyChangedViewModel
    {
        public ObservableCollection<string> Profiles { get; } = new(ModProfiles.List());

        public string? SelectedProfile { get; set; }

        public string NewProfileName { get; set; } = "";

        private string _status = "";
        public string Status { get => _status; private set { _status = value; OnPropertyChanged(nameof(Status)); } }

        public string CurrentText => ModProfiles.Current is { Length: > 0 } current
            ? $"In use right now: \"{current}\" (a game profile switched to it)."
            : "In use right now: your normal mods.";

        public ICommand SaveCommand => new RelayCommand(() =>
        {
            string name = FlagTools.SafeName(NewProfileName);

            if (name.Length == 0 || name.StartsWith('_'))
            {
                Status = "Type a name for the profile first.";
                return;
            }

            ModProfiles.Save(name);

            if (!Profiles.Contains(name))
                Profiles.Add(name);

            NewProfileName = "";
            OnPropertyChanged(nameof(NewProfileName));
            Status = $"Saved the mods you have now as \"{name}\". Pick it for a game on the Gameplay page.";
        });

        public ICommand DeleteCommand => new RelayCommand(() =>
        {
            if (SelectedProfile is null)
                return;

            ModProfiles.Delete(SelectedProfile);
            Profiles.Remove(SelectedProfile);
            Status = "Deleted.";
        });
    }

    /// <summary>PSYstrap: PSYstrap page - import from other straps, backup and restore.</summary>
    public class TransferViewModel : NotifyPropertyChangedViewModel
    {
        public List<string> Straps { get; } = SettingsTransfer.FindInstalledStraps();

        public string? SelectedStrap { get; set; }

        public bool HasStraps => Straps.Count > 0;

        public string ImportNote => HasStraps
            ? "Found on this PC: " + String.Join(", ", Straps) + "."
            : "No other straps found on this PC.";

        private string _status = "";
        public string Status { get => _status; private set { _status = value; OnPropertyChanged(nameof(Status)); } }

        public TransferViewModel() => SelectedStrap = Straps.FirstOrDefault();

        public ICommand ImportCommand => new RelayCommand(() =>
        {
            if (SelectedStrap is null)
                return;

            try { Status = SettingsTransfer.ImportFrom(SelectedStrap); }
            catch (Exception ex) { Status = $"Import failed: {ex.Message}"; }
        });

        public ICommand BackupCommand => new RelayCommand(() =>
        {
            var dialog = new SaveFileDialog
            {
                FileName = $"PSYstrap backup {DateTime.Now:yyyy-MM-dd}.zip",
                Filter = "Zip files|*.zip"
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                App.Settings.Save();
                App.FastFlags.Save();
                SettingsTransfer.ExportBackup(dialog.FileName);
                Status = $"Saved {Path.GetFileName(dialog.FileName)}. Saved accounts aren't in it (they only work on this PC).";
            }
            catch (Exception ex)
            {
                Status = $"Backup failed: {ex.Message}";
            }
        });

        public ICommand RestoreCommand => new RelayCommand(() =>
        {
            var dialog = new OpenFileDialog { Filter = "PSYstrap backups|*.zip" };

            if (dialog.ShowDialog() != true)
                return;

            var answer = Frontend.ShowMessageBox("Restore this backup? It replaces your current settings, macros, mods and fast flags. Close and reopen Settings afterwards.",
                MessageBoxImage.Warning, MessageBoxButton.YesNo);

            if (answer != MessageBoxResult.Yes)
                return;

            try
            {
                int files = SettingsTransfer.ImportBackup(dialog.FileName);
                Status = $"Restored {files} files. Close and reopen Settings to see them.";
            }
            catch (Exception ex)
            {
                Status = $"Restore failed: {ex.Message}";
            }
        });
    }
}
