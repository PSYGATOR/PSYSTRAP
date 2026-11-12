using System.Runtime.InteropServices;

namespace Bloxstrap
{
    /// <summary>
    /// PSYstrap multi-instance support.
    ///
    /// Roblox stops a second client in two ways:
    ///  1. the session-wide names ROBLOX_singletonMutex / ROBLOX_singletonEvent - a background PSYstrap process
    ///     (the "host") claims both before the first client starts. Roblox tolerates not getting them.
    ///  2. SingleInstanceGuard: a shared-memory block named after the client's own exe path
    ///     ("C:_Users_..._RobloxPlayerBeta.exe.shm") holding the running client's window. A new client from the same
    ///     path hands its launch to that window and quits, so every extra client is started from its own path:
    ///     a directory junction (Instances\2, Instances\3, ...) pointing at the same version folder.
    ///
    /// Nothing is injected into Roblox and no Roblox files are modified or copied.
    /// </summary>
    public static class MultiInstance
    {
        private const string LOG_IDENT = "MultiInstance";

        public const string RobloxProcessName = "RobloxPlayerBeta";
        public const string HostMutexName = "PSYstrap-MultiInstanceHost";
        private const string HostReadyEventName = "PSYstrap-MultiInstanceHostReady";
        private const int MaxInstances = 16;

        public static string InstancesDirectory => Path.Combine(Paths.Base, "Instances");

        public static bool IsHostRunning
        {
            get
            {
                if (!Mutex.TryOpenExisting(HostMutexName, out var mutex))
                    return false;

                mutex.Dispose();
                return true;
            }
        }

        public static List<Process> GetRobloxProcesses() =>
            Utilities.GetProcessesSafe().Where(process => process.ProcessName == RobloxProcessName).ToList();

        /// <summary>Full exe path of a process, using the minimum access right (works where MainModule doesn't).</summary>
        public static string? GetProcessPath(int processId)
        {
            IntPtr handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);

            if (handle == IntPtr.Zero)
                return null;

            try
            {
                var buffer = new StringBuilder(1024);
                int size = buffer.Capacity;
                return QueryFullProcessImageNameW(handle, 0, buffer, ref size) ? buffer.ToString() : null;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        #region Slots
        // Windows resolves junctions when reporting a process's path, so a client started from Instances\2 looks like
        // it runs from the version folder. Two things are used instead:
        //  - Roblox's own guard section (named after the launch path) exists exactly while a client from that path runs
        //  - PSYstrap records which slot each client it launched went to

        private static string SlotsFile => Path.Combine(InstancesDirectory, "slots.json");

        private static string GetGuardSectionName(string directory) =>
            Path.Combine(directory, $"{RobloxProcessName}.exe").Replace('\\', '_') + ".shm";

        /// <summary>True while a Roblox client started from this folder is running.</summary>
        public static bool IsDirectoryInUse(string directory)
        {
            IntPtr section = OpenFileMappingW(FILE_MAP_READ, false, GetGuardSectionName(directory.TrimEnd('\\')));

            if (section == IntPtr.Zero)
                return false;

            CloseHandle(section);
            return true;
        }

        private static Dictionary<int, int> ReadSlots()
        {
            try
            {
                if (File.Exists(SlotsFile))
                    return JsonSerializer.Deserialize<Dictionary<int, int>>(File.ReadAllText(SlotsFile)) ?? new();
            }
            catch { }

            return new();
        }

        /// <summary>Remembers which slot a launched client uses (and forgets clients that have closed).</summary>
        public static void RecordSlot(int processId, string launchDirectory)
        {
            try
            {
                int slot = 1;
                string name = Path.GetFileName(launchDirectory.TrimEnd('\\'));

                if (launchDirectory.StartsWith(InstancesDirectory, StringComparison.OrdinalIgnoreCase) && int.TryParse(name, out int parsed))
                    slot = parsed;

                var alive = GetRobloxProcesses().Select(process => process.Id).ToHashSet();
                var slots = ReadSlots().Where(pair => alive.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value);
                slots[processId] = slot;

                Directory.CreateDirectory(InstancesDirectory);
                File.WriteAllText(SlotsFile, JsonSerializer.Serialize(slots));
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
            }
        }

        /// <summary>Which instance slot a client runs from: 1 for the normal version folder, 2+ for Instances\N.</summary>
        public static int GetSlot(int processId) => ReadSlots().TryGetValue(processId, out int slot) ? slot : 1;
        #endregion

        /// <summary>Starts the host process if it isn't running, and waits until it holds the Roblox names.</summary>
        public static void EnsureHost()
        {
            if (IsHostRunning)
                return;

            using var ready = new EventWaitHandle(false, EventResetMode.ManualReset, HostReadyEventName);
            Process.Start(Paths.Process, "-multiinstancehost");

            if (!ready.WaitOne(TimeSpan.FromSeconds(5)))
                App.Logger.WriteLine(LOG_IDENT, "Host didn't report ready in time");
        }

        /// <summary>
        /// Picks where to start the next client from. The first client uses the real version folder; while that one runs,
        /// further clients use a free junction slot, created or re-pointed at the current version folder as needed.
        /// </summary>
        public static string GetLaunchDirectory(string versionDirectory)
        {
            // a client that has only just started may not have created its guard yet, so slots recorded for live
            // clients count as taken too
            var alive = GetRobloxProcesses().Select(process => process.Id).ToHashSet();
            var recordedSlots = ReadSlots().Where(pair => alive.Contains(pair.Key)).Select(pair => pair.Value).ToHashSet();

            bool InUse(string dir, int slot) => recordedSlots.Contains(slot) || IsDirectoryInUse(dir);

            if (!InUse(versionDirectory, 1))
                return versionDirectory;

            Directory.CreateDirectory(InstancesDirectory);

            for (int slot = 2; slot <= MaxInstances; slot++)
            {
                string junction = Path.Combine(InstancesDirectory, slot.ToString());

                if (InUse(junction, slot))
                    continue;

                EnsureJunction(junction, versionDirectory);
                App.Logger.WriteLine(LOG_IDENT, $"Using instance slot {slot}");
                return junction;
            }

            throw new InvalidOperationException($"All {MaxInstances} instance slots are in use.");
        }

        private static void EnsureJunction(string junction, string target)
        {
            var info = new DirectoryInfo(junction);

            if (info.Exists)
            {
                if (String.Equals(info.LinkTarget?.TrimEnd('\\'), target.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                    return;

                if (info.LinkTarget is null)
                    throw new InvalidOperationException($"{junction} is a real folder, not a PSYstrap instance link. Remove it and try again.");

                // non-recursive delete removes only the junction itself, never the files it points to
                Directory.Delete(junction, false);
            }

            var mklink = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c mklink /J \"{junction}\" \"{target}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            })!;

            mklink.WaitForExit(10000);

            if (!File.Exists(Path.Combine(junction, $"{RobloxProcessName}.exe")))
                throw new InvalidOperationException($"Couldn't create the instance link {junction}.");
        }

        /// <summary>
        /// Body of the "-multiinstancehost" process. Holds Roblox's singleton names while any client is running,
        /// applies the low-resource options to clients you aren't using, then exits once every client has closed.
        /// </summary>
        public static void RunHost()
        {
            const string HOST_IDENT = "MultiInstance::Host";

            using var hostMutex = new Mutex(true, HostMutexName, out bool isOnlyHost);
            using var ready = new EventWaitHandle(false, EventResetMode.ManualReset, HostReadyEventName);

            if (!isOnlyHost)
            {
                App.Logger.WriteLine(HOST_IDENT, "Another host is already running");
                ready.Set();
                return;
            }

            using var robloxMutex = new Mutex(false, "ROBLOX_singletonMutex");
            try { robloxMutex.WaitOne(0); } catch (AbandonedMutexException) { }

            // occupy Roblox's event name with an object of another type so no client can create the real event
            Semaphore? eventBlocker = null;

            try
            {
                eventBlocker = new Semaphore(1, 1, "ROBLOX_singletonEvent", out bool createdNew);

                if (!createdNew)
                    App.Logger.WriteLine(HOST_IDENT, "ROBLOX_singletonEvent was already taken");
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                // a Roblox client started before PSYstrap's host owns the real event - extra clients will hand off to it
                // until every client has been closed once
                App.Logger.WriteLine(HOST_IDENT, "A Roblox client already owns ROBLOX_singletonEvent; multi-instance starts working after all clients close");
            }

            ready.Set();
            App.Logger.WriteLine(HOST_IDENT, "Holding Roblox singleton names");

            var startedAt = DateTime.Now;
            DateTime? emptySince = null;
            var nextTrim = DateTime.Now.AddSeconds(30);

            while (true)
            {
                // once a second, so a window you switch to gets full speed back quickly
                Thread.Sleep(1000);

                var clients = GetRobloxProcesses();

                if (clients.Count == 0)
                {
                    emptySince ??= DateTime.Now;

                    // give the first client time to start, then leave once nothing has run for a while
                    if (DateTime.Now - startedAt > TimeSpan.FromSeconds(60) && DateTime.Now - emptySince > TimeSpan.FromSeconds(15))
                        break;

                    continue;
                }

                emptySince = null;

                try
                {
                    if (App.Settings.HasFileOnDiskChanged())
                        App.Settings.Load(false);

                    bool trimNow = DateTime.Now >= nextTrim;

                    if (trimNow)
                        nextTrim = DateTime.Now.AddSeconds(60);

                    ApplyResourceRules(clients, trimNow);
                }
                catch (Exception ex)
                {
                    App.Logger.WriteException(HOST_IDENT, ex);
                }
            }

            UnmuteAll();
            eventBlocker?.Dispose();
            App.Logger.WriteLine(HOST_IDENT, "All Roblox clients closed, exiting");
        }

        /// <summary>Lowers priority (and optionally trims memory) of clients that aren't the focused window.</summary>
        private static void ApplyResourceRules(List<Process> clients, bool trimNow)
        {
            var options = App.Settings.Prop.MultiInstanceOptions;
            bool muteBackground = App.Settings.Prop.Gameplay.MuteBackgroundWindows;

            if (clients.Count < 2)
            {
                // nothing to balance with a single client - and it should never be left muted
                foreach (var client in clients)
                    SetMuted(client.Id, false);

                return;
            }

            GetWindowThreadProcessId(GetForegroundWindow(), out uint foregroundPid);

            // if none of the clients is focused (you're in another app), keep the one used last audible
            if (clients.Any(client => client.Id == foregroundPid))
                _lastFocusedClient = (int)foregroundPid;
            else if (clients.All(client => client.Id != _lastFocusedClient))
                _lastFocusedClient = clients[0].Id; // none used yet (or it closed): keep one audible

            foreach (var client in clients)
            {
                bool focused = client.Id == foregroundPid;

                SetMuted(client.Id, muteBackground && client.Id != _lastFocusedClient);

                // efficiency mode (Task Manager's leaf icon) = idle priority + Windows' power throttling
                SetEfficiencyMode(client.Id, options.EfficiencyModeBackground && !focused);

                if (options.EfficiencyModeBackground)
                    SetPriority(client.Id, focused ? ProcessPriorityClass.Normal : ProcessPriorityClass.Idle);
                else if (options.LowerBackgroundPriority)
                    SetPriority(client.Id, focused ? ProcessPriorityClass.Normal : ProcessPriorityClass.BelowNormal);

                if (options.TrimBackgroundMemory && trimNow && !focused)
                    TrimMemory(client.Id);
            }
        }

        private static int _lastFocusedClient;
        private static readonly Dictionary<int, bool> _muted = new();

        /// <summary>Mutes/unmutes a client in the volume mixer, only touching it when the wanted state changes.</summary>
        private static void SetMuted(int processId, bool mute)
        {
            if (_muted.TryGetValue(processId, out bool current) && current == mute)
                return;

            // a client can take a while to open its audio session (and may start muted from last time),
            // so keep trying until the session exists
            var (found, changed) = Integrations.AudioMute.SetMute(processId, mute);

            if (found == 0)
                return;

            if (changed > 0)
                App.Logger.WriteLine("MultiInstance::SetMuted", $"{(mute ? "Muted" : "Unmuted")} client {processId}");

            _muted[processId] = mute;
        }

        /// <summary>Unmutes every client this host muted (when it exits).</summary>
        private static void UnmuteAll()
        {
            foreach (var pair in _muted.Where(pair => pair.Value).ToList())
                Integrations.AudioMute.SetMute(pair.Key, false);

            _muted.Clear();
        }

        // Each helper opens the client with only the one right it needs; asking for full access (Process.Handle)
        // is more likely to be refused for a game client.

        public static bool SetPriority(int processId, ProcessPriorityClass priority)
        {
            IntPtr handle = OpenProcess(PROCESS_SET_INFORMATION | PROCESS_QUERY_LIMITED_INFORMATION, false, processId);

            if (handle == IntPtr.Zero)
                return false;

            try
            {
                return GetPriorityClass(handle) == (uint)priority || SetPriorityClass(handle, (uint)priority);
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        /// <summary>
        /// Turns Windows' power throttling (EcoQoS) on or off for a process: Windows then runs it on slower,
        /// power-efficient scheduling. Same as Task Manager's "Efficiency mode" together with idle priority.
        /// </summary>
        public static bool SetEfficiencyMode(int processId, bool enabled)
        {
            IntPtr handle = OpenProcess(PROCESS_SET_INFORMATION, false, processId);

            if (handle == IntPtr.Zero)
                return false;

            try
            {
                var state = new PROCESS_POWER_THROTTLING_STATE
                {
                    Version = PROCESS_POWER_THROTTLING_CURRENT_VERSION,
                    ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED,
                    StateMask = enabled ? PROCESS_POWER_THROTTLING_EXECUTION_SPEED : 0
                };

                return SetProcessInformation(handle, ProcessPowerThrottling, ref state, (uint)Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>());
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        public static TimeSpan? GetCpuTime(int processId)
        {
            IntPtr handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);

            if (handle == IntPtr.Zero)
                return null;

            try
            {
                return GetProcessTimes(handle, out _, out _, out long kernel, out long user) ? TimeSpan.FromTicks(kernel + user) : null;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        public static DateTime? GetStartTime(int processId)
        {
            IntPtr handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);

            if (handle == IntPtr.Zero)
                return null;

            try
            {
                return GetProcessTimes(handle, out long created, out _, out _, out _) ? DateTime.FromFileTime(created) : null;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        public static bool TrimMemory(int processId)
        {
            IntPtr handle = OpenProcess(PROCESS_SET_QUOTA | PROCESS_QUERY_LIMITED_INFORMATION, false, processId);

            if (handle == IntPtr.Zero)
                return false;

            try
            {
                return EmptyWorkingSet(handle);
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        #region Native
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        private const uint PROCESS_SET_INFORMATION = 0x0200;
        private const uint PROCESS_SET_QUOTA = 0x0100;
        private const uint FILE_MAP_READ = 0x0004;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr OpenFileMappingW(uint access, bool inherit, string name);

        private const int ProcessPowerThrottling = 4; // PROCESS_INFORMATION_CLASS
        private const uint PROCESS_POWER_THROTTLING_CURRENT_VERSION = 1;
        private const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x1;

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_POWER_THROTTLING_STATE
        {
            public uint Version;
            public uint ControlMask;
            public uint StateMask;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessInformation(IntPtr process, int informationClass, ref PROCESS_POWER_THROTTLING_STATE information, uint size);

        [DllImport("kernel32.dll")]
        private static extern bool GetProcessTimes(IntPtr process, out long creation, out long exit, out long kernel, out long user);

        [DllImport("kernel32.dll")]
        private static extern uint GetPriorityClass(IntPtr process);

        [DllImport("kernel32.dll")]
        private static extern bool SetPriorityClass(IntPtr process, uint priorityClass);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int processId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags, StringBuilder name, ref int size);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("psapi.dll")]
        private static extern bool EmptyWorkingSet(IntPtr process);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        #endregion
    }
}
