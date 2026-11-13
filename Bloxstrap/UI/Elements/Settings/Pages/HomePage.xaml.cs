using Bloxstrap.UI.ViewModels.Settings;

namespace Bloxstrap.UI.Elements.Settings.Pages
{
    /// <summary>PSYstrap: the first page of the settings window.</summary>
    public partial class HomePage
    {
        public HomePage()
        {
            var viewModel = new HomeViewModel();
            DataContext = viewModel;
            InitializeComponent();

            // only keeps refreshing while it's on screen
            Loaded += (_, _) => viewModel.Start();
            Unloaded += (_, _) => viewModel.Stop();
        }
    }
}
