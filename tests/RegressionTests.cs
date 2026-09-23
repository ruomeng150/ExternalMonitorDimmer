using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace ExternalMonitorDimmer
{
    internal static class RegressionTests
    {
        private static int passed;

        [STAThread]
        private static int Main()
        {
            Environment.SetEnvironmentVariable("EXTERNAL_MONITOR_DIMMER_DATA_DIR",
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data-" + Guid.NewGuid().ToString("N")));
            try
            {
                TestMonitorDetection();
                TestBrightnessRestoreIdentity();
                TestSessionLifecycle();
                TestDeferredBrightnessRecovery();
                TestBrightnessReadbackAndFailures();
                Console.WriteLine("PASS: " + passed + " regression checks. No real lock, mute, or brightness write was performed.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        private static void Check(bool value, string name)
        {
            if (!value)
            {
                throw new InvalidOperationException("FAIL: " + name);
            }
            passed++;
            Console.WriteLine("PASS: " + name);
        }

        private static MonitorInfo MakeMonitor(string name, string pnp, BrightnessSource source)
        {
            return new MonitorInfo
            {
                DeviceName = name, PnpInstanceId = pnp, Source = source,
                DeviceId = pnp, DeviceKey = "key:" + pnp,
                Description = "Generic PnP Monitor", Minimum = 0, Current = 65, Maximum = 100
            };
        }

        private static void TestMonitorDetection()
        {
            string panelId = @"DISPLAY\AAA0001\5&panel&0&UID2";
            string externalId = @"DISPLAY\BBB0002\5&external&0&UID3";
            Check(MonitorIdentity.NormalizePnpId(@"\\?\DISPLAY#AAA0001#5&panel&0&UID2#{GUID}") == panelId,
                "Generic Windows interface path maps to PnP identity");
            Check(MonitorIdentity.NormalizePnpId(panelId + "_0") == panelId,
                "Generic WMI output suffix maps to the same PnP identity");
            Check(MonitorIdentity.NormalizePnpId(null) == String.Empty, "Missing identity stays unknown");
            Check(MonitorIdentity.Same(panelId, panelId.ToLowerInvariant()), "PnP matching ignores case");
            Check(!MonitorIdentity.Same(null, null), "Missing identities never match");

            MonitorInfo external = MakeMonitor(@"\\.\DISPLAY2", externalId, BrightnessSource.DdcCi);
            MonitorInfo panel = MakeMonitor(@"\\.\DISPLAY1", panelId, BrightnessSource.Unsupported);
            MonitorInfo unsupported = MakeMonitor(@"\\.\DISPLAY3", @"DISPLAY\CCC0003\virtual", BrightnessSource.Unsupported);
            List<MonitorInfo> monitors = new List<MonitorInfo> { external, panel, unsupported };
            List<WmiBrightnessPanel> wmi = new List<WmiBrightnessPanel>
            {
                new WmiBrightnessPanel { InstanceName = panelId + "_0", CurrentBrightness = 70,
                    Levels = new byte[] { 0, 20, 40, 60, 80, 100 } },
                new WmiBrightnessPanel { InstanceName = externalId + "_0", CurrentBrightness = 80 },
                new WmiBrightnessPanel { InstanceName = @"DISPLAY\DDD0004\disabled_0", CurrentBrightness = 90 }
            };
            WmiBrightnessProvider.MergeMonitors(monitors, wmi);
            Check(monitors.Count == 3, "No duplicate panels or stale disabled outputs are added");
            Check(external.Source == BrightnessSource.DdcCi && external.Current == 65,
                "Existing external DDC/CI path is preserved");
            Check(panel.Source == BrightnessSource.WindowsWmi && panel.Current == 70,
                "Built-in panel gains system brightness capability");
            Check(panel.BrightnessInstanceName == panelId + "_0", "WMI endpoint is retained for targeted writes");
            Check(panel.Minimum == 0 && panel.Maximum == 100, "Windows percentage scale is preserved");
            Check(!unsupported.CanControlBrightness, "Unsupported display remains visible but non-writable");
            Check(!NativeMethods.SetBrightness(unsupported, 0), "Unsupported display is never sent a brightness write");

            MonitorInfo twin1 = MakeMonitor("one", panelId, BrightnessSource.Unsupported);
            MonitorInfo twin2 = MakeMonitor("two", panelId, BrightnessSource.Unsupported);
            WmiBrightnessProvider.MergeMonitors(new List<MonitorInfo> { twin1, twin2 }, wmi);
            Check(!twin1.CanControlBrightness && !twin2.CanControlBrightness,
                "Ambiguous identity is not guessed by display order");
            Check(WmiBrightnessProvider.SelectSupportedLevel(0, new byte[] { 10, 30, 70 }) == 10,
                "Minimum dim level respects the driver supported levels");
            Check(WmiBrightnessProvider.SelectSupportedLevel(58, new byte[] { 0, 40, 60, 100 }) == 60,
                "System brightness is rounded to the nearest supported level");
            Check(WmiBrightnessProvider.SelectSupportedLevel(50, new byte[] { 60, 40 }) == 40,
                "Equal-distance brightness levels have stable selection");
            Check(WmiBrightnessProvider.SelectSupportedLevel(200, null) == 100,
                "Brightness is clamped without driver level metadata");
            Check(WmiBrightnessProvider.SelectSupportedLevel(35, new byte[] { 255 }) == 35,
                "Invalid driver brightness levels are ignored");
        }

        private static void TestBrightnessRestoreIdentity()
        {
            MonitorInfo original = MakeMonitor(@"\\.\DISPLAY1", @"DISPLAY\AAA0001\panel", BrightnessSource.WindowsWmi);
            original.BrightnessInstanceName = original.PnpInstanceId + "_0";
            BrightnessSnapshot saved = BrightnessSnapshot.FromMonitor(original);
            original.DeviceName = @"\\.\DISPLAY2";
            MonitorInfo wrong = MakeMonitor(@"\\.\DISPLAY1", @"DISPLAY\BBB0002\external", BrightnessSource.DdcCi);
            List<MonitorInfo> current = new List<MonitorInfo> { wrong, original };
            Check(MainForm.FindCurrentMonitor(saved, current) == original,
                "Panel restore survives DISPLAY renumbering");
            original.BrightnessInstanceName = original.PnpInstanceId + "_1";
            Check(MainForm.FindCurrentMonitor(saved, current) == original,
                "Panel restore survives a driver WMI output-index change");
            Check(MainForm.FindCurrentMonitor(saved, new List<MonitorInfo> { wrong }) == null,
                "Disconnected panel cannot restore brightness to an external display");
            XmlSerializer serializer = new XmlSerializer(typeof(BrightnessSnapshot));
            using (StringWriter writer = new StringWriter())
            {
                serializer.Serialize(writer, saved);
                using (StringReader reader = new StringReader(writer.ToString()))
                {
                    BrightnessSnapshot roundTrip = (BrightnessSnapshot)serializer.Deserialize(reader);
                    Check(roundTrip.Source == BrightnessSource.WindowsWmi &&
                        roundTrip.BrightnessInstanceName == saved.BrightnessInstanceName && roundTrip.Brightness == 65,
                        "Saved panel brightness/backend/identity round trip");
                }
            }
            using (StringReader reader = new StringReader(
                "<BrightnessSnapshot><DeviceName>old</DeviceName><DeviceKey>legacy-key</DeviceKey>" +
                "<PhysicalIndex>0</PhysicalIndex><Brightness>50</Brightness></BrightnessSnapshot>"))
            {
                BrightnessSnapshot legacy = (BrightnessSnapshot)serializer.Deserialize(reader);
                Check(legacy.Source == BrightnessSource.DdcCi, "Legacy v1.6 snapshot defaults to DDC/CI");
                wrong.DeviceKey = "legacy-key";
                Check(MainForm.FindCurrentMonitor(legacy, current) == wrong,
                    "Legacy external snapshot still restores by registry identity");
            }

            BrightnessSnapshot externalSaved = BrightnessSnapshot.FromMonitor(wrong);
            MonitorInfo replacement = MakeMonitor(wrong.DeviceName, @"DISPLAY\CCC0003\replacement", BrightnessSource.DdcCi);
            Check(MainForm.FindCurrentMonitor(externalSaved, new List<MonitorInfo> { replacement }) == null,
                "A reused DISPLAY number cannot redirect a known external snapshot");
        }

        private static T Get<T>(MainForm form, string name)
        {
            return (T)typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
        }

        private static void Set(MainForm form, string name, object value)
        {
            typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(form, value);
        }

        private static object Call(MainForm form, string name, params object[] arguments)
        {
            return typeof(MainForm).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, arguments);
        }

        private static void TestDeferredBrightnessRecovery()
        {
            // Reproduce the reported state: the external screen is back at 50%,
            // but a now-disabled panel still has a saved 100% recovery record.
            MonitorInfo external = MakeMonitor(@"\\.\DISPLAY2", @"DISPLAY\BBB0002\external", BrightnessSource.DdcCi);
            external.Current = 50;
            MonitorInfo panel = MakeMonitor(@"\\.\DISPLAY1", @"DISPLAY\AAA0001\panel", BrightnessSource.WindowsWmi);
            panel.Current = 100;
            panel.BrightnessInstanceName = panel.PnpInstanceId + "_0";
            BrightnessState pending = new BrightnessState();
            pending.SavedAt = DateTime.Now;
            pending.Monitors.Add(BrightnessSnapshot.FromMonitor(panel));
            SettingsStore.SaveBrightnessState(pending);

            List<MonitorInfo> connected = new List<MonitorInfo> { external };
            int writes = 0;
            using (MainForm form = new MainForm(false, delegate { return connected; },
                delegate(MonitorInfo monitor, uint value) { writes++; monitor.Current = value; return true; }))
            {
                Get<NotifyIcon>(form, "trayIcon").Visible = false;
                try
                {
                    Set(form, "monitoring", true);
                    bool restored = (bool)Call(form, "RestoreSavedBrightness");
                    Check(restored && !Get<bool>(form, "dimmed") &&
                        Get<Label>(form, "statusLabel").Text == "监控中",
                        "An offline panel recovery record does not block monitoring after unlock");
                    Check(writes == 0 && SettingsStore.LoadBrightnessState().Monitors.Count == 1,
                        "Offline brightness is retained without writing to another screen");

                    DateTime previousWrite = File.GetLastWriteTimeUtc(AppPaths.BrightnessStateFile);
                    Call(form, "RestoreSavedBrightness");
                    Check(File.GetLastWriteTimeUtc(AppPaths.BrightnessStateFile) == previousWrite,
                        "An unchanged offline queue is not rewritten on every timer tick");
                    Check(Get<ToolTip>(form, "toolTip").GetToolTip(Get<Label>(form, "statusLabel"))
                        .Contains("1 台"), "Offline recovery is explained without replacing the normal status");

                    Check((bool)Call(form, "EnsureMonitorsDimmed") && external.Current == 0,
                        "The next hotkey brightness phase dims connected monitors despite an offline record");
                    BrightnessState duringDim = SettingsStore.LoadBrightnessState();
                    Check(duringDim.Monitors.Count == 2 && duringDim.Monitors[0].Brightness == 100 &&
                        duringDim.Monitors[1].Brightness == 50,
                        "New dimming merges recovery records and preserves both original brightness values");
                    int previousWrites = writes;
                    Check((bool)Call(form, "EnsureMonitorsDimmed") && writes == previousWrites,
                        "An already-active cycle is not dimmed or snapshotted twice");

                    ArmSimulatedLock(form, DateTime.UtcNow);
                    SendSessionMessage(form, NativeMethods.WtsSessionUnlock);
                    Check(external.Current == 50 && !Get<bool>(form, "immediateSleepActive") &&
                        Get<Label>(form, "statusLabel").Text == "监控中",
                        "Unlock completes the active cycle even while an offline panel is pending");
                    Check(SettingsStore.LoadBrightnessState().Monitors.Count == 1,
                        "A completed external restore retires only that external snapshot");

                    Call(form, "ProcessScreenSaverState", true, DateTime.UtcNow.AddSeconds(3));
                    Check(external.Current == 0 && Get<bool>(form, "dimmed"),
                        "Windows screen saver following also dims with an offline recovery queue");
                    Call(form, "ProcessScreenSaverState", false, DateTime.UtcNow.AddSeconds(6));
                    Check(external.Current == 50 && !Get<bool>(form, "dimmed") &&
                        Get<Label>(form, "statusLabel").Text == "监控中",
                        "Windows screen saver exit restores connected screens and resumes monitoring");

                    panel.Current = 0;
                    panel.DeviceName = @"\\.\DISPLAY3";
                    connected.Add(panel);
                    Set(form, "monitoring", false);
                    Call(form, "RetryPendingBrightness", DateTime.UtcNow.AddSeconds(10));
                    Check(panel.Current == 100 && external.Current == 50 &&
                        !File.Exists(AppPaths.BrightnessStateFile),
                        "A re-enabled panel restores by identity even with automatic monitoring stopped");
                    Check(Get<int>(form, "disconnectedRestoreCount") == 0 &&
                        Get<Label>(form, "statusLabel").Text == "未启动",
                        "Completing deferred recovery clears its notice without starting monitoring");
                }
                finally
                {
                    Get<NotifyIcon>(form, "trayIcon").Dispose();
                    Get<ToolTip>(form, "toolTip").Dispose();
                    Get<System.Drawing.Icon>(form, "applicationIcon").Dispose();
                    Get<Timer>(form, "monitorTimer").Dispose();
                    SettingsStore.DeleteBrightnessState();
                }
            }
        }

        private sealed class FakeBrightnessHardware
        {
            public readonly List<MonitorInfo> Monitors = new List<MonitorInfo>();
            public int Writes { get; private set; }
            public bool FailWrites { get; set; }
            public bool ApplyFailedWrites { get; set; }
            public bool FailReads { get; set; }
            public bool SavedBeforeEveryWrite { get; private set; }

            public FakeBrightnessHardware()
            {
                SavedBeforeEveryWrite = true;
            }

            public List<MonitorInfo> Read()
            {
                if (FailReads)
                {
                    throw new IOException("Simulated temporary display enumeration failure.");
                }
                return Monitors;
            }

            public bool Write(MonitorInfo monitor, uint value)
            {
                Writes++;
                BrightnessState state = SettingsStore.LoadBrightnessState();
                SavedBeforeEveryWrite &= state != null && state.Monitors.Exists(
                    delegate(BrightnessSnapshot saved) { return MainForm.FindCurrentMonitor(saved, Monitors) == monitor; });
                if (!FailWrites || ApplyFailedWrites)
                {
                    monitor.Current = value;
                }
                return !FailWrites;
            }
        }

        private static void SaveRecovery(params MonitorInfo[] monitors)
        {
            BrightnessState state = new BrightnessState();
            state.SavedAt = DateTime.Now;
            foreach (MonitorInfo monitor in monitors)
            {
                state.Monitors.Add(BrightnessSnapshot.FromMonitor(monitor));
            }
            SettingsStore.SaveBrightnessState(state);
        }

        private static void TestBrightnessReadbackAndFailures()
        {
            MonitorInfo panel = MakeMonitor(@"\\.\DISPLAY1", @"DISPLAY\AAA0001\panel", BrightnessSource.WindowsWmi);
            panel.BrightnessInstanceName = panel.PnpInstanceId + "_0";
            panel.Current = 100;
            FakeBrightnessHardware hardware = new FakeBrightnessHardware();
            hardware.Monitors.Add(panel);
            using (MainForm form = new MainForm(false, hardware.Read, hardware.Write))
            {
                Get<NotifyIcon>(form, "trayIcon").Visible = false;
                try
                {
                    Set(form, "monitoring", true);
                    // Exact user sequence: brightness restored, unlock, THEN
                    // disable the panel. An unnecessary failed same-value write
                    // used to leave this already-restored panel in the queue.
                    hardware.FailWrites = true;
                    SaveRecovery(panel);
                    ArmSimulatedLock(form, DateTime.UtcNow);
                    SendSessionMessage(form, NativeMethods.WtsSessionUnlock);
                    Check(!File.Exists(AppPaths.BrightnessStateFile) && hardware.Writes == 0,
                        "Unlock retires an already-restored panel without requiring a same-value write");
                    hardware.Monitors.Clear();
                    Call(form, "ProcessScreenSaverState", false, DateTime.UtcNow);
                    Check(Get<Label>(form, "statusLabel").Text == "监控中" &&
                        Get<int>(form, "disconnectedRestoreCount") == 0,
                        "Disabling the panel after restore and unlock cannot resurrect a stale recovery record");

                    hardware.Monitors.Add(panel);
                    hardware.ApplyFailedWrites = true;
                    Check((bool)Call(form, "EnsureMonitorsDimmed") && panel.Current == 0,
                        "A dim write with a failed acknowledgement is accepted only after matching readback");
                    Check(SettingsStore.LoadBrightnessState().Monitors[0].Brightness == 100,
                        "Readback confirmation does not overwrite the saved pre-dim brightness");
                    Check((bool)Call(form, "RestoreSavedBrightness") && panel.Current == 100 &&
                        !File.Exists(AppPaths.BrightnessStateFile),
                        "A restored value confirmed by readback clears an unacknowledged write");
                    Check(hardware.SavedBeforeEveryWrite,
                        "Crash recovery snapshots exist before all dim and restore hardware writes");

                    MonitorInfo offline = MakeMonitor(@"\\.\DISPLAY2", @"DISPLAY\BBB0002\offline", BrightnessSource.DdcCi);
                    SaveRecovery(offline);
                    hardware.ApplyFailedWrites = false;
                    Check(!(bool)Call(form, "EnsureMonitorsDimmed") && panel.Current == 100,
                        "A genuinely failed dim write is not claimed as successful");
                    Check(SettingsStore.LoadBrightnessState().Monitors.Count == 2,
                        "Failed dimming cannot delete an unrelated offline recovery record");
                    int previousWrites = hardware.Writes;
                    Check((bool)Call(form, "RestoreSavedBrightness") && hardware.Writes == previousWrites &&
                        SettingsStore.LoadBrightnessState().Monitors.Count == 1,
                        "A dim failure that changed nothing retires by readback while retaining offline recovery");

                    hardware.Monitors.Clear();
                    Check(!(bool)Call(form, "EnsureMonitorsDimmed") &&
                        SettingsStore.LoadBrightnessState().Monitors.Count == 1,
                        "No connected monitors is not a successful dim and does not erase recovery data");

                    hardware.Monitors.Add(panel);
                    SaveRecovery(panel);
                    panel.Current = 0;
                    previousWrites = hardware.Writes;
                    Check(!(bool)Call(form, "RestoreSavedBrightness") &&
                        hardware.Writes == previousWrites + 3 && File.Exists(AppPaths.BrightnessStateFile),
                        "A real restore failure retains the snapshot and uses bounded retries");
                    Check(Get<Label>(form, "statusLabel").Text.Contains("恢复失败") &&
                        Get<int>(form, "disconnectedRestoreCount") == 0,
                        "A connected display write failure is not mislabeled as a disconnected screen");

                    panel.Source = BrightnessSource.Unsupported;
                    previousWrites = hardware.Writes;
                    Check(!(bool)Call(form, "RestoreSavedBrightness") && hardware.Writes == previousWrites &&
                        Get<int>(form, "disconnectedRestoreCount") == 0,
                        "A present display with an unavailable brightness interface stays a recoverable error");
                    panel.Source = BrightnessSource.WindowsWmi;
                    hardware.FailReads = true;
                    Check(!(bool)Call(form, "RestoreSavedBrightness") && File.Exists(AppPaths.BrightnessStateFile),
                        "Enumeration failure preserves recovery data and is not mistaken for disconnection");
                    hardware.FailReads = false;

                    hardware.FailWrites = false;
                    Check((bool)Call(form, "EnsureMonitorsDimmed") &&
                        SettingsStore.LoadBrightnessState().Monitors[0].Brightness == 100,
                        "A new cycle never replaces the original brightness with an unrestored dim level");
                    Check((bool)Call(form, "RestoreSavedBrightness") && panel.Current == 100 &&
                        !File.Exists(AppPaths.BrightnessStateFile),
                        "Restoration succeeds after the driver recovers");

                    panel.BrightnessLevels = new byte[] { 10, 40, 70, 100 };
                    panel.Current = 10;
                    previousWrites = hardware.Writes;
                    Check((bool)Call(form, "EnsureMonitorsDimmed") && hardware.Writes == previousWrites &&
                        !File.Exists(AppPaths.BrightnessStateFile),
                        "An already-minimum panel creates no unnecessary recovery record or write");
                    Check((bool)Call(form, "RestoreSavedBrightness") && panel.Current == 10,
                        "Ending a no-op dim cycle preserves the user's existing minimum brightness");
                }
                finally
                {
                    Get<NotifyIcon>(form, "trayIcon").Dispose();
                    Get<ToolTip>(form, "toolTip").Dispose();
                    Get<System.Drawing.Icon>(form, "applicationIcon").Dispose();
                    Get<Timer>(form, "monitorTimer").Dispose();
                    SettingsStore.DeleteBrightnessState();
                }
            }
        }

        private static void ArmSimulatedLock(MainForm form, DateTime requested)
        {
            // No LockWorkStation/StartBlank/DimMonitors call: state injection only.
            Set(form, "immediateSleepActive", true);
            Set(form, "immediateSleepSyncLock", true);
            Set(form, "immediateSleepSyncMute", false);
            Set(form, "immediateSleepLockRequested", true);
            Set(form, "immediateSleepSessionLocked", false);
            Set(form, "immediateSleepLockRequestedUtc", requested);
        }

        private static void SendSessionMessage(MainForm form, int change)
        {
            Message message = Message.Create(form.Handle, NativeMethods.WmWtsSessionChange,
                new IntPtr(change), IntPtr.Zero);
            Call(form, "WndProc", message);
        }

        private static void TestSessionLifecycle()
        {
            Application.EnableVisualStyles();
            using (MainForm form = new MainForm(false))
            {
                // Never show the test form or run its monitoring timer. This also
                // avoids reading/writing the user's settings or autostart entry.
                Get<NotifyIcon>(form, "trayIcon").Visible = false;
                Set(form, "monitoring", true);
                IntPtr initialHandle = form.Handle;
                Check(Get<IntPtr>(form, "sessionNotificationWindow") == initialHandle,
                    "WTS subscription is bound to the initial HWND");

                for (int cycle = 0; cycle < 3; cycle++)
                {
                    form.ShowInTaskbar = false;
                    Check(Get<IntPtr>(form, "sessionNotificationWindow") == form.Handle,
                        "WTS remains registered after tray transition " + cycle);
                    form.ShowInTaskbar = true;
                    Check(Get<IntPtr>(form, "sessionNotificationWindow") == form.Handle,
                        "WTS remains registered after window transition " + cycle);
                }
                typeof(Control).GetMethod("DestroyHandle", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(form, null);
                Check(Get<IntPtr>(form, "sessionNotificationWindow") == IntPtr.Zero,
                    "Destroyed HWND clears the WTS registration token");
                IntPtr recreatedHandle = form.Handle;
                Check(recreatedHandle != IntPtr.Zero,
                    "Handle can be recreated after destruction");
                Check(Get<IntPtr>(form, "sessionNotificationWindow") == form.Handle,
                    "Recreated HWND receives a fresh WTS registration");

                DateTime now = DateTime.UtcNow;
                ArmSimulatedLock(form, now);
                SendSessionMessage(form, NativeMethods.WtsSessionLock);
                Check(Get<bool>(form, "immediateSleepSessionLocked"), "Lock notification enters locked state");
                SendSessionMessage(form, NativeMethods.WtsSessionUnlock);
                Check(!Get<bool>(form, "immediateSleepActive") &&
                    Get<Label>(form, "statusLabel").Text == "监控中",
                    "Unlock notification clears stale status and resumes monitoring");
                Check(!Get<bool>(form, "immediateSleepLockRequested"), "Unlock clears pending lock flag");

                ArmSimulatedLock(form, now);
                Call(form, "ReconcileImmediateSessionState", false, now.AddSeconds(1));
                Check(Get<bool>(form, "immediateSleepActive"), "Async lock request is not finished prematurely");
                Call(form, "ReconcileImmediateSessionState", true, now.AddSeconds(2));
                Call(form, "ReconcileImmediateSessionState", false, now.AddSeconds(3));
                Check(!Get<bool>(form, "immediateSleepActive"), "Polling recovers missed lock/unlock notifications");

                ArmSimulatedLock(form, now);
                Call(form, "ReconcileImmediateSessionState", false, now.AddSeconds(6));
                Check(!Get<bool>(form, "immediateSleepActive"), "Fast lock/unlock with both events missed recovers");

                ArmSimulatedLock(form, now);
                SendSessionMessage(form, NativeMethods.WtsSessionUnlock);
                Check(!Get<bool>(form, "immediateSleepActive"), "Unlock does not require a prior lock notification");
                string status = Get<Label>(form, "statusLabel").Text;
                SendSessionMessage(form, NativeMethods.WtsSessionLock);
                SendSessionMessage(form, NativeMethods.WtsSessionUnlock);
                Check(Get<Label>(form, "statusLabel").Text == status &&
                    Get<AudioVolumeState>(form, "immediateSleepAudioState") == null,
                    "Independent Windows locks do not change app state or audio");

                Call(form, "RequestImmediateScreenSaver", 0, 0, true);
                Check(Get<bool>(form, "immediateSleepTriggeredByMouse") &&
                    !Get<bool>(form, "immediateSleepLockRequested"), "Tray request remains outside the lock flow");
                Call(form, "CancelImmediateScreenSaver");
                Get<NotifyIcon>(form, "trayIcon").Dispose();
                Get<ToolTip>(form, "toolTip").Dispose();
                Get<System.Drawing.Icon>(form, "applicationIcon").Dispose();
                Get<Timer>(form, "monitorTimer").Dispose();
            }
        }
    }
}
