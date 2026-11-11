using Bloxstrap.Models.Macros;

namespace Bloxstrap.Integrations.Macros
{
    /// <summary>A window's client area in screen pixels.</summary>
    public readonly record struct ScreenRect(int Left, int Top, int Width, int Height);

    /// <summary>
    /// Records keyboard and mouse input made while the target Roblox window is focused.
    /// Feed it InputHooks.RawInput events on the UI thread.
    /// </summary>
    public sealed class MacroRecorder
    {
        private readonly Func<ScreenRect?> _getClientArea;
        private readonly Func<bool> _isTargetFocused;
        private readonly Func<int, bool> _isIgnoredKey;
        private readonly HashSet<int> _keysDown = new();

        private Macro? _macro;
        private long _lastStepTicks;
        private long _startTicks;
        private long _lastMoveTicks;
        private double _lastX = -1, _lastY = -1;

        public bool Recording => _macro is not null;

        public TimeSpan Elapsed => Recording ? TimeSpan.FromMilliseconds(Environment.TickCount64 - _startTicks) : TimeSpan.Zero;

        public int StepCount => _macro?.Steps.Count ?? 0;

        /// <param name="isIgnoredKey">Keys never recorded, e.g. the record/play hotkeys and the menu key.</param>
        public MacroRecorder(Func<ScreenRect?> getClientArea, Func<bool> isTargetFocused, Func<int, bool> isIgnoredKey)
        {
            _getClientArea = getClientArea;
            _isTargetFocused = isTargetFocused;
            _isIgnoredKey = isIgnoredKey;
        }

        public void Start()
        {
            _macro = new Macro();
            _keysDown.Clear();
            _startTicks = _lastStepTicks = Environment.TickCount64;
            _lastX = _lastY = -1;
        }

        /// <summary>Stops recording and returns the macro, or null if nothing was recorded.</summary>
        public Macro? Stop()
        {
            var macro = _macro;
            _macro = null;

            if (macro is null || macro.Steps.Count == 0)
                return null;

            // keys still held when recording stopped get released at the end, so playback never leaves one stuck
            foreach (var step in macro.Steps.Where(step => step.Kind == MacroStepKind.KeyDown).ToList())
            {
                if (_keysDown.Contains(step.Key))
                {
                    macro.Steps.Add(new MacroStep { Kind = MacroStepKind.KeyUp, Key = step.Key, ScanCode = step.ScanCode, Extended = step.Extended });
                    _keysDown.Remove(step.Key);
                }
            }

            return macro;
        }

        public void OnInput(InputHooks.RawInputEvent input)
        {
            if (_macro is null || input.Injected || !_isTargetFocused())
                return;

            bool isKey = input.Kind is MacroStepKind.KeyDown or MacroStepKind.KeyUp;

            if (isKey && _isIgnoredKey(input.Key))
                return;

            long now = Environment.TickCount64;
            var step = new MacroStep { Kind = input.Kind };

            if (isKey)
            {
                // ignore auto-repeat: only the first KeyDown of a held key is recorded
                if (input.Kind == MacroStepKind.KeyDown && !_keysDown.Add(input.Key))
                    return;

                if (input.Kind == MacroStepKind.KeyUp && !_keysDown.Remove(input.Key))
                    return;

                step.Key = input.Key;
                step.ScanCode = input.ScanCode;
                step.Extended = input.Extended;
            }
            else
            {
                if (_getClientArea() is not ScreenRect area || area.Width <= 0 || area.Height <= 0)
                    return;

                double x = Math.Clamp((input.X - area.Left) / (double)area.Width, 0, 1);
                double y = Math.Clamp((input.Y - area.Top) / (double)area.Height, 0, 1);

                if (input.Kind == MacroStepKind.MouseMove)
                {
                    // ~60 samples a second is plenty and keeps files small
                    if (now - _lastMoveTicks < 16 || (Math.Abs(x - _lastX) < 0.0005 && Math.Abs(y - _lastY) < 0.0005))
                        return;

                    _lastMoveTicks = now;
                }

                _lastX = x;
                _lastY = y;
                step.X = x;
                step.Y = y;
                step.Button = input.Button;
                step.WheelDelta = input.WheelDelta;
            }

            step.Delay = (int)Math.Min(int.MaxValue, now - _lastStepTicks);
            _lastStepTicks = now;
            _macro.Steps.Add(step);
        }
    }
}
