using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;


namespace Bloxstrap.UI.ViewModels.Installer
{
    public class LaunchMenuViewModel
    {
        public string Version => string.Format(Strings.Menu_About_Version, App.Version);

        /// <summary>PSYstrap: "v2.14.0" for the little tag under the logo.</summary>
        public string ShortVersion => $"v{App.Version}";

        /// <summary>PSYstrap: opens the Instances window and leaves this menu open.</summary>
        public ICommand OpenInstancesCommand => new RelayCommand(() => Process.Start(Paths.Process, "-instances"));

        public ICommand LaunchSettingsCommand => new RelayCommand(LaunchSettings);

        public ICommand LaunchRobloxCommand => new RelayCommand(LaunchRoblox);

        public ICommand LaunchRobloxStudioCommand => new RelayCommand(LaunchRobloxStudio);


        public event EventHandler<NextAction>? CloseWindowRequest;

        private void LaunchSettings() => CloseWindowRequest?.Invoke(this, NextAction.LaunchSettings);

        private void LaunchRoblox() => CloseWindowRequest?.Invoke(this, NextAction.LaunchRoblox);

        private void LaunchRobloxStudio() => CloseWindowRequest?.Invoke(this, NextAction.LaunchRobloxStudio);

    }
}
