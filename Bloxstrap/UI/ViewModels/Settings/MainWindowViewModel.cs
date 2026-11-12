using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace Bloxstrap.UI.ViewModels.Settings
{
    public class MainWindowViewModel : NotifyPropertyChangedViewModel
    {

        // PSYstrap: separate process, because the manager saves settings straight away and must not
        // reload over this window's unsaved changes
        public ICommand OpenInstancesCommand => new RelayCommand(() => Process.Start(Paths.Process, "-instances"));
        
        public ICommand SaveSettingsCommand => new RelayCommand(SaveSettings);
        
        public ICommand CloseWindowCommand => new RelayCommand(CloseWindow);

        public EventHandler? RequestSaveNoticeEvent;
        
        public EventHandler? RequestCloseWindowEvent;

        public bool TestModeEnabled
        {
            get => App.LaunchSettings.TestModeFlag.Active;
            set
            {
                if (value)
                {
                    var result = Frontend.ShowMessageBox(Strings.Menu_TestMode_Prompt, MessageBoxImage.Information, MessageBoxButton.YesNo);

                    if (result != MessageBoxResult.Yes)
                        return;
                }

                App.LaunchSettings.TestModeFlag.Active = value;
            }
        }


        private void CloseWindow() => RequestCloseWindowEvent?.Invoke(this, EventArgs.Empty);

        private void SaveSettings()
        {
            const string LOG_IDENT = "MainWindowViewModel::SaveSettings";

            App.Settings.Save();
            App.State.Save();
            App.FastFlags.Save();

            // PSYstrap: start-with-Windows entry and scheduled launches live in Windows itself
            Task.Run(Launching.SyncWithWindows);

            foreach (var pair in App.PendingSettingTasks)
            {
                var task = pair.Value;

                if (task.Changed)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Executing pending task '{task}'");
                    task.Execute();
                }
            }

            App.PendingSettingTasks.Clear();

            RequestSaveNoticeEvent?.Invoke(this, EventArgs.Empty);
        }
    }
}
