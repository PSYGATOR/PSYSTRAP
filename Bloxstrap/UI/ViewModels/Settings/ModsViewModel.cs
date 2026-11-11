using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

using Microsoft.Win32;

using Windows.Win32;
using Windows.Win32.UI.Shell;
using Windows.Win32.Foundation;

using CommunityToolkit.Mvvm.Input;

using Bloxstrap.Models.SettingTasks;
using Bloxstrap.AppData;

namespace Bloxstrap.UI.ViewModels.Settings
{
    public class ModsViewModel : NotifyPropertyChangedViewModel
    {
        private void OpenModsFolder() => Process.Start("explorer.exe", Paths.Modifications);

        private readonly Dictionary<string, byte[]> FontHeaders = new()
        {
            { "ttf", new byte[4] { 0x00, 0x01, 0x00, 0x00 } },
            { "otf", new byte[4] { 0x4F, 0x54, 0x54, 0x4F } },
            { "ttc", new byte[4] { 0x74, 0x74, 0x63, 0x66 } } 
        };

        private void ManageCustomFont()
        {
            if (!String.IsNullOrEmpty(TextFontTask.NewState))
            {
                TextFontTask.NewState = "";
            }
            else
            {
                var dialog = new OpenFileDialog
                {
                    Filter = $"{Strings.Menu_FontFiles}|*.ttf;*.otf;*.ttc"
                };

                if (dialog.ShowDialog() != true)
                    return;

                string type = dialog.FileName.Substring(dialog.FileName.Length-3, 3).ToLowerInvariant();

                if (!FontHeaders.ContainsKey(type) 
                    || !FontHeaders.Any(x => File.ReadAllBytes(dialog.FileName).Take(4).SequenceEqual(x.Value)))
                {
                    Frontend.ShowMessageBox(Strings.Menu_Mods_Misc_CustomFont_Invalid, MessageBoxImage.Error);
                    return;
                }

                TextFontTask.NewState = dialog.FileName;
            }

            OnPropertyChanged(nameof(ChooseCustomFontVisibility));
            OnPropertyChanged(nameof(DeleteCustomFontVisibility));
        }

        public ICommand OpenModsFolderCommand => new RelayCommand(OpenModsFolder);

        public Visibility ChooseCustomFontVisibility => !String.IsNullOrEmpty(TextFontTask.NewState) ? Visibility.Collapsed : Visibility.Visible;

        public Visibility DeleteCustomFontVisibility => !String.IsNullOrEmpty(TextFontTask.NewState) ? Visibility.Visible : Visibility.Collapsed;

        public ICommand ManageCustomFontCommand => new RelayCommand(ManageCustomFont);

        public ICommand OpenCompatSettingsCommand => new RelayCommand(OpenCompatSettings);

        public ModPresetTask OldAvatarBackgroundTask { get; } = new("OldAvatarBackground", @"ExtraContent\places\Mobile.rbxl", "OldAvatarBackground.rbxl");

        public ModPresetTask OldCharacterSoundsTask { get; } = new("OldCharacterSounds", new()
        {
            { @"content\sounds\action_footsteps_plastic.mp3", "Sounds.OldWalk.mp3"  },
            { @"content\sounds\action_jump.mp3",              "Sounds.OldJump.mp3"  },
            { @"content\sounds\action_get_up.mp3",            "Sounds.OldGetUp.mp3" },
            { @"content\sounds\action_falling.mp3",           "Sounds.Empty.mp3"    },
            { @"content\sounds\action_jump_land.mp3",         "Sounds.Empty.mp3"    },
            { @"content\sounds\action_swim.mp3",              "Sounds.Empty.mp3"    },
            { @"content\sounds\impact_water.mp3",             "Sounds.Empty.mp3"    }
        });

        public EmojiModPresetTask EmojiFontTask { get; } = new();

        public EnumModPresetTask<Enums.CursorType> CursorTypeTask { get; } = new("CursorType", new()
        {
            {
                Enums.CursorType.From2006, new()
                {
                    { @"content\textures\Cursors\KeyboardMouse\ArrowCursor.png",    "Cursor.From2006.ArrowCursor.png"    },
                    { @"content\textures\Cursors\KeyboardMouse\ArrowFarCursor.png", "Cursor.From2006.ArrowFarCursor.png" }
                }
            },
            {
                Enums.CursorType.From2013, new()
                {
                    { @"content\textures\Cursors\KeyboardMouse\ArrowCursor.png",    "Cursor.From2013.ArrowCursor.png"    },
                    { @"content\textures\Cursors\KeyboardMouse\ArrowFarCursor.png", "Cursor.From2013.ArrowFarCursor.png" }
                }
            }
        });

        public FontModPresetTask TextFontTask { get; } = new();

        #region Skybox (PSYstrap)
        public SkyboxTask SkyboxTask { get; } = new();

        public IEnumerable<SkyboxPreset> SkyboxPresets { get; } = Enum.GetValues<SkyboxPreset>();

        private Dictionary<string, BitmapSource>? _skyboxPreview;

        public ModsViewModel()
        {
            RefreshSkyboxPreview();
            SkyboxTask.Applied += (_, _) => OnPropertyChanged(nameof(SkyboxStatusText));
        }

        public SkyboxPreset SelectedSkybox
        {
            get => SkyboxTask.NewState;
            set
            {
                if (value == SkyboxTask.NewState)
                    return;

                if (value == SkyboxPreset.Custom)
                {
                    if (!ChooseSkyboxImages())
                    {
                        // dialog cancelled or images rejected: put the combo box back on the old choice.
                        // deferred because the ComboBox ignores change notifications raised mid-selection.
                        Application.Current.Dispatcher.BeginInvoke(() => OnPropertyChanged(nameof(SelectedSkybox)));
                        return;
                    }
                }
                else
                {
                    SkyboxTask.SetPreset(value);
                }

                RefreshSkyboxPreview();
            }
        }

        public ICommand ChooseSkyboxImagesCommand => new RelayCommand(() =>
        {
            if (ChooseSkyboxImages())
                RefreshSkyboxPreview();
        });

        private bool ChooseSkyboxImages()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Pick 6 skybox images (bk, dn, ft, lf, rt, up) or 1 image for every side",
                Filter = "Images and Roblox sky textures|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.tex;*.dds|All files|*.*",
                Multiselect = true
            };

            if (dialog.ShowDialog() != true)
                return false;

            try
            {
                SkyboxTask.SetCustom(Skybox.LoadImages(dialog.FileNames));
                return true;
            }
            catch (InvalidOperationException ex)
            {
                Frontend.ShowMessageBox(ex.Message, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("ModsViewModel::ChooseSkyboxImages", ex);
                Frontend.ShowMessageBox($"Those images couldn't be loaded:\n\n{ex.Message}", MessageBoxImage.Error);
            }

            return false;
        }

        private void RefreshSkyboxPreview()
        {
            IReadOnlyDictionary<string, byte[]>? faces = null;

            try
            {
                if (SkyboxTask.NewState == SkyboxPreset.Custom)
                    faces = SkyboxTask.PendingCustomFaces ?? Skybox.LoadInstalled();
                else if (Skybox.IsGenerated(SkyboxTask.NewState))
                    faces = Skybox.Generate(SkyboxTask.NewState, 128);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("ModsViewModel::RefreshSkyboxPreview", ex);
            }

            _skyboxPreview = faces?.ToDictionary(pair => pair.Key, pair => Skybox.MakePreview(pair.Value));

            OnPropertyChanged(nameof(SelectedSkybox));
            OnPropertyChanged(nameof(SkyboxPreviewVisibility));
            OnPropertyChanged(nameof(SkyboxStatusText));

            foreach (string name in new[] { nameof(SkyboxPreviewBk), nameof(SkyboxPreviewDn), nameof(SkyboxPreviewFt), nameof(SkyboxPreviewLf), nameof(SkyboxPreviewRt), nameof(SkyboxPreviewUp) })
                OnPropertyChanged(name);
        }

        private BitmapSource? GetSkyboxPreview(string face) => _skyboxPreview is not null && _skyboxPreview.TryGetValue(face, out var preview) ? preview : null;

        public BitmapSource? SkyboxPreviewBk => GetSkyboxPreview("bk");
        public BitmapSource? SkyboxPreviewDn => GetSkyboxPreview("dn");
        public BitmapSource? SkyboxPreviewFt => GetSkyboxPreview("ft");
        public BitmapSource? SkyboxPreviewLf => GetSkyboxPreview("lf");
        public BitmapSource? SkyboxPreviewRt => GetSkyboxPreview("rt");
        public BitmapSource? SkyboxPreviewUp => GetSkyboxPreview("up");

        public Visibility SkyboxPreviewVisibility => _skyboxPreview is null ? Visibility.Collapsed : Visibility.Visible;

        public string SkyboxStatusText
        {
            get
            {
                if (SkyboxTask.Changed)
                    return "Not applied yet: click Save, then restart Roblox.";

                if (SkyboxTask.NewState == SkyboxPreset.Default)
                    return "Using Roblox's own sky.";

                return "Applied. Shows in games that don't load their own sky, starting next time Roblox launches.";
            }
        }
        #endregion

        private void OpenCompatSettings()
        {
            string path = new RobloxPlayerData().ExecutablePath;

            if (File.Exists(path))
                PInvoke.SHObjectProperties(HWND.Null, SHOP_TYPE.SHOP_FILEPATH, path, "Compatibility");
            else
                Frontend.ShowMessageBox(Strings.Common_RobloxNotInstalled, MessageBoxImage.Error);

        }
    }
}
