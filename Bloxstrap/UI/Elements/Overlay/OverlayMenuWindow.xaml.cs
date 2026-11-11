using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Interop;

using Bloxstrap.UI.ViewModels.Overlay;

namespace Bloxstrap.UI.Elements.Overlay
{
    /// <summary>PSYstrap in-game menu. Hidden rather than closed, so it opens instantly next time.</summary>
    public partial class OverlayMenuWindow
    {
        private bool _allowClose;

        public event EventHandler? CloseRequested;

        public IntPtr Handle => new WindowInteropHelper(this).Handle;

        public OverlayMenuWindow(OverlayOptionsViewModel viewModel)
        {
            DataContext = viewModel;
            InitializeComponent();
            RefreshFooter();

            viewModel.Changed += (_, _) => RefreshFooter();
        }

        private void RefreshFooter()
        {
            FooterText.Text = $"Changes save right away. Press {App.Settings.Prop.Overlay.MenuKey.ToEnumName()} or click Roblox to close.";
        }

        private void OpenInstances_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            Process.Start(Paths.Process, "-instances");
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                CloseRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            }

            base.OnKeyDown(e);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            // title bar X just hides it
            if (!_allowClose)
            {
                e.Cancel = true;
                CloseRequested?.Invoke(this, EventArgs.Empty);
            }

            base.OnClosing(e);
        }

        public void ForceClose()
        {
            _allowClose = true;
            Close();
        }
    }
}
