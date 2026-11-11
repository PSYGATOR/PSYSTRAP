using System.Windows.Input;

using Bloxstrap.Models.Macros;

namespace Bloxstrap.Integrations.Macros
{
    /// <summary>
    /// Runs a smart macro on one client (F8 in the overlay): every IntervalMs it captures the Roblox window and fires
    /// the first ready rule. Pauses while Roblox isn't the focused window, so actions never land in another app.
    /// </summary>
    public sealed class SmartMacroRunner
    {
        private const string LOG_IDENT = "SmartMacroRunner";

        private readonly Func<ScreenRect?> _getClientArea;
        private readonly Func<bool> _isTargetFocused;
        private readonly MacroPlayer _player;
        private readonly Action _rejoin;

        private CancellationTokenSource? _cancel;
        private Thread? _thread;

        public bool Running => _thread is { IsAlive: true };

        public string MacroName { get; private set; } = "";

        /// <summary>Short live status for the overlay, e.g. which rule fired last.</summary>
        public string Status { get; private set; } = "";

        public bool Paused { get; private set; }

        public SmartMacroRunner(Func<ScreenRect?> getClientArea, Func<bool> isTargetFocused, MacroPlayer player, Action rejoin)
        {
            _getClientArea = getClientArea;
            _isTargetFocused = isTargetFocused;
            _player = player;
            _rejoin = rejoin;
        }

        public void Start(SmartMacro macro)
        {
            Stop();

            MacroName = macro.Name;
            Status = "watching";
            _cancel = new CancellationTokenSource();
            var token = _cancel.Token;

            _thread = new Thread(() => Loop(macro, token)) { IsBackground = true, Name = "PSYstrap smart macro" };
            _thread.Start();

            App.Logger.WriteLine(LOG_IDENT, $"Started '{macro.Name}' ({macro.Detectors.Count} detectors, {macro.Rules.Count} rules)");
        }

        public void Stop()
        {
            _cancel?.Cancel();
            _player.Stop();
            _thread?.Join(3000);
            _thread = null;
        }

        private void Loop(SmartMacro macro, CancellationToken token)
        {
            var brain = new SmartMacroBrain(macro);

            try
            {
                while (!token.IsCancellationRequested)
                {
                    if (!_isTargetFocused() || _getClientArea() is not ScreenRect area || area.Width < 16 || area.Height < 16)
                    {
                        Paused = true;
                        brain.ResetTiming();
                        Thread.Sleep(250);
                        continue;
                    }

                    Paused = false;

                    var decision = brain.Decide(ScreenFrame.Capture(area));

                    if (decision is { } fire)
                    {
                        Status = fire.Description;

                        bool keepGoing = SmartMacroBrain.Execute(fire.Rule, fire.Result, _player, _rejoin, token, out string? problem);

                        if (problem is not null)
                            Status = problem;

                        if (!keepGoing)
                            return;

                        brain.MarkFired(fire.Rule);
                    }

                    Thread.Sleep(Math.Max(100, macro.IntervalMs));
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                Status = "stopped after an error (see log)";
            }
            finally
            {
                Paused = false;
                App.Logger.WriteLine(LOG_IDENT, $"Stopped '{macro.Name}'");
            }
        }

        /// <summary>"E", "Space", "F", "D1"/"1", "LeftShift"... - WPF key names.</summary>
        public static int? ParseKey(string name)
        {
            name = name.Trim();

            if (name.Length == 1 && char.IsDigit(name[0]))
                name = "D" + name;

            return Enum.TryParse<Key>(name, ignoreCase: true, out var key) && key != Key.None
                ? KeyInterop.VirtualKeyFromKey(key)
                : null;
        }
    }
}
