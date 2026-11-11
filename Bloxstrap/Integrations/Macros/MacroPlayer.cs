using System.Runtime.InteropServices;

using Bloxstrap.Models.Macros;

namespace Bloxstrap.Integrations.Macros
{
    /// <summary>
    /// Plays a macro into the focused Roblox window with SendInput (the same path real keyboards and mice use).
    /// Playback pauses whenever the target window isn't focused, so input never lands in another app, and every key or
    /// button still held is released when playback ends or is stopped.
    /// </summary>
    public sealed class MacroPlayer
    {
        private readonly Func<ScreenRect?> _getClientArea;
        private readonly Func<bool> _isTargetFocused;
        private readonly HashSet<(bool isKey, int code)> _held = new();
        private readonly object _heldLock = new();

        private CancellationTokenSource? _cancel;
        private Thread? _thread;

        public bool Playing => _thread is { IsAlive: true };

        public bool Paused { get; private set; }

        public string MacroName { get; private set; } = "";

        /// <summary>1-based run currently playing.</summary>
        public int Run { get; private set; }

        /// <summary>Total runs, or 0 for "until stopped".</summary>
        public int Runs { get; private set; }

        public event EventHandler? Finished;

        public MacroPlayer(Func<ScreenRect?> getClientArea, Func<bool> isTargetFocused)
        {
            _getClientArea = getClientArea;
            _isTargetFocused = isTargetFocused;
        }

        /// <param name="runs">How many times to play it; 0 = until stopped.</param>
        public void Play(Macro macro, int runs, double speed)
        {
            Stop();

            MacroName = macro.Name;
            Runs = runs;
            Run = 0;
            _cancel = new CancellationTokenSource();
            var token = _cancel.Token;
            speed = Math.Clamp(speed, 0.1, 10);

            _thread = new Thread(() => PlayLoop(macro, runs, speed, token))
            {
                IsBackground = true,
                Name = "PSYstrap macro player"
            };

            _thread.Start();
        }

        public void Stop()
        {
            _cancel?.Cancel();
            _thread?.Join(2000);
            _thread = null;
        }

        private void PlayLoop(Macro macro, int runs, double speed, CancellationToken token)
        {
            try
            {
                for (Run = 1; runs == 0 || Run <= runs; Run++)
                {
                    foreach (var step in macro.Steps)
                    {
                        if (!Wait(step.Delay / speed, token))
                            return;

                        Execute(step);
                    }

                    ReleaseHeld(); // each run starts clean

                    if (macro.Steps.Count == 0 || !Wait(50, token))
                        return;
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("MacroPlayer", ex);
            }
            finally
            {
                ReleaseHeld();
                Paused = false;
                Finished?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>Waits the step delay, holding the clock while the target isn't focused. False if cancelled.</summary>
        private bool Wait(double milliseconds, CancellationToken token)
        {
            var clock = Stopwatch.StartNew();
            double waited = 0;

            while (true)
            {
                if (token.IsCancellationRequested)
                    return false;

                if (!_isTargetFocused())
                {
                    if (!Paused)
                    {
                        Paused = true;
                        ReleaseHeld(); // don't leave keys held down in whatever got focus instead
                    }

                    clock.Restart();
                    Thread.Sleep(50);
                    continue;
                }

                Paused = false;
                waited += clock.Elapsed.TotalMilliseconds;
                clock.Restart();

                double remaining = milliseconds - waited;

                if (remaining <= 0)
                    return true;

                Thread.Sleep(remaining > 20 ? (int)(remaining - 10) : 1);
            }
        }

        private void Execute(MacroStep step)
        {
            switch (step.Kind)
            {
                case MacroStepKind.KeyDown:
                    SendKey(step, up: false);
                    lock (_heldLock) _held.Add((true, step.Key));
                    break;

                case MacroStepKind.KeyUp:
                    SendKey(step, up: true);
                    lock (_heldLock) _held.Remove((true, step.Key));
                    break;

                case MacroStepKind.MouseMove:
                    MoveTo(step.X, step.Y);
                    break;

                case MacroStepKind.MouseDown:
                    MoveTo(step.X, step.Y);
                    SendMouse(ButtonFlag(step.Button, down: true), 0);
                    lock (_heldLock) _held.Add((false, (int)step.Button));
                    break;

                case MacroStepKind.MouseUp:
                    MoveTo(step.X, step.Y);
                    SendMouse(ButtonFlag(step.Button, down: false), 0);
                    lock (_heldLock) _held.Remove((false, (int)step.Button));
                    break;

                case MacroStepKind.Wheel:
                    SendMouse(MOUSEEVENTF_WHEEL, step.WheelDelta);
                    break;
            }
        }

        private void ReleaseHeld()
        {
            List<(bool isKey, int code)> held;

            lock (_heldLock)
            {
                held = _held.ToList();
                _held.Clear();
            }

            foreach (var (isKey, code) in held)
            {
                if (isKey)
                    SendKey(new MacroStep { Key = code, ScanCode = (int)MapVirtualKeyW((uint)code, 0) }, up: true);
                else
                    SendMouse(ButtonFlag((MacroMouseButton)code, down: false), 0);
            }
        }

        #region Single actions (used by smart macros)
        /// <summary>Clicks at a point given as fractions of the client area.</summary>
        public void Click(double fx, double fy, MacroMouseButton button = MacroMouseButton.Left)
        {
            MoveTo(fx, fy);
            Thread.Sleep(30);
            SendMouse(ButtonFlag(button, down: true), 0);
            Thread.Sleep(50);
            SendMouse(ButtonFlag(button, down: false), 0);
        }

        /// <summary>Taps a key (virtual-key code).</summary>
        public void Tap(int virtualKey)
        {
            var step = new MacroStep { Key = virtualKey, ScanCode = (int)MapVirtualKeyW((uint)virtualKey, 0) };
            SendKey(step, up: false);
            Thread.Sleep(60);
            SendKey(step, up: true);
        }

        /// <summary>Plays a macro once and blocks until it's done (or stopped).</summary>
        public void PlayAndWait(Macro macro, double speed, CancellationToken token)
        {
            Play(macro, 1, speed);

            while (Playing && !token.IsCancellationRequested)
                Thread.Sleep(50);

            if (token.IsCancellationRequested)
                Stop();
        }
        #endregion

        #region SendInput
        private void MoveTo(double fx, double fy)
        {
            if (_getClientArea() is not ScreenRect area)
                return;

            int x = area.Left + (int)Math.Round(fx * area.Width);
            int y = area.Top + (int)Math.Round(fy * area.Height);

            // absolute coordinates are 0..65535 across the whole virtual desktop
            int left = GetSystemMetrics(SM_XVIRTUALSCREEN), top = GetSystemMetrics(SM_YVIRTUALSCREEN);
            int width = GetSystemMetrics(SM_CXVIRTUALSCREEN), height = GetSystemMetrics(SM_CYVIRTUALSCREEN);

            var input = new INPUT { type = INPUT_MOUSE };
            input.u.mi.dx = (int)((x - left) * 65535L / Math.Max(1, width - 1));
            input.u.mi.dy = (int)((y - top) * 65535L / Math.Max(1, height - 1));
            input.u.mi.dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK;
            SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        }

        private static void SendMouse(uint flags, int data)
        {
            var input = new INPUT { type = INPUT_MOUSE };
            input.u.mi.dwFlags = flags;
            input.u.mi.mouseData = unchecked((uint)data);
            SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        }

        private static void SendKey(MacroStep step, bool up)
        {
            ushort scan = step.ScanCode != 0 ? (ushort)step.ScanCode : (ushort)MapVirtualKeyW((uint)step.Key, 0);

            var input = new INPUT { type = INPUT_KEYBOARD };
            input.u.ki.wVk = (ushort)step.Key;
            input.u.ki.wScan = scan;
            input.u.ki.dwFlags = KEYEVENTF_SCANCODE | (up ? KEYEVENTF_KEYUP : 0) | (step.Extended ? KEYEVENTF_EXTENDEDKEY : 0);
            SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        }

        private static uint ButtonFlag(MacroMouseButton button, bool down) => button switch
        {
            MacroMouseButton.Right => down ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP,
            MacroMouseButton.Middle => down ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP,
            _ => down ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP
        };

        private const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1;
        private const uint MOUSEEVENTF_MOVE = 0x0001, MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008, MOUSEEVENTF_RIGHTUP = 0x0010;
        private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020, MOUSEEVENTF_MIDDLEUP = 0x0040;
        private const uint MOUSEEVENTF_WHEEL = 0x0800, MOUSEEVENTF_VIRTUALDESK = 0x4000, MOUSEEVENTF_ABSOLUTE = 0x8000;
        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001, KEYEVENTF_KEYUP = 0x0002, KEYEVENTF_SCANCODE = 0x0008;
        private const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT { public uint type; public InputUnion u; }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint count, INPUT[] inputs, int size);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKeyW(uint code, uint mapType);
        #endregion
    }
}
