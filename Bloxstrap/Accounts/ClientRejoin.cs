namespace Bloxstrap.Accounts
{
    /// <summary>
    /// PSYstrap: closes one Roblox client and opens it again in the same server. If the client was signed in to one
    /// of the saved accounts it comes back as that account; otherwise it opens with Roblox's normal rejoin link.
    /// </summary>
    public static class ClientRejoin
    {
        private const string LOG_IDENT = "ClientRejoin";

        public static void Rejoin(int processId)
        {
            var session = LiveSessions.List().FirstOrDefault(s => s.ProcessId == processId);
            string? link = null;

            if (session is not null && session.PlaceId != 0)
            {
                var account = AccountStore.Load().FirstOrDefault(a => a.UserId == session.UserId);
                string? cookie = account is null ? null : AccountStore.GetSession(account);

                if (cookie is not null)
                {
                    string? ticket = Task.Run(() => RobloxAuth.GetLaunchTicketAsync(cookie)).GetAwaiter().GetResult();

                    if (ticket is not null)
                        link = RobloxAuth.BuildLaunchLink(ticket, session.PlaceId, session.IsPrivate ? null : session.ServerId, session.IsPrivate ? session.AccessCode : null);
                }

                link ??= session.IsPrivate
                    ? $"roblox://experiences/start?placeId={session.PlaceId}&accessCode={session.AccessCode}"
                    : RobloxServers.GetJoinLink(session.PlaceId, session.ServerId);
            }

            App.Logger.WriteLine(LOG_IDENT, $"Rejoining client {processId} (known server: {session is not null})");

            try
            {
                using var process = Process.GetProcessById(processId);
                process.CloseMainWindow();

                if (!process.WaitForExit(10000))
                {
                    process.Kill();
                    process.WaitForExit(5000);
                }
            }
            catch (ArgumentException)
            {
                // already gone
            }

            Process.Start(Paths.Process, link is null ? "-player" : $"-player \"{link}\"");
        }
    }
}
