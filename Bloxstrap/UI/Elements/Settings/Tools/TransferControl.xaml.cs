using Bloxstrap.UI.ViewModels.Settings;

namespace Bloxstrap.UI.Elements.Settings.Tools
{
    public partial class TransferControl
    {
        public TransferControl()
        {
            DataContext = new TransferViewModel();
            InitializeComponent();
        }
    }
}
