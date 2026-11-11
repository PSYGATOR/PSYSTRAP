using System.Windows;

namespace Bloxstrap.Models.SettingTasks
{
    /// <summary>
    /// PSYstrap: applies the chosen skybox when settings are saved.
    /// Custom images are loaded and validated when picked (so errors show immediately) and written on save.
    /// </summary>
    public class SkyboxTask : BaseTask
    {
        private Dictionary<string, byte[]>? _pendingCustomFaces;

        public SkyboxPreset OriginalState { get; private set; }

        public SkyboxPreset NewState { get; private set; }

        public IReadOnlyDictionary<string, byte[]>? PendingCustomFaces => _pendingCustomFaces;

        public event EventHandler? Applied;

        // picking new custom images counts as a change even when Custom was already selected
        public override bool Changed => NewState != OriginalState || _pendingCustomFaces is not null;

        public SkyboxTask() : base("ModPreset", "Skybox")
        {
            if (!Skybox.IsInstalled)
                OriginalState = SkyboxPreset.Default;
            else if (App.Settings.Prop.Skybox == SkyboxPreset.Default)
                OriginalState = SkyboxPreset.Custom; // sky files were put in the mods folder by hand
            else
                OriginalState = App.Settings.Prop.Skybox;

            NewState = OriginalState;
        }

        public void SetPreset(SkyboxPreset preset)
        {
            if (preset == SkyboxPreset.Custom)
                throw new ArgumentException("Use SetCustom for custom images", nameof(preset));

            NewState = preset;
            _pendingCustomFaces = null;
            UpdatePending();
        }

        public void SetCustom(Dictionary<string, byte[]> faces)
        {
            NewState = SkyboxPreset.Custom;
            _pendingCustomFaces = faces;
            UpdatePending();
        }

        private void UpdatePending()
        {
            if (Changed)
                App.PendingSettingTasks[Name] = this;
            else
                App.PendingSettingTasks.Remove(Name);
        }

        public override void Execute()
        {
            const string LOG_IDENT = "SkyboxTask::Execute";

            try
            {
                if (NewState == SkyboxPreset.Default)
                    Skybox.Remove();
                else if (NewState == SkyboxPreset.Custom)
                {
                    if (_pendingCustomFaces is not null)
                        Skybox.Install(_pendingCustomFaces);
                }
                else
                    Skybox.Install(Skybox.Generate(NewState));

                // settings are saved before tasks run, so persist the choice ourselves
                App.Settings.Prop.Skybox = NewState;
                App.Settings.Save();

                OriginalState = NewState;
                _pendingCustomFaces = null;

                Applied?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                Frontend.ShowMessageBox($"The skybox couldn't be applied:\n\n{ex.Message}", MessageBoxImage.Error);
            }
        }
    }
}
