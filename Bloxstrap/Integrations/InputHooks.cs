using System.Runtime.InteropServices;

namespace Bloxstrap.Integrations
{
    /// <summary>
    /// PSYstrap: low-level keyboard/mouse hooks for the overlay. They only observe input - every event is passed on
    /// untouched - to count clicks per second and to notice the menu key.
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
                bool injected = (flags & 0x10) != 0;           // LLKHF_INJECTED
                bool extended = (flags & 0x01) != 0;           // LLKHF_EXTENDED
                bool down = message is WM_KEYDOWN or WM_SYSKEYDOWN;

                if (down)
                {
                    if (_keysDown.Add(vkCode) && !injected)
                        KeyPressed?.Invoke(vkCode);
                }
                else if (message is WM_KEYUP or WM_SYSKEYUP)
                {
                    _keysDown.Remove(vkCode);
                }

                RawInput?.Invoke(new RawInputEvent(down ? Models.Macros.MacroStepKind.KeyDown : Models.Macros.MacroStepKind.KeyUp,
                    vkCode, scanCode, extended, default, 0, 0, 0, injected));
            }

            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        private IntPtr MouseCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int message = (int)wParam;

                if (ShouldCountClicks?.Invoke() ?? true)
                {
                    if (message == WM_LBUTTONDOWN)
                        _leftClicks.Enqueue(Environment.TickCount64);
                    else if (message == WM_RBUTTONDOWN)
                        _rightClicks.Enqueue(Environment.TickCount64);
                }

                if (RawInput is not null)
                {
                    // MSLLHOOKSTRUCT: pt.x @0, pt.y @4, mouseData @8, flags @12
                    int x = Marshal.ReadInt32(lParam);
                    int y = Marshal.ReadInt32(lParam, 4);
                    int mouseData = Marshal.ReadInt32(lParam, 8);
                    bool injected = (Marshal.ReadInt32(lParam, 12) & 0x01) != 0; // LLMHF_INJECTED

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
                        _ => null
                    };

                    if (mapped is not null)
                        RawInput.Invoke(new RawInputEvent(mapped.Value.kind, 0, 0, false, mapped.Value.button, x, y, (short)(mouseData >> 16), injected));
                }
            }

            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
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
