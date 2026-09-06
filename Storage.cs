using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Forms;
using System.Xml.Serialization;
using Microsoft.Win32;

namespace ExternalMonitorDimmer
{
    [Serializable]
    public sealed class AppSettings
    {
        public int SettingsVersion { get; set; }
        public int IdleSeconds { get; set; }
        public int TriggerMode { get; set; }
        public int DimPercent { get; set; }
        public bool DisplayMinutes { get; set; }
        public bool AutoStart { get; set; }
        public bool SyncBlankScreenSaver { get; set; }
        public bool SyncLockWorkstation { get; set; }
        public bool MonitoringEnabled { get; set; }
        public int HotKeyModifiers { get; set; }
        public int HotKeyKey { get; set; }

        public AppSettings()
        {
            SettingsVersion = 0;
            IdleSeconds = 10;
            TriggerMode = 0;
            DimPercent = 0;
            DisplayMinutes = false;
            AutoStart = false;
            SyncBlankScreenSaver = true;
            SyncLockWorkstation = false;
            MonitoringEnabled = false;
            HotKeyModifiers = 0;
            HotKeyKey = 0;
        }
    }

    [Serializable]
    public sealed class BrightnessSnapshot
    {
        public string DeviceName { get; set; }
        public string DeviceId { get; set; }
        public string DeviceKey { get; set; }
        public string Description { get; set; }
        public int PhysicalIndex { get; set; }
        public uint Brightness { get; set; }

        public static BrightnessSnapshot FromMonitor(MonitorInfo monitor)
        {
            BrightnessSnapshot snapshot = new BrightnessSnapshot();
            snapshot.DeviceName = monitor.DeviceName;
            snapshot.DeviceId = monitor.DeviceId;
            snapshot.DeviceKey = monitor.DeviceKey;
            snapshot.Description = monitor.Description;
            snapshot.PhysicalIndex = monitor.PhysicalIndex;
            snapshot.Brightness = monitor.Current;
            return snapshot;
        }
    }

    [Serializable]
    public sealed class BrightnessState
    {
        public DateTime SavedAt { get; set; }
        public List<BrightnessSnapshot> Monitors { get; set; }

        public BrightnessState()
        {
            Monitors = new List<BrightnessSnapshot>();
        }
    }

    [Serializable]
    public sealed class RegistryValueBackup
    {
        public bool Exists { get; set; }
        public string Value { get; set; }
    }

    [Serializable]
    public sealed class ScreenSaverBackup
    {
        public RegistryValueBackup Active { get; set; }
        public RegistryValueBackup Timeout { get; set; }
        public RegistryValueBackup Executable { get; set; }

        public ScreenSaverBackup()
        {
            Active = new RegistryValueBackup();
            Timeout = new RegistryValueBackup();
            Executable = new RegistryValueBackup();
        }
    }

    internal static class AppPaths
    {
        private const string DataOverrideVariable = "EXTERNAL_MONITOR_DIMMER_DATA_DIR";

        public static readonly string DataDirectory = ResolveDataDirectory();
        public static readonly string SettingsFile = Path.Combine(DataDirectory, "settings.xml");
        public static readonly string BrightnessStateFile = Path.Combine(DataDirectory, "brightness-state.xml");
        public static readonly string ScreenSaverBackupFile = Path.Combine(DataDirectory, "screensaver-backup.xml");
        public static readonly string LogFile = Path.Combine(DataDirectory, "activity.log");
        public static readonly string InstalledExecutable = Path.Combine(DataDirectory, "ExternalMonitorDimmer.exe");

        private static string ResolveDataDirectory()
        {
            string overridePath = Environment.GetEnvironmentVariable(DataOverrideVariable);
            if (!String.IsNullOrWhiteSpace(overridePath))
            {
                return Path.GetFullPath(overridePath);
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ExternalMonitorDimmer");
        }

        public static void EnsureDirectory()
        {
            Directory.CreateDirectory(DataDirectory);
        }
    }

    internal static class SettingsStore
    {
        public static AppSettings LoadSettings()
        {
            AppSettings settings = LoadXml<AppSettings>(AppPaths.SettingsFile);
            if (settings == null)
            {
                return CreateDefaultSettings();
            }

            if (settings.SettingsVersion < 1)
            {
                AppSettings defaults = CreateDefaultSettings();
                SaveSettings(defaults);
                return defaults;
            }

            if (settings.SettingsVersion < 2)
            {
                settings.HotKeyModifiers = 0;
                settings.HotKeyKey = 0;
                settings.SettingsVersion = 2;
                SaveSettings(settings);
            }

            if (settings.TriggerMode != 1)
            {
                settings.TriggerMode = 0;
            }

            if (settings.SettingsVersion < 3)
            {
                settings.SettingsVersion = 3;
                SaveSettings(settings);
            }

            if (settings.SettingsVersion < 4)
            {
                settings.SyncLockWorkstation = false;
                settings.SettingsVersion = 4;
                SaveSettings(settings);
            }

            return settings;
        }

        private static AppSettings CreateDefaultSettings()
        {
            AppSettings settings = new AppSettings();
            settings.SettingsVersion = 4;
            return settings;
        }

        public static void SaveSettings(AppSettings settings)
        {
            SaveXml(AppPaths.SettingsFile, settings);
        }

        public static BrightnessState LoadBrightnessState()
        {
            return LoadXml<BrightnessState>(AppPaths.BrightnessStateFile);
        }

        public static void SaveBrightnessState(BrightnessState state)
        {
            SaveXml(AppPaths.BrightnessStateFile, state);
        }

        public static void DeleteBrightnessState()
        {
            DeleteFile(AppPaths.BrightnessStateFile);
        }

        public static ScreenSaverBackup LoadScreenSaverBackup()
        {
            return LoadXml<ScreenSaverBackup>(AppPaths.ScreenSaverBackupFile);
        }

        public static void SaveScreenSaverBackup(ScreenSaverBackup backup)
        {
            SaveXml(AppPaths.ScreenSaverBackupFile, backup);
        }

        public static void DeleteScreenSaverBackup()
        {
            DeleteFile(AppPaths.ScreenSaverBackupFile);
        }

        public static void Log(string message)
        {
            try
            {
                AppPaths.EnsureDirectory();
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message + Environment.NewLine;
                File.AppendAllText(AppPaths.LogFile, line, new UTF8Encoding(false));
            }
            catch
            {
            }
        }

        private static T LoadXml<T>(string path) where T : class
        {
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                XmlSerializer serializer = new XmlSerializer(typeof(T));
                using (FileStream stream = File.OpenRead(path))
                {
                    return serializer.Deserialize(stream) as T;
                }
            }
            catch (Exception ex)
            {
                Log("Could not read " + Path.GetFileName(path) + ": " + ex.Message);
                return null;
            }
        }

        private static void SaveXml<T>(string path, T value)
        {
            AppPaths.EnsureDirectory();
            string temporaryPath = path + ".tmp";
            XmlSerializer serializer = new XmlSerializer(typeof(T));

            using (FileStream stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                serializer.Serialize(stream, value);
            }

            File.Copy(temporaryPath, path, true);
            File.Delete(temporaryPath);
        }

        private static void DeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                Log("Could not delete " + Path.GetFileName(path) + ": " + ex.Message);
            }
        }
    }

    internal static class StartupManager
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "ExternalMonitorDimmer";

        public static void SetEnabled(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
            {
                if (key == null)
                {
                    throw new InvalidOperationException("无法打开当前用户的开机启动注册表项。");
                }

                if (!enabled)
                {
                    key.DeleteValue(ValueName, false);
                    return;
                }

                AppPaths.EnsureDirectory();
                string source = Path.GetFullPath(Application.ExecutablePath);
                string destination = Path.GetFullPath(AppPaths.InstalledExecutable);

                if (!source.Equals(destination, StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(source, destination, true);
                }

                key.SetValue(
                    ValueName,
                    "\"" + destination + "\" --background",
                    RegistryValueKind.String);
            }
        }

        public static bool IsEnabled()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
            {
                return key != null && key.GetValue(ValueName) != null;
            }
        }
    }

    internal static class ScreenSaverManager
    {
        private const string DesktopKeyPath = @"Control Panel\Desktop";

        public static void EnableBlank(int timeoutSeconds)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(DesktopKeyPath, true))
            {
                if (key == null)
                {
                    throw new InvalidOperationException("无法打开当前用户的屏幕保护程序设置。");
                }

                if (!File.Exists(AppPaths.ScreenSaverBackupFile))
                {
                    ScreenSaverBackup backup = new ScreenSaverBackup();
                    backup.Active = Capture(key, "ScreenSaveActive");
                    backup.Timeout = Capture(key, "ScreenSaveTimeOut");
                    backup.Executable = Capture(key, "SCRNSAVE.EXE");
                    SettingsStore.SaveScreenSaverBackup(backup);
                }

                key.SetValue("ScreenSaveActive", "1", RegistryValueKind.String);
                key.SetValue("ScreenSaveTimeOut", timeoutSeconds.ToString(), RegistryValueKind.String);
                key.SetValue(
                    "SCRNSAVE.EXE",
                    Path.Combine(Environment.SystemDirectory, "scrnsave.scr"),
                    RegistryValueKind.String);
            }

            NativeMethods.ApplyScreenSaverTiming(timeoutSeconds, true);
        }

        public static void RestoreOriginal()
        {
            ScreenSaverBackup backup = SettingsStore.LoadScreenSaverBackup();
            if (backup == null)
            {
                return;
            }

            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(DesktopKeyPath, true))
            {
                if (key == null)
                {
                    throw new InvalidOperationException("无法打开当前用户的屏幕保护程序设置。");
                }

                Restore(key, "ScreenSaveActive", backup.Active);
                Restore(key, "ScreenSaveTimeOut", backup.Timeout);
                Restore(key, "SCRNSAVE.EXE", backup.Executable);
            }

            int restoredTimeout = ParsePositiveInt(backup.Timeout.Value, 600);
            bool restoredActive = backup.Active.Exists && backup.Active.Value == "1";
            NativeMethods.ApplyScreenSaverTiming(restoredTimeout, restoredActive);
            SettingsStore.DeleteScreenSaverBackup();
        }

        public static Process StartBlank()
        {
            string screenSaver = Path.Combine(Environment.SystemDirectory, "scrnsave.scr");
            if (!File.Exists(screenSaver))
            {
                throw new FileNotFoundException("找不到 Windows 黑屏屏保。", screenSaver);
            }

            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = screenSaver;
            startInfo.Arguments = "/s";
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = false;

            Process process = Process.Start(startInfo);
            if (process == null)
            {
                throw new InvalidOperationException("无法启动 Windows 黑屏屏保。");
            }

            return process;
        }

        private static RegistryValueBackup Capture(RegistryKey key, string name)
        {
            RegistryValueBackup value = new RegistryValueBackup();
            value.Exists = Array.IndexOf(key.GetValueNames(), name) >= 0;
            object raw = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            value.Value = raw == null ? null : Convert.ToString(raw);
            return value;
        }

        private static void Restore(RegistryKey key, string name, RegistryValueBackup backup)
        {
            if (backup != null && backup.Exists)
            {
                key.SetValue(name, backup.Value ?? String.Empty, RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(name, false);
            }
        }

        private static int ParsePositiveInt(string value, int fallback)
        {
            int parsed;
            return Int32.TryParse(value, out parsed) && parsed > 0 ? parsed : fallback;
        }
    }
}
