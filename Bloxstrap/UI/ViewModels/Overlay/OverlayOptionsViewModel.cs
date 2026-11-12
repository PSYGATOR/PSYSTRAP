namespace Bloxstrap.UI.ViewModels.Overlay
{
    /// <summary>
    /// PSYstrap overlay options, shared by the Settings window page and the in-game menu.
    /// The Settings window saves through its own Save button; the in-game menu saves straight away.
    /// </summary>
    public class OverlayOptionsViewModel : NotifyPropertyChangedViewModel
    {
        private readonly bool _saveImmediately;

        /// <summary>Raised after any option changes, so the HUD can refresh.</summary>
        public event EventHandler? Changed;

        public OverlayOptionsViewModel(bool saveImmediately)
        {
            _saveImmediately = saveImmediately;
        }

        private static OverlaySettings Overlay => App.Settings.Prop.Overlay;

        public bool Enabled
        {
            get => Overlay.Enabled;
            set { Overlay.Enabled = value; Commit(nameof(Enabled)); }
        }

        public bool ShowFramerate
        {
            get => Overlay.ShowFramerate;
            set { Overlay.ShowFramerate = value; Commit(nameof(ShowFramerate)); }
        }

        public bool ShowClicksPerSecond
        {
            get => Overlay.ShowClicksPerSecond;
            set { Overlay.ShowClicksPerSecond = value; Commit(nameof(ShowClicksPerSecond)); }
        }

        public bool ShowMemory
        {
            get => Overlay.ShowMemory;
            set { Overlay.ShowMemory = value; Commit(nameof(ShowMemory)); }
        }

        public bool ShowSessionTime
        {
            get => Overlay.ShowSessionTime;
            set { Overlay.ShowSessionTime = value; Commit(nameof(ShowSessionTime)); }
        }

        public bool ShowClock
        {
            get => Overlay.ShowClock;
            set { Overlay.ShowClock = value; Commit(nameof(ShowClock)); }
        }

        public bool ShowServer
        {
            get => Overlay.ShowServer;
            set { Overlay.ShowServer = value; Commit(nameof(ShowServer)); }
        }

        public bool ShowPlaytime
        {
            get => Overlay.ShowPlaytime;
            set { Overlay.ShowPlaytime = value; Commit(nameof(ShowPlaytime)); }
        }

        public IEnumerable<OverlayCorner> Corners { get; } = Enum.GetValues<OverlayCorner>();

        public OverlayCorner Corner
        {
            get => Overlay.Corner;
            set { Overlay.Corner = value; Commit(nameof(Corner)); }
        }

        public IEnumerable<OverlayMenuKey> MenuKeys { get; } = Enum.GetValues<OverlayMenuKey>();

        public OverlayMenuKey MenuKey
        {
            get => Overlay.MenuKey;
            set { Overlay.MenuKey = value; Commit(nameof(MenuKey)); }
        }

        public bool PotatoMode
        {
            get => App.Settings.Prop.PotatoMode;
            set
            {
                App.Settings.Prop.PotatoMode = value;

                // the Settings window saves fast flags with its Save button; the in-game menu has to do it now,
                // reloading first so flags changed elsewhere since launch aren't overwritten
                if (_saveImmediately)
                    App.FastFlags.Load(false);

                Bloxstrap.PotatoMode.SetFlags(value);

                if (_saveImmediately)
                    App.FastFlags.Save();

                Commit(nameof(PotatoMode));
            }
        }

        public bool PotatoCapFramerate
        {
            get => App.Settings.Prop.PotatoCapFramerate;
            set { App.Settings.Prop.PotatoCapFramerate = value; Commit(nameof(PotatoCapFramerate)); }
        }

        public IReadOnlyList<KeyValuePair<int, string>> FramerateCaps => RobloxUserSettings.FramerateCapChoices;

        public int FramerateCap
        {
            get => App.Settings.Prop.FramerateCap;
            set { App.Settings.Prop.FramerateCap = value; Commit(nameof(FramerateCap)); }
        }

        public string RobloxFramerateCapText
        {
            get
            {
                int? current = RobloxUserSettings.ReadFramerateCap();
                string started = current switch
                {
                    null => "Roblox hasn't saved a frame rate cap yet.",
                    -1 => "Roblox's current cap: default (60 FPS).",
                    0 => "Roblox's current cap: uncapped.",
                    _ => $"Roblox's current cap: {current} FPS."
                };

                return _saveImmediately
                    ? started + " Roblox only reads the cap when it starts, so use the button below to apply a change now."
                    : started;
            }
        }

        /// <summary>Only the in-game menu can restart Roblox (the Settings window has no running game).</summary>
        public System.Windows.Visibility ApplyNowVisibility => _saveImmediately ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

        /// <summary>Raised when the user asks to restart Roblox so the frame rate cap takes effect.</summary>
        public event EventHandler? ApplyNowRequested;

        public System.Windows.Input.ICommand ApplyNowCommand => new CommunityToolkit.Mvvm.Input.RelayCommand(() => ApplyNowRequested?.Invoke(this, EventArgs.Empty));

        /// <summary>Raised when the user asks to move to the emptiest public server of the current game.</summary>
        public event EventHandler? SmallestServerRequested;

        public System.Windows.Input.ICommand SmallestServerCommand => new CommunityToolkit.Mvvm.Input.RelayCommand(() => SmallestServerRequested?.Invoke(this, EventArgs.Empty));

        private string _serverHopStatus = "";

        /// <summary>Shown under the hop button (searching, no servers found, ...).</summary>
        public string ServerHopStatus
        {
            get => _serverHopStatus;
            set { _serverHopStatus = value; OnPropertyChanged(nameof(ServerHopStatus)); }
        }

        /// <summary>Raised when the user toggles hiding the current game from Discord.</summary>
        public event EventHandler? DiscordHideRequested;

        public System.Windows.Input.ICommand DiscordHideCommand => new CommunityToolkit.Mvvm.Input.RelayCommand(() => DiscordHideRequested?.Invoke(this, EventArgs.Empty));

        private string _discordHideText = "Hide this game from Discord";

        public string DiscordHideText
        {
            get => _discordHideText;
            set { _discordHideText = value; OnPropertyChanged(nameof(DiscordHideText)); }
        }

        #region Macros
        public System.Collections.ObjectModel.ObservableCollection<string> MacroNames { get; } = new(Integrations.Macros.MacroStore.ListNames());

        public string SelectedMacro
        {
            get => Overlay.SelectedMacro;
            set { Overlay.SelectedMacro = value ?? ""; Commit(nameof(SelectedMacro)); OnPropertyChanged(nameof(SelectedMacroDetails)); }
        }

        public string SelectedMacroDetails
        {
            get
            {
                var macro = String.IsNullOrEmpty(SelectedMacro) ? null : Integrations.Macros.MacroStore.Load(SelectedMacro);

                return macro is null
                    ? "No macro yet. In Roblox, press F6 to start recording and F6 again to stop."
                    : $"{macro.Steps.Count} inputs, {macro.Duration.TotalSeconds:0.0} seconds long. Press F7 in Roblox to play, F7 again to stop.";
            }
        }

        public IReadOnlyList<KeyValuePair<int, string>> MacroRunChoices { get; } = new List<KeyValuePair<int, string>>
        {
            new(1, "Once"), new(5, "5 times"), new(10, "10 times"), new(50, "50 times"), new(0, "Until stopped")
        };

        public int MacroRuns
        {
            get => Overlay.MacroRuns;
            set { Overlay.MacroRuns = value; Commit(nameof(MacroRuns)); }
        }

        public IReadOnlyList<KeyValuePair<double, string>> MacroSpeedChoices { get; } = new List<KeyValuePair<double, string>>
        {
            new(0.5, "0.5x"), new(1.0, "1x"), new(1.5, "1.5x"), new(2.0, "2x")
        };

        public double MacroSpeed
        {
            get => Overlay.MacroSpeed;
            set { Overlay.MacroSpeed = value; Commit(nameof(MacroSpeed)); }
        }

        public System.Windows.Input.ICommand DeleteMacroCommand => new CommunityToolkit.Mvvm.Input.RelayCommand(() =>
        {
            if (String.IsNullOrEmpty(SelectedMacro))
                return;

            var answer = Frontend.ShowMessageBox($"Delete \"{SelectedMacro}\"? This can't be undone.",
                System.Windows.MessageBoxImage.Warning, System.Windows.MessageBoxButton.YesNo);

            if (answer != System.Windows.MessageBoxResult.Yes)
                return;

            Integrations.Macros.MacroStore.Delete(SelectedMacro);
            RefreshMacros();
            SelectedMacro = MacroNames.FirstOrDefault() ?? "";
        });

        public System.Windows.Input.ICommand OpenMacroFolderCommand => new CommunityToolkit.Mvvm.Input.RelayCommand(() =>
        {
            Directory.CreateDirectory(Integrations.Macros.MacroStore.Directory);
            Process.Start("explorer.exe", Integrations.Macros.MacroStore.Directory);
        });

        #region Smart macros
        public System.Collections.ObjectModel.ObservableCollection<string> SmartMacroNames { get; } = new(Integrations.Macros.SmartMacroStore.ListNames());

        public string SelectedSmartMacro
        {
            get => Overlay.SelectedSmartMacro;
            set { Overlay.SelectedSmartMacro = value ?? ""; Commit(nameof(SelectedSmartMacro)); OnPropertyChanged(nameof(SelectedSmartMacroDetails)); }
        }

        public string SelectedSmartMacroDetails
        {
            get
            {
                var smart = String.IsNullOrEmpty(SelectedSmartMacro) ? null : Integrations.Macros.SmartMacroStore.Load(SelectedSmartMacro);

                return smart is null
                    ? "None yet. New opens the editor: screenshot Roblox, mark what to watch for, then add rules."
                    : $"{smart.Detectors.Count} detectors, {smart.Rules.Count} rules. F8 in Roblox starts/stops it.";
            }
        }

        public System.Windows.Input.ICommand NewSmartMacroCommand => new CommunityToolkit.Mvvm.Input.RelayCommand(() => OpenSmartEditor(null));

        public System.Windows.Input.ICommand EditSmartMacroCommand => new CommunityToolkit.Mvvm.Input.RelayCommand(() =>
        {
            if (!String.IsNullOrEmpty(SelectedSmartMacro))
                OpenSmartEditor(SelectedSmartMacro);
        });

        public System.Windows.Input.ICommand DeleteSmartMacroCommand => new CommunityToolkit.Mvvm.Input.RelayCommand(() =>
        {
            if (String.IsNullOrEmpty(SelectedSmartMacro))
                return;

            var answer = Frontend.ShowMessageBox($"Delete the smart macro \"{SelectedSmartMacro}\"? This can't be undone.",
                System.Windows.MessageBoxImage.Warning, System.Windows.MessageBoxButton.YesNo);

            if (answer != System.Windows.MessageBoxResult.Yes)
                return;

            Integrations.Macros.SmartMacroStore.Delete(SelectedSmartMacro);
            RefreshSmartMacros();
            SelectedSmartMacro = SmartMacroNames.FirstOrDefault() ?? "";
        });

        private void OpenSmartEditor(string? name)
        {
            var editor = new Elements.Macros.SmartMacroEditorWindow(name);

            if (editor.ShowDialog() == true && editor.SavedName is not null)
            {
                RefreshSmartMacros();
                SelectedSmartMacro = editor.SavedName;

                // the editor saves its own files; make sure the selection itself is kept even from the Settings window
                if (!_saveImmediately)
                    SaveOverlayOnly();
            }
        }

        public void RefreshSmartMacros()
        {
            var names = Integrations.Macros.SmartMacroStore.ListNames();

            if (!names.SequenceEqual(SmartMacroNames))
            {
                SmartMacroNames.Clear();

                foreach (string name in names)
                    SmartMacroNames.Add(name);
            }

            OnPropertyChanged(nameof(SelectedSmartMacro));
            OnPropertyChanged(nameof(SelectedSmartMacroDetails));
        }
        #endregion

        /// <summary>
        /// Something changed on disk (another Roblox window's overlay, the Settings window, a macro added or deleted):
        /// reload the macro lists and refresh every binding so this menu shows the same thing everywhere.
        /// </summary>
        public void RefreshFromDisk()
        {
            RefreshMacros();
            RefreshSmartMacros();
            OnPropertyChanged(String.Empty); // empty name = all properties changed
        }

        /// <summary>Called by the overlay after a recording is saved.</summary>
        public void SelectMacro(string name)
        {
            RefreshMacros();
            SelectedMacro = name;
        }

        public void RefreshMacros()
        {
            var names = Integrations.Macros.MacroStore.ListNames();

            // leave the list alone if nothing changed, so an open dropdown isn't disturbed
            if (!names.SequenceEqual(MacroNames))
            {
                MacroNames.Clear();

                foreach (string name in names)
                    MacroNames.Add(name);
            }

            OnPropertyChanged(nameof(SelectedMacro));
            OnPropertyChanged(nameof(SelectedMacroDetails));
        }
        #endregion

        private void Commit(string propertyName)
        {
            if (_saveImmediately)
                SaveOverlayOnly();

            OnPropertyChanged(propertyName);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// The in-game menu runs in the long-lived watcher process, whose copy of the settings is from launch time.
        /// Reload from disk first and only write our fields, so changes made in the Settings window since aren't undone.
        /// </summary>
        private static void SaveOverlayOnly()
        {
            var overlay = App.Settings.Prop.Overlay;
            int framerateCap = App.Settings.Prop.FramerateCap;
            bool potato = App.Settings.Prop.PotatoMode;
            bool potatoCap = App.Settings.Prop.PotatoCapFramerate;

            App.Settings.Load(false);
            App.Settings.Prop.Overlay = overlay;
            App.Settings.Prop.FramerateCap = framerateCap;
            App.Settings.Prop.PotatoMode = potato;
            App.Settings.Prop.PotatoCapFramerate = potatoCap;
            App.Settings.Save();
        }
    }
}
