using System.Runtime.InteropServices;

namespace Bloxstrap.Integrations
{
    /// <summary>
    /// PSYstrap: an FPS limit that changes while Roblox is running, no restart. One per Roblox window (its watcher).
    ///
    /// Nothing is installed or injected. Roblox's window is put in a Windows job object and the job
    /// gets a CPU limit, which PSYstrap keeps adjusting (using the FPS counter) so Roblox lands near the chosen FPS.
    /// Frames aren't as evenly spaced as with a real frame limiter, so it suits AFK windows more than playing.
    /// </summary>
    public sealed class LiveFpsLimiter : IDisposable
    {
        private const string LOG_IDENT = "LiveFpsLimiter";
        private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

        private readonly int _robloxPid;
        private readonly Func<double?> _getFramerate;
        private readonly Thread _thread;
        private readonly CancellationTokenSource _cancel = new();

        private IntPtr _job;
        private bool _jobFailed;
        private bool _capped;
        private double _rate;           // share of the whole PC's CPU Roblox may use, 0..1
        private TimeSpan _lastCpu;
        private DateTime _lastSample;
        private double _cpuShare;       // what Roblox actually used over the last interval
        private int _ticks;
        private int _sinceAdjust;
        private readonly Queue<double> _recent = new();

        /// <summary>What the limiter is doing, for the in-game menu.</summary>
        public string Status { get; private set; } = "";

        /// <param name="getFramerate">Current FPS, or null when the FPS counter isn't available.</param>
        public LiveFpsLimiter(int robloxPid, Func<double?> getFramerate)
        {
            _robloxPid = robloxPid;
            _getFramerate = getFramerate;

            _thread = new Thread(Run) { IsBackground = true, Name = "PSYstrap live FPS limit" };
            _thread.Start();
        }

        private void Run()
        {
            var token = _cancel.Token;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    Tick();
                }
                catch (Exception ex)
                {
                    App.Logger.WriteException(LOG_IDENT, ex);
                }

                token.WaitHandle.WaitOne(Interval);
            }
        }

        private void Tick()
        {
            var settings = App.Settings.Prop.LiveFpsLimit;
            int target = Math.Clamp(settings.Limit, RobloxUserSettings.MinCustomFramerate, RobloxUserSettings.MaxCustomFramerate);

            SampleCpu();

            if (settings.Mode != LiveFpsLimitMode.Experimental && _capped)
                Uncap();

            // anything else (including RivaTuner from a test build) is off
            if (settings.Mode == LiveFpsLimitMode.Experimental)
                TickExperimental(target);
            else
                Status = "";
        }

        #region Experimental
        private void TickExperimental(int target)
        {
            if (_getFramerate() is not double fps)
            {
                Status = "Needs the FPS counter, which needs admin or the Performance Log Users group.";
                if (_capped) Uncap();
                return;
            }

            if (fps <= 0)
            {
                Status = "Roblox isn't drawing right now (minimized?).";
                return;
            }

            if (!_capped)
            {
                if (fps <= target * 1.05)
                {
                    Status = $"Roblox is already at {fps:0} FPS, no need to hold it back.";
                    return;
                }

                // start from what it uses now, scaled down to the frame rate we want
                double start = Math.Max(0.005, _cpuShare * target / fps);

                if (!SetRate(start))
                    return;

                _capped = true;
                _recent.Clear();
                _sinceAdjust = 0;
                Status = $"Bringing Roblox down to about {target} FPS...";
                return;
            }

            // the FPS swings from second to second under a CPU limit, and the reading lags behind by up to a second,
            // so steer by the average of the last few seconds and only every few seconds, in small steps
            _recent.Enqueue(fps);

            while (_recent.Count > 4)
                _recent.Dequeue();

            if (++_sinceAdjust < 3)
            {
                Status = $"Holding Roblox around {target} FPS (now {_recent.Average():0}).";
                return;
            }

            _sinceAdjust = 0;
            double average = _recent.Average();
            double step = Math.Clamp(Math.Pow(target / average, 0.6), 0.8, 1.25);
            double next = _rate * step;
            fps = average;

            if (next >= 1.0)
            {
                // even all of the CPU doesn't get it above the target: it's already slower than the limit
                Uncap();
                Status = $"Roblox is at {fps:0} FPS, under the limit.";
                return;
            }

            SetRate(Math.Max(0.002, next));
            Status = $"Holding Roblox around {target} FPS (now {fps:0}).";

            if (++_ticks % 2 == 0)
                App.Logger.WriteLine(LOG_IDENT, $"target {target}, average fps {fps:0.0}, CPU limit {_rate * 100:0.00}% of the PC, used {_cpuShare * 100:0.00}%");
        }

        private void SampleCpu()
        {
            try
            {
                using var process = Process.GetProcessById(_robloxPid);
                var cpu = process.TotalProcessorTime;
                var now = DateTime.UtcNow;

                if (_lastSample != default)
                {
                    double elapsed = (now - _lastSample).TotalMilliseconds * Environment.ProcessorCount;

                    if (elapsed > 0)
                        _cpuShare = Math.Clamp((cpu - _lastCpu).TotalMilliseconds / elapsed, 0, 1);
                }

                _lastCpu = cpu;
                _lastSample = now;
            }
            catch { }
        }

        private bool EnsureJob()
        {
            if (_job != IntPtr.Zero)
                return true;

            if (_jobFailed)
                return false;

            IntPtr process = OpenProcess(PROCESS_SET_QUOTA | PROCESS_TERMINATE, false, (uint)_robloxPid);

            if (process == IntPtr.Zero)
                return JobFailed($"couldn't open Roblox ({Marshal.GetLastWin32Error()})");

            try
            {
                IntPtr job = CreateJobObject(IntPtr.Zero, null);

                if (job == IntPtr.Zero)
                    return JobFailed($"couldn't make a job object ({Marshal.GetLastWin32Error()})");

                if (!AssignProcessToJobObject(job, process))
                {
                    int error = Marshal.GetLastWin32Error();
                    CloseHandle(job);
                    return JobFailed($"couldn't put Roblox in the job ({error})");
                }

                _job = job;
                App.Logger.WriteLine(LOG_IDENT, $"PID {_robloxPid} is in a job object now");
                return true;
            }
            finally
            {
                CloseHandle(process);
            }
        }

        private bool JobFailed(string why)
        {
            _jobFailed = true;
            Status = "Windows wouldn't let PSYstrap limit this window.";
            App.Logger.WriteLine(LOG_IDENT, $"Experimental limit unavailable: {why}");
            return false;
        }

        private bool SetRate(double share)
        {
            if (!EnsureJob())
                return false;

            var info = new JOBOBJECT_CPU_RATE_CONTROL_INFORMATION
            {
                ControlFlags = JOB_OBJECT_CPU_RATE_CONTROL_ENABLE | JOB_OBJECT_CPU_RATE_CONTROL_HARD_CAP,
                CpuRate = (uint)Math.Clamp(Math.Round(share * 10000), 1, 10000)
            };

            if (!SetInformationJobObject(_job, JobObjectCpuRateControlInformation, ref info, (uint)Marshal.SizeOf<JOBOBJECT_CPU_RATE_CONTROL_INFORMATION>()))
            {
                App.Logger.WriteLine(LOG_IDENT, $"Couldn't set the CPU limit ({Marshal.GetLastWin32Error()})");
                return false;
            }

            _rate = share;
            return true;
        }

        private void Uncap()
        {
            _capped = false;

            if (_job == IntPtr.Zero)
                return;

            var info = new JOBOBJECT_CPU_RATE_CONTROL_INFORMATION();
            SetInformationJobObject(_job, JobObjectCpuRateControlInformation, ref info, (uint)Marshal.SizeOf<JOBOBJECT_CPU_RATE_CONTROL_INFORMATION>());
            App.Logger.WriteLine(LOG_IDENT, "CPU limit removed");
        }
        #endregion

        public void Dispose()
        {
            _cancel.Cancel();
            _thread.Join(3000);

            // the job lives as long as Roblox does, so take the limit off before letting go of it
            if (_capped)
                Uncap();

            if (_job != IntPtr.Zero)
            {
                CloseHandle(_job);
                _job = IntPtr.Zero;
            }
        }

        #region Native
        private const uint PROCESS_SET_QUOTA = 0x0100;
        private const uint PROCESS_TERMINATE = 0x0001;
        private const int JobObjectCpuRateControlInformation = 15;
        private const uint JOB_OBJECT_CPU_RATE_CONTROL_ENABLE = 0x1;
        private const uint JOB_OBJECT_CPU_RATE_CONTROL_HARD_CAP = 0x4;

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_CPU_RATE_CONTROL_INFORMATION
        {
            public uint ControlFlags;
            public uint CpuRate;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref JOBOBJECT_CPU_RATE_CONTROL_INFORMATION info, uint length);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);
        #endregion
    }
}
