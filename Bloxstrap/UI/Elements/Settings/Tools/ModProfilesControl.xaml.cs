using Bloxstrap.UI.ViewModels.Settings;

namespace Bloxstrap.UI.Elements.Settings.Tools
{
    public partial class ModProfilesControl
    {
        public ModProfilesControl()
        {
            DataContext = new ModProfilesViewModel();
            InitializeComponent();
        }
    }
}
