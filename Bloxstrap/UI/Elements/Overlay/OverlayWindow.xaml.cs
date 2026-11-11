using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Bloxstrap.UI.Elements.Overlay
{
    /// <summary>
    /// PSYstrap HUD window. Click-through, never takes focus, never appears in alt-tab or the taskbar.
    /// OverlayController positions it over Roblox and feeds it values.
    /// </summary>
    public partial class OverlayWindow : Window
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        private static readonly Brush GoodBrush = Frozen(Color.FromRgb(0x5F, 0xE0, 0x4C));  // logo green
        private static readonly Brush OkayBrush = Frozen(Color.FromRgb(0xF2, 0xC1, 0x4E));  // crown gold
        private static readonly Brush BadBrush = Frozen(Color.FromRgb(0xFF, 0x5A, 0x6E));   // crown red
        private static readonly Brush PlainBrush = Frozen(Colors.White);

        public IntPtr Handle { get; private set; }

        public OverlayWindow()
        {
            InitializeComponent();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            Handle = new WindowInteropHelper(this).Handle;
            int style = GetWindowLong(Handle, GWL_EXSTYLE);
            SetWindowLong(Handle, GWL_EXSTYLE, style | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        }

        public void ApplyVisibility(OverlaySettings settings, bool framerateAvailable)
        {
            _lastSettings = settings;
            _lastFramerateAvailable = framerateAvailable;

            if (!_countersVisible)
            {
                MenuHintText.Text = $"{settings.MenuKey.ToEnumName()}: PSYstrap menu";
                return;
            }

            FramerateWidget.Visibility = settings.ShowFramerate ? Visibility.Visible : Visibility.Collapsed;
            ClicksWidget.Visibility = settings.ShowClicksPerSecond ? Visibility.Visible : Visibility.Collapsed;
            MemoryWidget.Visibility = settings.ShowMemory ? Visibility.Visible : Visibility.Collapsed;
            SessionWidget.Visibility = settings.ShowSessionTime ? Visibility.Visible : Visibility.Collapsed;
            ClockWidget.Visibility = settings.ShowClock ? Visibility.Visible : Visibility.Collapsed;
            MenuHintText.Text = $"{settings.MenuKey.ToEnumName()}: PSYstrap menu";

            if (!framerateAvailable)
            {
                FramerateText.Text = "n/a";
                FramerateText.Foreground = PlainBrush;
            }
        }

        private bool _countersVisible = true;
        private OverlaySettings? _lastSettings;
        private bool _lastFramerateAvailable;

        /// <summary>Hides every counter (the "Show counters" switch) while keeping the macro pill usable.</summary>
        public void SetCountersVisible(bool visible)
        {
            if (visible == _countersVisible)
                return;

            _countersVisible = visible;

            if (visible && _lastSettings is not null)
            {
                ApplyVisibility(_lastSettings, _lastFramerateAvailable);
                return;
            }

            foreach (var widget in new[] { FramerateWidget, ClicksWidget, MemoryWidget, SessionWidget, ClockWidget })
                widget.Visibility = Visibility.Collapsed;
        }

        public void ShowMenuHint(bool show) => MenuHint.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

        public void SetFramerate(double fps)
        {
            FramerateText.Text = Math.Round(fps).ToString(CultureInfo.InvariantCulture);
            FramerateText.Foreground = fps >= 55 ? GoodBrush : fps >= 30 ? OkayBrush : BadBrush;
        }

        /// <summary>Recording/playing status pill; null hides it. Recording is red, playing green, paused gold.</summary>
        public void SetMacroStatus(string? text, bool recording = false, bool paused = false)
        {
            if (text is null)
            {
                MacroWidget.Visibility = Visibility.Collapsed;
                return;
            }

            MacroWidget.Visibility = Visibility.Visible;
            MacroText.Text = text;
            MacroText.Foreground = recording ? BadBrush : paused ? OkayBrush : GoodBrush;
        }

        public void SetClicks(int left, int right) => ClicksText.Text = $"{left} | {right}";

        public void SetMemory(long bytes) => MemoryText.Text = $"{bytes / (1024.0 * 1024 * 1024):0.00} GB";

        public void SetSession(TimeSpan elapsed) => SessionText.Text = elapsed.TotalHours >= 1
            ? elapsed.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : elapsed.ToString(@"m\:ss", CultureInfo.InvariantCulture);

        public void SetClock(DateTime now) => ClockText.Text = now.ToString("t", CultureInfo.CurrentCulture);

        private bool _hiddenFromCapture;

        /// <summary>
        /// While a smart macro runs, the HUD is left out of screen captures so its own pills can't confuse detectors.
        /// (It also hides from screenshots/recordings meanwhile.)
        /// </summary>
        public void SetHiddenFromCapture(bool hidden)
        {
            if (hidden == _hiddenFromCapture || Handle == IntPtr.Zero)
                return;

            _hiddenFromCapture = hidden;
            SetWindowDisplayAffinity(Handle, hidden ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE);
        }

        private const uint WDA_NONE = 0x0, WDA_EXCLUDEFROMCAPTURE = 0x11;

        [DllImport("user32.dll")]
        private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);

        private static Brush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }
}
