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

    internal static class NativeMethods
    {
        public const int WmHotKey = 0x0312;
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
