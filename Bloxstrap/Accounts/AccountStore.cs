using System.Security.Cryptography;

namespace Bloxstrap.Accounts
{
    /// <summary>A saved Roblox account. The session is stored DPAPI-encrypted and only decrypted when launching.</summary>
    public class SavedAccount
    {
        public long UserId { get; set; }

        public string Username { get; set; } = "";

        public string DisplayName { get; set; } = "";

        public DateTime Added { get; set; } = DateTime.Now;

        /// <summary>Base64 of the DPAPI-protected session cookie. Never written anywhere decrypted.</summary>
        public string ProtectedSession { get; set; } = "";

        [JsonIgnore]
        public string Label => DisplayName == Username || String.IsNullOrEmpty(DisplayName) ? Username : $"{DisplayName} (@{Username})";
    }

    /// <summary>
    /// PSYstrap saved accounts, in %LocalAppData%\PSYstrap\Accounts.json.
    ///
    /// Sessions are encrypted with Windows DPAPI (CurrentUser scope + an app-specific entropy), so the file is useless
    /// to another Windows user or on another PC. Sessions are only ever sent to roblox.com, are never logged, and
    /// passwords are never seen - sign-in happens on Roblox's own page.
    /// </summary>
    public static class AccountStore
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("PSYstrap-Accounts-v1");

        private static string FilePath => Path.Combine(Paths.Base, "Accounts.json");

        public static List<SavedAccount> Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<List<SavedAccount>>(File.ReadAllText(FilePath)) ?? new();
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("AccountStore::Load", ex);
            }

            return new();
        }

        private static void Save(List<SavedAccount> accounts) =>
            File.WriteAllText(FilePath, JsonSerializer.Serialize(accounts, new JsonSerializerOptions { WriteIndented = true }));

        /// <summary>Adds the account, or refreshes its session if it's already saved.</summary>
        public static void AddOrUpdate(long userId, string username, string displayName, string session)
        {
            var accounts = Load();
            var account = accounts.FirstOrDefault(x => x.UserId == userId);

            if (account is null)
            {
                account = new SavedAccount { UserId = userId };
                accounts.Add(account);
            }

            account.Username = username;
            account.DisplayName = displayName;
            account.ProtectedSession = Protect(session);

            Save(accounts);
            App.Logger.WriteLine("AccountStore::AddOrUpdate", $"Saved account {username} ({userId})");
        }

        public static void Remove(long userId)
        {
            var accounts = Load();
            accounts.RemoveAll(x => x.UserId == userId);
            Save(accounts);
            App.Logger.WriteLine("AccountStore::Remove", $"Removed account {userId}");
        }

        public static string? GetSession(SavedAccount account)
        {
            try
            {
                return Unprotect(account.ProtectedSession);
            }
            catch (CryptographicException)
            {
                // saved by another Windows user or on another PC
                return null;
            }
        }

        private static string Protect(string value) =>
            Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser));

        private static string Unprotect(string value) =>
            Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), Entropy, DataProtectionScope.CurrentUser));
    }
}
