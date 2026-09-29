// AppLauncher - a tiny taskbar launcher for Windows.
//
// Pin AppLauncher.exe to the taskbar and drag it to the far left. Clicking it
// pops up a menu of the apps listed in apps.txt (next to the exe), launches
// the one you pick, and exits. Nothing stays running in the background.
//
// Written against .NET Framework 4.x / C# 5 so it builds with the csc.exe that
// ships with every Windows install (see build.bat).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("AppLauncher")]
[assembly: System.Reflection.AssemblyProduct("AppLauncher")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]

namespace AppLauncher
{
    internal sealed class AppEntry
    {
        public string Name;
        public string Target;
        public string Arguments;
        public bool IsSeparator;
        public string Header; // non-null for "# == Title ==" style group headers
    }

    internal static class Program
    {
        private const string ConfigFileName = "apps.txt";

        private const string DefaultConfig =
@"# AppLauncher - list the apps you want in the menu, one per line:
#
#     Display name | path to program, shortcut, folder, file or URL | optional arguments
#
# - Environment variables like %USERPROFILE% or %LOCALAPPDATA% are expanded.
# - A line containing only --- adds a separator.
# - A line like  [Work]  adds a group heading.
# - Lines starting with # are ignored.
# Save this file and click the taskbar icon again - changes apply immediately.

[Apps]
Notepad        | %WINDIR%\System32\notepad.exe
Calculator     | calc.exe
Paint          | mspaint.exe
---
[Places]
Downloads      | %USERPROFILE%\Downloads
Documents      | %USERPROFILE%\Documents
---
[Web]
Google         | https://www.google.com
";

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigFileName);
            if (!File.Exists(configPath))
            {
                try { File.WriteAllText(configPath, DefaultConfig, Encoding.UTF8); }
                catch (Exception ex)
                {
                    MessageBox.Show("Could not create " + configPath + "\n\n" + ex.Message,
                        "AppLauncher", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            List<AppEntry> entries;
            try { entries = LoadConfig(configPath); }
            catch (Exception ex)
            {
                MessageBox.Show("Could not read " + configPath + "\n\n" + ex.Message,
                    "AppLauncher", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // A hidden, zero-size window owns the menu. It needs to be the
            // foreground window so the menu closes when you click elsewhere.
            var host = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-32000, -32000),
                Size = new Size(1, 1),
                Opacity = 0,
                TopMost = true,
            };

            ContextMenuStrip menu = BuildMenu(entries, configPath);
            menu.Closed += delegate { host.BeginInvoke((MethodInvoker)host.Close); };

            host.Shown += delegate
            {
                SetForegroundWindow(host.Handle);
                host.Activate();
                ShowMenuNearTaskbar(menu);
            };

            Application.Run(host);
        }

        private static List<AppEntry> LoadConfig(string path)
        {
            var list = new List<AppEntry>();
            foreach (string rawLine in File.ReadAllLines(path))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;

                if (line.StartsWith("---"))
                {
                    list.Add(new AppEntry { IsSeparator = true });
                    continue;
                }

                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    list.Add(new AppEntry { Header = line.Substring(1, line.Length - 2).Trim() });
                    continue;
                }

                string[] parts = line.Split(new[] { '|' }, 3);
                var entry = new AppEntry();
                if (parts.Length == 1)
                {
                    entry.Target = parts[0].Trim();
                    entry.Name = Path.GetFileNameWithoutExtension(entry.Target);
                }
                else
                {
                    entry.Name = parts[0].Trim();
                    entry.Target = parts[1].Trim();
                    entry.Arguments = parts.Length > 2 ? parts[2].Trim() : "";
                }
                entry.Target = Environment.ExpandEnvironmentVariables(entry.Target.Trim('"'));
                if (entry.Arguments != null)
                    entry.Arguments = Environment.ExpandEnvironmentVariables(entry.Arguments);
                if (entry.Target.Length > 0) list.Add(entry);
            }
            return list;
        }

        private static ContextMenuStrip BuildMenu(List<AppEntry> entries, string configPath)
        {
            var menu = new ContextMenuStrip
            {
                ShowImageMargin = true,
                ImageScalingSize = new Size(24, 24),
                Font = new Font("Segoe UI", 10f),
                Padding = new Padding(2, 4, 2, 4),
            };

            foreach (AppEntry entry in entries)
            {
                if (entry.IsSeparator)
                {
                    // Avoid leading or doubled separators.
                    if (menu.Items.Count > 0 && !(menu.Items[menu.Items.Count - 1] is ToolStripSeparator))
                        menu.Items.Add(new ToolStripSeparator());
                    continue;
                }

                if (entry.Header != null)
                {
                    var header = new ToolStripMenuItem(EscapeMnemonic(entry.Header))
                    {
                        Enabled = false,
                        Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                    };
                    menu.Items.Add(header);
                    continue;
                }

                AppEntry captured = entry;
                var item = new ToolStripMenuItem(EscapeMnemonic(entry.Name))
                {
                    Image = GetIcon(entry.Target),
                    ToolTipText = entry.Target,
                };
                item.Click += delegate { Launch(captured); };
                menu.Items.Add(item);
            }

            if (menu.Items.Count > 0 && !(menu.Items[menu.Items.Count - 1] is ToolStripSeparator))
                menu.Items.Add(new ToolStripSeparator());

            var edit = new ToolStripMenuItem("Edit app list...");
            edit.Click += delegate
            {
                Launch(new AppEntry { Target = "notepad.exe", Arguments = "\"" + configPath + "\"" });
            };
            menu.Items.Add(edit);

            return menu;
        }

        private static void ShowMenuNearTaskbar(ContextMenuStrip menu)
        {
            Point cursor = Cursor.Position;
            Screen screen = Screen.FromPoint(cursor);
            Rectangle work = screen.WorkingArea;
            Rectangle bounds = screen.Bounds;

            // Work out which edge the taskbar is on and open the menu from there.
            if (work.Bottom < bounds.Bottom)       // taskbar at bottom (default)
                menu.Show(new Point(Math.Max(cursor.X - 16, work.Left), work.Bottom), ToolStripDropDownDirection.AboveRight);
            else if (work.Top > bounds.Top)        // taskbar at top
                menu.Show(new Point(Math.Max(cursor.X - 16, work.Left), work.Top), ToolStripDropDownDirection.BelowRight);
            else if (work.Left > bounds.Left)      // taskbar on the left
                menu.Show(new Point(work.Left, Math.Max(cursor.Y - 16, work.Top)), ToolStripDropDownDirection.BelowRight);
            else if (work.Right < bounds.Right)    // taskbar on the right
                menu.Show(new Point(work.Right, Math.Max(cursor.Y - 16, work.Top)), ToolStripDropDownDirection.BelowLeft);
            else                                   // auto-hide taskbar: open at cursor
                menu.Show(cursor, cursor.Y > bounds.Top + bounds.Height / 2
                    ? ToolStripDropDownDirection.AboveRight
                    : ToolStripDropDownDirection.BelowRight);
        }

        private static void Launch(AppEntry entry)
        {
            try
            {
                var psi = new ProcessStartInfo(entry.Target)
                {
                    UseShellExecute = true,
                    Arguments = entry.Arguments ?? "",
                };
                if (File.Exists(entry.Target))
                    psi.WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(entry.Target));
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not start \"" + entry.Target + "\"\n\n" + ex.Message,
                    "AppLauncher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static Image GetIcon(string target)
        {
            try
            {
                string path = target;
                if (!File.Exists(path) && !Directory.Exists(path))
                    path = FindOnPath(target);
                if (path == null) return null;

                if (Directory.Exists(path))
                    return ShellIcon(path);

                using (Icon icon = Icon.ExtractAssociatedIcon(path))
                    return icon == null ? null : icon.ToBitmap();
            }
            catch
            {
                return null;
            }
        }

        // Resolves bare names like "calc.exe" the same way the Run dialog does.
        private static string FindOnPath(string name)
        {
            if (name.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || name.Contains("://")) return null;
            if (Path.IsPathRooted(name)) return null;

            var dirs = new List<string> { Environment.SystemDirectory, Environment.GetEnvironmentVariable("WINDIR") ?? "" };
            dirs.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'));
            string[] exts = Path.HasExtension(name) ? new[] { "" } : new[] { ".exe", ".lnk", ".bat", ".cmd" };

            foreach (string dir in dirs)
            {
                if (dir.Trim().Length == 0) continue;
                foreach (string ext in exts)
                {
                    try
                    {
                        string candidate = Path.Combine(dir.Trim(), name + ext);
                        if (File.Exists(candidate)) return candidate;
                    }
                    catch (ArgumentException) { }
                }
            }
            return null;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private const uint SHGFI_ICON = 0x100;
        private const uint SHGFI_LARGEICON = 0x0;

        private static Image ShellIcon(string path)
        {
            var info = new SHFILEINFO();
            SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf(info), SHGFI_ICON | SHGFI_LARGEICON);
            if (info.hIcon == IntPtr.Zero) return null;
            try
            {
                using (Icon icon = Icon.FromHandle(info.hIcon))
                    return icon.ToBitmap();
            }
            finally
            {
                DestroyIcon(info.hIcon);
            }
        }

        private static string EscapeMnemonic(string text)
        {
            return text.Replace("&", "&&");
        }
    }
}
