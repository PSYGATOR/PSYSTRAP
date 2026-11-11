using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

using CommunityToolkit.Mvvm.Input;

namespace Bloxstrap.UI.ViewModels.Instances
{
    /// <summary>One running Roblox client in the instance manager.</summary>
    public class InstanceRowViewModel : NotifyPropertyChangedViewModel
    {
        private readonly InstanceManagerViewModel _owner;

        public int ProcessId { get; }

        public int Slot { get; }

        public string Title => $"Instance {Slot}";

        public string Details { get; private set; } = "";

        public bool IsFocused { get; private set; }

        public Visibility FocusedBadgeVisibility => IsFocused ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Turn-taking: smart macro for this client's slot ("" = not part of turn-taking).</summary>
        public string AssignedSmartMacro
        {
            get => App.Settings.Prop.MultiInstanceOptions.RotationMacros.TryGetValue(Slot, out string? name) && !String.IsNullOrEmpty(name) ? name : NoMacro;
            set => _owner.AssignSmartMacro(Slot, value == NoMacro ? "" : value);
        }

        public const string NoMacro = "(none)";

        public IEnumerable<string> SmartMacroChoices => _owner.SmartMacroChoices;

        public string RotationStatus { get; private set; } = "";

        internal void SetRotationStatus(string status)
        {
            if (status == RotationStatus)
                return;

            RotationStatus = status;
            OnPropertyChanged(nameof(RotationStatus));
        }

        internal TimeSpan LastCpuTime;
        internal DateTime LastSample = DateTime.MinValue;

        public ICommand FocusCommand => new RelayCommand(() => _owner.FocusClient(ProcessId));
        public ICommand MinimizeCommand => new RelayCommand(() => _owner.MinimizeClient(ProcessId));
        public ICommand TrimCommand => new RelayCommand(() => _owner.TrimClient(ProcessId));
        public ICommand CloseCommand => new RelayCommand(() => _owner.CloseClient(ProcessId));

        public InstanceRowViewModel(InstanceManagerViewModel owner, int processId, int slot)
        {
            _owner = owner;
            ProcessId = processId;
            Slot = slot;
        }

        internal void Update(string details, bool focused)
        {
            Details = details;
            IsFocused = focused;
            OnPropertyChanged(nameof(Details));
            OnPropertyChanged(nameof(IsFocused));
            OnPropertyChanged(nameof(FocusedBadgeVisibility));
        }
    }

    /// <summary>PSYstrap instance manager: lists Roblox clients and the multi-instance options.</summary>
    public class InstanceManagerViewModel : NotifyPropertyChangedViewModel, IDisposable
    {
        private readonly DispatcherTimer _timer;

        public ObservableCollection<InstanceRowViewModel> Instances { get; } = new();

        public AccountLauncherViewModel Launcher { get; } = new();

        #region Turn-taking
        private readonly Integrations.Macros.RotationRunner _rotation = new();

        public List<string> SmartMacroChoices { get; } =
            new[] { InstanceRowViewModel.NoMacro }.Concat(Integrations.Macros.SmartMacroStore.ListNames()).ToList();

        public bool RotationRunning => _rotation.Running;

        public string RotationButtonText => _rotation.Running ? "Stop taking turns" : "Start taking turns";

        public string RotationStatus { get; private set; } = "Pick a smart macro for each client below, then start.";

        public ICommand ToggleRotationCommand => new RelayCommand(() =>
        {
            if (_rotation.Running)
                _rotation.Stop();
            else if (!App.Settings.Prop.MultiInstanceOptions.RotationMacros.Values.Any(v => !String.IsNullOrEmpty(v)))
                RotationStatus = "Pick a smart macro for at least one client first.";
            else
                _rotation.Start();

            OnPropertyChanged(nameof(RotationRunning));
            OnPropertyChanged(nameof(RotationButtonText));
            OnPropertyChanged(nameof(RotationStatus));
        });

        public int RotationDwellMs
        {
            get => App.Settings.Prop.MultiInstanceOptions.RotationDwellMs;
            set => SaveSettings(s => s.MultiInstanceOptions.RotationDwellMs = Math.Clamp(value, 200, 30000), nameof(RotationDwellMs));
        }

        public int RotationResumeSeconds
        {
            get => App.Settings.Prop.MultiInstanceOptions.RotationResumeSeconds;
            set => SaveSettings(s => s.MultiInstanceOptions.RotationResumeSeconds = Math.Clamp(value, 1, 300), nameof(RotationResumeSeconds));
        }

        internal void AssignSmartMacro(int slot, string name)
        {
            SaveSettings(s =>
            {
                if (String.IsNullOrEmpty(name))
                    s.MultiInstanceOptions.RotationMacros.Remove(slot);
                else
                    s.MultiInstanceOptions.RotationMacros[slot] = name;
            }, nameof(Instances));
        }

        private void RefreshRotation()
        {
            if (_rotation.Running || _rotation.Status == "Stopped.")
                RotationStatus = _rotation.Status;

            foreach (var row in Instances)
                row.SetRotationStatus(_rotation.Running && _rotation.ClientStatus.TryGetValue(row.Slot, out string? status) ? status : "");

            OnPropertyChanged(nameof(RotationStatus));
            OnPropertyChanged(nameof(RotationRunning));
            OnPropertyChanged(nameof(RotationButtonText));
        }
        #endregion

        public string Summary { get; private set; } = "";

        public string Status { get; private set; } = "";

        public Visibility EmptyVisibility => Instances.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        public InstanceManagerViewModel()
        {
            _timer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Normal, (_, _) => Refresh(), Application.Current.Dispatcher);
            Refresh();
            _timer.Start();
        }

        #region Options (saved straight away - this window runs in its own process)
        public bool MultiInstanceEnabled
        {
            get => App.Settings.Prop.MultiInstance;
            set => SaveSettings(settings => settings.MultiInstance = value, nameof(MultiInstanceEnabled));
        }

        public bool LowerBackgroundPriority
        {
            get => App.Settings.Prop.MultiInstanceOptions.LowerBackgroundPriority;
            set => SaveSettings(settings => settings.MultiInstanceOptions.LowerBackgroundPriority = value, nameof(LowerBackgroundPriority));
        }

        public bool TrimBackgroundMemory
        {
            get => App.Settings.Prop.MultiInstanceOptions.TrimBackgroundMemory;
            set => SaveSettings(settings => settings.MultiInstanceOptions.TrimBackgroundMemory = value, nameof(TrimBackgroundMemory));
        }

        /// <summary>Reload first so changes made elsewhere (Settings window, in-game menu) aren't overwritten.</summary>
        private void SaveSettings(Action<Models.Persistable.Settings> change, string propertyName)
        {
            App.Settings.Load(false);
            change(App.Settings.Prop);
            App.Settings.Save();
            OnPropertyChanged(propertyName);
            Refresh();
        }
        #endregion

        #region Commands
        public ICommand LaunchCommand => new RelayCommand(LaunchInstance);

        public ICommand ArrangeCommand => new RelayCommand(ArrangeWindows);

        private void LaunchInstance()
        {
            if (!App.Settings.Prop.MultiInstance && MultiInstance.GetRobloxProcesses().Any())
            {
                var result = Frontend.ShowMessageBox(
                    "Multi-instance is off, so Roblox will just switch to the client that's already open.\n\nTurn multi-instance on?",
                    MessageBoxImage.Question, MessageBoxButton.YesNo);

                if (result != MessageBoxResult.Yes)
                    return;

                MultiInstanceEnabled = true;
            }

            Process.Start(Paths.Process, "-player");
        }

        /// <summary>Tiles every client in a grid on the main screen (handy for watching several at once or for macros).</summary>
        private void ArrangeWindows()
        {
            var windows = MultiInstance.GetRobloxProcesses()
                .OrderBy(process => MultiInstance.GetSlot(process.Id))
                .Select(process => process.MainWindowHandle)
                .Where(handle => handle != IntPtr.Zero)
                .ToList();

            if (windows.Count == 0)
                return;

            var area = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
            int columns = (int)Math.Ceiling(Math.Sqrt(windows.Count));
            int rows = (int)Math.Ceiling(windows.Count / (double)columns);
            int width = area.Width / columns;
            int height = area.Height / rows;

            for (int i = 0; i < windows.Count; i++)
            {
                ShowWindow(windows[i], SW_RESTORE);
                MoveWindow(windows[i], area.Left + (i % columns) * width, area.Top + (i / columns) * height, width, height, true);
            }
        }

        internal void FocusClient(int processId)
        {
            IntPtr window = GetWindow(processId);

            if (window == IntPtr.Zero)
                return;

            if (IsIconic(window))
                ShowWindow(window, SW_RESTORE);

            SetForegroundWindow(window);
        }

        internal void MinimizeClient(int processId)
        {
            IntPtr window = GetWindow(processId);

            if (window != IntPtr.Zero)
                ShowWindow(window, SW_MINIMIZE);
        }

        internal void TrimClient(int processId)
        {
            if (!MultiInstance.TrimMemory(processId))
                Frontend.ShowMessageBox("Windows wouldn't let PSYstrap trim that client's memory.", MessageBoxImage.Warning);

            Refresh();
        }

        internal void CloseClient(int processId)
        {
            var result = Frontend.ShowMessageBox($"Close Roblox instance {MultiInstance.GetSlot(processId)}?", MessageBoxImage.Question, MessageBoxButton.YesNo);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                using var process = Process.GetProcessById(processId);
                process.CloseMainWindow();
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("InstanceManager::CloseClient", ex);
            }
        }

        private static IntPtr GetWindow(int processId)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                return process.MainWindowHandle;
            }
            catch
            {
                return IntPtr.Zero;
            }
        }
        #endregion

        private void Refresh()
        {
            var clients = MultiInstance.GetRobloxProcesses();
            GetWindowThreadProcessId(GetForegroundWindow(), out uint foregroundPid);

            // drop closed clients, add new ones
            foreach (var gone in Instances.Where(row => clients.All(client => client.Id != row.ProcessId)).ToList())
                Instances.Remove(gone);

            foreach (var client in clients.Where(client => Instances.All(row => row.ProcessId != client.Id)))
                Instances.Add(new InstanceRowViewModel(this, client.Id, MultiInstance.GetSlot(client.Id)));

            var sorted = Instances.OrderBy(row => row.Slot).ToList();
            for (int i = 0; i < sorted.Count; i++)
            {
                int current = Instances.IndexOf(sorted[i]);
                if (current != i)
                    Instances.Move(current, i);
            }

            long totalMemory = 0;

            foreach (var row in Instances)
            {
                var client = clients.First(process => process.Id == row.ProcessId);
                long memory = 0;
                string cpu = "--";
                string uptime = "--";

                try
                {
                    memory = client.WorkingSet64;
                    totalMemory += memory;

                    var cpuTime = MultiInstance.GetCpuTime(row.ProcessId);

                    if (cpuTime is not null)
                    {
                        if (row.LastSample != DateTime.MinValue)
                        {
                            double elapsed = (DateTime.Now - row.LastSample).TotalMilliseconds * Environment.ProcessorCount;
                            cpu = $"{Math.Clamp((cpuTime.Value - row.LastCpuTime).TotalMilliseconds / elapsed * 100, 0, 100):0}%";
                        }

                        row.LastCpuTime = cpuTime.Value;
                        row.LastSample = DateTime.Now;
                    }

                    var started = MultiInstance.GetStartTime(row.ProcessId);

                    if (started is not null)
                        uptime = (DateTime.Now - started.Value).ToString(@"h\:mm\:ss");
                }
                catch { }

                row.Update($"PID {row.ProcessId}   ·   RAM {memory / (1024.0 * 1024 * 1024):0.00} GB   ·   CPU {cpu}   ·   up {uptime}", row.ProcessId == foregroundPid);
            }

            Summary = Instances.Count switch
            {
                0 => "No Roblox clients running.",
                1 => $"1 client running, using {totalMemory / (1024.0 * 1024 * 1024):0.0} GB of RAM.",
                _ => $"{Instances.Count} clients running, using {totalMemory / (1024.0 * 1024 * 1024):0.0} GB of RAM in total."
            };

            if (!App.Settings.Prop.MultiInstance)
                Status = "Multi-instance is off: launching Roblox again switches to the client that's already open.";
            else if (clients.Count > 0 && !MultiInstance.IsHostRunning)
                Status = "Roblox was already open when multi-instance was turned on. Close every Roblox window once, then launch through PSYstrap.";
            else
                Status = "Multi-instance is on: every launch through PSYstrap opens a new client. Each one can be signed in to a different account with Roblox's own account switcher.";

            OnPropertyChanged(nameof(Summary));
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(EmptyVisibility));

            Launcher.RefreshTargets();
            RefreshRotation();
        }

        public void Dispose()
        {
            _timer.Stop();
            _rotation.Dispose(); // stops turn-taking and removes its marker when the window closes
        }

        #region Native
        private const int SW_MINIMIZE = 6;
        private const int SW_RESTORE = 9;

        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] private static extern bool MoveWindow(IntPtr window, int x, int y, int width, int height, bool repaint);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        #endregion
    }
}
