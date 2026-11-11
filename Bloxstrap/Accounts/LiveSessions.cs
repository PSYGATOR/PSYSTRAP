namespace Bloxstrap.Accounts
{
    /// <summary>
    /// PSYstrap: which server each running client is in. Each watcher publishes its client's current game here
    /// (%LocalAppData%\PSYstrap\Sessions\{pid}.json) so the account launcher can send other accounts to the same server.
    /// </summary>
    public static class LiveSessions
    {
        public class Session
        {
            public int ProcessId { get; set; }
            public long PlaceId { get; set; }
            public string ServerId { get; set; } = "";
            public string AccessCode { get; set; } = "";
            public bool IsPrivate { get; set; }
            public long UserId { get; set; }
            public DateTime Joined { get; set; }
        }

        private static string Folder => Path.Combine(Paths.Base, "Sessions");

        private static string FileFor(int processId) => Path.Combine(Folder, $"{processId}.json");

        public static void Publish(int processId, ActivityData data)
        {
            try
            {
                Directory.CreateDirectory(Folder);

                var session = new Session
                {
                    ProcessId = processId,
                    PlaceId = data.PlaceId,
                    ServerId = data.JobId,
                    AccessCode = data.AccessCode,
                    IsPrivate = data.ServerType == ServerType.Private,
                    UserId = data.UserId,
                    Joined = DateTime.Now
                };

                File.WriteAllText(FileFor(processId), JsonSerializer.Serialize(session));
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("LiveSessions::Publish", ex);
            }
        }

        public static void Clear(int processId)
        {
            try
            {
                if (File.Exists(FileFor(processId)))
                    File.Delete(FileFor(processId));
            }
            catch { }
        }

        /// <summary>Sessions of clients that are still running (files of closed clients are cleaned up).</summary>
        public static List<Session> List()
        {
            var result = new List<Session>();

            if (!Directory.Exists(Folder))
                return result;

            var alive = MultiInstance.GetRobloxProcesses().Select(process => process.Id).ToHashSet();

            foreach (string file in Directory.GetFiles(Folder, "*.json"))
            {
                try
                {
                    var session = JsonSerializer.Deserialize<Session>(File.ReadAllText(file));

                    if (session is not null && alive.Contains(session.ProcessId))
                        result.Add(session);
                    else
                        File.Delete(file);
                }
                catch { }
            }

            return result;
        }
    }
}
