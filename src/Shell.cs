// Helpers for launching apps, getting their icons and asking for a name.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AppLauncher
{
    internal static class Shell
    {
        private static readonly Dictionary<string, Image> iconCache = new Dictionary<string, Image>();

        // ---- Launching ------------------------------------------------------

        public static void Launch(string path)
        {
            try
            {
                var psi = new ProcessStartInfo(path) { UseShellExecute = true };
                string dir = Path.GetDirectoryName(path);
                if (File.Exists(path) && !string.IsNullOrEmpty(dir)) psi.WorkingDirectory = dir;
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                ShowError("Could not start \"" + path + "\"", ex);
            }
        }

        public static void ShowError(string message, Exception ex)
        {
            MessageBox.Show(message + "\n\n" + ex.Message, "AppLauncher",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        /// <summary>A friendly default name for an app picked from disk.</summary>
        public static string DefaultName(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            if (string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    string desc = (FileVersionInfo.GetVersionInfo(path).FileDescription ?? "").Trim();
                    if (desc.Length > 0 && desc.Length <= 40) name = desc;
                }
                catch { }
            }
            return name;
        }

        // ---- Icons ----------------------------------------------------------

        public static void ClearIconCache()
        {
            lock (iconCache)
                iconCache.Clear();
        }

        /// <summary>The icon of an app, rendered sharply at px x px. Never null.</summary>
        public static Image GetAppIcon(string path, int px)
        {
            string key = path.ToLowerInvariant() + "|" + px;
            lock (iconCache)
            {
                Image cached;
                if (iconCache.TryGetValue(key, out cached)) return cached;
            }

            Image icon = null;
            try { icon = LoadAppIcon(path, px); }
            catch { }
            if (icon == null)
                icon = Fit(SystemIcons.Application.ToBitmap(), px);

            lock (iconCache)
                iconCache[key] = icon;
            return icon;
        }

        private static Image LoadAppIcon(string path, int px)
        {
            string source = path;
            int index = 0;

            if (string.Equals(Path.GetExtension(path), ".lnk", StringComparison.OrdinalIgnoreCase))
            {
                string target, iconFile;
                int iconIndex;
                if (ResolveShortcut(path, out target, out iconFile, out iconIndex))
                {
                    if (iconFile.Length > 0 && File.Exists(iconFile)) { source = iconFile; index = iconIndex; }
                    else if (target.Length > 0 && File.Exists(target)) { source = target; }
                }
            }

            string ext = Path.GetExtension(source).ToLowerInvariant();
            if (ext == ".exe" || ext == ".dll" || ext == ".ico" || ext == ".icl")
            {
                Image sharp = ExtractIcon(source, index, px);
                if (sharp != null) return sharp;
            }

            using (Icon icon = Icon.ExtractAssociatedIcon(path))
                return icon == null ? null : Fit(icon.ToBitmap(), px);
        }

        private static Image ExtractIcon(string file, int index, int px)
        {
            var handles = new IntPtr[1];
            var ids = new uint[1];
            uint got = NativeMethods.PrivateExtractIcons(file, index, px, px, handles, ids, 1, 0);
            if (got == 0 || got == 0xFFFFFFFF || handles[0] == IntPtr.Zero) return null;
            try
            {
                using (Icon icon = Icon.FromHandle(handles[0]))
                    return Fit(icon.ToBitmap(), px);
            }
            finally
            {
                NativeMethods.DestroyIcon(handles[0]);
            }
        }

        /// <summary>Returns a px x px copy of the image (scaled to fit, keeping proportions).</summary>
        public static Bitmap Fit(Image src, int px)
        {
            var result = new Bitmap(px, px, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(result))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;
                float k = Math.Min((float)px / src.Width, (float)px / src.Height);
                float w = src.Width * k, h = src.Height * k;
                g.DrawImage(src, (px - w) / 2, (px - h) / 2, w, h);
            }
            src.Dispose();
            return result;
        }

        // Reads the target and icon location of a .lnk through the scripting host (no extra references needed).
        private static bool ResolveShortcut(string lnk, out string target, out string iconFile, out int iconIndex)
        {
            target = "";
            iconFile = "";
            iconIndex = 0;
            object shell = null, link = null;
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return false;
                shell = Activator.CreateInstance(shellType);
                link = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                Type linkType = link.GetType();

                target = Environment.ExpandEnvironmentVariables(
                    ((string)linkType.InvokeMember("TargetPath", BindingFlags.GetProperty, null, link, null) ?? "").Trim());
                string location = ((string)linkType.InvokeMember("IconLocation", BindingFlags.GetProperty, null, link, null) ?? "").Trim();

                int comma = location.LastIndexOf(',');
                if (comma >= 0)
                {
                    int.TryParse(location.Substring(comma + 1), out iconIndex);
                    location = location.Substring(0, comma);
                }
                iconFile = Environment.ExpandEnvironmentVariables(location.Trim().Trim('"'));
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (link != null && Marshal.IsComObject(link)) Marshal.ReleaseComObject(link);
                if (shell != null && Marshal.IsComObject(shell)) Marshal.ReleaseComObject(shell);
            }
        }

        // ---- A tiny "type a name" dialog -------------------------------------

        public static string Ask(IWin32Window owner, string title, string label, string initial)
        {
            using (var form = new Form())
            {
                form.AutoScaleDimensions = new SizeF(96f, 96f);
                form.AutoScaleMode = AutoScaleMode.Dpi;
                form.Text = title;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.StartPosition = FormStartPosition.CenterScreen;
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.ShowInTaskbar = false;
                form.TopMost = true;
                form.ClientSize = new Size(360, 110);

                var text = new Label { Text = label, Left = 12, Top = 12, Width = 336, AutoSize = false, Height = 20 };
                var box = new TextBox { Text = initial, Left = 12, Top = 36, Width = 336 };
                var ok = new Button { Text = "OK", Left = 192, Top = 72, Width = 75, DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "Cancel", Left = 273, Top = 72, Width = 75, DialogResult = DialogResult.Cancel };
                form.Controls.AddRange(new Control[] { text, box, ok, cancel });
                form.AcceptButton = ok;
                form.CancelButton = cancel;
                form.Shown += delegate { box.SelectAll(); box.Focus(); };

                if (form.ShowDialog(owner) != DialogResult.OK) return null;
                string result = box.Text.Trim();
                return result.Length == 0 ? null : result;
            }
        }
    }
}
