namespace Bloxstrap.Accounts
{
    /// <summary>
    /// PSYstrap: closes one Roblox client and opens it again in the same server. If the client was signed in to one
    /// of the saved accounts it comes back as that account; otherwise it opens with Roblox's normal rejoin link.
    /// </summary>
    public static class ClientRejoin
    {
        private const string LOG_IDENT = "ClientRejoin";

        /// <summary>Rejoin using the server info the client's watcher published (for turn-taking).</summary>
        public static void Rejoin(int processId)
        {
            var session = LiveSessions.List().FirstOrDefault(s => s.ProcessId == processId);

            if (session is null || session.PlaceId == 0)
                RejoinServer(processId, 0, "", "", false, 0);
            else
                RejoinServer(processId, session.PlaceId, session.ServerId, session.AccessCode, session.IsPrivate, session.UserId);
        }

        /// <summary>Closes the client, then opens placeId/serverId again - as the saved account for userId if there is one.</summary>
        public static void RejoinServer(int processId, long placeId, string serverId, string accessCode, bool isPrivate, long userId)
        {
            string? link = null;

            if (placeId != 0)
            {
                var account = AccountStore.Load().FirstOrDefault(a => a.UserId == userId);
                string? cookie = account is null ? null : AccountStore.GetSession(account);

                if (cookie is not null)
                {
                    string? ticket = Task.Run(() => RobloxAuth.GetLaunchTicketAsync(cookie)).GetAwaiter().GetResult();

                    if (ticket is not null)
                        link = RobloxAuth.BuildLaunchLink(ticket, placeId, isPrivate ? null : serverId, isPrivate ? accessCode : null);
                }

                link ??= isPrivate
                    ? $"roblox://experiences/start?placeId={placeId}&accessCode={accessCode}"
                    : RobloxServers.GetJoinLink(placeId, serverId);
            }

            App.Logger.WriteLine(LOG_IDENT, $"Rejoining client {processId} (place {placeId})");

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
