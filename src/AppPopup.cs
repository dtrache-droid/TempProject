// The pop-up list of apps: icon on the left, name on the right, sorted A-Z.
//
// It is a layered window drawn with per-pixel alpha, so the background colour
// can be anything from solid to fully transparent (icons and text stay opaque).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;

namespace AppLauncher
{
    internal sealed class AppPopup : Form
    {
        private readonly List<AppItem> items;
        private readonly PopupPainter painter;
        private readonly float scale;
        private readonly Timer watchdog;

        private readonly int shownAt = Environment.TickCount;
        private int scroll, hot = -1;
        private bool closing;

        public AppPopup(Settings settings, float scale, Rectangle taskbar, Rectangle button)
        {
            this.scale = scale;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            KeyPreview = true;
            Text = "AppLauncher menu";

            items = settings.SortedApps();

            Screen screen = Screen.FromRectangle(taskbar);
            Rectangle work = screen.WorkingArea;
            painter = new PopupPainter(settings, scale, items, (int)(work.Width * 0.6), work.Height - Px(16));

            Size = new Size(painter.Width, painter.Height);
            Location = Position(taskbar, button, screen);

            watchdog = new Timer { Interval = 200 };
            watchdog.Tick += delegate
            {
                // Safety net in case Deactivate never fires: close once we've lost the foreground.
                bool grace = unchecked(Environment.TickCount - shownAt) < 700;
                if (!grace && NativeMethods.GetForegroundWindow() != Handle) CloseSoon();
            };
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

        public void Open()
        {
            IntPtr unused = Handle;
            Show();
            Render();
            NativeMethods.SetForegroundWindow(Handle);
            Activate();
            watchdog.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                watchdog.Dispose();
                painter.Dispose();
            }
            base.Dispose(disposing);
        }

        private int Px(float value)
        {
            return (int)Math.Round(value * scale);
        }

        private Point Position(Rectangle taskbar, Rectangle button, Screen screen)
        {
            Rectangle bounds = screen.Bounds;
            int edge = Px(6);
            int x, y;

            if (taskbar.Width >= taskbar.Height)
            {
                x = button.Left;
                y = taskbar.Top > bounds.Top
                    ? taskbar.Top - edge - Height     // bottom taskbar: open upwards
                    : taskbar.Bottom + edge;          // top taskbar: open downwards
            }
            else
            {
                y = button.Top;
                x = taskbar.Left <= bounds.Left
                    ? taskbar.Right + edge            // left taskbar
                    : taskbar.Left - edge - Width;    // right taskbar
            }

            x = Math.Max(bounds.Left + edge, Math.Min(x, bounds.Right - Width - edge));
            y = Math.Max(bounds.Top + edge, Math.Min(y, bounds.Bottom - Height - edge));
            return new Point(x, y);
        }

        // ---- Input ----------------------------------------------------------

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            CloseSoon();
        }

        private void CloseSoon()
        {
            if (closing) return;
            closing = true;
            watchdog.Stop();
            if (IsHandleCreated) BeginInvoke((MethodInvoker)Close);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int row = painter.RowAt(e.Y, scroll);
            if (row != hot)
            {
                hot = row;
                Render();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (hot != -1)
            {
                hot = -1;
                Render();
            }
            base.OnMouseLeave(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                int row = painter.RowAt(e.Y, scroll);
                if (row >= 0) Choose(row);
            }
            else if (e.Button == MouseButtons.Right)
            {
                CloseSoon();
            }
            base.OnMouseUp(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (painter.ContentHeight > painter.ViewHeight)
            {
                scroll = Math.Max(0, Math.Min(painter.ContentHeight - painter.ViewHeight, scroll - Math.Sign(e.Delta) * painter.RowH));
                hot = painter.RowAt(PointToClient(Cursor.Position).Y, scroll);
                Render();
            }
            base.OnMouseWheel(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Escape:
                    CloseSoon();
                    return true;
                case Keys.Down:
                    MoveSelection(1);
                    return true;
                case Keys.Up:
                    MoveSelection(-1);
                    return true;
                case Keys.Home:
                    if (!painter.Empty) SelectRow(0);
                    return true;
                case Keys.End:
                    if (!painter.Empty) SelectRow(items.Count - 1);
                    return true;
                case Keys.Enter:
                    if (hot >= 0) Choose(hot);
                    return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void MoveSelection(int delta)
        {
            if (painter.Empty) return;
            int next = hot < 0 ? (delta > 0 ? 0 : items.Count - 1) : Math.Max(0, Math.Min(items.Count - 1, hot + delta));
            SelectRow(next);
        }

        private void SelectRow(int row)
        {
            hot = row;
            int top = row * painter.RowH, bottom = top + painter.RowH;
            if (top < scroll) scroll = top;
            else if (bottom > scroll + painter.ViewHeight) scroll = bottom - painter.ViewHeight;
            Render();
        }

        private void Choose(int row)
        {
            string path = items[row].Path;
            closing = true;
            watchdog.Stop();
            Close();
            Shell.Launch(path);
        }

        private void Render()
        {
            if (!IsHandleCreated || closing) return;
            using (Bitmap bmp = painter.Render(hot, scroll))
                Layered.Present(Handle, bmp, Location);
        }
    }
}
