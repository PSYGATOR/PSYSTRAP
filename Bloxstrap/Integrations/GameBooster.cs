using System.Runtime.InteropServices;

namespace Bloxstrap.Integrations
{
    /// <summary>
    /// PSYstrap: while any Roblox is open, switches Windows to the High performance power plan and closes apps you picked.
    /// When the last Roblox closes, the old power plan comes back and the closed apps are opened again.
    ///
    /// Every watcher runs one of these, but only the one holding the "Booster" lock does anything. What it changed is
    /// written to Booster.json, so whichever watcher is left last can undo it (even if the first one crashed).
    /// </summary>
    public sealed class GameBooster : IDisposable
    {
        private const string LOG_IDENT = "GameBooster";
        private static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

        private class State
        {
            public Guid? OriginalPlan { get; set; }
            public List<string> ClosedApps { get; set; } = new();
        }

        private static string StateFile => Path.Combine(Paths.Base, "Booster.json");

        private readonly int _ownPid;
        private readonly CancellationTokenSource _cancel = new();
        private readonly Thread _thread;

        public GameBooster(int robloxPid)
        {
            _ownPid = robloxPid;
            _thread = new Thread(Loop) { IsBackground = true, Name = "PSYstrap game booster" };
            _thread.Start();
        }

        private void Loop()
        {
            InterProcessLock? owner = null;

            try
            {
                while (!_cancel.IsCancellationRequested)
                {
                    owner ??= TryLock();

                    if (owner is not null)
                    {
                        try { Apply(App.Settings.Prop.Gameplay.Booster); }
                        catch (Exception ex) { App.Logger.WriteException(LOG_IDENT, ex); }
                    }

                    _cancel.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(10));
                }
            }
            finally
            {
                if (owner is not null)
                {
                    // the last Roblox is closing: put everything back
                    bool othersOpen = MultiInstance.GetRobloxProcesses().Any(p => p.Id != _ownPid && !p.HasExited);

                    if (!othersOpen)
                        Undo();

                    owner.Dispose();
                }
            }
        }

        private static InterProcessLock? TryLock()
        {
            var candidate = new InterProcessLock("Booster");

            if (candidate.IsAcquired)
                return candidate;

            candidate.Mutex.Dispose();
            return null;
        }

        private static void Apply(Models.Persistable.BoosterSettings options)
        {
            var state = Load();

            if (!options.Enabled)
            {
                if (state.OriginalPlan is not null || state.ClosedApps.Count > 0)
                    Undo();

                return;
            }

            bool changed = false;

            if (options.HighPerformancePower && state.OriginalPlan is null && GetActivePlan() is Guid current && current != HighPerformance)
            {
                if (PowerSetActiveScheme(IntPtr.Zero, HighPerformance) == 0)
                {
                    state.OriginalPlan = current;
                    changed = true;
                    App.Logger.WriteLine(LOG_IDENT, "Switched to the High performance power plan");
                }
                else
                {
                    App.Logger.WriteLine(LOG_IDENT, "This PC has no High performance power plan");
                }
            }
            else if (!options.HighPerformancePower && state.OriginalPlan is Guid original)
            {
                PowerSetActiveScheme(IntPtr.Zero, original);
                state.OriginalPlan = null;
                changed = true;
            }

            foreach (string name in options.CloseApps.Select(n => n.Trim().Replace(".exe", "", StringComparison.OrdinalIgnoreCase)).Where(n => n.Length > 0))
            {
                // never anything Roblox or PSYstrap needs
                if (name.StartsWith("Roblox", StringComparison.OrdinalIgnoreCase) || name.Equals(App.ProjectName, StringComparison.OrdinalIgnoreCase))
                    continue;

                foreach (var process in Process.GetProcessesByName(name))
                {
                    using (process)
                    {
                        string? path = MultiInstance.GetProcessPath(process.Id);

                        try
                        {
                            // ask nicely first (lets apps save), then make sure
                            if (!process.CloseMainWindow() || !process.WaitForExit(8000))
                                process.Kill();

                            if (path is not null && !state.ClosedApps.Contains(path, StringComparer.OrdinalIgnoreCase))
                                state.ClosedApps.Add(path);

                            changed = true;
                            App.Logger.WriteLine(LOG_IDENT, $"Closed {name}");
                        }
                        catch { }
                    }
                }
            }

            if (changed)
                Save(state);
        }

        private static void Undo()
        {
            var state = Load();

            if (state.OriginalPlan is Guid original)
            {
                PowerSetActiveScheme(IntPtr.Zero, original);
                App.Logger.WriteLine(LOG_IDENT, "Put the old power plan back");
            }

            foreach (string path in state.ClosedApps.Where(File.Exists))
            {
                try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
                catch (Exception ex) { App.Logger.WriteLine(LOG_IDENT, $"Couldn't reopen {path}: {ex.Message}"); }
            }

            try { File.Delete(StateFile); } catch { }
        }

        private static State Load()
        {
            try { return File.Exists(StateFile) ? JsonSerializer.Deserialize<State>(File.ReadAllText(StateFile)) ?? new() : new(); }
            catch { return new(); }
        }

        private static void Save(State state)
        {
            try { File.WriteAllText(StateFile, JsonSerializer.Serialize(state)); }
            catch (Exception ex) { App.Logger.WriteException(LOG_IDENT, ex); }
        }

        private static Guid? GetActivePlan()
        {
            if (PowerGetActiveScheme(IntPtr.Zero, out IntPtr pointer) != 0)
                return null;

            try { return Marshal.PtrToStructure<Guid>(pointer); }
            finally { LocalFree(pointer); }
        }

        public void Dispose()
        {
            _cancel.Cancel();
            _thread.Join(15000);
        }

        [DllImport("powrprof.dll")]
        private static extern uint PowerGetActiveScheme(IntPtr rootPowerKey, out IntPtr activePolicyGuid);

        [DllImport("powrprof.dll")]
        private static extern uint PowerSetActiveScheme(IntPtr rootPowerKey, [MarshalAs(UnmanagedType.LPStruct)] Guid schemeGuid);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);
    }
}
