// Everything the user can change, stored in %APPDATA%\AppLauncher\settings.ini.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;

namespace AppLauncher
{
    internal sealed class AppItem
    {
        public string Name;
        public string Path;
    }

    internal sealed class Settings
    {
        public static readonly string Folder = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AppLauncher");

        public static readonly string FilePath = System.IO.Path.Combine(Folder, "settings.ini");
        public static readonly string CustomIconPath = System.IO.Path.Combine(Folder, "custom-icon.png");

        public readonly List<AppItem> Apps = new List<AppItem>();
        public Color BackColor = Color.FromArgb(32, 32, 32);
        public int TransparencyPercent = 10;   // 0 = solid, 100 = no background
        public int IconSize = 24;              // in pixels at 100% scaling; the menu text and rows scale with it
        public string ButtonIcon = "savy";     // savy, grid, dots, list, sparkle or custom

        public bool IsFirstRun { get; private set; }

        public static Settings Load()
        {
            var s = new Settings();
            if (!File.Exists(FilePath))
            {
                s.IsFirstRun = true;
                return s;
            }

            try
            {
                foreach (string raw in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    int eq = raw.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = raw.Substring(0, eq).Trim();
                    string value = raw.Substring(eq + 1);

                    switch (key)
                    {
                        case "backColor":
                            s.BackColor = ParseColor(value.Trim(), s.BackColor);
                            break;
                        case "transparency":
                            s.TransparencyPercent = ParseInt(value, s.TransparencyPercent, 0, 100);
                            break;
                        case "iconSize":
                            s.IconSize = ParseInt(value, s.IconSize, 12, 96);
                            break;
                        case "buttonIcon":
                            s.ButtonIcon = value.Trim();
                            break;
                        case "app":
                            string[] parts = value.Split('\t');
                            if (parts.Length == 2 && parts[1].Trim().Length > 0)
                                s.Apps.Add(new AppItem { Name = parts[0].Trim(), Path = parts[1].Trim() });
                            break;
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                var lines = new List<string>();
                lines.Add("backColor=" + string.Format("#{0:X2}{1:X2}{2:X2}", BackColor.R, BackColor.G, BackColor.B));
                lines.Add("transparency=" + TransparencyPercent.ToString(CultureInfo.InvariantCulture));
                lines.Add("iconSize=" + IconSize.ToString(CultureInfo.InvariantCulture));
                lines.Add("buttonIcon=" + ButtonIcon);
                foreach (AppItem app in Apps)
                    lines.Add("app=" + app.Name.Replace('\t', ' ') + "\t" + app.Path);
                File.WriteAllLines(FilePath, lines.ToArray(), Encoding.UTF8);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        /// <summary>The apps in the order they are shown: alphabetical, ignoring case.</summary>
        public List<AppItem> SortedApps()
        {
            var sorted = new List<AppItem>(Apps);
            sorted.Sort(delegate (AppItem a, AppItem b)
            {
                int c = string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
                return c != 0 ? c : string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
            });
            return sorted;
        }

        public bool ContainsPath(string path)
        {
            foreach (AppItem app in Apps)
                if (string.Equals(app.Path, path, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static int ParseInt(string text, int fallback, int min, int max)
        {
            int v;
            if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return fallback;
            return Math.Max(min, Math.Min(max, v));
        }

        private static Color ParseColor(string text, Color fallback)
        {
            if (text.Length == 7 && text[0] == '#')
            {
                int rgb;
                if (int.TryParse(text.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb))
                    return Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
            }
            return fallback;
        }
    }
}
