using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace ExternalMonitorDimmer
{
    internal static class Program
    {
        private const string MutexName = @"Local\ExternalMonitorDimmer.Singleton";
        private const string ShowEventName = @"Local\ExternalMonitorDimmer.ShowWindow";

        [STAThread]
        private static void Main(string[] args)
        {
            string diagnosticsPath = GetArgumentValue(args, "--diagnostics");
            if (!String.IsNullOrEmpty(diagnosticsPath))
            {
                RunDiagnostics(diagnosticsPath);
                return;
            }

            bool createdNew;
            using (Mutex mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    SignalExistingInstance();
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                SettingsStore.Log("Application process started. Arguments=" + String.Join(" ", args));

                bool startHidden = HasArgument(args, "--background");
                using (EventWaitHandle showEvent = new EventWaitHandle(
                    false,
                    EventResetMode.AutoReset,
                    ShowEventName))
                using (MainForm form = new MainForm(startHidden))
                {
                    SettingsStore.Log("Main window constructed.");
                    Thread showThread = new Thread(delegate()
                    {
                        while (!form.IsDisposed)
                        {
                            if (!showEvent.WaitOne(500))
                            {
                                continue;
                            }

                            try
                            {
                                form.BeginInvoke(new Action(form.ShowFromTray));
                            }
                            catch (InvalidOperationException)
                            {
                                return;
                            }
                        }
                    });
                    showThread.IsBackground = true;
                    showThread.Name = "ExternalMonitorDimmer.ShowWindow";
                    showThread.Start();

                    SettingsStore.Log("Entering the Windows message loop.");
                    Application.Run(form);
                    SettingsStore.Log("Windows message loop exited.");
                }

                try
                {
                    mutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                }
            }
        }

        private static bool HasArgument(string[] args, string expected)
        {
            foreach (string arg in args)
            {
                if (String.Equals(arg, expected, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetArgumentValue(string[] args, string expected)
        {
            for (int index = 0; index < args.Length - 1; index++)
            {
                if (String.Equals(args[index], expected, StringComparison.OrdinalIgnoreCase))
                {
                    return args[index + 1];
                }
            }

            return null;
        }

        private static void SignalExistingInstance()
        {
            try
            {
                using (EventWaitHandle showEvent = EventWaitHandle.OpenExisting(ShowEventName))
                {
                    showEvent.Set();
                }
            }
            catch (WaitHandleCannotBeOpenedException)
            {
            }
        }

        private static void RunDiagnostics(string outputPath)
        {
            StringBuilder report = new StringBuilder();
            report.AppendLine("External Monitor Dimmer diagnostics");
            report.AppendLine("IdleMilliseconds=" + NativeMethods.GetIdleMilliseconds());

            try
            {
                System.Collections.Generic.List<MonitorInfo> monitors = NativeMethods.GetBrightnessMonitors();
                report.AppendLine("MonitorCount=" + monitors.Count);

                foreach (MonitorInfo monitor in monitors)
                {
                    bool sameValueWrite = NativeMethods.SetBrightness(
                        monitor.DeviceName,
                        monitor.PhysicalIndex,
                        monitor.Current);
                    report.AppendLine(String.Format(
                        "Display={0};Monitor={1};Brightness={2};Range={3}-{4};SameValueWrite={5}",
                        monitor.DeviceName,
                        monitor.Description,
                        monitor.Current,
                        monitor.Minimum,
                        monitor.Maximum,
                        sameValueWrite));
                }
            }
            catch (Exception ex)
            {
                report.AppendLine("Error=" + ex);
            }

            string fullPath = Path.GetFullPath(outputPath);
            string directory = Path.GetDirectoryName(fullPath);
            if (!String.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllText(fullPath, report.ToString(), new UTF8Encoding(false));
        }
    }
}
