using System;
using System.Collections.Generic;
using System.Management;

namespace ExternalMonitorDimmer
{
    internal sealed class WmiBrightnessPanel
    {
        public string InstanceName;
        public uint CurrentBrightness;
        public byte[] Levels;
    }

    internal static class MonitorIdentity
    {
        public static string NormalizePnpId(string instanceName)
        {
            if (String.IsNullOrWhiteSpace(instanceName))
            {
                return String.Empty;
            }
            string value = instanceName.Trim();
            if (value.StartsWith(@"\\?\", StringComparison.Ordinal) ||
                value.StartsWith(@"\??\", StringComparison.Ordinal))
            {
                value = value.Substring(4);
            }
            string[] parts = value.Split('#');
            if (parts.Length >= 3)
            {
                value = parts[0] + "\\" + parts[1] + "\\" + parts[2];
            }
            else
            {
                // WMI adds an output index (for example _0) to the PnP instance.
                int suffix = value.LastIndexOf('_');
                uint outputIndex;
                if (suffix > value.LastIndexOf('\\') &&
                    UInt32.TryParse(value.Substring(suffix + 1), out outputIndex))
                {
                    value = value.Substring(0, suffix);
                }
            }
            return value;
        }

        public static bool Same(string left, string right)
        {
            return !String.IsNullOrEmpty(left) && !String.IsNullOrEmpty(right) &&
                String.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal static class WmiBrightnessProvider
    {
        private static DateTime nextErrorLogUtc = DateTime.MinValue;
        public static string LastDetectionError { get; private set; }

        private static ManagementObjectSearcher CreateSearcher(string className)
        {
            EnumerationOptions options = new EnumerationOptions();
            options.Timeout = TimeSpan.FromSeconds(3);
            return new ManagementObjectSearcher(
                new ManagementScope(@"\\.\root\wmi"),
                new ObjectQuery("SELECT * FROM " + className + " WHERE Active = TRUE"),
                options);
        }

        public static List<WmiBrightnessPanel> GetActivePanels()
        {
            List<WmiBrightnessPanel> panels = new List<WmiBrightnessPanel>();
            LastDetectionError = null;
            try
            {
                HashSet<string> writableInstances = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (ManagementObjectSearcher searcher = CreateSearcher("WmiMonitorBrightnessMethods"))
                using (ManagementObjectCollection objects = searcher.Get())
                {
                    foreach (ManagementObject endpoint in objects)
                    {
                        using (endpoint)
                        {
                            writableInstances.Add(Convert.ToString(endpoint["InstanceName"]));
                        }
                    }
                }

                using (ManagementObjectSearcher searcher = CreateSearcher("WmiMonitorBrightness"))
                using (ManagementObjectCollection objects = searcher.Get())
                {
                    foreach (ManagementObject panel in objects)
                    {
                        using (panel)
                        {
                            string instanceName = Convert.ToString(panel["InstanceName"]);
                            if (String.IsNullOrWhiteSpace(instanceName) || !writableInstances.Contains(instanceName))
                            {
                                continue;
                            }
                            panels.Add(new WmiBrightnessPanel
                            {
                                InstanceName = instanceName,
                                CurrentBrightness = Math.Min(100U, Convert.ToUInt32(panel["CurrentBrightness"])),
                                Levels = panel["Level"] as byte[]
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LastDetectionError = ex.Message;
                if (DateTime.UtcNow >= nextErrorLogUtc)
                {
                    SettingsStore.Log("Windows WMI brightness detection unavailable: " + ex.Message);
                    nextErrorLogUtc = DateTime.UtcNow.AddMinutes(1);
                }
                // An absent/disabled WMI provider must not hide DDC/CI or other outputs.
            }
            return panels;
        }

        internal static void MergeMonitors(List<MonitorInfo> monitors, List<WmiBrightnessPanel> panels)
        {
            foreach (WmiBrightnessPanel panel in panels)
            {
                string pnpId = MonitorIdentity.NormalizePnpId(panel.InstanceName);
                List<MonitorInfo> matches = monitors.FindAll(delegate(MonitorInfo monitor)
                {
                    return MonitorIdentity.Same(monitor.PnpInstanceId, pnpId);
                });
                // Use exact Windows device identity, never a model/brand whitelist or
                // DISPLAY1/2 numbering. Ignore stale WMI entries for disabled outputs.
                if (matches.Count != 1 || matches[0].CanControlBrightness)
                {
                    continue;
                }

                MonitorInfo match = matches[0];
                match.Source = BrightnessSource.WindowsWmi;
                match.BrightnessInstanceName = panel.InstanceName;
                match.BrightnessLevels = panel.Levels;
                match.Minimum = 0;
                match.Maximum = 100;
                match.Current = panel.CurrentBrightness;
            }
        }

        internal static byte SelectSupportedLevel(uint requested, byte[] levels)
        {
            int target = (int)Math.Min(100U, requested);
            int best = target;
            int distance = Int32.MaxValue;
            if (levels != null)
            {
                foreach (byte level in levels)
                {
                    if (level > 100)
                    {
                        continue;
                    }
                    int difference = Math.Abs(level - target);
                    if (difference < distance || (difference == distance && level < best))
                    {
                        best = level;
                        distance = difference;
                    }
                }
            }
            return (byte)best;
        }

        public static bool SetBrightness(MonitorInfo monitor, uint brightness)
        {
            try
            {
                using (ManagementObjectSearcher searcher = CreateSearcher("WmiMonitorBrightnessMethods"))
                using (ManagementObjectCollection objects = searcher.Get())
                {
                    foreach (ManagementObject endpoint in objects)
                    {
                        using (endpoint)
                        {
                            if (!MonitorIdentity.Same(Convert.ToString(endpoint["InstanceName"]),
                                monitor.BrightnessInstanceName))
                            {
                                continue;
                            }
                            using (ManagementBaseObject input = endpoint.GetMethodParameters("WmiSetBrightness"))
                            {
                                input["Timeout"] = 0U;
                                input["Brightness"] = SelectSupportedLevel(brightness, monitor.BrightnessLevels);
                                using (ManagementBaseObject output = endpoint.InvokeMethod("WmiSetBrightness", input,
                                    new InvokeMethodOptions(null, TimeSpan.FromSeconds(3))))
                                {
                                    return output != null && output["ReturnValue"] != null &&
                                        Convert.ToUInt32(output["ReturnValue"]) == 0;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                SettingsStore.Log("Could not set Windows WMI brightness for " +
                    monitor.DeviceName + ": " + ex.Message);
            }
            return false;
        }
    }
}
