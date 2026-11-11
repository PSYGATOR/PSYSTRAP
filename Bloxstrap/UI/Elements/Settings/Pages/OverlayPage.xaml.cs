using Bloxstrap.UI.ViewModels.Overlay;

namespace Bloxstrap.UI.Elements.Settings.Pages
{
    public partial class OverlayPage
    {
        public OverlayPage()
        {
            // saved by the Settings window's Save button like every other page
            DataContext = new OverlayOptionsViewModel(saveImmediately: false);
            InitializeComponent();
        }
    }
}
