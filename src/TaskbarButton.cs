// The button that sits in the empty far-left part of the taskbar.
//
// Windows 11 has no API for adding buttons to the taskbar, so this is a small
// always-on-top window placed exactly over the left end of the taskbar and
// drawn with per-pixel transparency so it blends in. It keeps itself in place
// when the taskbar moves or Explorer restarts, and hides while a fullscreen
// app is running or the taskbar is auto-hidden.
//
// Left-click opens the app list (AppPopup); right-click opens the settings window.

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

        private readonly Settings settings;
        private readonly Timer timer;
        private readonly float scale;
        private readonly uint taskbarCreatedMessage;

        private Image icon;
        private bool iconIsMask;
        private AppPopup popup;
        private SettingsForm settingsForm;

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
                    ShowSettings();
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

        // ---- The settings window (right-click) -----------------------------

        private void ShowSettings()
        {
            if (settingsForm != null && !settingsForm.IsDisposed)
            {
                if (settingsForm.WindowState == FormWindowState.Minimized)
                    settingsForm.WindowState = FormWindowState.Normal;
                settingsForm.Activate();
                return;
            }

            settingsForm = new SettingsForm(settings);
            settingsForm.ButtonIconChanged += delegate { LoadButtonIcon(); Render(); };
            settingsForm.ExitRequested += delegate { Application.Exit(); };
            settingsForm.FormClosed += delegate
            {
                settingsForm.Dispose();
                settingsForm = null;
                Shell.ClearIconCache();
            };
            settingsForm.Show();
            settingsForm.Activate();
        }

        // Loads the icon chosen in the settings; falls back to the Savy S if something is wrong.
        private void LoadButtonIcon()
        {
            bool mask;
            Image loaded = ButtonIcons.LoadCurrent(settings, out mask);

            Image old = icon;
            icon = loaded;
            iconIsMask = mask;
            if (old != null) old.Dispose();
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
                    using (ImageAttributes attrs = iconIsMask ? ButtonIcons.Tint(light ? 0.15f : 1f) : null)
                        ButtonIcons.Draw(g, icon, target, attrs);
                }
                Layered.Present(Handle, bmp, Location);
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
    }
}
