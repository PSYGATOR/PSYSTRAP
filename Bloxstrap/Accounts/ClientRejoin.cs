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

            // remember which server we're aiming for, so a failed join can fall back to another one
            if (placeId != 0 && !isPrivate && !String.IsNullOrEmpty(serverId))
                SavePendingRejoin(new PendingRejoin { PlaceId = placeId, ServerId = serverId, UserId = userId, At = DateTime.Now });

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

        public class PendingRejoin
        {
            public long PlaceId { get; set; }
            public string ServerId { get; set; } = "";
            public long UserId { get; set; }
            public DateTime At { get; set; }
        }

        private static string PendingFolder => Path.Combine(Paths.Base, "PendingRejoins");

        private static void SavePendingRejoin(PendingRejoin pending)
        {
            try
            {
                Directory.CreateDirectory(PendingFolder);
                File.WriteAllText(Path.Combine(PendingFolder, $"{pending.PlaceId}-{pending.ServerId}.json"), JsonSerializer.Serialize(pending));
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
            }
        }

        /// <summary>
        /// The rejoin PSYstrap started into this exact server in the last 5 minutes, if any, removed so it's only used
        /// once. Older ones are cleaned up on the way.
        /// </summary>
        public static PendingRejoin? TakePendingRejoin(long placeId, string serverId)
        {
            try
            {
                if (!Directory.Exists(PendingFolder))
                    return null;

                foreach (string file in Directory.GetFiles(PendingFolder, "*.json"))
                {
                    if (DateTime.Now - File.GetLastWriteTime(file) > TimeSpan.FromMinutes(10))
                        File.Delete(file);
                }

                string path = Path.Combine(PendingFolder, $"{placeId}-{serverId}.json");

                if (!File.Exists(path))
                    return null;

                var pending = JsonSerializer.Deserialize<PendingRejoin>(File.ReadAllText(path));
                File.Delete(path);

                return pending is not null && DateTime.Now - pending.At < TimeSpan.FromMinutes(5) ? pending : null;
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                return null;
            }
        }
    }
}
