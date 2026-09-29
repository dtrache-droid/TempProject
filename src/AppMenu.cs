// Reads apps.txt and turns it into the pop-up menu of apps.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace AppLauncher
{
    internal sealed class AppEntry
    {
        public string Name;
        public string Target;
        public string Arguments;
        public bool IsSeparator;
        public string Header; // non-null for "[Group]" headings
    }

    internal static class AppMenu
    {
        public static readonly string ConfigPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "apps.txt");

        private const string DefaultConfig =
@"# AppLauncher - list the apps you want in the menu, one per line:
#
#     Display name | path to program, shortcut, folder, file or URL | optional arguments
#
# - Environment variables like %USERPROFILE% or %LOCALAPPDATA% are expanded.
# - A line containing only --- adds a separator.
# - A line like  [Work]  adds a group heading.
# - Lines starting with # are ignored.
# Save this file and click the taskbar button again - changes apply immediately.

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

        /// <summary>Creates apps.txt with examples. Returns true if it was created now.</summary>
        public static bool EnsureConfigExists()
        {
            if (File.Exists(ConfigPath)) return false;
            File.WriteAllText(ConfigPath, DefaultConfig, Encoding.UTF8);
            return true;
        }

        public static void EditConfig()
        {
            try { EnsureConfigExists(); }
            catch (Exception ex) { ShowError("Could not create " + ConfigPath, ex); return; }
            Launch(new AppEntry { Target = "notepad.exe", Arguments = "\"" + ConfigPath + "\"" });
        }

        public static ContextMenuStrip Build(float scale)
        {
            var menu = NewMenu(scale);

            List<AppEntry> entries;
            try
            {
                EnsureConfigExists();
                entries = Load(ConfigPath);
            }
            catch (Exception ex)
            {
                entries = new List<AppEntry>();
                menu.Items.Add(new ToolStripMenuItem("Could not read apps.txt: " + ex.Message) { Enabled = false });
            }

            var headerFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            foreach (AppEntry entry in entries)
            {
                if (entry.IsSeparator)
                {
                    if (menu.Items.Count > 0 && !(menu.Items[menu.Items.Count - 1] is ToolStripSeparator))
                        menu.Items.Add(new ToolStripSeparator());
                    continue;
                }

                if (entry.Header != null)
                {
                    menu.Items.Add(new ToolStripMenuItem(EscapeMnemonic(entry.Header))
                    {
                        Enabled = false,
                        Font = headerFont,
                    });
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

            if (entries.Count == 0)
            {
                var edit = new ToolStripMenuItem("No apps yet - click to edit the list");
                edit.Click += delegate { EditConfig(); };
                menu.Items.Add(edit);
            }

            // Drop a trailing separator.
            if (menu.Items.Count > 0 && menu.Items[menu.Items.Count - 1] is ToolStripSeparator)
                menu.Items.RemoveAt(menu.Items.Count - 1);

            return menu;
        }

        public static ContextMenuStrip NewMenu(float scale)
        {
            int icon = (int)Math.Round(24 * scale);
            return new ContextMenuStrip
            {
                ShowImageMargin = true,
                ImageScalingSize = new Size(icon, icon),
                Font = new Font("Segoe UI", 10f),
                Padding = new Padding(2, 4, 2, 4),
            };
        }

        private static List<AppEntry> Load(string path)
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
                    entry.Name = Path.GetFileNameWithoutExtension(entry.Target.Trim('"'));
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
                ShowError("Could not start \"" + entry.Target + "\"", ex);
            }
        }

        public static void ShowError(string message, Exception ex)
        {
            MessageBox.Show(message + "\n\n" + ex.Message, "AppLauncher",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

        private static Image ShellIcon(string path)
        {
            const uint SHGFI_ICON = 0x100, SHGFI_LARGEICON = 0x0;
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
