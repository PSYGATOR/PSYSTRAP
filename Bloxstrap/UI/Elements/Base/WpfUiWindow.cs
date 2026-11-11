using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using Wpf.Ui.Mvvm.Contracts;
using Wpf.Ui.Mvvm.Services;

namespace Bloxstrap.UI.Elements.Base
{
    public abstract class WpfUiWindow : UiWindow
    {
        private readonly IThemeService _themeService = new ThemeService();

        // PSYstrap: the backdrop this window asked for in XAML, while the PSY theme has replaced it
        private BackgroundType? _replacedBackdrop;

        public WpfUiWindow()
        {
            ApplyTheme();
        }

        public static bool IsPsyTheme => App.Settings.Prop.Theme == Enums.Theme.PSY;

        public void ApplyTheme()
        {
            const int customThemeIndex = 2; // index for CustomTheme merged dictionary

            _themeService.SetTheme(App.Settings.Prop.Theme.GetFinal() == Enums.Theme.Dark ? ThemeType.Dark : ThemeType.Light);

            if (IsPsyTheme)
                ApplyPsyAccent();
            else
                _themeService.SetSystemAccent();

            // there doesn't seem to be a way to query the name for merged dictionaries
            string dictName = IsPsyTheme ? "PSY" : Enum.GetName(App.Settings.Prop.Theme.GetFinal())!;
            var dict = new ResourceDictionary { Source = new Uri($"pack://application:,,,/UI/Style/{dictName}.xaml") };
            Application.Current.Resources.MergedDictionaries[customThemeIndex] = dict;

            ApplyPsyBackdrop();

#if QA_BUILD
            this.BorderBrush = System.Windows.Media.Brushes.Red;
            this.BorderThickness = new Thickness(4);
#endif
        }

        /// <summary>
        /// Logo greens instead of the Windows accent colour. Secondary is bright enough that WPF UI
        /// switches text-on-accent to black, which reads well on green buttons and toggles.
        /// </summary>
        private static void ApplyPsyAccent()
        {
            Accent.Apply(
                systemAccent: Color.FromRgb(0x2F, 0xB5, 0x2A),
                primaryAccent: Color.FromRgb(0x45, 0xCC, 0x37),
                secondaryAccent: Color.FromRgb(0x5F, 0xE0, 0x4C),
                tertiaryAccent: Color.FromRgb(0x8C, 0xF0, 0x7B)
            );
        }

        /// <summary>
        /// Mica/acrylic show the desktop wallpaper tint through the window, which would hide the theme,
        /// so under PSY the backdrop is switched off and the themed gradient painted instead.
        /// Switching back to another theme restores whatever backdrop the window originally used.
        /// </summary>
        private void ApplyPsyBackdrop()
        {
            if (new WindowInteropHelper(this).Handle == IntPtr.Zero)
                return; // not shown yet - OnSourceInitialized calls this again

            if (IsPsyTheme)
            {
                if (WindowBackdropType != BackgroundType.None && WindowBackdropType != BackgroundType.Unknown)
                {
                    _replacedBackdrop = WindowBackdropType;
                    WindowBackdropType = BackgroundType.None;
                }

                if (_replacedBackdrop is not null && Application.Current.TryFindResource("PsyWindowBackgroundBrush") is Brush brush)
                    Background = brush;
            }
            else if (_replacedBackdrop is BackgroundType original)
            {
                _replacedBackdrop = null;
                WindowBackdropType = original;
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            if (App.Settings.Prop.WPFSoftwareRender || App.LaunchSettings.NoGPUFlag.Active)
            {
                if (PresentationSource.FromVisual(this) is HwndSource hwndSource)
                    hwndSource.CompositionTarget.RenderMode = RenderMode.SoftwareOnly;
            }

            base.OnSourceInitialized(e);

            ApplyPsyBackdrop();
        }
    }
}
