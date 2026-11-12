using Bloxstrap.UI.ViewModels.Settings;

namespace Bloxstrap.UI.Elements.Settings.Pages
{
    public partial class GameplayPage
    {
        public GameplayPage()
        {
            DataContext = new GameplayViewModel();
            InitializeComponent();
        }
    }
}
