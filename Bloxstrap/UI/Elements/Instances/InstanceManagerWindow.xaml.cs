using Bloxstrap.UI.ViewModels.Instances;

namespace Bloxstrap.UI.Elements.Instances
{
    public partial class InstanceManagerWindow
    {
        private readonly InstanceManagerViewModel _viewModel = new();

        public InstanceManagerWindow()
        {
            DataContext = _viewModel;
            InitializeComponent();
            Closed += (_, _) => _viewModel.Dispose();
        }
    }
}
