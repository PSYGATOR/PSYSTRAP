using System.Runtime.InteropServices;

namespace Bloxstrap.Integrations
{
    /// <summary>
    /// PSYstrap: decides which Roblox window gets to show Discord presence when several are open. Discord only has one
    /// status, so one window "owns" it. The owner's process ID is kept in a small file every watcher reads.
    ///
    /// With "follow the window you're using" on, the window you last clicked into takes over. Otherwise the first
    /// window keeps it until it closes.
    /// </summary>
    public static class DiscordOwner
    {
        private static string FilePath => Path.Combine(Paths.Base, "DiscordOwner.txt");

        /// <summary>True if this Roblox client should show presence right now. May claim ownership.</summary>
        public static bool Check(int robloxProcessId)
        {
            int? owner = Read();

            bool focused = App.Settings.Prop.Gameplay.DiscordFollowFocusedWindow && ForegroundProcessId() == robloxProcessId;

            if (owner != robloxProcessId && (focused || owner is null || !IsRoblox(owner.Value)))
            {
                Write(robloxProcessId);
                return true;
            }

            return owner == robloxProcessId;
        }

        private static int? Read()
        {
            try
            {
                return File.Exists(FilePath) && int.TryParse(File.ReadAllText(FilePath).Trim(), out int pid) ? pid : null;
            }
            catch (IOException)
            {
                return null;
            }
        }

        private static void Write(int pid)
        {
            try
            {
                File.WriteAllText(FilePath, pid.ToString());
            }
            catch (IOException)
            {
                // someone else is writing it; next check sorts it out
            }
        }

        private static bool IsRoblox(int pid)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                return !process.HasExited && process.ProcessName.StartsWith("RobloxPlayerBeta", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static uint ForegroundProcessId()
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
            return pid;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    }
}
