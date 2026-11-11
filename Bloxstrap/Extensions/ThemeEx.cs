using Microsoft.Win32;

namespace Bloxstrap.Extensions
{
    public static class ThemeEx
    {
        /// <summary>
        /// Resolves to Light or Dark. PSY counts as Dark, so every existing light/dark check keeps working.
        /// </summary>
        public static Theme GetFinal(this Theme dialogTheme)
        {
            if (dialogTheme == Theme.PSY)
                return Theme.Dark;

            if (dialogTheme != Theme.Default)
                return dialogTheme;

            using var key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize");

            if (key?.GetValue("AppsUseLightTheme") is int value && value == 0)
                return Theme.Dark;

            return Theme.Light;
        }
    }
}
