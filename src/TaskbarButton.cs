// The button that sits in the empty far-left part of the taskbar.
//
// Windows 11 has no API for adding buttons to the taskbar, so this is a small
// always-on-top window placed exactly over the left end of the taskbar and
// drawn with per-pixel transparency so it blends in. It keeps itself in place
// when the taskbar moves or Explorer restarts, and hides while a fullscreen
// app is running or the taskbar is auto-hidden.

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

        private readonly Timer timer;
        private readonly Image icon;
        private readonly float scale;
        private readonly uint taskbarCreatedMessage;
        private readonly NativeMethods.WinEventDelegate foregroundHookProc; // kept alive for the native hook
        private IntPtr foregroundHook;

        private IntPtr taskbar;
        private Rectangle taskbarRect;
        private bool hover, pressed, menuOpen;
        private int lastMenuClosedAt = Environment.TickCount - 10000;

        public TaskbarButton()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Text = "AppLauncher";

            using (Graphics g = CreateGraphics())
                scale = g.DpiX / 96f;

            icon = LoadButtonIcon();
            taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");

            timer = new Timer { Interval = 300 };
            timer.Tick += delegate { UpdatePlacement(); };

            foregroundHookProc = OnForegroundChanged;

            // If the window is ever closed (e.g. Alt+F4), exit instead of lingering invisibly.
            FormClosed += delegate { Application.ExitThread(); };
        }

        public void Start()
        {
            // Create the window without showing it; UpdatePlacement shows it once the taskbar is found.
            IntPtr unused = Handle;
            foregroundHook = NativeMethods.SetWinEventHook(
                NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero, foregroundHookProc, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);
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
            if (foregroundHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWinEvent(foregroundHook);
                foregroundHook = IntPtr.Zero;
            }
            if (disposing)
            {
                timer.Dispose();
                icon.Dispose();
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

        private void OnForegroundChanged(IntPtr hook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint thread, uint time)
        {
            UpdatePlacement();
            if (Visible && ShouldStayOnTop()) BringAboveTaskbar();
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

        // ---- Mouse & menus --------------------------------------------------

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

            // A click on the button while its menu is open just closes the menu.
            bool justClosed = unchecked(Environment.TickCount - lastMenuClosedAt) < 300;

            if (e.Button == MouseButtons.Left && wasPressed && !justClosed)
                ShowMenu(AppMenu.Build(scale));
            else if (e.Button == MouseButtons.Right && !justClosed)
                ShowMenu(BuildOptionsMenu());

            base.OnMouseUp(e);
        }

        private ContextMenuStrip BuildOptionsMenu()
        {
            ContextMenuStrip menu = AppMenu.NewMenu(scale);

            var edit = new ToolStripMenuItem("Edit app list...");
            edit.Click += delegate { AppMenu.EditConfig(); };
            menu.Items.Add(edit);

            var folder = new ToolStripMenuItem("Open AppLauncher folder");
            folder.Click += delegate
            {
                System.Diagnostics.Process.Start("explorer.exe", "\"" + AppDomain.CurrentDomain.BaseDirectory + "\"");
            };
            menu.Items.Add(folder);

            var autostart = new ToolStripMenuItem("Start with Windows") { Checked = Autostart.IsEnabled };
            autostart.Click += delegate
            {
                try { Autostart.Set(!Autostart.IsEnabled); }
                catch (Exception ex) { AppMenu.ShowError("Could not change the startup setting.", ex); }
            };
            menu.Items.Add(autostart);

            menu.Items.Add(new ToolStripSeparator());

            var exit = new ToolStripMenuItem("Exit AppLauncher");
            exit.Click += delegate { Application.Exit(); };
            menu.Items.Add(exit);

            return menu;
        }

        private void ShowMenu(ContextMenuStrip menu)
        {
            menuOpen = true;
            Render();

            menu.Closed += delegate
            {
                menuOpen = false;
                lastMenuClosedAt = Environment.TickCount;
                hover = Bounds.Contains(Cursor.Position);
                if (IsDisposed || !IsHandleCreated) return; // "Exit" was clicked
                Render();
                BeginInvoke((MethodInvoker)menu.Dispose);
            };

            // The menu only closes on outside clicks if our window is in the foreground.
            NativeMethods.SetForegroundWindow(Handle);

            Rectangle b = Bounds;
            Rectangle screen = Screen.FromRectangle(taskbarRect).Bounds;
            if (taskbarRect.Width >= taskbarRect.Height)
            {
                if (taskbarRect.Top > screen.Top)   // bottom taskbar
                    menu.Show(new Point(b.Left, taskbarRect.Top), ToolStripDropDownDirection.AboveRight);
                else                                // top taskbar
                    menu.Show(new Point(b.Left, taskbarRect.Bottom), ToolStripDropDownDirection.BelowRight);
            }
            else
            {
                if (taskbarRect.Left <= screen.Left) // left taskbar
                    menu.Show(new Point(taskbarRect.Right, b.Top), ToolStripDropDownDirection.BelowRight);
                else                                 // right taskbar
                    menu.Show(new Point(taskbarRect.Left, b.Top), ToolStripDropDownDirection.BelowLeft);
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

            using (var bmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    // Alpha 1 is invisible but still catches mouse clicks.
                    g.Clear(Color.FromArgb(1, 0, 0, 0));
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                    if (hover || menuOpen)
                    {
                        bool light = IsLightTaskbar();
                        int alpha = pressed ? 12 : 24;
                        Color fill = light ? Color.FromArgb(alpha, 0, 0, 0) : Color.FromArgb(alpha, 255, 255, 255);
                        int inset = Px(HoverInsetPx);
                        var area = new Rectangle(inset, inset, Width - 2 * inset, Height - 2 * inset);
                        using (GraphicsPath path = RoundedRect(area, Px(HoverRadiusPx)))
                        using (var brush = new SolidBrush(fill))
                            g.FillPath(brush, path);
                    }

                    int size = Px(pressed ? IconPx - 4 : IconPx);
                    g.DrawImage(icon, new Rectangle((Width - size) / 2, (Height - size) / 2, size, size));
                }
                Present(bmp);
            }
        }

        private void Present(Bitmap bmp)
        {
            IntPtr screenDc = NativeMethods.GetDC(IntPtr.Zero);
            IntPtr memDc = NativeMethods.CreateCompatibleDC(screenDc);
            IntPtr hBitmap = bmp.GetHbitmap(Color.FromArgb(0));
            IntPtr oldBitmap = NativeMethods.SelectObject(memDc, hBitmap);
            try
            {
                var size = new NativeMethods.SIZE { Width = bmp.Width, Height = bmp.Height };
                var source = new NativeMethods.POINT();
                var dest = new NativeMethods.POINT { X = Left, Y = Top };
                var blend = new NativeMethods.BLENDFUNCTION
                {
                    BlendOp = NativeMethods.AC_SRC_OVER,
                    SourceConstantAlpha = 255,
                    AlphaFormat = NativeMethods.AC_SRC_ALPHA,
                };
                NativeMethods.UpdateLayeredWindow(Handle, screenDc, ref dest, ref size, memDc, ref source,
                    0, ref blend, NativeMethods.ULW_ALPHA);
            }
            finally
            {
                NativeMethods.SelectObject(memDc, oldBitmap);
                NativeMethods.DeleteObject(hBitmap);
                NativeMethods.DeleteDC(memDc);
                NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
            }
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

        // Uses icon.png or icon.ico next to the exe if present, otherwise the built-in icon.
        private static Image LoadButtonIcon()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            try
            {
                string png = Path.Combine(dir, "icon.png");
                if (File.Exists(png))
                    using (var fs = File.OpenRead(png))
                    using (Image img = Image.FromStream(fs))
                        return new Bitmap(img);

                string ico = Path.Combine(dir, "icon.ico");
                if (File.Exists(ico))
                    using (var i = new Icon(ico, 256, 256))
                        return i.ToBitmap();
            }
            catch { }

            using (Stream s = typeof(TaskbarButton).Assembly.GetManifestResourceStream("launcher.png"))
            using (Image img = Image.FromStream(s))
                return new Bitmap(img);
        }
    }
}
