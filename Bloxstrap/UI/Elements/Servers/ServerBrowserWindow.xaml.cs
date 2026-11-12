using Bloxstrap.UI.ViewModels.Servers;

namespace Bloxstrap.UI.Elements.Servers
{
    public partial class ServerBrowserWindow
    {
        public ServerBrowserWindow(long placeId)
        {
            DataContext = new ServerBrowserViewModel(placeId);
            InitializeComponent();
        }
    }
}
