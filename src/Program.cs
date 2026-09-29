// AppLauncher - a button on the far left of the Windows taskbar that opens a
// menu of your favourite apps (listed in apps.txt next to the exe).
//
// Written against .NET Framework 4.x / C# 5 so it builds with the csc.exe that
// ships with every Windows install (see build.bat).

using System;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("AppLauncher")]
[assembly: System.Reflection.AssemblyProduct("AppLauncher")]
[assembly: System.Reflection.AssemblyVersion("2.0.0.0")]

namespace AppLauncher
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            bool isFirstInstance;
            using (var mutex = new Mutex(true, @"Local\AppLauncher.TaskbarButton", out isFirstInstance))
            {
                if (!isFirstInstance) return; // already running

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                // First run: create an example apps.txt and start with Windows from now on.
                bool firstRun = false;
                try { firstRun = AppMenu.EnsureConfigExists(); }
                catch (Exception ex) { AppMenu.ShowError("Could not create " + AppMenu.ConfigPath, ex); }
                if (firstRun)
                {
                    try { Autostart.Set(true); }
                    catch { }
                }

                using (var button = new TaskbarButton())
                {
                    button.Start();
                    Application.Run();
                }
                GC.KeepAlive(mutex);
            }
        }
    }

    internal static class Autostart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "AppLauncher";

        private static string Command
        {
            get { return "\"" + Application.ExecutablePath + "\""; }
        }

        public static bool IsEnabled
        {
            get
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey))
                {
                    string value = key == null ? null : key.GetValue(ValueName) as string;
                    return value != null && string.Equals(value, Command, StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        public static void Set(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enabled) key.SetValue(ValueName, Command);
                else key.DeleteValue(ValueName, false);
            }
        }
    }
}
