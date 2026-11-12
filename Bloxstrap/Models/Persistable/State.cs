namespace Bloxstrap.Models.Persistable
{
    public class State
    {
        public bool PromptWebView2Install { get; set; } = true;

        public bool ForceReinstall { get; set; } = false;

        /// <summary>PSYstrap: the PSYstrap loading window has been switched on once for people still on the old default.</summary>
        public bool PsyDialogIntroduced { get; set; } = false;

        public WindowState SettingsWindow { get; set; } = new();
    }
}
