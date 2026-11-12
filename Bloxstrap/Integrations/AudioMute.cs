using System.Runtime.InteropServices;

namespace Bloxstrap.Integrations
{
    /// <summary>
    /// PSYstrap: mutes/unmutes one process in Windows' volume mixer (Core Audio session volume), the same switch as
    /// Settings > Sound > Volume mixer. Only touches sessions on the default output device.
    /// </summary>
    public static class AudioMute
    {
        private static readonly Guid EventContext = Guid.NewGuid();

        /// <summary>
        /// Mutes or unmutes every audio session of a process. Found = sessions the process has (0 if it hasn't opened
        /// audio yet), Changed = how many actually flipped.
        ///
        /// Windows remembers a mute per app path, so a client closed while muted opens muted next time. Callers
        /// should keep asserting the state they want until Found is above 0.
        /// </summary>
        public static (int Found, int Changed) SetMute(int processId, bool mute)
        {
            int found = 0, changed = 0;

            try
            {
                var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                enumerator.GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out var device);

                var managerIid = typeof(IAudioSessionManager2).GUID;
                device.Activate(ref managerIid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out object managerObject);
                var manager = (IAudioSessionManager2)managerObject;

                manager.GetSessionEnumerator(out var sessions);
                sessions.GetCount(out int count);

                for (int i = 0; i < count; i++)
                {
                    sessions.GetSession(i, out var session);

                    if (session is not IAudioSessionControl2 control || control.GetProcessId(out uint pid) != 0 || pid != processId)
                        continue;

                    if (session is ISimpleAudioVolume volume)
                    {
                        found++;
                        volume.GetMute(out bool isMuted);

                        if (isMuted != mute)
                        {
                            var context = EventContext;
                            volume.SetMute(mute, ref context);
                            changed++;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("AudioMute::SetMute", ex);
            }

            return (found, changed);
        }

        #region COM
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumeratorComObject { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        }

        [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionManager2
        {
            // IAudioSessionManager
            int GetAudioSessionControl(IntPtr sessionGuid, int streamFlags, out IntPtr sessionControl);
            int GetSimpleAudioVolume(IntPtr sessionGuid, int streamFlags, out IntPtr audioVolume);

            // IAudioSessionManager2
            [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum);
        }

        [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionEnumerator
        {
            [PreserveSig] int GetCount(out int sessionCount);
            [PreserveSig] int GetSession(int sessionIndex, [MarshalAs(UnmanagedType.IUnknown)] out object session);
        }

        [ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionControl2
        {
            // IAudioSessionControl
            [PreserveSig] int GetState(out int state);
            [PreserveSig] int GetDisplayName(out IntPtr name);
            [PreserveSig] int SetDisplayName(IntPtr value, ref Guid eventContext);
            [PreserveSig] int GetIconPath(out IntPtr path);
            [PreserveSig] int SetIconPath(IntPtr value, ref Guid eventContext);
            [PreserveSig] int GetGroupingParam(out Guid groupingParam);
            [PreserveSig] int SetGroupingParam(ref Guid groupingParam, ref Guid eventContext);
            [PreserveSig] int RegisterAudioSessionNotification(IntPtr client);
            [PreserveSig] int UnregisterAudioSessionNotification(IntPtr client);

            // IAudioSessionControl2
            [PreserveSig] int GetSessionIdentifier(out IntPtr id);
            [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr id);
            [PreserveSig] int GetProcessId(out uint processId);
        }

        [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ISimpleAudioVolume
        {
            [PreserveSig] int SetMasterVolume(float level, ref Guid eventContext);
            [PreserveSig] int GetMasterVolume(out float level);
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);
            [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        }
        #endregion
    }
}
