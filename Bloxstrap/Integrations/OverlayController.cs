using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

using Bloxstrap.UI.Elements.Overlay;
using Bloxstrap.UI.ViewModels.Overlay;

namespace Bloxstrap.Integrations
{
    /// <summary>
    /// PSYstrap in-game overlay. Lives in the watcher process alongside Roblox and draws separate windows on top of the
    /// Roblox window - nothing is injected into Roblox. Must be created and used on the WPF UI thread.
    ///
    /// Shows over windowed and borderless-fullscreen Roblox. Exclusive fullscreen (the Alt+Enter preset) draws
    /// over everything, so the overlay can't appear there.
    /// </summary>
    public sealed class OverlayController : IDisposable
    {
        private const string LOG_IDENT = "OverlayController";

        private readonly Process _roblox;
        private readonly DateTime _robloxStarted;
        private readonly FrameRateMonitor _frameRate;
        private readonly InputHooks _hooks;
        private readonly KeyRemapper _remapper;
        private readonly OverlayWindow _hud;
        private readonly OverlayOptionsViewModel _options = new(saveImmediately: true);
        private readonly DispatcherTimer _timer;
        private readonly Stopwatch _sampleClock = Stopwatch.StartNew();

        private OverlayMenuWindow? _menu;
        private IntPtr _robloxWindow;
        private Dictionary<ulong, long> _lastCounts = new();
        private TimeSpan _lastSampleTime;
        private TimeSpan _nextMemorySample = TimeSpan.Zero;
        private DateTime _hintUntil = DateTime.MinValue;
        private bool _wasShowing;
        private bool _disposed;

        private const int VK_RECORD = 0x75; // F6
        private const int VK_PLAY = 0x76;   // F7
        private const int VK_SMART = 0x77;  // F8

        private readonly Macros.SmartMacroRunner _smartRunner;
        private readonly AntiAfk _antiAfk;

        /// <summary>Time played today in the current game (set by the watcher).</summary>
        public Func<TimeSpan?>? GetPlaytimeToday { get; init; }

        // server/playtime counters are looked up now and then, not every frame
        private string? _serverForInfo;
        private DateTime _nextServerLookup = DateTime.MinValue;
        private DateTime _nextPlaytimeLookup = DateTime.MinValue;
        private bool _serverLookupRunning;

        private readonly Macros.MacroRecorder _recorder;
        private readonly Macros.MacroPlayer _player;

        private readonly Func<ActivityData?> _getGame;
        private TimeSpan _nextFramerateLog = TimeSpan.FromSeconds(15);
        private bool _restarting;

        /// <param name="getGame">The game/server this client is in, or null when not in a game.</param>
        public OverlayController(int robloxProcessId, Func<ActivityData?> getGame)
        {
            _getGame = getGame;

            _roblox = Process.GetProcessById(robloxProcessId);

            try { _robloxStarted = _roblox.StartTime; }
            catch { _robloxStarted = DateTime.Now; }

            _frameRate = new FrameRateMonitor(robloxProcessId);

            if (!_frameRate.Start())
                App.Logger.WriteLine(LOG_IDENT, $"FPS counter unavailable: {_frameRate.Error}");

            _remapper = new KeyRemapper(IsRobloxFocusedThreadSafe);
            _hooks = new InputHooks { ShouldCountClicks = IsRobloxFocused, Intercept = _remapper.Intercept };
            _hooks.KeyPressed += OnKeyPressed;

            _hud = new OverlayWindow();
            _hud.ApplyVisibility(App.Settings.Prop.Overlay, _frameRate.Running);

            // macros: the player runs on its own thread, so it uses the thread-safe window helpers
            _recorder = new Macros.MacroRecorder(GetClientAreaThreadSafe, IsRobloxFocusedThreadSafe,
                key => key is VK_RECORD or VK_PLAY or VK_SMART || key == (int)App.Settings.Prop.Overlay.MenuKey);
            _player = new Macros.MacroPlayer(GetClientAreaThreadSafe, IsRobloxFocusedThreadSafe);
            _hooks.RawInput += _recorder.OnInput;

            // anti-AFK: any real input to this window resets its idle timer
            _antiAfk = new AntiAfk(GetRobloxWindow, () => _getGame() is not null);
            _hooks.RawInput += input =>
            {
                if (!input.Injected && IsRobloxFocusedThreadSafe())
                    _antiAfk.NoteActivity();
            };

            // smart macros get their own player so they don't fight with F7 playback
            _smartRunner = new Macros.SmartMacroRunner(GetClientAreaThreadSafe, IsRobloxFocusedThreadSafe,
                new Macros.MacroPlayer(GetClientAreaThreadSafe, IsRobloxFocusedThreadSafe),
                () => Application.Current.Dispatcher.Invoke(() => RestartRoblox(_getGame()?.GetInviteDeeplink(false))));

            _options.ApplyNowRequested += (_, _) => RestartRoblox(_getGame()?.GetInviteDeeplink(false));
            _options.SmallestServerRequested += async (_, _) => await HopToSmallestServer();
            _options.DiscordHideRequested += (_, _) => ToggleDiscordHidden();

            _options.Changed += (_, _) =>
            {
                _hud.ApplyVisibility(App.Settings.Prop.Overlay, _frameRate.Running);
                _hintUntil = DateTime.Now.AddSeconds(5);
            };

            StartMacroWatcher();

            _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(200), DispatcherPriority.Normal, (_, _) => SafeTick(), Application.Current.Dispatcher);
            _timer.Start();

            App.Logger.WriteLine(LOG_IDENT, $"Started for PID {robloxProcessId} (fps={_frameRate.Running})");
        }

        #region Window tracking
        private IntPtr GetRobloxWindow()
        {
            if (_robloxWindow == IntPtr.Zero || !IsWindow(_robloxWindow))
            {
                try
                {
                    _roblox.Refresh();
                    _robloxWindow = _roblox.HasExited ? IntPtr.Zero : _roblox.MainWindowHandle;
                }
                catch
                {
                    _robloxWindow = IntPtr.Zero;
                }
            }

            return _robloxWindow;
        }

        private bool IsRobloxFocused()
        {
            IntPtr window = GetRobloxWindow();
            return window != IntPtr.Zero && GetForegroundWindow() == window;
        }

        private bool IsMenuFocused() => _menu is not null && _menu.IsVisible && GetForegroundWindow() == _menu.Handle;

        // Used from the macro player thread: only reads the cached window handle (refreshed by the UI thread's Tick).
        private bool IsRobloxFocusedThreadSafe()
        {
            IntPtr window = _robloxWindow;
            return window != IntPtr.Zero && GetForegroundWindow() == window;
        }

        private Macros.ScreenRect? GetClientAreaThreadSafe()
        {
            IntPtr window = _robloxWindow;

            if (window == IntPtr.Zero || IsIconic(window) || !GetClientRect(window, out var rect))
                return null;

            var topLeft = new POINT { X = 0, Y = 0 };
            ClientToScreen(window, ref topLeft);
            return new Macros.ScreenRect(topLeft.X, topLeft.Y, rect.Right, rect.Bottom);
        }

        /// <summary>Roblox's client area in screen pixels, plus its DPI scale.</summary>
        private bool TryGetRobloxClientRect(out RECT rect, out double scale)
        {
            rect = default;
            scale = 1;

            IntPtr window = GetRobloxWindow();

            if (window == IntPtr.Zero || IsIconic(window) || !GetClientRect(window, out rect))
                return false;

            var topLeft = new POINT { X = rect.Left, Y = rect.Top };
            ClientToScreen(window, ref topLeft);

            rect = new RECT { Left = topLeft.X, Top = topLeft.Y, Right = topLeft.X + rect.Right, Bottom = topLeft.Y + rect.Bottom };
            scale = GetDpiForWindow(window) / 96.0;
            return rect.Right > rect.Left && rect.Bottom > rect.Top;
        }
        #endregion

        private int _tickErrors;

        /// <summary>
        /// The watcher process also runs Discord presence and app auto-close, so an overlay bug must never crash it:
        /// errors are logged, and the overlay switches itself off if they keep happening.
        /// </summary>
        private void SafeTick()
        {
            try
            {
                Tick();
                _tickErrors = 0;
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);

                if (++_tickErrors >= 5)
                {
                    App.Logger.WriteLine(LOG_IDENT, "Too many errors, stopping the overlay");
                    _timer.Stop();
                    _hud.Hide();
                }
            }
        }

        #region Sync between Roblox windows
        // Every Roblox window has its own overlay process with its own copy of the settings and macro list.
        // These keep them in step: a macro recorded, deleted or selected in one window (or in the Settings app)
        // shows up in all the others within about a second.

        private FileSystemWatcher? _macroWatcher;
        private volatile bool _macrosChanged;
        private DateTime _nextSettingsCheck = DateTime.MinValue;

        private void StartMacroWatcher()
        {
            try
            {
                Directory.CreateDirectory(Macros.MacroStore.Directory);

                _macroWatcher = new FileSystemWatcher(Macros.MacroStore.Directory)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite
                };

                FileSystemEventHandler changed = (_, _) => _macrosChanged = true;
                _macroWatcher.Created += changed;
                _macroWatcher.Changed += changed;
                _macroWatcher.Deleted += changed;
                _macroWatcher.Renamed += (_, _) => _macrosChanged = true;
                _macroWatcher.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
            }
        }

        private void SyncFromDisk()
        {
            bool refresh = false;

            if (_macrosChanged)
            {
                _macrosChanged = false;
                refresh = true;
            }

            if (DateTime.Now >= _nextSettingsCheck)
            {
                _nextSettingsCheck = DateTime.Now.AddSeconds(1);

                if (TryReloadSettings())
                    refresh = true;
            }

            if (refresh)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Synced from disk: {_options.MacroNames.Count} macro(s) -> {Macros.MacroStore.ListNames().Count}, selected '{App.Settings.Prop.Overlay.SelectedMacro}'");
                _options.RefreshFromDisk();
                _hud.ApplyVisibility(App.Settings.Prop.Overlay, _frameRate.Running);
            }
        }

        /// <summary>
        /// Reloads Settings.json if another process saved it. Only reloads once the file parses: a half-written file
        /// would make the settings manager fall back to re-saving this process's older copy over it.
        /// </summary>
        private static bool TryReloadSettings()
        {
            try
            {
                if (!App.Settings.HasFileOnDiskChanged())
                    return false;

                string json = File.ReadAllText(App.Settings.FileLocation);

                if (JsonSerializer.Deserialize<Models.Persistable.Settings>(json) is null)
                    return false;

                return App.Settings.Load(false);
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                return false; // mid-save or locked - try again next second
            }
        }
        #endregion

        private void Tick()
        {
            if (_disposed)
                return;

            SyncFromDisk();

            // a running macro is input too; otherwise nudge the window before Roblox's 20 minute idle kick
            if (_player.Playing || _smartRunner.Running)
                _antiAfk.NoteActivity();
            else
                _antiAfk.Tick();

            var settings = App.Settings.Prop.Overlay;
            bool robloxFocused = IsRobloxFocused();
            bool macroActive = _recorder.Recording || _player.Playing || DateTime.Now < _macroMessageUntil;

            // macro status always shows while a macro is recording/playing, even with the counters switched off
            _hud.SetCountersVisible(settings.Enabled);
            UpdateMacroStatus();

            bool show = (settings.Enabled || macroActive) && (robloxFocused || IsMenuFocused()) && TryGetRobloxClientRect(out _, out _);

            if (!show)
            {
                if (_hud.IsVisible)
                    _hud.Hide();

                _wasShowing = false;
                return;
            }

            if (!_wasShowing)
            {
                // first time the HUD appears this session (or after alt-tabbing back): remind the user about the menu key
                if (_hintUntil == DateTime.MinValue)
                    _hintUntil = DateTime.Now.AddSeconds(10);

                _wasShowing = true;
            }

            UpdateValues(settings);
            _hud.ShowMenuHint(DateTime.Now < _hintUntil);

            if (!_hud.IsVisible)
                _hud.Show();

            PositionHud(settings.Corner);
        }

        private void UpdateValues(OverlaySettings settings)
        {
            var now = _sampleClock.Elapsed;

            if (_frameRate.Running && now - _lastSampleTime >= TimeSpan.FromMilliseconds(500))
            {
                var counts = _frameRate.GetPresentCountsBySwapChain();
                double seconds = (now - _lastSampleTime).TotalSeconds;

                if (_lastSampleTime != TimeSpan.Zero)
                {
                    // the busiest swapchain is the game view; extra ones (UI layers etc.) would inflate a plain total
                    var rates = counts
                        .Select(pair => (pair.Key, Rate: (pair.Value - (_lastCounts.TryGetValue(pair.Key, out long before) ? before : 0)) / seconds))
                        .OrderByDescending(x => x.Rate)
                        .ToList();

                    double fps = rates.Count > 0 ? rates[0].Rate : 0;
                    _hud.SetFramerate(fps);

                    // a periodic line in the log makes frame rate / cap problems diagnosable after the fact
                    if (now >= _nextFramerateLog)
                    {
                        string perSwapChain = string.Join(", ", rates.Select(x => $"{x.Key:X}={x.Rate:0}"));
                        App.Logger.WriteLine(LOG_IDENT, $"FPS {fps:0} (Roblox cap {RobloxUserSettings.ReadFramerateCap()?.ToString() ?? "?"}; per swapchain: {perSwapChain})");
                        _nextFramerateLog = now + TimeSpan.FromSeconds(15);
                    }
                }

                _lastCounts = counts;
                _lastSampleTime = now;
            }

            if (settings.ShowClicksPerSecond)
            {
                var (left, right) = _hooks.GetClicksPerSecond();
                _hud.SetClicks(left, right);
            }

            if (settings.ShowMemory && now >= _nextMemorySample)
            {
                try
                {
                    _roblox.Refresh();
                    _hud.SetMemory(_roblox.WorkingSet64);
                }
                catch { }

                _nextMemorySample = now + TimeSpan.FromSeconds(1);
            }

            if (settings.ShowSessionTime)
                _hud.SetSession(DateTime.Now - _robloxStarted);

            if (settings.ShowClock)
                _hud.SetClock(DateTime.Now);

            if (settings.ShowPlaytime && DateTime.Now >= _nextPlaytimeLookup)
            {
                _nextPlaytimeLookup = DateTime.Now.AddSeconds(15);
                _hud.SetPlaytime(GetPlaytimeToday?.Invoke());
            }

            if (settings.ShowServer)
                UpdateServerInfo();
        }

        /// <summary>
        /// Region (only with "Query server location" on, which sends the server address to ipinfo.io like Bloxstrap
        /// does) and player count (from Roblox's public server list). Refreshed every minute.
        /// </summary>
        private void UpdateServerInfo()
        {
            var game = _getGame();

            if (game is null)
            {
                _hud.SetServer(null);
                _serverForInfo = null;
                return;
            }

            if (_serverLookupRunning || (game.JobId == _serverForInfo && DateTime.Now < _nextServerLookup))
                return;

            _serverForInfo = game.JobId;
            _nextServerLookup = DateTime.Now.AddMinutes(1);
            _serverLookupRunning = true;

            _ = Task.Run(async () =>
            {
                try
                {
                    string? region = App.Settings.Prop.ShowServerDetails && game.MachineAddressValid ? await game.QueryServerLocation() : null;
                    var server = game.ServerType == ServerType.Public ? await RobloxServers.FindServerAsync(game.PlaceId, game.JobId) : null;

                    string text = String.Join("  ·  ", new[]
                    {
                        region,
                        server is null ? null : $"{server.Playing}/{server.MaxPlayers} players"
                    }.Where(part => !String.IsNullOrEmpty(part)));

                    Application.Current.Dispatcher.Invoke(() => _hud.SetServer(String.IsNullOrEmpty(text) ? "?" : text));
                }
                catch (Exception ex)
                {
                    App.Logger.WriteException(LOG_IDENT, ex);
                }
                finally
                {
                    _serverLookupRunning = false;
                }
            });
        }

        private void PositionHud(OverlayCorner corner)
        {
            if (!TryGetRobloxClientRect(out var client, out double scale) || _hud.Handle == IntPtr.Zero)
                return;

            GetWindowRect(_hud.Handle, out var hudRect);
            int width = hudRect.Right - hudRect.Left;
            int height = hudRect.Bottom - hudRect.Top;

            int margin = (int)(12 * scale);
            int topMargin = (int)(64 * scale); // clear of Roblox's top bar buttons

            int x = corner is OverlayCorner.TopLeft or OverlayCorner.BottomLeft ? client.Left + margin : client.Right - width - margin;
            int y = corner is OverlayCorner.TopLeft or OverlayCorner.TopRight ? client.Top + topMargin : client.Bottom - height - margin;

            SetWindowPos(_hud.Handle, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
        }

        #region Menu
        private void OnKeyPressed(int virtualKey)
        {
            if (virtualKey is VK_RECORD or VK_PLAY or VK_SMART)
            {
                try
                {
                    HandleMacroKey(virtualKey);
                }
                catch (Exception ex)
                {
                    App.Logger.WriteException(LOG_IDENT, ex);
                }

                return;
            }

            if (virtualKey != (int)App.Settings.Prop.Overlay.MenuKey)
                return;

            try
            {
                if (_menu is not null && _menu.IsVisible)
                    CloseMenu();
                else if (IsRobloxFocused())
                    OpenMenu();
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
            }
        }

        /// <summary>F6 starts/stops recording, F7 plays/stops the selected macro. Only for the focused client.</summary>
        private void HandleMacroKey(int virtualKey)
        {
            if (virtualKey == VK_SMART)
            {
                if (_smartRunner.Running)
                {
                    _smartRunner.Stop();
                    _hud.SetHiddenFromCapture(false);
                    _hud.SetMacroStatus("Smart macro stopped");
                    _macroMessageUntil = DateTime.Now.AddSeconds(3);
                    return;
                }

                if (_recorder.Recording || _player.Playing || !IsRobloxFocused())
                    return;

                string name = App.Settings.Prop.Overlay.SelectedSmartMacro;
                var smart = String.IsNullOrEmpty(name) ? null : Macros.SmartMacroStore.Load(name);

                if (smart is null || smart.Rules.Count == 0)
                {
                    _hud.SetMacroStatus(smart is null ? "No smart macro selected (Settings → Macros)" : $"{smart.Name} has no rules yet");
                    _macroMessageUntil = DateTime.Now.AddSeconds(4);
                    return;
                }

                _hud.SetHiddenFromCapture(true);
                _smartRunner.Start(smart);
                return;
            }

            if (_smartRunner.Running)
                return; // F8 stops the smart macro first

            if (virtualKey == VK_RECORD)
            {
                if (_recorder.Recording)
                {
                    var macro = _recorder.Stop();

                    if (macro is null)
                    {
                        _hud.SetMacroStatus("Nothing recorded");
                        _macroMessageUntil = DateTime.Now.AddSeconds(3);
                        return;
                    }

                    macro.Name = Macros.MacroStore.NextFreeName();
                    Macros.MacroStore.Save(macro);
                    _options.SelectMacro(macro.Name);

                    App.Logger.WriteLine(LOG_IDENT, $"Recorded '{macro.Name}' ({macro.Steps.Count} steps, {macro.Duration.TotalSeconds:0.0}s)");
                    _hud.SetMacroStatus($"Saved {macro.Name}  ·  F7 to play");
                    _macroMessageUntil = DateTime.Now.AddSeconds(4);
                }
                else if (!_player.Playing && IsRobloxFocused())
                {
                    _recorder.Start();
                }

                return;
            }

            // F7
            if (_player.Playing)
            {
                _player.Stop();
                return;
            }

            if (_recorder.Recording || !IsRobloxFocused())
                return;

            var settings = App.Settings.Prop.Overlay;
            var selected = String.IsNullOrEmpty(settings.SelectedMacro) ? null : Macros.MacroStore.Load(settings.SelectedMacro);

            if (selected is null)
            {
                _hud.SetMacroStatus("No macro selected  ·  F6 to record one");
                _macroMessageUntil = DateTime.Now.AddSeconds(4);
                return;
            }

            App.Logger.WriteLine(LOG_IDENT, $"Playing '{selected.Name}' x{(settings.MacroRuns == 0 ? "∞" : settings.MacroRuns.ToString())} at {settings.MacroSpeed}x");
            _player.Play(selected, settings.MacroRuns, settings.MacroSpeed);
        }

        private DateTime _macroMessageUntil = DateTime.MinValue;

        private bool _rotationActive;
        private DateTime _nextRotationCheck = DateTime.MinValue;

        /// <summary>True while turn-taking runs (its marker file exists and the process that wrote it is alive).</summary>
        private bool IsRotationActive()
        {
            if (DateTime.Now < _nextRotationCheck)
                return _rotationActive;

            _nextRotationCheck = DateTime.Now.AddSeconds(1);

            try
            {
                string marker = Macros.RotationRunner.MarkerFile;
                _rotationActive = File.Exists(marker)
                    && int.TryParse(File.ReadAllText(marker), out int pid)
                    && Utilities.GetProcessesSafe().Any(process => process.Id == pid);
            }
            catch
            {
                _rotationActive = false;
            }

            return _rotationActive;
        }

        private void UpdateMacroStatus()
        {
            if (_smartRunner.Running)
            {
                _hud.SetMacroStatus(_smartRunner.Paused
                    ? $"❚❚ {_smartRunner.MacroName} paused (Roblox not focused)"
                    : $"◆ {_smartRunner.MacroName}: {_smartRunner.Status}  ·  F8 to stop", paused: _smartRunner.Paused);
                return;
            }

            // turn-taking (run from the Instances window) screenshots this client too
            _hud.SetHiddenFromCapture(IsRotationActive());

            if (_recorder.Recording)
                _hud.SetMacroStatus($"● REC {_recorder.Elapsed:m\\:ss}  ·  F6 to stop", recording: true);
            else if (_player.Playing)
            {
                string runs = _player.Runs == 0 ? $"{_player.Run}/∞" : $"{_player.Run}/{_player.Runs}";
                _hud.SetMacroStatus(_player.Paused
                    ? $"❚❚ {_player.MacroName} paused (Roblox not focused)"
                    : $"▶ {_player.MacroName}  {runs}  ·  F7 to stop", paused: _player.Paused);
            }
            else if (DateTime.Now >= _macroMessageUntil)
                _hud.SetMacroStatus(null);
        }

        /// <summary>Puts the current game on (or takes it off) the Discord hidden list. Every window picks it up.</summary>
        private void ToggleDiscordHidden()
        {
            if (_getGame() is not ActivityData game || game.UniverseId == 0)
            {
                _options.DiscordHideText = "Join a game first";
                return;
            }

            var hidden = App.Settings.Prop.Gameplay.DiscordHiddenGames;

            if (!hidden.Remove(game.UniverseId))
                hidden.Add(game.UniverseId);

            App.Settings.Save();
            UpdateDiscordHideText();
        }

        private void UpdateDiscordHideText()
        {
            bool hidden = _getGame() is ActivityData game && App.Settings.Prop.Gameplay.DiscordHiddenGames.Contains(game.UniverseId);
            _options.DiscordHideText = hidden ? "Show this game on Discord again" : "Hide this game from Discord";
        }

        private void OpenMenu()
        {
            UpdateDiscordHideText();

            if (_menu is null)
            {
                _menu = new OverlayMenuWindow(_options);
                _menu.CloseRequested += (_, _) => CloseMenu();

                // clicking back into Roblox closes the menu
                _menu.Deactivated += (_, _) =>
                {
                    if (_menu.IsVisible && IsRobloxFocused())
                        CloseMenu(refocusRoblox: false);
                };
            }

            _menu.Show();

            if (TryGetRobloxClientRect(out var client, out _))
            {
                GetWindowRect(_menu.Handle, out var menuRect);
                int width = menuRect.Right - menuRect.Left;
                int height = menuRect.Bottom - menuRect.Top;
                int x = client.Left + (client.Right - client.Left - width) / 2;
                int y = client.Top + Math.Max(0, (client.Bottom - client.Top - height) / 2);

                SetWindowPos(_menu.Handle, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE);
            }

            _menu.Activate();
            SetForegroundWindow(_menu.Handle);
        }

        private void CloseMenu(bool refocusRoblox = true)
        {
            if (_menu is null || !_menu.IsVisible)
                return;

            _menu.Hide();

            if (refocusRoblox)
            {
                IntPtr window = GetRobloxWindow();

                if (window != IntPtr.Zero)
                    SetForegroundWindow(window);
            }
        }
        #endregion

        /// <summary>
        /// Applies a new frame rate cap: closes Roblox (letting it save its settings), then launches it again through
        /// PSYstrap - which writes the cap before Roblox starts - straight back into the same server when in a game.
        /// </summary>
        private async Task HopToSmallestServer()
        {
            var game = _getGame();

            if (game is null || game.PlaceId == 0)
            {
                _options.ServerHopStatus = "Join a game first - this hops within the game you're in.";
                return;
            }

            _options.ServerHopStatus = "Looking for the smallest server...";
            var server = await RobloxServers.FindSmallestAsync(game.PlaceId, game.JobId);

            if (server is null)
            {
                _options.ServerHopStatus = "Couldn't find another public server with space (or Roblox didn't answer).";
                return;
            }

            _options.ServerHopStatus = $"Joining a server with {server.Playing}/{server.MaxPlayers} players...";
            RestartRoblox(RobloxServers.GetJoinLink(game.PlaceId, server.Id));
        }

        /// <param name="joinLink">roblox:// link to open after the restart; null just reopens Roblox.</param>
        private async void RestartRoblox(string? joinLink)
        {
            if (_restarting)
                return;

            _restarting = true;

            try
            {
                string? rejoinLink = joinLink;
                App.Logger.WriteLine(LOG_IDENT, $"Restarting Roblox (join link={(rejoinLink is not null)})");

                CloseMenu(refocusRoblox: false);

                // a normal close lets Roblox write its settings file first; ours is applied on the next launch
                _roblox.CloseMainWindow();

                bool exited = await Task.Run(() => _roblox.WaitForExit(10000));

                if (!exited)
                {
                    App.Logger.WriteLine(LOG_IDENT, "Roblox didn't close in time, ending it");
                    _roblox.Kill();
                    await Task.Run(() => _roblox.WaitForExit(5000));
                }

                string args = rejoinLink is null ? "-player" : $"-player \"{rejoinLink}\"";
                Process.Start(Paths.Process, args);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                _restarting = false;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            // releases any keys/buttons a playing macro is holding
            try { _smartRunner.Stop(); } catch (Exception ex) { App.Logger.WriteException(LOG_IDENT, ex); }
            try { _player.Stop(); } catch (Exception ex) { App.Logger.WriteException(LOG_IDENT, ex); }

            void Cleanup()
            {
                _timer.Stop();
                _macroWatcher?.Dispose();
                _hooks.Dispose();
                _hud.Close();
                _menu?.ForceClose();
            }

            try
            {
                if (Application.Current.Dispatcher.CheckAccess())
                    Cleanup();
                else
                    Application.Current.Dispatcher.Invoke(Cleanup);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
            }

            // stops the ETW session; must happen even if the UI cleanup failed
            _frameRate.Dispose();
            _roblox.Dispose();
        }

        #region Native
        private static readonly IntPtr HWND_TOPMOST = new(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hWnd, ref POINT point);
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
        #endregion
    }
}
