using Bloxstrap.UI.ViewModels.Settings;

namespace Bloxstrap.UI.Elements.Settings.Pages
{
    public partial class LaunchingPage
    {
        public LaunchingPage()
        {
            DataContext = new LaunchingViewModel();
            InitializeComponent();
        }
    }
}
