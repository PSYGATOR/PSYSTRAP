using System.Runtime.InteropServices;
using System.Windows.Input;

using Bloxstrap.Models.Macros;

namespace Bloxstrap.Integrations
{
    /// <summary>
    /// PSYstrap: swaps one key or mouse button for another while Roblox is focused (Settings > Gameplay > Remaps).
    /// The real press is swallowed by the hook and the new one is sent with SendInput, which is the same as pressing it.
    /// Releases always go through once the press was remapped, even if Roblox lost focus in between, so nothing sticks.
    /// </summary>
    public sealed class KeyRemapper
    {
        public readonly record struct InputId(bool IsMouse, int Code);

        private readonly Func<bool> _isTargetFocused;
        private readonly Dictionary<InputId, InputId> _held = new();

        private List<KeyRemap>? _source;
        private int _sourceCount = -1;
        private Dictionary<InputId, InputId> _map = new();

        public KeyRemapper(Func<bool> isTargetFocused)
        {
            _isTargetFocused = isTargetFocused;
        }

        /// <summary>For <see cref="InputHooks.Intercept"/>. True = swallow the real input.</summary>
        public bool Intercept(InputHooks.RawInputEvent input)
        {
            InputId id;
            bool down;

            switch (input.Kind)
            {
                case MacroStepKind.KeyDown: id = new(false, input.Key); down = true; break;
                case MacroStepKind.KeyUp: id = new(false, input.Key); down = false; break;
                case MacroStepKind.MouseDown: id = new(true, (int)input.Button); down = true; break;
                case MacroStepKind.MouseUp: id = new(true, (int)input.Button); down = false; break;
                default: return false;
            }

            if (!down)
            {
                if (!_held.Remove(id, out var heldTarget))
                    return false;

                Send(heldTarget, down: false);
                return true;
            }

            if (_held.TryGetValue(id, out var repeatTarget))
            {
                // key auto-repeat: keys repeat too, mouse buttons don't
                if (!repeatTarget.IsMouse)
                    Send(repeatTarget, down: true);

                return true;
            }

            RefreshMap();

            if (!_map.TryGetValue(id, out var target) || !_isTargetFocused())
                return false;

            _held[id] = target;
            Send(target, down: true);
            return true;
        }

        private void RefreshMap()
        {
            var remaps = App.Settings.Prop.Gameplay.Remaps;

            if (ReferenceEquals(remaps, _source) && remaps.Count == _sourceCount)
                return;

            var map = new Dictionary<InputId, InputId>();

            foreach (var remap in remaps)
            {
                if (Parse(remap.From) is InputId from && Parse(remap.To) is InputId to && from != to)
                    map[from] = to;
            }

            _map = map;
            _source = remaps;
            _sourceCount = remaps.Count;
        }

        #region Names
        private static readonly Dictionary<string, MacroMouseButton> MouseNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["MouseLeft"] = MacroMouseButton.Left, ["LeftClick"] = MacroMouseButton.Left, ["LMB"] = MacroMouseButton.Left,
            ["MouseRight"] = MacroMouseButton.Right, ["RightClick"] = MacroMouseButton.Right, ["RMB"] = MacroMouseButton.Right,
            ["MouseMiddle"] = MacroMouseButton.Middle, ["MiddleClick"] = MacroMouseButton.Middle, ["MMB"] = MacroMouseButton.Middle,
            ["Mouse4"] = MacroMouseButton.XButton1, ["XButton1"] = MacroMouseButton.XButton1, ["MouseBack"] = MacroMouseButton.XButton1,
            ["Mouse5"] = MacroMouseButton.XButton2, ["XButton2"] = MacroMouseButton.XButton2, ["MouseForward"] = MacroMouseButton.XButton2,
        };

        /// <summary>The names the settings page offers for mouse buttons.</summary>
        public static readonly string[] MouseButtonNames = { "MouseLeft", "MouseRight", "MouseMiddle", "Mouse4", "Mouse5" };

        /// <summary>A key name ("E", "Space", "LeftShift", "F1", "1") or a mouse button ("Mouse4", "MouseMiddle"...).</summary>
        public static InputId? Parse(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            if (MouseNames.TryGetValue(name.Trim(), out var button))
                return new InputId(true, (int)button);

            return Macros.SmartMacroRunner.ParseKey(name) is int vk ? new InputId(false, vk) : null;
        }

        /// <summary>Name for a key the user just pressed, in the form <see cref="Parse"/> reads.</summary>
        public static string NameOf(Key key) => key switch
        {
            >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
            _ => key.ToString()
        };
        #endregion

        #region SendInput
        private static void Send(InputId target, bool down)
        {
            var input = new INPUT();

            if (target.IsMouse)
            {
                input.type = 0; // INPUT_MOUSE
                input.u.mi.dwExtraInfo = InputHooks.RemapMarker;

                (input.u.mi.dwFlags, input.u.mi.mouseData) = (MacroMouseButton)target.Code switch
                {
                    MacroMouseButton.Right => (down ? 0x0008u : 0x0010u, 0u),
                    MacroMouseButton.Middle => (down ? 0x0020u : 0x0040u, 0u),
                    MacroMouseButton.XButton1 => (down ? 0x0080u : 0x0100u, 1u),
                    MacroMouseButton.XButton2 => (down ? 0x0080u : 0x0100u, 2u),
                    _ => (down ? 0x0002u : 0x0004u, 0u)
                };
            }
            else
            {
                input.type = 1; // INPUT_KEYBOARD
                input.u.ki.wVk = (ushort)target.Code;
                input.u.ki.wScan = (ushort)MapVirtualKeyW((uint)target.Code, 0);
                input.u.ki.dwExtraInfo = InputHooks.RemapMarker;
                input.u.ki.dwFlags = 0x0008 /* SCANCODE */ | (down ? 0u : 0x0002u /* KEYUP */) | (IsExtended(target.Code) ? 0x0001u : 0u);
            }

            SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        }

        private static bool IsExtended(int vk) => vk is
            0x21 or 0x22 or 0x23 or 0x24 or       // page up/down, end, home
            0x25 or 0x26 or 0x27 or 0x28 or       // arrows
            0x2D or 0x2E or                       // insert, delete
            0xA3 or 0xA5 or 0x6F or 0x90 or       // right ctrl, right alt, numpad divide, num lock
            0x5B or 0x5C or 0x5D;                 // windows keys, menu

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
        private static extern uint MapVirtualKeyW(uint code, uint mapType);
        #endregion
    }
}
