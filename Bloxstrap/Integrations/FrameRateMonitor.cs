using System.Runtime.InteropServices;

namespace Bloxstrap.Integrations
{
    /// <summary>
    /// PSYstrap: counts a process's DXGI Present calls (one per rendered frame) through a real-time ETW session,
    /// the same signal PresentMon uses. Works from outside the game - nothing is injected into Roblox.
    ///
    /// Starting an ETW session needs admin rights or membership of the "Performance Log Users" group;
    /// without either, Start() returns false and Error says why. Only D3D11/D3D12 (DXGI) presents are seen,
    /// so Roblox's Vulkan/OpenGL renderers won't report a frame rate.
    /// </summary>
    public sealed unsafe class FrameRateMonitor : IDisposable
    {
        // one session per Roblox client, so multi-instance clients each get their own counter
        private const string SessionPrefix = "PSYstrap-FrameRate-";
        private string SessionName => SessionPrefix + _processId;

        private static readonly Guid DxgiProvider = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9"); // Microsoft-Windows-DXGI
        private const ushort PresentStartEventId = 42;

        private const uint ERROR_SUCCESS = 0;
        private const uint ERROR_ACCESS_DENIED = 5;
        private const uint EVENT_TRACE_CONTROL_STOP = 1;
        private const uint EVENT_CONTROL_CODE_ENABLE_PROVIDER = 1;
        private const uint EVENT_TRACE_REAL_TIME_MODE = 0x100;
        private const uint WNODE_FLAG_TRACED_GUID = 0x00020000;
        private const uint PROCESS_TRACE_MODE_REAL_TIME = 0x100;
        private const uint PROCESS_TRACE_MODE_EVENT_RECORD = 0x10000000;
        private const ulong INVALID_PROCESSTRACE_HANDLE = ulong.MaxValue;
        private const byte TRACE_LEVEL_VERBOSE = 5;

        private readonly uint _processId;
        private readonly EventRecordCallback _callback; // kept as a field so the GC can't collect it while ETW holds the pointer

        private ulong _sessionHandle;
        private ulong _traceHandle = INVALID_PROCESSTRACE_HANDLE;
        private IntPtr _loggerNamePtr;
        private Thread? _thread;
        private long _presentCount;
        private bool _disposed;

        public string? Error { get; private set; }

        public bool Running { get; private set; }

        /// <summary>Total presents seen so far. Sample it periodically and divide the difference by elapsed time.</summary>
        public long PresentCount => Interlocked.Read(ref _presentCount);

        public FrameRateMonitor(int processId)
        {
            _processId = (uint)processId;
            _callback = OnEvent;
        }

        public bool Start()
        {
            const string LOG_IDENT = "FrameRateMonitor::Start";

            // a PSYstrap that crashed can leave its session running - ETW sessions outlive processes
            StopSession("PSYstrap-FrameRate"); // name used by older builds
            StopSession(SessionName);
            StopOrphanedSessions();

            int propsSize = sizeof(EVENT_TRACE_PROPERTIES);
            int totalSize = propsSize + (SessionName.Length + 1) * 2;
            IntPtr props = Marshal.AllocHGlobal(totalSize);

            try
            {
                new Span<byte>((void*)props, totalSize).Clear();

                var p = (EVENT_TRACE_PROPERTIES*)props;
                p->Wnode.BufferSize = (uint)totalSize;
                p->Wnode.Flags = WNODE_FLAG_TRACED_GUID;
                p->Wnode.ClientContext = 1; // QPC timestamps
                p->Wnode.Guid = Guid.NewGuid();
                p->BufferSize = 64;
                p->MinimumBuffers = 4;
                p->MaximumBuffers = 16;
                p->LogFileMode = EVENT_TRACE_REAL_TIME_MODE;
                p->FlushTimer = 1;
                p->LoggerNameOffset = (uint)propsSize;

                uint result = StartTraceW(out _sessionHandle, SessionName, props);

                if (result != ERROR_SUCCESS)
                {
                    Error = result == ERROR_ACCESS_DENIED
                        ? "needs admin or the Performance Log Users group"
                        : $"couldn't start ETW session (error {result})";

                    App.Logger.WriteLine(LOG_IDENT, $"StartTrace failed: {result}");
                    return false;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(props);
            }

            var provider = DxgiProvider;
            uint enable = EnableTraceEx2(_sessionHandle, ref provider, EVENT_CONTROL_CODE_ENABLE_PROVIDER, TRACE_LEVEL_VERBOSE, ulong.MaxValue, 0, 0, IntPtr.Zero);

            if (enable != ERROR_SUCCESS)
            {
                Error = $"couldn't enable DXGI events (error {enable})";
                App.Logger.WriteLine(LOG_IDENT, $"EnableTraceEx2 failed: {enable}");
                StopSession(SessionName);
                return false;
            }

            _loggerNamePtr = Marshal.StringToHGlobalUni(SessionName);

            var logfile = new EVENT_TRACE_LOGFILEW
            {
                LoggerName = _loggerNamePtr,
                ProcessTraceMode = PROCESS_TRACE_MODE_REAL_TIME | PROCESS_TRACE_MODE_EVENT_RECORD,
                EventRecordCallback = Marshal.GetFunctionPointerForDelegate(_callback)
            };

            _traceHandle = OpenTraceW(ref logfile);

            if (_traceHandle == INVALID_PROCESSTRACE_HANDLE)
            {
                Error = $"couldn't open ETW session (error {Marshal.GetLastWin32Error()})";
                App.Logger.WriteLine(LOG_IDENT, Error);
                StopSession(SessionName);
                return false;
            }

            _thread = new Thread(() =>
            {
                // blocks until the session is stopped
                uint processResult = ProcessTrace(new[] { _traceHandle }, 1, IntPtr.Zero, IntPtr.Zero);
                App.Logger.WriteLine("FrameRateMonitor::ProcessTrace", $"Finished ({processResult})");
            })
            {
                IsBackground = true,
                Name = "PSYstrap FPS monitor"
            };

            _thread.Start();
            Running = true;

            App.Logger.WriteLine(LOG_IDENT, $"Watching DXGI presents for PID {_processId}");
            return true;
        }

        private void OnEvent(IntPtr record)
        {
            // EVENT_RECORD starts with EVENT_HEADER: ProcessId @12, ProviderId @24, EventDescriptor.Id @40
            byte* header = (byte*)record;

            if (*(uint*)(header + 12) != _processId)
                return;

            if (*(ushort*)(header + 40) != PresentStartEventId)
                return;

            if (*(Guid*)(header + 24) != DxgiProvider)
                return;

            Interlocked.Increment(ref _presentCount);

            // Present_Start's first field is the IDXGISwapChain pointer. A game can present to more than one
            // swapchain per frame, so frames are counted per swapchain (like PresentMon) instead of in total.
            // EVENT_RECORD: UserDataLength @86, UserData pointer @96
            ulong swapChain = 0;

            if (*(ushort*)(header + 86) >= 8)
                swapChain = *(ulong*)(*(IntPtr*)(header + 96));

            // EVENT_HEADER.TimeStamp @16, a raw QPC value since the session uses ClientContext 1
            long timestamp = *(long*)(header + 16);

            lock (_frames)
            {
                if (!_frames.TryGetValue(swapChain, out var queue))
                    _frames[swapChain] = queue = new Queue<long>();

                queue.Enqueue(timestamp);

                // keep two seconds; enough for a one second window ending at the newest frame
                long oldest = timestamp - 2 * Stopwatch.Frequency;

                while (queue.Count > 0 && queue.Peek() < oldest)
                    queue.Dequeue();
            }
        }

        // frame times per swapchain. ETW hands events over in batches (about once a second), so counting
        // arrivals per half second swings between 0 and double. Counting by when each frame happened doesn't.
        private readonly Dictionary<ulong, Queue<long>> _frames = new();

        /// <summary>
        /// Frames per second of the busiest swapchain (the game view; extra ones are UI layers), over the second
        /// before its newest frame. 0 when nothing was presented for a while (minimized, frozen).
        /// </summary>
        public double GetFrameRate()
        {
            long now = Stopwatch.GetTimestamp();
            long second = Stopwatch.Frequency;
            double best = 0;

            lock (_frames)
            {
                foreach (var swapChain in _frames.Keys.ToList())
                {
                    var queue = _frames[swapChain];

                    if (queue.Count == 0)
                    {
                        _frames.Remove(swapChain);
                        continue;
                    }

                    long newest = queue.Last();

                    // events can lag up to about a second behind, so only call it stopped after 2.5
                    if (now - newest > second * 5 / 2)
                    {
                        if (now - newest > second * 10)
                            _frames.Remove(swapChain);

                        continue;
                    }

                    long from = newest - second;
                    int count = 0;

                    foreach (long t in queue)
                        if (t > from)
                            count++;

                    best = Math.Max(best, count);
                }
            }

            return best;
        }

        /// <summary>Frame rate of every swapchain, for the log.</summary>
        public string DescribeSwapChains()
        {
            lock (_frames)
            {
                return string.Join(", ", _frames.Select(pair =>
                {
                    long newest = pair.Value.Count > 0 ? pair.Value.Last() : 0;
                    return $"{pair.Key:X}={pair.Value.Count(t => t > newest - Stopwatch.Frequency)}";
                }));
            }
        }

        private static void StopSession(string name)
        {
            int size = sizeof(EVENT_TRACE_PROPERTIES) + 1024 * 2;
            IntPtr props = Marshal.AllocHGlobal(size);

            try
            {
                new Span<byte>((void*)props, size).Clear();
                var p = (EVENT_TRACE_PROPERTIES*)props;
                p->Wnode.BufferSize = (uint)size;
                p->LoggerNameOffset = (uint)sizeof(EVENT_TRACE_PROPERTIES);

                ControlTraceW(0, name, props, EVENT_TRACE_CONTROL_STOP);
            }
            finally
            {
                Marshal.FreeHGlobal(props);
            }
        }

        /// <summary>Stops PSYstrap FPS sessions whose Roblox client is gone (left behind by a crash).</summary>
        private static void StopOrphanedSessions()
        {
            const int maxSessions = 64;
            const int nameChars = 1024;
            int entrySize = sizeof(EVENT_TRACE_PROPERTIES) + nameChars * 2 * 2; // logger name + log file name
            IntPtr block = Marshal.AllocHGlobal(entrySize * maxSessions);
            var pointers = new IntPtr[maxSessions];

            try
            {
                new Span<byte>((void*)block, entrySize * maxSessions).Clear();

                for (int i = 0; i < maxSessions; i++)
                {
                    pointers[i] = block + i * entrySize;
                    var p = (EVENT_TRACE_PROPERTIES*)pointers[i];
                    p->Wnode.BufferSize = (uint)entrySize;
                    p->LoggerNameOffset = (uint)sizeof(EVENT_TRACE_PROPERTIES);
                    p->LogFileNameOffset = (uint)(sizeof(EVENT_TRACE_PROPERTIES) + nameChars * 2);
                }

                if (QueryAllTracesW(pointers, maxSessions, out uint count) != ERROR_SUCCESS)
                    return;

                for (int i = 0; i < count; i++)
                {
                    var p = (EVENT_TRACE_PROPERTIES*)pointers[i];
                    string? name = Marshal.PtrToStringUni(pointers[i] + (int)p->LoggerNameOffset);

                    if (name is null || !name.StartsWith(SessionPrefix, StringComparison.Ordinal))
                        continue;

                    if (int.TryParse(name[SessionPrefix.Length..], out int pid) && !Utilities.GetProcessesSafe().Any(x => x.Id == pid))
                    {
                        App.Logger.WriteLine("FrameRateMonitor::StopOrphanedSessions", $"Stopping orphaned session {name}");
                        StopSession(name);
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("FrameRateMonitor::StopOrphanedSessions", ex);
            }
            finally
            {
                Marshal.FreeHGlobal(block);
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Running = false;

            StopSession(SessionName);

            if (_traceHandle != INVALID_PROCESSTRACE_HANDLE)
            {
                CloseTrace(_traceHandle);
                _traceHandle = INVALID_PROCESSTRACE_HANDLE;
            }

            _thread?.Join(2000);

            if (_loggerNamePtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_loggerNamePtr);
                _loggerNamePtr = IntPtr.Zero;
            }
        }

        #region Native
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate void EventRecordCallback(IntPtr eventRecord);

        [StructLayout(LayoutKind.Sequential)]
        private struct WNODE_HEADER
        {
            public uint BufferSize;
            public uint ProviderId;
            public ulong HistoricalContext;
            public long TimeStamp;
            public Guid Guid;
            public uint ClientContext;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct EVENT_TRACE_PROPERTIES
        {
            public WNODE_HEADER Wnode;
            public uint BufferSize;
            public uint MinimumBuffers;
            public uint MaximumBuffers;
            public uint MaximumFileSize;
            public uint LogFileMode;
            public uint FlushTimer;
            public uint EnableFlags;
            public int AgeLimit;
            public uint NumberOfBuffers;
            public uint FreeBuffers;
            public uint EventsLost;
            public uint BuffersWritten;
            public uint LogBuffersLost;
            public uint RealTimeBuffersLost;
            public IntPtr LoggerThreadId;
            public uint LogFileNameOffset;
            public uint LoggerNameOffset;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct EVENT_TRACE_HEADER
        {
            public ushort Size;
            public ushort FieldTypeFlags;
            public uint Version;
            public uint ThreadId;
            public uint ProcessId;
            public long TimeStamp;
            public Guid Guid;
            public ulong ProcessorTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct EVENT_TRACE
        {
            public EVENT_TRACE_HEADER Header;
            public uint InstanceId;
            public uint ParentInstanceId;
            public Guid ParentGuid;
            public IntPtr MofData;
            public uint MofLength;
            public uint ClientContext;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TRACE_LOGFILE_HEADER
        {
            public uint BufferSize;
            public uint Version;
            public uint ProviderVersion;
            public uint NumberOfProcessors;
            public long EndTime;
            public uint TimerResolution;
            public uint MaximumFileSize;
            public uint LogFileMode;
            public uint BuffersWritten;
            public Guid LogInstanceGuid;
            public IntPtr LoggerName;
            public IntPtr LogFileName;
            public fixed byte TimeZone[172]; // TIME_ZONE_INFORMATION
            public long BootTime;
            public long PerfFreq;
            public long StartTime;
            public uint ReservedFlags;
            public uint BuffersLost;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct EVENT_TRACE_LOGFILEW
        {
            public IntPtr LogFileName;
            public IntPtr LoggerName;
            public long CurrentTime;
            public uint BuffersRead;
            public uint ProcessTraceMode;
            public EVENT_TRACE CurrentEvent;
            public TRACE_LOGFILE_HEADER LogfileHeader;
            public IntPtr BufferCallback;
            public uint BufferSize;
            public uint Filled;
            public uint EventsLost;
            public IntPtr EventRecordCallback;
            public uint IsKernelTrace;
            public IntPtr Context;
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        private static extern uint StartTraceW(out ulong sessionHandle, string sessionName, IntPtr properties);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        private static extern uint ControlTraceW(ulong sessionHandle, string sessionName, IntPtr properties, uint controlCode);

        [DllImport("advapi32.dll")]
        private static extern uint EnableTraceEx2(ulong sessionHandle, ref Guid providerId, uint controlCode, byte level,
            ulong matchAnyKeyword, ulong matchAllKeyword, uint timeout, IntPtr enableParameters);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern ulong OpenTraceW(ref EVENT_TRACE_LOGFILEW logfile);

        [DllImport("advapi32.dll")]
        private static extern uint ProcessTrace(ulong[] handleArray, uint handleCount, IntPtr startTime, IntPtr endTime);

        [DllImport("advapi32.dll")]
        private static extern uint CloseTrace(ulong traceHandle);

        [DllImport("advapi32.dll")]
        private static extern uint QueryAllTracesW(IntPtr[] propertyArray, uint propertyArrayCount, out uint loggerCount);
        #endregion
    }
}
