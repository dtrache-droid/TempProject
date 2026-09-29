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
        private readonly List<Image> icons = new List<Image>();
        private readonly Settings settings;
        private readonly float scale;
        private readonly Font font;
        private readonly Timer watchdog;

        private readonly int iconPx, rowPad, hPad, gap, rowH, outerPad, radius;
        private readonly int viewHeight, contentHeight;
        private readonly bool empty;
        private readonly string emptyText = "No apps yet - right-click the button and choose \"Add app...\"";

        private readonly int shownAt = Environment.TickCount;
        private int scroll, hot = -1;
        private bool closing;

        public AppPopup(Settings settings, float scale, Rectangle taskbar, Rectangle button)
        {
            this.settings = settings;
            this.scale = scale;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            KeyPreview = true;
            Text = "AppLauncher menu";

            items = settings.SortedApps();
            empty = items.Count == 0;

            int size = settings.IconSize;
            iconPx = Px(size);
            rowPad = Px(Math.Max(5, size * 0.22f));
            hPad = rowPad + Px(4);
            gap = Px(Math.Max(8, size * 0.4f));
            outerPad = Px(6);
            radius = Px(8);
            rowH = iconPx + 2 * rowPad;
            font = new Font("Segoe UI", Math.Max(12f, size * 0.5f) * scale, FontStyle.Regular, GraphicsUnit.Pixel);

            Screen screen = Screen.FromRectangle(taskbar);
            Rectangle work = screen.WorkingArea;

            // Measure the widest name.
            int textWidth = 0;
            using (var probe = new Bitmap(1, 1))
            using (Graphics g = Graphics.FromImage(probe))
            using (StringFormat fmt = TextFormat())
            {
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                if (empty)
                {
                    textWidth = (int)Math.Ceiling(g.MeasureString(emptyText, font, 10000, fmt).Width);
                }
                else
                {
                    foreach (AppItem item in items)
                    {
                        textWidth = Math.Max(textWidth, (int)Math.Ceiling(g.MeasureString(item.Name, font, 10000, fmt).Width));
                        icons.Add(Shell.GetAppIcon(item.Path, iconPx));
                    }
                }
            }

            int width = empty
                ? textWidth + 2 * hPad
                : textWidth + iconPx + gap + 2 * hPad;
            width = Math.Max(width, Px(160));
            width = Math.Min(width, (int)(work.Width * 0.6));

            int rows = empty ? 1 : items.Count;
            contentHeight = rows * rowH;
            viewHeight = Math.Min(contentHeight, work.Height - 2 * outerPad - Px(16));
            Size = new Size(width, viewHeight + 2 * outerPad);

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
                font.Dispose();
            }
            base.Dispose(disposing);
        }

        private int Px(float value)
        {
            return (int)Math.Round(value * scale);
        }

        private static StringFormat TextFormat()
        {
            var fmt = new StringFormat(StringFormat.GenericTypographic);
            fmt.FormatFlags |= StringFormatFlags.NoWrap;
            fmt.Trimming = StringTrimming.EllipsisCharacter;
            fmt.LineAlignment = StringAlignment.Center;
            return fmt;
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

        private int RowAt(int y)
        {
            if (empty) return -1;
            int i = (y - outerPad + scroll) / rowH;
            if (y < outerPad || y >= outerPad + viewHeight || i < 0 || i >= items.Count) return -1;
            return i;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int row = RowAt(e.Y);
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
                int row = RowAt(e.Y);
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
            if (contentHeight > viewHeight)
            {
                scroll = Math.Max(0, Math.Min(contentHeight - viewHeight, scroll - Math.Sign(e.Delta) * rowH));
                hot = RowAt(PointToClient(Cursor.Position).Y);
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
                    if (!empty) SelectRow(0);
                    return true;
                case Keys.End:
                    if (!empty) SelectRow(items.Count - 1);
                    return true;
                case Keys.Enter:
                    if (hot >= 0) Choose(hot);
                    return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void MoveSelection(int delta)
        {
            if (empty) return;
            int next = hot < 0 ? (delta > 0 ? 0 : items.Count - 1) : Math.Max(0, Math.Min(items.Count - 1, hot + delta));
            SelectRow(next);
        }

        private void SelectRow(int row)
        {
            hot = row;
            int top = row * rowH, bottom = top + rowH;
            if (top < scroll) scroll = top;
            else if (bottom > scroll + viewHeight) scroll = bottom - viewHeight;
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

        // ---- Drawing --------------------------------------------------------

        private void Render()
        {
            if (!IsHandleCreated || closing) return;

            Color back = settings.BackColor;
            double transparency = settings.TransparencyPercent / 100.0;
            int panelAlpha = Math.Max(1, (int)Math.Round(255 * (1 - transparency))); // alpha 1 keeps the panel clickable
            bool lightBack = (0.299 * back.R + 0.587 * back.G + 0.114 * back.B) > 150;
            Color textColor = lightBack ? Color.FromArgb(28, 28, 28) : Color.FromArgb(245, 245, 245);
            Color shadowColor = lightBack ? Color.FromArgb(150, 255, 255, 255) : Color.FromArgb(150, 0, 0, 0);

            using (var bmp = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb))
            using (Graphics g = Graphics.FromImage(bmp))
            using (StringFormat fmt = TextFormat())
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = TextRenderingHint.AntiAlias;

                var panel = new Rectangle(0, 0, Width - 1, Height - 1);
                using (GraphicsPath path = RoundedRect(panel, radius))
                {
                    using (var brush = new SolidBrush(Color.FromArgb(panelAlpha, back)))
                        g.FillPath(brush, path);

                    int borderAlpha = (int)Math.Round(70 * (1 - transparency));
                    if (borderAlpha > 2)
                        using (var pen = new Pen(Color.FromArgb(borderAlpha, lightBack ? 0 : 255, lightBack ? 0 : 255, lightBack ? 0 : 255), 1f))
                            g.DrawPath(pen, path);
                }

                g.SetClip(new Rectangle(0, outerPad, Width, viewHeight));

                if (empty)
                {
                    var r = new RectangleF(hPad, outerPad, Width - 2 * hPad, rowH);
                    DrawText(g, emptyText, r, fmt, Color.FromArgb(170, textColor), shadowColor, transparency);
                }
                else
                {
                    for (int i = 0; i < items.Count; i++)
                    {
                        int y = outerPad + i * rowH - scroll;
                        if (y + rowH < outerPad || y > outerPad + viewHeight) continue;

                        if (i == hot)
                        {
                            var hl = new Rectangle(Px(4), y + Px(1), Width - 2 * Px(4), rowH - Px(2));
                            using (GraphicsPath hp = RoundedRect(hl, Px(6)))
                            using (var hb = new SolidBrush(lightBack ? Color.FromArgb(36, 0, 0, 0) : Color.FromArgb(40, 255, 255, 255)))
                                g.FillPath(hb, hp);
                        }

                        g.DrawImage(icons[i], new Rectangle(hPad, y + rowPad, iconPx, iconPx));

                        int tx = hPad + iconPx + gap;
                        var tr = new RectangleF(tx, y, Width - tx - hPad, rowH);
                        DrawText(g, items[i].Name, tr, fmt, textColor, shadowColor, transparency);
                    }
                }

                Layered.Present(Handle, bmp, Location);
            }
        }

        // With a see-through background the text may sit on anything, so give it a soft outline.
        private void DrawText(Graphics g, string text, RectangleF r, StringFormat fmt, Color color, Color shadow, double transparency)
        {
            if (transparency >= 0.3)
            {
                int o = Math.Max(1, Px(1));
                using (var sb = new SolidBrush(shadow))
                {
                    g.DrawString(text, font, sb, new RectangleF(r.X + o, r.Y + o, r.Width, r.Height), fmt);
                    g.DrawString(text, font, sb, new RectangleF(r.X - o, r.Y, r.Width, r.Height), fmt);
                    g.DrawString(text, font, sb, new RectangleF(r.X, r.Y - o, r.Width, r.Height), fmt);
                }
            }
            using (var brush = new SolidBrush(color))
                g.DrawString(text, font, brush, r, fmt);
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
    }
}
