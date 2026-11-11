using System.Windows;

using Microsoft.Web.WebView2.Core;

using Bloxstrap.Accounts;

namespace Bloxstrap.UI.Elements.Accounts
{
    /// <summary>
    /// PSYstrap: shows Roblox's real login page (WebView2) in a fresh, throwaway browser profile.
    /// After sign-in, the session cookie Roblox sets is checked with roblox.com, saved encrypted, and the profile deleted.
    /// Only roblox.com pages are allowed to load as the main page.
    /// </summary>
    public partial class AccountSignInWindow
    {
        private const string LOG_IDENT = "AccountSignInWindow";

        private readonly string _profileFolder = Path.Combine(Path.GetTempPath(), "PSYstrap", "SignIn", Guid.NewGuid().ToString("N"));
        private bool _saving;

        /// <summary>The account that was saved, if sign-in finished.</summary>
        public RobloxAuth.AccountInfo? SavedAccount { get; private set; }

        public AccountSignInWindow()
        {
            InitializeComponent();
            Loaded += async (_, _) => await StartAsync();
            Closed += (_, _) => Cleanup();
        }

        private async Task StartAsync()
        {
            try
            {
                var environment = await CoreWebView2Environment.CreateAsync(null, _profileFolder);
                await Browser.EnsureCoreWebView2Async(environment);

                Browser.CoreWebView2.NavigationStarting += (_, e) =>
                {
                    // the login page itself must stay on roblox.com (captchas load inside frames, which this doesn't affect)
                    if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) && !IsRoblox(uri))
                        e.Cancel = true;
                };

                Browser.CoreWebView2.NewWindowRequested += (_, e) => e.Handled = true;
                Browser.CoreWebView2.NavigationCompleted += async (_, _) => await CheckSignedInAsync();

                Browser.CoreWebView2.Navigate("https://www.roblox.com/login");
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                StatusText.Text = "The sign-in page couldn't open. Microsoft Edge WebView2 may be missing - it's part of Windows 11 and installs with Microsoft Edge.";
            }
        }

        private static bool IsRoblox(Uri uri) =>
            uri.Scheme == Uri.UriSchemeHttps && (uri.Host == "roblox.com" || uri.Host.EndsWith(".roblox.com", StringComparison.OrdinalIgnoreCase));

        private async Task CheckSignedInAsync()
        {
            if (_saving)
                return;

            var cookies = await Browser.CoreWebView2.CookieManager.GetCookiesAsync("https://www.roblox.com");
            string? session = cookies.FirstOrDefault(cookie => cookie.Name == ".ROBLOSECURITY")?.Value;

            if (String.IsNullOrEmpty(session))
                return;

            _saving = true;
            StatusText.Text = "Signed in - checking the account...";

            try
            {
                var account = await RobloxAuth.GetAccountAsync(session);

                if (account is null)
                {
                    StatusText.Text = "Roblox didn't accept that sign-in. Try again.";
                    _saving = false;
                    return;
                }

                AccountStore.AddOrUpdate(account.Id, account.Name, account.DisplayName, session);
                SavedAccount = account;
                DialogResult = true;
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                StatusText.Text = "Couldn't reach Roblox to check the account. Check your connection and try again.";
                _saving = false;
            }
        }

        /// <summary>The throwaway profile holds the session too, so it's removed as soon as the window closes.</summary>
        private void Cleanup()
        {
            try
            {
                Browser.Dispose();
            }
            catch { }

            // the browser process can take a moment to release the folder
            Task.Run(async () =>
            {
                for (int attempt = 0; attempt < 10; attempt++)
                {
                    try
                    {
                        if (Directory.Exists(_profileFolder))
                            Directory.Delete(_profileFolder, true);

                        return;
                    }
                    catch
                    {
                        await Task.Delay(500);
                    }
                }

                App.Logger.WriteLine(LOG_IDENT, "Couldn't delete the sign-in profile folder");
            });
        }
    }
}
