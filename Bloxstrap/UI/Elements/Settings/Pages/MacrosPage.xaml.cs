using Bloxstrap.UI.ViewModels.Overlay;

namespace Bloxstrap.UI.Elements.Settings.Pages
{
    public partial class MacrosPage
    {
        public MacrosPage()
        {
            DataContext = new OverlayOptionsViewModel(saveImmediately: false);
            InitializeComponent();
        }
    }
}
