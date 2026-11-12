using System.Runtime.InteropServices;

namespace Bloxstrap.Integrations
{
    /// <summary>
    /// PSYstrap: low-level keyboard/mouse hooks for the overlay. They count clicks per second, notice the menu key, feed
    /// the macro recorder, and let the key remapper swap one input for another. Nothing is blocked unless
    /// <see cref="Intercept"/> says so.
    /// Must be created on a thread with a message loop (the WPF UI thread); callbacks arrive on that thread.
    /// </summary>
    public sealed class InputHooks : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WH_MOUSE_LL = 14;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_RBUTTONDOWN = 0x0204;

        private readonly HookProc _keyboardProc;
        private readonly HookProc _mouseProc;
        private readonly IntPtr _keyboardHook;
        private readonly IntPtr _mouseHook;

        private readonly Queue<long> _leftClicks = new();
        private readonly Queue<long> _rightClicks = new();
        private readonly HashSet<int> _keysDown = new();

        /// <summary>Fired once per physical key press (auto-repeat and injected input ignored), with the virtual-key code.</summary>
        public event Action<int>? KeyPressed;

        /// <summary>Every keyboard and mouse event, for the macro recorder. Positions are screen pixels.</summary>
        public event Action<RawInputEvent>? RawInput;

        public readonly record struct RawInputEvent(
            Models.Macros.MacroStepKind Kind, int Key, int ScanCode, bool Extended,
            Models.Macros.MacroMouseButton Button, int X, int Y, int WheelDelta, bool Injected);

        /// <summary>Clicks only count while this returns true (e.g. while Roblox is focused).</summary>
        public Func<bool>? ShouldCountClicks { get; set; }

        /// <summary>
        /// Sees every real (not injected) key and button press first. Returning true swallows it: nothing else on the PC
        /// gets it, and the other events here don't fire for it.
        /// </summary>
        public Func<RawInputEvent, bool>? Intercept { get; set; }

        /// <summary>dwExtraInfo PSYstrap puts on remapped input, so it counts as the user's own (recorder, anti-AFK).</summary>
        public static readonly IntPtr RemapMarker = new(0x5059_5352); // "PYSR"

        public InputHooks()
        {
            _keyboardProc = KeyboardCallback;
            _mouseProc = MouseCallback;

            IntPtr module = GetModuleHandleW(null);
            _keyboardHook = SetWindowsHookExW(WH_KEYBOARD_LL, _keyboardProc, module, 0);
            _mouseHook = SetWindowsHookExW(WH_MOUSE_LL, _mouseProc, module, 0);

            if (_keyboardHook == IntPtr.Zero || _mouseHook == IntPtr.Zero)
                App.Logger.WriteLine("InputHooks", $"Failed to install hooks (error {Marshal.GetLastWin32Error()})");
        }

        /// <summary>Left and right clicks in the last second.</summary>
        public (int Left, int Right) GetClicksPerSecond()
        {
            long cutoff = Environment.TickCount64 - 1000;

            while (_leftClicks.Count > 0 && _leftClicks.Peek() < cutoff)
                _leftClicks.Dequeue();

            while (_rightClicks.Count > 0 && _rightClicks.Peek() < cutoff)
                _rightClicks.Dequeue();

            return (_leftClicks.Count, _rightClicks.Count);
        }

        private IntPtr KeyboardCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int message = (int)wParam;
                int vkCode = Marshal.ReadInt32(lParam);        // KBDLLHOOKSTRUCT.vkCode
                int scanCode = Marshal.ReadInt32(lParam, 4);   // .scanCode
                int flags = Marshal.ReadInt32(lParam, 8);      // .flags
                IntPtr extraInfo = Marshal.ReadIntPtr(lParam, 16); // .dwExtraInfo
                bool injected = (flags & 0x10) != 0;           // LLKHF_INJECTED
                bool extended = (flags & 0x01) != 0;           // LLKHF_EXTENDED
                bool down = message is WM_KEYDOWN or WM_SYSKEYDOWN;

                if (injected && extraInfo == RemapMarker)
                    injected = false; // a remapped key is still the user pressing something

                var keyEvent = new RawInputEvent(down ? Models.Macros.MacroStepKind.KeyDown : Models.Macros.MacroStepKind.KeyUp,
                    vkCode, scanCode, extended, default, 0, 0, 0, injected);

                if ((flags & 0x10) == 0 && SafeIntercept(keyEvent))
                    return (IntPtr)1;

                if (down)
                {
                    if (_keysDown.Add(vkCode) && !injected)
                        KeyPressed?.Invoke(vkCode);
                }
                else if (message is WM_KEYUP or WM_SYSKEYUP)
                {
                    _keysDown.Remove(vkCode);
                }

                RawInput?.Invoke(keyEvent);
            }

            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        private IntPtr MouseCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int message = (int)wParam;

                // MSLLHOOKSTRUCT: pt.x @0, pt.y @4, mouseData @8, flags @12, time @16, dwExtraInfo @24 (x64)
                int x = Marshal.ReadInt32(lParam);
                int y = Marshal.ReadInt32(lParam, 4);
                int mouseData = Marshal.ReadInt32(lParam, 8);
                bool realInjected = (Marshal.ReadInt32(lParam, 12) & 0x01) != 0; // LLMHF_INJECTED
                bool injected = realInjected && Marshal.ReadIntPtr(lParam, IntPtr.Size == 8 ? 24 : 20) != RemapMarker;
                int xButton = (mouseData >> 16) & 0xFFFF;
                var sideButton = xButton == 2 ? Models.Macros.MacroMouseButton.XButton2 : Models.Macros.MacroMouseButton.XButton1;

                (Models.Macros.MacroStepKind kind, Models.Macros.MacroMouseButton button)? mapped = message switch
                {
                    0x0200 => (Models.Macros.MacroStepKind.MouseMove, default),
                    0x0201 => (Models.Macros.MacroStepKind.MouseDown, Models.Macros.MacroMouseButton.Left),
                    0x0202 => (Models.Macros.MacroStepKind.MouseUp, Models.Macros.MacroMouseButton.Left),
                    0x0204 => (Models.Macros.MacroStepKind.MouseDown, Models.Macros.MacroMouseButton.Right),
                    0x0205 => (Models.Macros.MacroStepKind.MouseUp, Models.Macros.MacroMouseButton.Right),
                    0x0207 => (Models.Macros.MacroStepKind.MouseDown, Models.Macros.MacroMouseButton.Middle),
                    0x0208 => (Models.Macros.MacroStepKind.MouseUp, Models.Macros.MacroMouseButton.Middle),
                    0x020A => (Models.Macros.MacroStepKind.Wheel, default),
                    0x020B => (Models.Macros.MacroStepKind.MouseDown, sideButton),
                    0x020C => (Models.Macros.MacroStepKind.MouseUp, sideButton),
                    _ => null
                };

                RawInputEvent? mouseEvent = mapped is null ? null
                    : new RawInputEvent(mapped.Value.kind, 0, 0, false, mapped.Value.button, x, y, (short)(mouseData >> 16), injected);

                if (!realInjected
                    && mouseEvent is { Kind: Models.Macros.MacroStepKind.MouseDown or Models.Macros.MacroStepKind.MouseUp } button
                    && SafeIntercept(button))
                    return (IntPtr)1;

                if (ShouldCountClicks?.Invoke() ?? true)
                {
                    if (message == WM_LBUTTONDOWN)
                        _leftClicks.Enqueue(Environment.TickCount64);
                    else if (message == WM_RBUTTONDOWN)
                        _rightClicks.Enqueue(Environment.TickCount64);
                }

                if (mouseEvent is not null)
                    RawInput?.Invoke(mouseEvent.Value);
            }

            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        /// <summary>An exception escaping a hook callback would take the whole process down, so never let one out.</summary>
        private bool SafeIntercept(RawInputEvent input)
        {
            if (Intercept is null)
                return false;

            try
            {
                return Intercept(input);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("InputHooks::Intercept", ex);
                return false;
            }
        }

        public void Dispose()
        {
            if (_keyboardHook != IntPtr.Zero)
                UnhookWindowsHookEx(_keyboardHook);

            if (_mouseHook != IntPtr.Zero)
                UnhookWindowsHookEx(_mouseHook);
        }

        private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookExW(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandleW(string? lpModuleName);
    }
}
