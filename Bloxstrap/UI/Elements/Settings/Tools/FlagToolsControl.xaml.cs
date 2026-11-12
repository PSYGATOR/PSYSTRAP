using Bloxstrap.UI.ViewModels.Settings;

namespace Bloxstrap.UI.Elements.Settings.Tools
{
    public partial class FlagToolsControl
    {
        public FlagToolsControl()
        {
            DataContext = new FlagToolsViewModel();
            InitializeComponent();
        }
    }
}
