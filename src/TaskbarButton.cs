// The button that sits in the empty far-left part of the taskbar.
//
// Windows 11 has no API for adding buttons to the taskbar, so this is a small
// always-on-top window placed exactly over the left end of the taskbar and
// drawn with per-pixel transparency so it blends in. It keeps itself in place
// when the taskbar moves or Explorer restarts, and hides while a fullscreen
// app is running or the taskbar is auto-hidden.
//
// Left-click opens the app list (AppPopup); right-click opens the settings menu.

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AppLauncher
{
    internal sealed class TaskbarButton : Form
    {
        private const int MarginPx = 4;      // gap between the taskbar's left edge and the button (at 100% scaling)
        private const int IconPx = 24;       // same icon size Windows uses on the taskbar
        private const int HoverInsetPx = 4;
        private const int HoverRadiusPx = 4;

        // id, menu label, tinted to match the taskbar (true) or drawn as-is (false)
        private static readonly object[][] BuiltInIcons =
        {
            new object[] { "savy",    "Savy S",      false },
            new object[] { "grid",    "Color tiles", false },
            new object[] { "dots",    "Dots",        true  },
            new object[] { "list",    "List",        true  },
            new object[] { "sparkle", "Sparkle",     true  },
        };

        private readonly Settings settings;
        private readonly Timer timer;
        private readonly float scale;
        private readonly uint taskbarCreatedMessage;

        private Image icon;
        private bool iconIsMask;
        private AppPopup popup;

        private IntPtr taskbar;
        private Rectangle taskbarRect;
        private bool hover, pressed, menuOpen, suppressClick;
        private int lastMenuClosedAt = Environment.TickCount - 10000;

        public TaskbarButton(Settings settings)
        {
            this.settings = settings;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Text = "AppLauncher";

            using (Graphics g = CreateGraphics())
                scale = g.DpiX / 96f;

            LoadButtonIcon();
            taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");

            timer = new Timer { Interval = 150 };
            timer.Tick += delegate { UpdatePlacement(); };

            // If the window is ever closed (e.g. Alt+F4), exit instead of lingering invisibly.
            FormClosed += delegate { Application.ExitThread(); };
        }

        public void Start()
        {
            // Create the window without showing it; UpdatePlacement shows it once the taskbar is found.
            IntPtr unused = Handle;
            UpdatePlacement();
            timer.Start();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.Style = NativeMethods.WS_POPUP;
                cp.ExStyle |= NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose();
                if (icon != null) icon.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == taskbarCreatedMessage)
            {
                // Explorer restarted: find the new taskbar.
                taskbar = IntPtr.Zero;
                taskbarRect = Rectangle.Empty;
                UpdatePlacement();
            }
            else if (m.Msg == NativeMethods.WM_DISPLAYCHANGE || m.Msg == NativeMethods.WM_SETTINGCHANGE)
            {
                taskbarRect = Rectangle.Empty; // force re-layout (and re-read the light/dark theme)
                UpdatePlacement();
            }
            base.WndProc(ref m);
        }

        // ---- Placement ------------------------------------------------------

        private void UpdatePlacement()
        {
            if (taskbar == IntPtr.Zero || !NativeMethods.IsWindow(taskbar))
                taskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);

            Rectangle tb = Rectangle.Empty;
            bool show = false;

            if (taskbar != IntPtr.Zero && NativeMethods.IsWindowVisible(taskbar))
            {
                NativeMethods.RECT r;
                if (NativeMethods.GetWindowRect(taskbar, out r))
                {
                    tb = r.ToRectangle();
                    Rectangle visible = Rectangle.Intersect(tb, Screen.FromRectangle(tb).Bounds);
                    bool horizontal = tb.Width >= tb.Height;
                    bool fullyShown = horizontal
                        ? visible.Height >= tb.Height - 2
                        : visible.Width >= tb.Width - 2;
                    show = tb.Width > 0 && tb.Height > 0 && fullyShown && !IsFullscreenAppActive();
                }
            }

            if (!show)
            {
                if (Visible && !menuOpen) Hide();
                return;
            }

            if (tb != taskbarRect || !Visible)
            {
                taskbarRect = tb;
                int margin = Px(MarginPx);
                Bounds = tb.Width >= tb.Height
                    ? new Rectangle(tb.Left + margin, tb.Top, tb.Height, tb.Height)   // bottom/top taskbar: far left
                    : new Rectangle(tb.Left, tb.Top + margin, tb.Width, tb.Width);   // side taskbar: very top
                if (!Visible) Show();
                Render();
                BringAboveTaskbar();
            }
            else if (ShouldStayOnTop())
            {
                // Clicking the taskbar (or opening Start) raises it over us; step back in front.
                BringAboveTaskbar();
            }
        }

        // Stay in front of the taskbar, but don't fight a focused window that
        // covers our spot (Task View, a window dragged over the taskbar, ...).
        private bool ShouldStayOnTop()
        {
            IntPtr fg = NativeMethods.GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == Handle || fg == taskbar) return true;

            string cls = NativeMethods.GetClassName(fg);
            if (cls == "Progman" || cls == "WorkerW" || cls.StartsWith("Shell_")) return true;

            NativeMethods.RECT r;
            return !NativeMethods.GetWindowRect(fg, out r) || !r.ToRectangle().IntersectsWith(Bounds);
        }

        private void BringAboveTaskbar()
        {
            NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE |
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER);
        }

        private bool IsFullscreenAppActive()
        {
            int state;
            if (NativeMethods.SHQueryUserNotificationState(out state) == 0 &&
                (state == NativeMethods.QUNS_BUSY || state == NativeMethods.QUNS_RUNNING_D3D_FULL_SCREEN))
                return true;

            IntPtr fg = NativeMethods.GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == Handle || fg == taskbar) return false;

            string cls = NativeMethods.GetClassName(fg);
            if (cls == "Progman" || cls == "WorkerW" || cls.StartsWith("Shell_")) return false;

            NativeMethods.RECT r;
            if (!NativeMethods.GetWindowRect(fg, out r)) return false;
            Rectangle screen = Screen.FromHandle(fg).Bounds;
            return r.Left <= screen.Left && r.Top <= screen.Top &&
                   r.Right >= screen.Right && r.Bottom >= screen.Bottom;
        }

        // ---- Mouse ----------------------------------------------------------

        protected override void OnMouseEnter(EventArgs e)
        {
            hover = true;
            Render();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover = false;
            pressed = false;
            Render();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            // Clicking the button while the list is open first closes the list (it loses focus);
            // remember that so the same click doesn't immediately reopen it.
            suppressClick = unchecked(Environment.TickCount - lastMenuClosedAt) < 400;

            if (e.Button == MouseButtons.Left)
            {
                pressed = true;
                Render();
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            bool wasPressed = pressed;
            pressed = false;
            Render();

            if (!suppressClick)
            {
                if (e.Button == MouseButtons.Left && wasPressed)
                    ShowPopup();
                else if (e.Button == MouseButtons.Right)
                    ShowSettingsMenu();
            }
            suppressClick = false;

            base.OnMouseUp(e);
        }

        // ---- The app list ---------------------------------------------------

        private void ShowPopup()
        {
            if (popup != null) return;

            popup = new AppPopup(settings, scale, taskbarRect, Bounds);
            menuOpen = true;
            popup.FormClosed += delegate
            {
                popup.Dispose();
                popup = null;
                MenuClosed();
            };
            Render();
            popup.Open();
        }

        private void MenuClosed()
        {
            menuOpen = false;
            lastMenuClosedAt = Environment.TickCount;
            hover = Bounds.Contains(Cursor.Position);
            if (!IsDisposed && IsHandleCreated) Render();
        }

        // ---- The settings menu (right-click) -------------------------------

        private ContextMenuStrip NewMenu()
        {
            int thumb = Px(20);
            return new ContextMenuStrip { ImageScalingSize = new Size(thumb, thumb) };
        }

        private void ShowSettingsMenu()
        {
            if (popup != null) return;

            ContextMenuStrip menu = NewMenu();

            var autostart = new ToolStripMenuItem("Start with Windows") { Checked = Autostart.IsEnabled };
            autostart.Click += delegate
            {
                try { Autostart.Set(!Autostart.IsEnabled); }
                catch (Exception ex) { Shell.ShowError("Could not change the startup setting.", ex); }
            };
            menu.Items.Add(autostart);
            menu.Items.Add(new ToolStripSeparator());

            // Apps
            var add = new ToolStripMenuItem("Add app...");
            add.Click += delegate { Later(AddApps); };
            menu.Items.Add(add);
            menu.Items.Add(AppSubmenu("Rename app", delegate (AppItem a) { Later(delegate { RenameApp(a); }); }));
            menu.Items.Add(AppSubmenu("Remove app", RemoveApp));
            menu.Items.Add(new ToolStripSeparator());

            // Look of the list
            var color = new ToolStripMenuItem("Background color...");
            color.Click += delegate { Later(PickBackColor); };
            menu.Items.Add(color);

            var transparency = new ToolStripMenuItem("Background transparency");
            for (int p = 0; p <= 100; p += 10)
            {
                int percent = p;
                string label = p == 0 ? "0% (solid)" : p == 100 ? "100% (no background)" : p + "%";
                var item = new ToolStripMenuItem(label) { Checked = settings.TransparencyPercent == percent };
                item.Click += delegate { settings.TransparencyPercent = percent; settings.Save(); };
                transparency.DropDownItems.Add(item);
            }
            menu.Items.Add(transparency);

            var sizes = new ToolStripMenuItem("Icon and menu size");
            foreach (int s in Settings.IconSizes)
            {
                int size = s;
                var item = new ToolStripMenuItem(size + " px") { Checked = settings.IconSize == size };
                item.Click += delegate
                {
                    settings.IconSize = size;
                    settings.Save();
                    Shell.ClearIconCache();
                };
                sizes.DropDownItems.Add(item);
            }
            menu.Items.Add(sizes);
            menu.Items.Add(new ToolStripSeparator());

            // Taskbar button icon
            var icons = new ToolStripMenuItem("Button icon");
            foreach (object[] built in BuiltInIcons)
            {
                string id = (string)built[0];
                var item = new ToolStripMenuItem((string)built[1])
                {
                    Checked = settings.ButtonIcon == id,
                    Image = MenuThumbnail(id),
                };
                item.Click += delegate { SetButtonIcon(id); };
                icons.DropDownItems.Add(item);
            }
            icons.DropDownItems.Add(new ToolStripSeparator());
            if (File.Exists(Settings.CustomIconPath))
            {
                var mine = new ToolStripMenuItem("My own icon")
                {
                    Checked = settings.ButtonIcon == "custom",
                    Image = MenuThumbnail("custom"),
                };
                mine.Click += delegate { SetButtonIcon("custom"); };
                icons.DropDownItems.Add(mine);
            }
            var choose = new ToolStripMenuItem("Choose an image...");
            choose.Click += delegate { Later(ChooseCustomIcon); };
            icons.DropDownItems.Add(choose);
            menu.Items.Add(icons);
            menu.Items.Add(new ToolStripSeparator());

            var exit = new ToolStripMenuItem("Exit AppLauncher");
            exit.Click += delegate { Application.Exit(); };
            menu.Items.Add(exit);

            menuOpen = true;
            Render();
            menu.Closed += delegate
            {
                if (IsDisposed || !IsHandleCreated) return; // "Exit" was clicked
                MenuClosed();
                BeginInvoke((MethodInvoker)menu.Dispose);
            };

            // The menu only closes on outside clicks if our window is in the foreground.
            NativeMethods.SetForegroundWindow(Handle);
            Point at = MenuAnchor();
            menu.Show(at, taskbarRect.Width >= taskbarRect.Height && taskbarRect.Top > Screen.FromRectangle(taskbarRect).Bounds.Top
                ? ToolStripDropDownDirection.AboveRight
                : ToolStripDropDownDirection.BelowRight);
        }

        private Point MenuAnchor()
        {
            Rectangle screen = Screen.FromRectangle(taskbarRect).Bounds;
            if (taskbarRect.Width >= taskbarRect.Height)
                return new Point(Bounds.Left, taskbarRect.Top > screen.Top ? taskbarRect.Top : taskbarRect.Bottom);
            return new Point(taskbarRect.Left <= screen.Left ? taskbarRect.Right : taskbarRect.Left, Bounds.Top);
        }

        private ToolStripMenuItem AppSubmenu(string title, Action<AppItem> onPick)
        {
            var parent = new ToolStripMenuItem(title);
            foreach (AppItem app in settings.SortedApps())
            {
                AppItem picked = app;
                var item = new ToolStripMenuItem(app.Name.Replace("&", "&&"));
                item.Click += delegate { onPick(picked); };
                parent.DropDownItems.Add(item);
            }
            if (parent.DropDownItems.Count == 0)
                parent.DropDownItems.Add(new ToolStripMenuItem("(no apps yet)") { Enabled = false });
            return parent;
        }

        // Runs after the menu has finished closing (dialogs opened from inside a click look wrong).
        private void Later(MethodInvoker action)
        {
            BeginInvoke(action);
        }

        private void AddApps()
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "Choose the apps to add";
                dlg.Filter = "Apps and shortcuts (*.exe;*.lnk)|*.exe;*.lnk|All files (*.*)|*.*";
                dlg.Multiselect = true;
                dlg.DereferenceLinks = false;
                string startMenu = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
                if (Directory.Exists(startMenu)) dlg.InitialDirectory = startMenu;

                NativeMethods.SetForegroundWindow(Handle);
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                foreach (string path in dlg.FileNames)
                {
                    if (settings.ContainsPath(path)) continue;
                    settings.Apps.Add(new AppItem { Name = Shell.DefaultName(path), Path = path });
                }
                settings.Save();
            }
        }

        private void RenameApp(AppItem app)
        {
            NativeMethods.SetForegroundWindow(Handle);
            string name = Shell.Ask(this, "Rename app", "Name shown in the list:", app.Name);
            if (name == null) return;
            app.Name = name;
            settings.Save();
        }

        private void RemoveApp(AppItem app)
        {
            settings.Apps.Remove(app);
            settings.Save();
        }

        private void PickBackColor()
        {
            using (var dlg = new ColorDialog())
            {
                dlg.AnyColor = true;
                dlg.FullOpen = true;
                dlg.Color = settings.BackColor;
                NativeMethods.SetForegroundWindow(Handle);
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                settings.BackColor = dlg.Color;
                settings.Save();
            }
        }

        // ---- Button icon ----------------------------------------------------

        private void SetButtonIcon(string id)
        {
            settings.ButtonIcon = id;
            settings.Save();
            LoadButtonIcon();
            Render();
        }

        private void ChooseCustomIcon()
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "Choose an icon for the taskbar button";
                dlg.Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico";
                NativeMethods.SetForegroundWindow(Handle);
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    Bitmap square;
                    if (Path.GetExtension(dlg.FileName).ToLowerInvariant() == ".ico")
                    {
                        using (var ico = new Icon(dlg.FileName, 256, 256))
                            square = Shell.Fit(ico.ToBitmap(), 256);
                    }
                    else
                    {
                        using (Image img = Image.FromFile(dlg.FileName))
                            square = Shell.Fit(new Bitmap(img), 256);
                    }

                    Directory.CreateDirectory(Settings.Folder);
                    using (square)
                    using (var clean = new Bitmap(square.Width, square.Height, PixelFormat.Format32bppArgb))
                    {
                        using (Graphics g = Graphics.FromImage(clean))
                            g.DrawImage(square, 0, 0, square.Width, square.Height);
                        clean.Save(Settings.CustomIconPath, ImageFormat.Png);
                    }
                    SetButtonIcon("custom");
                }
                catch (Exception ex)
                {
                    Shell.ShowError("Could not use that image.", ex);
                }
            }
        }

        // Loads the current button icon; falls back to the Savy S if something is wrong.
        private void LoadButtonIcon()
        {
            Image loaded = null;
            bool mask = false;
            string id = settings.ButtonIcon;

            if (id == "custom")
            {
                try
                {
                    using (var fs = File.OpenRead(Settings.CustomIconPath))
                    using (Image img = Image.FromStream(fs))
                        loaded = new Bitmap(img);
                }
                catch { loaded = null; }
            }
            else
            {
                loaded = LoadBuiltIn(id, out mask);
            }

            if (loaded == null)
            {
                settings.ButtonIcon = "savy";
                loaded = LoadBuiltIn("savy", out mask);
            }

            Image old = icon;
            icon = loaded;
            iconIsMask = mask;
            if (old != null) old.Dispose();
        }

        private static Image LoadBuiltIn(string id, out bool mask)
        {
            mask = false;
            foreach (object[] built in BuiltInIcons)
            {
                if ((string)built[0] != id) continue;
                mask = (bool)built[2];
                using (var s = typeof(TaskbarButton).Assembly.GetManifestResourceStream("icon-" + id + ".png"))
                {
                    if (s == null) return null;
                    using (Image img = Image.FromStream(s))
                        return new Bitmap(img);
                }
            }
            return null;
        }

        // Small preview for the "Button icon" menu (the menu itself is always light).
        private Image MenuThumbnail(string id)
        {
            int px = Px(20);
            try
            {
                Image full;
                bool mask = false;
                if (id == "custom")
                {
                    using (var fs = File.OpenRead(Settings.CustomIconPath))
                    using (Image img = Image.FromStream(fs))
                        full = new Bitmap(img);
                }
                else
                {
                    full = LoadBuiltIn(id, out mask);
                }
                if (full == null) return null;

                var thumb = new Bitmap(px, px, PixelFormat.Format32bppPArgb);
                using (full)
                using (Graphics g = Graphics.FromImage(thumb))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    using (ImageAttributes attrs = mask ? Tint(0.15f) : null)
                        DrawIcon(g, full, new Rectangle(0, 0, px, px), attrs);
                }
                return thumb;
            }
            catch
            {
                return null;
            }
        }

        // ---- Drawing --------------------------------------------------------

        private int Px(int px)
        {
            return (int)Math.Round(px * scale);
        }

        private void Render()
        {
            if (!IsHandleCreated || Width <= 0 || Height <= 0) return;

            using (var bmp = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    // Alpha 1 is invisible but still catches mouse clicks.
                    g.Clear(Color.FromArgb(1, 0, 0, 0));
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                    bool light = IsLightTaskbar();

                    if (hover || menuOpen)
                    {
                        int alpha = pressed ? 12 : 24;
                        Color fill = light ? Color.FromArgb(alpha, 0, 0, 0) : Color.FromArgb(alpha, 255, 255, 255);
                        int inset = Px(HoverInsetPx);
                        var area = new Rectangle(inset, inset, Width - 2 * inset, Height - 2 * inset);
                        using (GraphicsPath path = RoundedRect(area, Px(HoverRadiusPx)))
                        using (var brush = new SolidBrush(fill))
                            g.FillPath(brush, path);
                    }

                    int size = Px(pressed ? IconPx - 4 : IconPx);
                    var target = new Rectangle((Width - size) / 2, (Height - size) / 2, size, size);
                    using (ImageAttributes attrs = iconIsMask ? Tint(light ? 0.15f : 1f) : null)
                        DrawIcon(g, icon, target, attrs);
                }
                Layered.Present(Handle, bmp, Location);
            }
        }

        private static void DrawIcon(Graphics g, Image image, Rectangle target, ImageAttributes attrs)
        {
            if (attrs == null)
                g.DrawImage(image, target);
            else
                g.DrawImage(image, target, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attrs);
        }

        // Recolours a white-on-transparent mask: t = 1 keeps it white, small t makes it dark.
        private static ImageAttributes Tint(float t)
        {
            var matrix = new ColorMatrix(new float[][]
            {
                new float[] { t, 0, 0, 0, 0 },
                new float[] { 0, t, 0, 0, 0 },
                new float[] { 0, 0, t, 0, 0 },
                new float[] { 0, 0, 0, 1, 0 },
                new float[] { 0, 0, 0, 0, 1 },
            });
            var attrs = new ImageAttributes();
            attrs.SetColorMatrix(matrix);
            return attrs;
        }

        private static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            int d = Math.Max(1, radius * 2);
            var path = new GraphicsPath();
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static bool IsLightTaskbar()
        {
            try
            {
                object value = Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "SystemUsesLightTheme", 0);
                return value is int && (int)value == 1;
            }
            catch
            {
                return false;
            }
        }
    }
}
