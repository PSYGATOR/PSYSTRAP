using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

using Bloxstrap.Integrations.Macros;
using Bloxstrap.Models.Macros;
using Bloxstrap.UI.ViewModels.Macros;

namespace Bloxstrap.UI.Elements.Macros
{
    public partial class SmartMacroEditorWindow
    {
        private readonly SmartMacroEditorViewModel _viewModel;
        private Point? _dragStart;
        private Rectangle? _dragRect;

        public string? SavedName => _viewModel.SavedName;

        public SmartMacroEditorWindow(string? existingName)
        {
            _viewModel = new SmartMacroEditorViewModel(existingName);
            DataContext = _viewModel;
            InitializeComponent();

            _viewModel.DetectorsChanged += (_, _) => Dispatcher.BeginInvoke(DrawMarkers);
            ScreenshotImage.SizeChanged += (_, _) => DrawMarkers();
        }

        #region Screenshot
        /// <summary>Hides this window, brings Roblox up, captures its client area, then comes back.</summary>
        private async void Capture_Click(object sender, RoutedEventArgs e)
        {
            var roblox = MultiInstance.GetRobloxProcesses().FirstOrDefault(process => process.MainWindowHandle != IntPtr.Zero);

            if (roblox is null)
            {
                _viewModel.Status = "Open Roblox first - the screenshot is taken from its window.";
                return;
            }

            IntPtr window = roblox.MainWindowHandle;
            var previousState = WindowState;
            WindowState = System.Windows.WindowState.Minimized;

            try
            {
                if (IsIconic(window))
                    ShowWindow(window, SW_RESTORE);

                SetForegroundWindow(window);
                await Task.Delay(600);

                if (!GetClientRect(window, out var rect) || rect.Right < 16 || rect.Bottom < 16)
                {
                    _viewModel.Status = "Couldn't read the Roblox window's size.";
                    return;
                }

                var topLeft = new POINT();
                ClientToScreen(window, ref topLeft);
                _viewModel.SetFrame(ScreenFrame.Capture(new ScreenRect(topLeft.X, topLeft.Y, rect.Right, rect.Bottom)));
                _viewModel.Status = _viewModel.PixelMode
                    ? "Click a spot whose colour tells you something (e.g. a health bar or a button)."
                    : _viewModel.TextMode
                        ? "Drag a box around some text (e.g. the word Disconnected); PSYstrap reads it for you."
                        : "Drag a box around something you want recognised (e.g. a Reconnect button).";
            }
            finally
            {
                WindowState = previousState;
                Activate();
            }
        }

        private Point? ToFraction(Point point)
        {
            if (ScreenshotImage.ActualWidth <= 0 || ScreenshotImage.ActualHeight <= 0)
                return null;

            return new Point(Math.Clamp(point.X / ScreenshotImage.ActualWidth, 0, 1), Math.Clamp(point.Y / ScreenshotImage.ActualHeight, 0, 1));
        }

        private void Screenshot_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_viewModel.Frame is null)
                return;

            var point = e.GetPosition(ScreenshotImage);

            if (_viewModel.PixelMode)
            {
                if (ToFraction(point) is Point fraction)
                    _viewModel.AddPixelDetector(fraction.X, fraction.Y);

                return;
            }

            _dragStart = point;
            _dragRect = new Rectangle { Stroke = new SolidColorBrush(Color.FromRgb(0xF2, 0xC1, 0x4E)), StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 3, 2 } };
            MarkerCanvas.Children.Add(_dragRect);
            ScreenshotImage.CaptureMouse();
        }

        private void Screenshot_MouseMove(object sender, MouseEventArgs e)
        {
            if (_dragStart is not Point start || _dragRect is null)
                return;

            var point = e.GetPosition(ScreenshotImage);
            Canvas.SetLeft(_dragRect, Math.Min(start.X, point.X));
            Canvas.SetTop(_dragRect, Math.Min(start.Y, point.Y));
            _dragRect.Width = Math.Abs(point.X - start.X);
            _dragRect.Height = Math.Abs(point.Y - start.Y);
        }

        private void Screenshot_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_dragStart is not Point start)
                return;

            ScreenshotImage.ReleaseMouseCapture();
            var end = e.GetPosition(ScreenshotImage);
            _dragStart = null;
            _dragRect = null;

            if (ToFraction(start) is Point a && ToFraction(end) is Point b)
            {
                if (_viewModel.TextMode)
                    _viewModel.AddTextDetector(a.X, a.Y, b.X, b.Y);
                else
                    _viewModel.AddImageDetector(a.X, a.Y, b.X, b.Y);
            }

            DrawMarkers();
        }

        /// <summary>Green dots for pixel checks, green boxes for picture checks, labelled with their names.</summary>
        private void DrawMarkers()
        {
            MarkerCanvas.Children.Clear();

            double width = ScreenshotImage.ActualWidth, height = ScreenshotImage.ActualHeight;

            if (width <= 0 || height <= 0)
                return;

            var green = new SolidColorBrush(Color.FromRgb(0x5F, 0xE0, 0x4C));

            foreach (var row in _viewModel.Detectors)
            {
                var detector = row.Detector;
                double x = detector.X * width, y = detector.Y * height;

                if (detector.Kind == DetectorKind.Pixel)
                {
                    var dot = new Ellipse { Width = 12, Height = 12, Stroke = green, StrokeThickness = 2, Fill = new SolidColorBrush(Color.FromRgb(detector.R, detector.G, detector.B)) };
                    Canvas.SetLeft(dot, x - 6);
                    Canvas.SetTop(dot, y - 6);
                    MarkerCanvas.Children.Add(dot);
                }
                else
                {
                    // picture checks green, text checks crown gold
                    var stroke = detector.Kind == DetectorKind.Text ? new SolidColorBrush(Color.FromRgb(0xF2, 0xC1, 0x4E)) : green;
                    var box = new Rectangle { Width = detector.Width * width, Height = detector.Height * height, Stroke = stroke, StrokeThickness = 2 };
                    Canvas.SetLeft(box, x);
                    Canvas.SetTop(box, y);
                    MarkerCanvas.Children.Add(box);
                }

                var label = new TextBlock { Text = detector.Name, Foreground = Brushes.White, FontSize = 11, Background = new SolidColorBrush(Color.FromArgb(0xC0, 0x0A, 0x17, 0x0E)), Padding = new Thickness(3, 0, 3, 1) };
                Canvas.SetLeft(label, x + 8);
                Canvas.SetTop(label, y - 18);
                MarkerCanvas.Children.Add(label);
            }
        }
        #endregion

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_viewModel.Save())
                    DialogResult = true;
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("SmartMacroEditorWindow::Save", ex);
                _viewModel.Status = $"Couldn't save: {ex.Message}";
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        #region Native
        private const int SW_RESTORE = 9;

        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out RECT rect);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window, ref POINT point);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        #endregion
    }
}
