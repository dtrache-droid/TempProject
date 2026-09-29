// AppLauncher - a button on the far left of the Windows taskbar that opens a
// list of your favourite apps.
//
// Written against .NET Framework 4.x / C# 5 so it builds with the csc.exe that
// ships with every Windows install (see build.bat).

using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("AppLauncher")]
[assembly: AssemblyDescription("Taskbar button that opens a list of your favourite apps")]
[assembly: AssemblyCompany("AppLauncher")]
[assembly: AssemblyProduct("AppLauncher")]
[assembly: AssemblyCopyright("Copyright (c) 2026")]
[assembly: AssemblyVersion("3.2.0.0")]
[assembly: AssemblyFileVersion("3.2.0.0")]

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

                Settings settings = Settings.Load();
                Autostart.MigrateFromRegistry();

                // First run: start with Windows from now on and write out the default settings.
                if (settings.IsFirstRun)
                {
                    try { Autostart.Set(true); }
                    catch { }
                    settings.Save();
                }

                using (var button = new TaskbarButton(settings))
                {
                    button.Start();
                    Application.Run();
                }
                GC.KeepAlive(mutex);
            }
        }
    }

    /// <summary>"Start with Windows" = a shortcut in the user's Startup folder (visible in Task Manager > Startup).</summary>
    internal static class Autostart
    {
        private const string OldRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "AppLauncher";

        private static string ShortcutPath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "AppLauncher.lnk");
            }
        }

        public static bool IsEnabled
        {
            get { return File.Exists(ShortcutPath); }
        }

        public static void Set(bool enabled)
        {
            if (!enabled)
            {
                if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath);
                return;
            }

            object shell = null, link = null;
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                shell = Activator.CreateInstance(shellType);
                link = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { ShortcutPath });
                Type linkType = link.GetType();
                linkType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { Application.ExecutablePath });
                linkType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, link, new object[] { Path.GetDirectoryName(Application.ExecutablePath) });
                linkType.InvokeMember("Description", BindingFlags.SetProperty, null, link, new object[] { "AppLauncher" });
                linkType.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
            }
            finally
            {
                if (link != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(link);
                if (shell != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);
            }
        }

        // Earlier builds started with Windows through the registry; move that over to the Startup folder.
        public static void MigrateFromRegistry()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(OldRunKey, true))
                {
                    if (key == null || key.GetValue(ValueName) == null) return;
                    key.DeleteValue(ValueName, false);
                }
                Set(true);
            }
            catch { }
        }
    }
}
