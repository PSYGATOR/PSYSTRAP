using System.Runtime.InteropServices;

using Bloxstrap.Models.Macros;

namespace Bloxstrap.Integrations.Macros
{
    /// <summary>
    /// PSYstrap turn-taking: Roblox only accepts input in the focused window, so this cycles through the clients that
    /// have a smart macro assigned - focus one, screenshot it, fire its next ready rule, move on.
    ///
    /// Any real mouse or keyboard input from the user pauses it at once (and a running action lets go of its keys);
    /// it resumes after the user has left the PC alone for a few seconds. Must be created on a UI thread
    /// (it uses low-level input hooks to notice the user).
    /// </summary>
    public sealed class RotationRunner : IDisposable
    {
        private const string LOG_IDENT = "RotationRunner";

        private readonly InputHooks _hooks;
        private long _lastUserInput;

        private CancellationTokenSource? _cancel;
        private Thread? _thread;

        public static string MarkerFile => Path.Combine(MultiInstance.InstancesDirectory, "rotation.active");

        public bool Running => _thread is { IsAlive: true };

        public string Status { get; private set; } = "";

        /// <summary>Latest status per instance slot, for the Instances window.</summary>
        public System.Collections.Concurrent.ConcurrentDictionary<int, string> ClientStatus { get; } = new();

        public RotationRunner()
        {
            _hooks = new InputHooks();
            _hooks.RawInput += input =>
            {
                if (!input.Injected)
                    Interlocked.Exchange(ref _lastUserInput, Environment.TickCount64);
            };
        }

        private bool UserIsActive =>
            Environment.TickCount64 - Interlocked.Read(ref _lastUserInput) < App.Settings.Prop.MultiInstanceOptions.RotationResumeSeconds * 1000L;

        public void Start()
        {
            if (Running)
                return;

            Directory.CreateDirectory(MultiInstance.InstancesDirectory);
            File.WriteAllText(MarkerFile, Environment.ProcessId.ToString());

            // starting counts as "touching the PC", so the user gets a moment to let go of the mouse
            Interlocked.Exchange(ref _lastUserInput, Environment.TickCount64);

            _cancel = new CancellationTokenSource();
            var token = _cancel.Token;
            _thread = new Thread(() => Loop(token)) { IsBackground = true, Name = "PSYstrap turn-taking" };
            _thread.Start();

            App.Logger.WriteLine(LOG_IDENT, "Started");
        }

        public void Stop()
        {
            _cancel?.Cancel();
            _thread?.Join(5000);
            _thread = null;

            try { File.Delete(MarkerFile); } catch { }

            Status = "Stopped.";
            App.Logger.WriteLine(LOG_IDENT, "Stopped");
        }

        private void Loop(CancellationToken token)
        {
            var brains = new Dictionary<int, SmartMacroBrain>();   // per client process
            var stopped = new HashSet<int>();                       // clients whose macro hit a Stop rule

            try
            {
                while (!token.IsCancellationRequested)
                {
                    if (UserIsActive)
                    {
                        long idle = Environment.TickCount64 - Interlocked.Read(ref _lastUserInput);
                        int left = Math.Max(1, (int)Math.Ceiling((App.Settings.Prop.MultiInstanceOptions.RotationResumeSeconds * 1000L - idle) / 1000.0));
                        Status = $"Paused - you're using the PC. Resumes {left}s after you stop.";

                        foreach (var brain in brains.Values)
                            brain.ResetTiming();

                        Thread.Sleep(200);
                        continue;
                    }

                    if (App.Settings.HasFileOnDiskChanged())
                        App.Settings.Load(false);

                    var assignments = App.Settings.Prop.MultiInstanceOptions.RotationMacros;

                    var clients = MultiInstance.GetRobloxProcesses()
                        .Select(process => (Process: process, Slot: MultiInstance.GetSlot(process.Id)))
                        .Where(c => assignments.TryGetValue(c.Slot, out string? name) && !String.IsNullOrEmpty(name) && !stopped.Contains(c.Process.Id))
                        .OrderBy(c => c.Slot)
                        .ToList();

                    // forget clients that closed
                    foreach (int pid in brains.Keys.Where(pid => clients.All(c => c.Process.Id != pid)).ToList())
                        brains.Remove(pid);

                    if (clients.Count == 0)
                    {
                        Status = "No running client has a smart macro assigned.";
                        Thread.Sleep(1000);
                        continue;
                    }

                    Status = $"Taking turns between {clients.Count} client{(clients.Count == 1 ? "" : "s")}. Move the mouse or press a key to take over.";

                    foreach (var (process, slot) in clients)
                    {
                        if (token.IsCancellationRequested || UserIsActive)
                            break;

                        RunTurn(process, slot, assignments[slot], brains, stopped, token);
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                Status = "Stopped after an error (see log).";
            }
        }

        private void RunTurn(Process process, int slot, string macroName, Dictionary<int, SmartMacroBrain> brains, HashSet<int> stopped, CancellationToken token)
        {
            var started = Environment.TickCount64;
            IntPtr window;

            try
            {
                process.Refresh();
                window = process.MainWindowHandle;
            }
            catch
            {
                return;
            }

            if (window == IntPtr.Zero)
                return;

            if (IsIconic(window))
            {
                ClientStatus[slot] = "Minimised - skipped (it has to be visible to be read).";
                return;
            }

            if (!brains.TryGetValue(process.Id, out var brain) || brain.Macro.Name != macroName)
            {
                var macro = SmartMacroStore.Load(macroName);

                if (macro is null)
                {
                    ClientStatus[slot] = $"Smart macro \"{macroName}\" not found.";
                    return;
                }

                brains[process.Id] = brain = new SmartMacroBrain(macro);
            }

            if (!Focus(window))
            {
                ClientStatus[slot] = "Windows wouldn't let PSYstrap switch to this client.";
                return;
            }

            Thread.Sleep(120); // let the window come forward and redraw

            if (GetClientArea(window) is not ScreenRect area || area.Width < 16 || area.Height < 16)
                return;

            var decision = brain.Decide(ScreenFrame.Capture(area));

            if (decision is { } fire)
            {
                ClientStatus[slot] = fire.Description;
                App.Logger.WriteLine(LOG_IDENT, $"Instance {slot}: {fire.Description}");

                // the player pauses (and releases keys) if the user grabs the mouse or the client loses focus
                var player = new MacroPlayer(() => GetClientArea(window), () => GetForegroundWindow() == window && !UserIsActive);
                int pid = process.Id;

                bool keepGoing = SmartMacroBrain.Execute(fire.Rule, fire.Result, player, () => Accounts.ClientRejoin.Rejoin(pid), token, out string? problem);

                if (problem is not null)
                    ClientStatus[slot] = problem;

                if (!keepGoing && fire.Rule.Action == RuleAction.Stop)
                {
                    stopped.Add(pid);
                    ClientStatus[slot] = "Stopped by a Stop rule.";
                }

                brain.MarkFired(fire.Rule);
            }
            else if (!ClientStatus.ContainsKey(slot) || ClientStatus[slot].StartsWith("Minimised"))
            {
                ClientStatus[slot] = "Watching.";
            }

            // stay at least the dwell time, so a client isn't flicked past faster than it can react
            int dwell = App.Settings.Prop.MultiInstanceOptions.RotationDwellMs;
            int remaining = dwell - (int)(Environment.TickCount64 - started);

            if (remaining > 0)
                token.WaitHandle.WaitOne(remaining);
        }

        private static ScreenRect? GetClientArea(IntPtr window)
        {
            if (!GetClientRect(window, out var rect))
                return null;

            var topLeft = new POINT();
            ClientToScreen(window, ref topLeft);
            return new ScreenRect(topLeft.X, topLeft.Y, rect.Right, rect.Bottom);
        }

        /// <summary>
        /// Windows only lets the foreground app hand focus around, so this briefly attaches to the current foreground
        /// thread's input (the standard way for switchers to do it).
        /// </summary>
        private static bool Focus(IntPtr window)
        {
            if (GetForegroundWindow() == window)
                return true;

            IntPtr foreground = GetForegroundWindow();
            uint foregroundThread = GetWindowThreadProcessId(foreground, out _);
            uint thisThread = GetCurrentThreadId();
            bool attached = foregroundThread != thisThread && AttachThreadInput(thisThread, foregroundThread, true);

            try
            {
                BringWindowToTop(window);
                SetForegroundWindow(window);
            }
            finally
            {
                if (attached)
                    AttachThreadInput(thisThread, foregroundThread, false);
            }

            return GetForegroundWindow() == window;
        }

        public void Dispose()
        {
            Stop();
            _hooks.Dispose();
        }

        #region Native
        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out RECT rect);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window, ref POINT point);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint attach, uint attachTo, bool doAttach);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        #endregion
    }
}
