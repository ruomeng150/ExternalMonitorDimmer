using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ExternalMonitorDimmer
{
    [Serializable]
    public sealed class MonitorInfo
    {
        public string DeviceName { get; set; }
        public string DeviceId { get; set; }
        public string DeviceKey { get; set; }
        public string Description { get; set; }
        public int PhysicalIndex { get; set; }
        public uint Minimum { get; set; }
        public uint Current { get; set; }
        public uint Maximum { get; set; }
    }

    internal sealed class AudioVolumeState
    {
        public float MasterVolumeScalar { get; set; }
        public bool Muted { get; set; }
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    internal class MMDeviceEnumeratorComObject
    {
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, uint stateMask, out IMMDeviceCollection devices);
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        int RegisterEndpointNotificationCallback(IntPtr client);
        int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        int Activate(
            ref Guid interfaceId,
            uint context,
            IntPtr activationParameters,
            [MarshalAs(UnmanagedType.Interface)] out object interfaceObject);
        int OpenPropertyStore(int access, out IntPtr properties);
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        int GetState(out uint state);
    }

    [ComImport]
    [Guid("0BD7A1BE-7A1A-44DB-8397-C0E6BDEAD2E9")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceCollection
    {
        int GetCount(out uint count);
        int Item(uint index, out IMMDevice device);
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioEndpointVolume
    {
        int RegisterControlChangeNotify(IntPtr notify);
        int UnregisterControlChangeNotify(IntPtr notify);
        int GetChannelCount(out uint channelCount);
        int SetMasterVolumeLevel(float levelDb, ref Guid eventContext);
        int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
        int GetMasterVolumeLevel(out float levelDb);
        int GetMasterVolumeLevelScalar(out float level);
        int SetChannelVolumeLevel(uint channelNumber, float levelDb, ref Guid eventContext);
        int SetChannelVolumeLevelScalar(uint channelNumber, float level, ref Guid eventContext);
        int GetChannelVolumeLevel(uint channelNumber, out float levelDb);
        int GetChannelVolumeLevelScalar(uint channelNumber, out float level);
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        int GetVolumeStepInfo(out uint step, out uint stepCount);
        int VolumeStepUp(ref Guid eventContext);
        int VolumeStepDown(ref Guid eventContext);
        int QueryHardwareSupport(out uint hardwareSupportMask);
        int GetVolumeRange(out float minDb, out float maxDb, out float incrementDb);
    }

    internal static class NativeMethods
    {
        public const int WmHotKey = 0x0312;
        public const int WmWtsSessionChange = 0x02B1;
        public const int WtsSessionLock = 0x0007;
        public const int WtsSessionUnlock = 0x0008;
        public const int HotKeyModifierAlt = 0x0001;
        public const int HotKeyModifierControl = 0x0002;
        public const int HotKeyModifierShift = 0x0004;

        private const byte BrightnessVcpCode = 0x10;
        private const uint HotKeyModifierNoRepeat = 0x4000;
        private const uint SpiSetScreenSaverTimeout = 0x000F;
        private const uint SpiSetScreenSaverActive = 0x0011;
        private const uint SpiGetScreenSaverRunning = 0x0072;
        private const uint SpifUpdateIniFile = 0x0001;
        private const uint SpifSendChange = 0x0002;
        private const uint NotifyForThisSession = 0;
        private const int AudioRenderDataFlow = 0;
        private const int AudioMultimediaRole = 1;
        private const uint ClsctxAll = 23;
        private static readonly Guid AudioEndpointVolumeInterfaceId =
            new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");

        [StructLayout(LayoutKind.Sequential)]
        private struct LastInputInfo
        {
            public uint Size;
            public uint Time;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MonitorInfoEx
        {
            public int Size;
            public Rect Monitor;
            public Rect Work;
            public uint Flags;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string Device;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DisplayDevice
        {
            public int Size;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceString;

            public uint StateFlags;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceId;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceKey;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct PhysicalMonitor
        {
            public IntPtr Handle;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string Description;
        }

        private delegate bool MonitorEnumProc(
            IntPtr monitor,
            IntPtr monitorDc,
            ref Rect monitorRect,
            IntPtr data);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetLastInputInfo(ref LastInputInfo info);

        [DllImport("user32.dll", EntryPoint = "RegisterHotKey", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RegisterHotKeyNative(
            IntPtr window,
            int identifier,
            uint modifiers,
            uint virtualKey);

        [DllImport("user32.dll", EntryPoint = "UnregisterHotKey", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnregisterHotKeyNative(IntPtr window, int identifier);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int virtualKey);

        [DllImport("user32.dll", EntryPoint = "LockWorkStation", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool LockWorkStationNative();

        [DllImport("wtsapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WTSRegisterSessionNotification(
            IntPtr window,
            uint flags);

        [DllImport("wtsapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WTSUnRegisterSessionNotification(IntPtr window);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumDisplayMonitors(
            IntPtr deviceContext,
            IntPtr clipRect,
            MonitorEnumProc callback,
            IntPtr data);

        [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);

        [DllImport("user32.dll", EntryPoint = "EnumDisplayDevicesW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumDisplayDevices(
            string deviceName,
            uint deviceIndex,
            ref DisplayDevice displayDevice,
            uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SystemParametersInfo(
            uint action,
            uint parameter,
            IntPtr value,
            uint updateFlags);

        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SystemParametersInfoGetScreenSaverRunning(
            uint action,
            uint parameter,
            ref int value,
            uint updateFlags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(IntPtr icon);

        [DllImport("dxva2.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(
            IntPtr monitor,
            out uint physicalMonitorCount);

        [DllImport("dxva2.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetPhysicalMonitorsFromHMONITOR(
            IntPtr monitor,
            uint physicalMonitorArraySize,
            [Out] PhysicalMonitor[] physicalMonitors);

        [DllImport("dxva2.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyPhysicalMonitors(
            uint physicalMonitorArraySize,
            [In] PhysicalMonitor[] physicalMonitors);

        [DllImport("dxva2.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorBrightness(
            IntPtr physicalMonitor,
            out uint minimumBrightness,
            out uint currentBrightness,
            out uint maximumBrightness);

        [DllImport("dxva2.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetMonitorBrightness(
            IntPtr physicalMonitor,
            uint brightness);

        [DllImport("dxva2.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetVCPFeature(
            IntPtr physicalMonitor,
            byte vcpCode,
            uint value);

        public static uint GetIdleMilliseconds()
        {
            LastInputInfo info = new LastInputInfo();
            info.Size = (uint)Marshal.SizeOf(typeof(LastInputInfo));

            if (!GetLastInputInfo(ref info))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            uint now = unchecked((uint)Environment.TickCount);
            return unchecked(now - info.Time);
        }

        public static uint GetLastInputTickCount()
        {
            LastInputInfo info = new LastInputInfo();
            info.Size = (uint)Marshal.SizeOf(typeof(LastInputInfo));

            if (!GetLastInputInfo(ref info))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            return info.Time;
        }

        public static bool IsScreenSaverRunning()
        {
            int running = 0;
            if (!SystemParametersInfoGetScreenSaverRunning(
                SpiGetScreenSaverRunning,
                0,
                ref running,
                0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            return running != 0;
        }

        public static void RegisterGlobalHotKey(
            IntPtr window,
            int identifier,
            uint modifiers,
            uint virtualKey)
        {
            if (!RegisterHotKeyNative(
                window,
                identifier,
                modifiers | HotKeyModifierNoRepeat,
                virtualKey))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }

        public static void UnregisterGlobalHotKey(IntPtr window, int identifier)
        {
            UnregisterHotKeyNative(window, identifier);
        }

        public static bool IsKeyDown(int virtualKey)
        {
            return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
        }

        public static void LockWorkStation()
        {
            if (!LockWorkStationNative())
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }

        public static void RegisterSessionNotifications(IntPtr window)
        {
            if (!WTSRegisterSessionNotification(window, NotifyForThisSession))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }

        public static void UnregisterSessionNotifications(IntPtr window)
        {
            WTSUnRegisterSessionNotification(window);
        }

        public static AudioVolumeState MuteDefaultAudioEndpoint()
        {
            IMMDeviceEnumerator enumerator = null;
            IMMDevice device = null;
            IAudioEndpointVolume endpoint = null;
            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                CheckHResult(enumerator.GetDefaultAudioEndpoint(
                    AudioRenderDataFlow,
                    AudioMultimediaRole,
                    out device));

                Guid interfaceId = AudioEndpointVolumeInterfaceId;
                object endpointObject;
                CheckHResult(device.Activate(
                    ref interfaceId,
                    ClsctxAll,
                    IntPtr.Zero,
                    out endpointObject));
                endpoint = endpointObject as IAudioEndpointVolume;
                if (endpoint == null)
                {
                    throw new InvalidOperationException("无法访问 Windows 默认音频输出设备。");
                }

                float volume;
                bool muted;
                CheckHResult(endpoint.GetMasterVolumeLevelScalar(out volume));
                CheckHResult(endpoint.GetMute(out muted));

                Guid eventContext = Guid.Empty;
                CheckHResult(endpoint.SetMute(true, ref eventContext));
                return new AudioVolumeState
                {
                    MasterVolumeScalar = volume,
                    Muted = muted
                };
            }
            finally
            {
                ReleaseComObject(endpoint);
                ReleaseComObject(device);
                ReleaseComObject(enumerator);
            }
        }

        public static void RestoreDefaultAudioEndpoint(AudioVolumeState state)
        {
            if (state == null)
            {
                return;
            }

            IMMDeviceEnumerator enumerator = null;
            IMMDevice device = null;
            IAudioEndpointVolume endpoint = null;
            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                CheckHResult(enumerator.GetDefaultAudioEndpoint(
                    AudioRenderDataFlow,
                    AudioMultimediaRole,
                    out device));

                Guid interfaceId = AudioEndpointVolumeInterfaceId;
                object endpointObject;
                CheckHResult(device.Activate(
                    ref interfaceId,
                    ClsctxAll,
                    IntPtr.Zero,
                    out endpointObject));
                endpoint = endpointObject as IAudioEndpointVolume;
                if (endpoint == null)
                {
                    throw new InvalidOperationException("无法访问 Windows 默认音频输出设备。");
                }

                Guid eventContext = Guid.Empty;
                CheckHResult(endpoint.SetMasterVolumeLevelScalar(
                    state.MasterVolumeScalar,
                    ref eventContext));
                CheckHResult(endpoint.SetMute(state.Muted, ref eventContext));
            }
            finally
            {
                ReleaseComObject(endpoint);
                ReleaseComObject(device);
                ReleaseComObject(enumerator);
            }
        }

        private static void CheckHResult(int result)
        {
            if (result < 0)
            {
                Marshal.ThrowExceptionForHR(result);
            }
        }

        private static void ReleaseComObject(object value)
        {
            if (value != null && Marshal.IsComObject(value))
            {
                Marshal.ReleaseComObject(value);
            }
        }

        public static List<MonitorInfo> GetBrightnessMonitors()
        {
            List<MonitorInfo> result = new List<MonitorInfo>();

            MonitorEnumProc callback = delegate(
                IntPtr monitor,
                IntPtr monitorDc,
                ref Rect monitorRect,
                IntPtr data)
            {
                MonitorInfoEx logicalMonitor = new MonitorInfoEx();
                logicalMonitor.Size = Marshal.SizeOf(typeof(MonitorInfoEx));
                string deviceName = String.Empty;

                if (GetMonitorInfo(monitor, ref logicalMonitor))
                {
                    deviceName = logicalMonitor.Device ?? String.Empty;
                }

                DisplayDevice displayDevice = new DisplayDevice();
                displayDevice.Size = Marshal.SizeOf(typeof(DisplayDevice));
                string deviceId = String.Empty;
                string deviceKey = String.Empty;

                if (EnumDisplayDevices(deviceName, 0, ref displayDevice, 0))
                {
                    deviceId = displayDevice.DeviceId ?? String.Empty;
                    deviceKey = displayDevice.DeviceKey ?? String.Empty;
                }

                uint count;
                if (!GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out count) || count == 0)
                {
                    return true;
                }

                PhysicalMonitor[] physicalMonitors = new PhysicalMonitor[count];
                if (!GetPhysicalMonitorsFromHMONITOR(monitor, count, physicalMonitors))
                {
                    return true;
                }

                try
                {
                    for (int index = 0; index < physicalMonitors.Length; index++)
                    {
                        uint minimum;
                        uint current;
                        uint maximum;

                        if (!GetMonitorBrightness(
                            physicalMonitors[index].Handle,
                            out minimum,
                            out current,
                            out maximum))
                        {
                            continue;
                        }

                        MonitorInfo info = new MonitorInfo();
                        info.DeviceName = deviceName;
                        info.DeviceId = deviceId;
                        info.DeviceKey = deviceKey;
                        info.Description = physicalMonitors[index].Description ?? String.Empty;
                        info.PhysicalIndex = index;
                        info.Minimum = minimum;
                        info.Current = current;
                        info.Maximum = maximum;
                        result.Add(info);
                    }
                }
                finally
                {
                    DestroyPhysicalMonitors(count, physicalMonitors);
                }

                return true;
            };

            if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            GC.KeepAlive(callback);
            return result;
        }

        public static bool SetBrightness(string deviceName, int physicalIndex, uint brightness)
        {
            bool changed = false;

            MonitorEnumProc callback = delegate(
                IntPtr monitor,
                IntPtr monitorDc,
                ref Rect monitorRect,
                IntPtr data)
            {
                MonitorInfoEx logicalMonitor = new MonitorInfoEx();
                logicalMonitor.Size = Marshal.SizeOf(typeof(MonitorInfoEx));

                if (!GetMonitorInfo(monitor, ref logicalMonitor) ||
                    !String.Equals(logicalMonitor.Device, deviceName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                uint count;
                if (!GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out count) ||
                    physicalIndex < 0 ||
                    (uint)physicalIndex >= count)
                {
                    return true;
                }

                PhysicalMonitor[] physicalMonitors = new PhysicalMonitor[count];
                if (!GetPhysicalMonitorsFromHMONITOR(monitor, count, physicalMonitors))
                {
                    return true;
                }

                try
                {
                    IntPtr handle = physicalMonitors[physicalIndex].Handle;
                    changed = SetMonitorBrightness(handle, brightness);
                    if (!changed)
                    {
                        changed = SetVCPFeature(handle, BrightnessVcpCode, brightness);
                    }
                }
                finally
                {
                    DestroyPhysicalMonitors(count, physicalMonitors);
                }

                return true;
            };

            if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero))
            {
                return false;
            }

            GC.KeepAlive(callback);
            return changed;
        }

        public static void ApplyScreenSaverTiming(int timeoutSeconds, bool active)
        {
            uint flags = SpifUpdateIniFile | SpifSendChange;
            SystemParametersInfo(SpiSetScreenSaverTimeout, (uint)timeoutSeconds, IntPtr.Zero, flags);
            SystemParametersInfo(SpiSetScreenSaverActive, active ? 1U : 0U, IntPtr.Zero, flags);
        }

        public static void ReleaseIconHandle(IntPtr iconHandle)
        {
            if (iconHandle != IntPtr.Zero)
            {
                DestroyIcon(iconHandle);
            }
        }
    }
}
