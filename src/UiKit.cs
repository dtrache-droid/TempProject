// Small custom-drawn controls used by the settings window, so it looks like a
// modern Windows 11 page instead of a stack of grey dialog boxes.
// (Pixel sizes below are multiplied by the screen scale, s = DPI / 96.)

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;

namespace AppLauncher
{
    internal static class Theme
    {
        public static readonly Color Accent = Color.FromArgb(0, 103, 192);
        public static readonly Color AccentHover = Color.FromArgb(25, 118, 210);
        public static readonly Color AccentDown = Color.FromArgb(0, 84, 160);
        public static readonly Color PageBack = Color.FromArgb(249, 249, 250);
        public static readonly Color NavBack = Color.FromArgb(238, 238, 243);
        public static readonly Color Border = Color.FromArgb(222, 222, 228);
        public static readonly Color Text = Color.FromArgb(28, 28, 30);
        public static readonly Color Muted = Color.FromArgb(104, 104, 112);

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            float d = Math.Max(1f, radius * 2);
            var path = new GraphicsPath();
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void Quality(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        }

        public static float ScaleOf(Control c)
        {
            using (Graphics g = c.CreateGraphics())
                return g.DpiX / 96f;
        }

        public static StringFormat Line()
        {
            var fmt = new StringFormat(StringFormat.GenericTypographic);
            fmt.FormatFlags |= StringFormatFlags.NoWrap;
            fmt.Trimming = StringTrimming.EllipsisCharacter;
            fmt.LineAlignment = StringAlignment.Center;
            return fmt;
        }
    }

    /// <summary>The Windows icon font (Segoe Fluent Icons on Windows 11, Segoe MDL2 Assets on Windows 10).</summary>
    internal static class IconFont
    {
        public static readonly string Family = Detect();

        private static string Detect()
        {
            try
            {
                using (var fonts = new InstalledFontCollection())
                {
                    foreach (string wanted in new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" })
                        foreach (FontFamily f in fonts.Families)
                            if (f.Name == wanted) return wanted;
                }
            }
            catch { }
            return null;
        }

        public static Font Create(float px)
        {
            return Family == null ? null : new Font(Family, px, FontStyle.Regular, GraphicsUnit.Pixel);
        }

        public const string Add = "", Edit = "", Delete = "", Folder = "",
            Apps = "", Color = "", Picture = "", Settings = "",
            Power = "", Refresh = "", Check = "";
    }

    /// <summary>A rounded button with an icon glyph. Primary = filled with the accent color.</summary>
    internal sealed class IconButton : Button
    {
        public string Glyph;
        public bool Primary;
        private bool hover, down;

        public IconButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI", 9.5f);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            float s = g.DpiX / 96f;
            Theme.Quality(g);
            g.Clear(Parent != null ? Parent.BackColor : SystemColors.Control);

            Color bg, fg, border;
            if (!Enabled) { bg = Color.FromArgb(240, 240, 243); fg = Color.FromArgb(165, 165, 170); border = Color.FromArgb(228, 228, 232); }
            else if (Primary)
            {
                bg = down ? Theme.AccentDown : hover ? Theme.AccentHover : Theme.Accent;
                fg = Color.White;
                border = bg;
            }
            else
            {
                bg = down ? Color.FromArgb(232, 232, 236) : hover ? Color.FromArgb(243, 243, 246) : Color.White;
                fg = Theme.Text;
                border = Color.FromArgb(206, 206, 212);
            }

            var rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            using (GraphicsPath path = Theme.Round(rect, 6 * s))
            {
                using (var brush = new SolidBrush(bg)) g.FillPath(brush, path);
                using (var pen = new Pen(border, 1f)) g.DrawPath(pen, path);
            }

            using (Font glyphFont = string.IsNullOrEmpty(Glyph) ? null : IconFont.Create(15 * s))
            using (StringFormat fmt = Theme.Line())
            using (var brush = new SolidBrush(fg))
            {
                float glyphW = glyphFont == null ? 0 : 20 * s;
                float textW = g.MeasureString(Text, Font, 10000, fmt).Width;
                float gap = glyphFont != null && Text.Length > 0 ? 6 * s : 0;
                float x = Math.Max(8 * s, (Width - (glyphW + gap + textW)) / 2f);

                if (glyphFont != null)
                    g.DrawString(Glyph, glyphFont, brush, new RectangleF(x, 0, glyphW, Height), fmt);
                g.DrawString(Text, Font, brush, new RectangleF(x + glyphW + gap, 0, Width - x - glyphW - gap - 4 * s, Height), fmt);
            }

            if (Focused && ShowFocusCues)
                using (GraphicsPath ring = Theme.Round(new RectangleF(2 * s, 2 * s, Width - 4 * s, Height - 4 * s), 5 * s))
                using (var pen = new Pen(Color.FromArgb(Primary ? 200 : 160, Theme.Accent), 1.5f))
                    g.DrawPath(pen, ring);
        }
    }

    /// <summary>A white rounded panel with a thin border.</summary>
    internal sealed class Card : Panel
    {
        public Card()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            float s = g.DpiX / 96f;
            Theme.Quality(g);
            g.Clear(Parent != null ? Parent.BackColor : Theme.PageBack);
            using (GraphicsPath path = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), 8 * s))
            {
                using (var brush = new SolidBrush(Color.White)) g.FillPath(brush, path);
                using (var pen = new Pen(Theme.Border, 1f)) g.DrawPath(pen, path);
            }
        }
    }

    /// <summary>A round color swatch. IsCustom draws a rainbow "pick your own" swatch.</summary>
    internal sealed class Swatch : Control
    {
        public Color Value;
        public bool IsCustom;
        public bool Selected;

        public Swatch()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            float s = g.DpiX / 96f;
            Theme.Quality(g);
            g.Clear(Parent != null ? Parent.BackColor : Theme.PageBack);

            float pad = 4 * s;
            var disc = new RectangleF(pad, pad, Width - 2 * pad, Height - 2 * pad);

            if (IsCustom)
            {
                Color[] wheel = { Color.FromArgb(239, 68, 68), Color.FromArgb(245, 158, 11), Color.FromArgb(34, 197, 94), Color.FromArgb(59, 130, 246) };
                for (int i = 0; i < 4; i++)
                    using (var brush = new SolidBrush(wheel[i]))
                        g.FillPie(brush, Rectangle.Round(disc), i * 90, 90);
                using (var brush = new SolidBrush(Color.White))
                    g.FillEllipse(brush, disc.X + disc.Width * 0.3f, disc.Y + disc.Height * 0.3f, disc.Width * 0.4f, disc.Height * 0.4f);
            }
            else
            {
                using (var brush = new SolidBrush(Value)) g.FillEllipse(brush, disc);
            }
            using (var pen = new Pen(Color.FromArgb(70, 0, 0, 0), 1f)) g.DrawEllipse(pen, disc);

            if (Selected)
                using (var pen = new Pen(Theme.Accent, 2f * s))
                    g.DrawEllipse(pen, 1.2f * s, 1.2f * s, Width - 2.4f * s, Height - 2.4f * s);
        }
    }

    /// <summary>An on/off switch like the ones in Windows 11 settings.</summary>
    internal sealed class ToggleSwitch : Control
    {
        private bool isChecked;
        public event EventHandler CheckedChanged;

        public ToggleSwitch()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
        }

        public bool Checked
        {
            get { return isChecked; }
            set
            {
                if (isChecked == value) return;
                isChecked = value;
                Invalidate();
                EventHandler handler = CheckedChanged;
                if (handler != null) handler(this, EventArgs.Empty);
            }
        }

        protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { Checked = !Checked; e.Handled = true; }
            base.OnKeyDown(e);
        }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            float s = g.DpiX / 96f;
            Theme.Quality(g);
            g.Clear(Parent != null ? Parent.BackColor : Theme.PageBack);

            var pill = new RectangleF(1 * s, 1 * s, Width - 2 * s, Height - 2 * s);
            using (GraphicsPath path = Theme.Round(pill, pill.Height / 2))
            {
                if (isChecked)
                {
                    using (var brush = new SolidBrush(Theme.Accent)) g.FillPath(brush, path);
                }
                else
                {
                    using (var brush = new SolidBrush(Color.FromArgb(246, 246, 248))) g.FillPath(brush, path);
                    using (var pen = new Pen(Color.FromArgb(120, 120, 128), 1.2f)) g.DrawPath(pen, path);
                }
            }

            float knob = pill.Height - 8 * s;
            float x = isChecked ? pill.Right - knob - 4 * s : pill.Left + 4 * s;
            using (var brush = new SolidBrush(isChecked ? Color.White : Color.FromArgb(96, 96, 104)))
                g.FillEllipse(brush, x, pill.Top + 4 * s, knob, knob);

            if (Focused && ShowFocusCues)
                using (GraphicsPath ring = Theme.Round(new RectangleF(0, 0, Width - 1, Height - 1), Height / 2f))
                using (var pen = new Pen(Color.FromArgb(150, Theme.Accent), 1.5f))
                    g.DrawPath(pen, ring);
        }
    }

    /// <summary>The page list on the left of the settings window.</summary>
    internal sealed class NavList : Control
    {
        public string[] Labels = new string[0];
        public string[] Glyphs = new string[0];
        public event EventHandler SelectedChanged;

        private readonly Font labelFont = new Font("Segoe UI", 10.5f);
        private int selected, hoverRow = -1;

        public NavList()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
        }

        public int SelectedIndex
        {
            get { return selected; }
            set
            {
                if (value == selected || value < 0 || value >= Labels.Length) return;
                selected = value;
                Invalidate();
                EventHandler handler = SelectedChanged;
                if (handler != null) handler(this, EventArgs.Empty);
            }
        }

        private int RowHeight { get { return (int)Math.Round(44 * Theme.ScaleOf(this)); } }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int row = e.Y / RowHeight;
            if (row >= Labels.Length) row = -1;
            if (row != hoverRow) { hoverRow = row; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hoverRow = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int row = e.Y / RowHeight;
            if (row >= 0 && row < Labels.Length) SelectedIndex = row;
            Focus();
            base.OnMouseDown(e);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            return keyData == Keys.Up || keyData == Keys.Down || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Down) { SelectedIndex = selected + 1; e.Handled = true; }
            else if (e.KeyCode == Keys.Up) { SelectedIndex = selected - 1; e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            float s = g.DpiX / 96f;
            Theme.Quality(g);
            g.Clear(Parent != null ? Parent.BackColor : Theme.NavBack);

            int rowH = RowHeight;
            using (Font glyphFont = IconFont.Create(17 * s))
            using (StringFormat fmt = Theme.Line())
            using (var textBrush = new SolidBrush(Theme.Text))
            using (var accentBrush = new SolidBrush(Theme.Accent))
            {
                for (int i = 0; i < Labels.Length; i++)
                {
                    var row = new RectangleF(4 * s, i * rowH + 2 * s, Width - 8 * s, rowH - 4 * s);
                    if (i == selected || i == hoverRow)
                        using (GraphicsPath path = Theme.Round(row, 6 * s))
                        using (var brush = new SolidBrush(i == selected ? Color.White : Color.FromArgb(14, 0, 0, 0)))
                            g.FillPath(brush, path);

                    if (i == selected)
                        using (GraphicsPath bar = Theme.Round(new RectangleF(row.Left + 2 * s, row.Top + row.Height * 0.25f, 3 * s, row.Height * 0.5f), 1.5f * s))
                            g.FillPath(accentBrush, bar);

                    if (glyphFont != null && i < Glyphs.Length)
                        g.DrawString(Glyphs[i], glyphFont, i == selected ? accentBrush : textBrush,
                            new RectangleF(row.Left + 16 * s, row.Top, 28 * s, row.Height), fmt);
                    g.DrawString(Labels[i], labelFont, textBrush,
                        new RectangleF(row.Left + (glyphFont != null ? 50 : 18) * s, row.Top, row.Width - 56 * s, row.Height), fmt);
                }
            }

            if (Focused && ShowFocusCues)
                using (var pen = new Pen(Color.FromArgb(120, Theme.Accent), 1f))
                    g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) labelFont.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>Shows the app list over a fake desktop and taskbar, so changes can be seen as you make them.</summary>
    internal sealed class PreviewBox : Control
    {
        private Bitmap panel;
        private Image buttonIcon;
        private bool buttonMask;

        public PreviewBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        /// <summary>Takes ownership of both images.</summary>
        public void SetContent(Bitmap panelBitmap, Image icon, bool mask)
        {
            if (panel != null) panel.Dispose();
            if (buttonIcon != null) buttonIcon.Dispose();
            panel = panelBitmap;
            buttonIcon = icon;
            buttonMask = mask;
            Invalidate();
        }

        /// <summary>Height of the fake taskbar strip in pixels, for sizing the panel above it.</summary>
        public static int StripHeight(float scale)
        {
            return (int)Math.Round(46 * scale);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            float s = g.DpiX / 96f;
            Theme.Quality(g);
            g.Clear(Parent != null ? Parent.BackColor : Theme.PageBack);

            var area = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            using (GraphicsPath clip = Theme.Round(area, 10 * s))
            {
                g.SetClip(clip);

                // Fake wallpaper.
                using (var wall = new LinearGradientBrush(new RectangleF(0, 0, Width, Height),
                    Color.FromArgb(52, 96, 176), Color.FromArgb(176, 80, 150), 60f))
                    g.FillRectangle(wall, 0, 0, Width, Height);
                using (var glow = new SolidBrush(Color.FromArgb(46, 255, 255, 255)))
                {
                    g.FillEllipse(glow, Width * 0.45f, -Height * 0.15f, Width * 0.8f, Height * 0.6f);
                    g.FillEllipse(glow, -Width * 0.25f, Height * 0.35f, Width * 0.6f, Height * 0.5f);
                }

                // Fake taskbar with the button on the far left and a few centered icons.
                int strip = StripHeight(s);
                float top = Height - strip;
                using (var bar = new SolidBrush(Color.FromArgb(226, 30, 30, 34)))
                    g.FillRectangle(bar, 0, top, Width, strip);

                float iconSize = 24 * s;
                if (buttonIcon != null)
                {
                    var target = new Rectangle((int)(10 * s), (int)(top + (strip - iconSize) / 2), (int)iconSize, (int)iconSize);
                    using (ImageAttributes attrs = buttonMask ? ButtonIcons.Tint(1f) : null)
                        ButtonIcons.Draw(g, buttonIcon, target, attrs);
                }
                Color[] dots = { Color.FromArgb(59, 130, 246), Color.FromArgb(245, 158, 11), Color.FromArgb(34, 197, 94), Color.FromArgb(160, 160, 170) };
                float cx = Width / 2f - (dots.Length * 34 * s) / 2f;
                for (int i = 0; i < dots.Length; i++)
                    using (var brush = new SolidBrush(dots[i]))
                        g.FillEllipse(brush, cx + i * 34 * s, top + (strip - 20 * s) / 2, 20 * s, 20 * s);

                // The list, opening above the button.
                if (panel != null)
                {
                    int x = (int)(8 * s);
                    int y = (int)(top - panel.Height - 6 * s);
                    g.DrawImageUnscaled(panel, x, y);
                }
                g.ResetClip();
            }
            using (var pen = new Pen(Theme.Border, 1f))
            using (GraphicsPath outline = Theme.Round(area, 10 * s))
                g.DrawPath(pen, outline);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (panel != null) panel.Dispose();
                if (buttonIcon != null) buttonIcon.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
