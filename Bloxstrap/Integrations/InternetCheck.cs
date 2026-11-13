using System.Net.Sockets;

namespace Bloxstrap.Integrations
{
    /// <summary>PSYstrap: is the internet up? Tries to open a connection to roblox.com (then a backup) with a short timeout.</summary>
    public static class InternetCheck
    {
        private static readonly string[] Hosts = { "www.roblox.com", "apis.roblox.com" };

        public static async Task<bool> IsOnlineAsync()
        {
            foreach (string host in Hosts)
            {
                try
                {
                    using var client = new TcpClient();
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                    await client.ConnectAsync(host, 443, timeout.Token);
                    return true;
                }
                catch
                {
                    // try the next one
                }
            }

            return false;
        }

        /// <summary>Waits until the internet is back (checking every 15 s). False if it gave up after maxWait.</summary>
        public static async Task<bool> WaitUntilOnlineAsync(TimeSpan maxWait, CancellationToken token = default)
        {
            var until = DateTime.Now + maxWait;

            while (DateTime.Now < until && !token.IsCancellationRequested)
            {
                if (await IsOnlineAsync())
                    return true;

                try { await Task.Delay(TimeSpan.FromSeconds(15), token); }
                catch (TaskCanceledException) { return false; }
            }

            return false;
        }
    }
}
