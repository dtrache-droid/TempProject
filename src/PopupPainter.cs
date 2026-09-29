// Lays out and draws the app list (icon on the left, name on the right).
// Used by the real pop-up and by the live preview in the settings window.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace AppLauncher
{
    internal sealed class PopupPainter : IDisposable
    {
        private const string EmptyText = "No apps yet - right-click the button to add some";

        private readonly Settings settings;
        private readonly float scale;
        private readonly List<AppItem> items;
        private readonly List<Image> icons = new List<Image>();
        private readonly Font font;

        public readonly bool Empty;
        public readonly int IconPx, RowPad, HPad, Gap, RowH, OuterPad, Radius;
        public readonly int Width, ViewHeight, ContentHeight;

        public PopupPainter(Settings settings, float scale, List<AppItem> items, int maxWidth, int maxHeight)
        {
            this.settings = settings;
            this.scale = scale;
            this.items = items;
            Empty = items.Count == 0;

            int size = settings.IconSize;
            IconPx = Px(size);
            RowPad = Px(Math.Max(5, size * 0.22f));
            HPad = RowPad + Px(4);
            Gap = Px(Math.Max(8, size * 0.4f));
            OuterPad = Px(6);
            Radius = Px(8);
            RowH = IconPx + 2 * RowPad;
            font = new Font("Segoe UI", Math.Max(12f, size * 0.5f) * scale, FontStyle.Regular, GraphicsUnit.Pixel);

            int textWidth = 0;
            using (var probe = new Bitmap(1, 1))
            using (Graphics g = Graphics.FromImage(probe))
            using (StringFormat fmt = TextFormat())
            {
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                if (Empty)
                {
                    textWidth = (int)Math.Ceiling(g.MeasureString(EmptyText, font, 10000, fmt).Width);
                }
                else
                {
                    foreach (AppItem item in items)
                    {
                        textWidth = Math.Max(textWidth, (int)Math.Ceiling(g.MeasureString(item.Name, font, 10000, fmt).Width));
                        icons.Add(Shell.GetAppIcon(item.Path, IconPx));
                    }
                }
            }

            int width = Empty ? textWidth + 2 * HPad : textWidth + IconPx + Gap + 2 * HPad;
            Width = Math.Min(Math.Max(width, Px(160)), maxWidth);

            ContentHeight = (Empty ? 1 : items.Count) * RowH;
            ViewHeight = Math.Max(RowH, Math.Min(ContentHeight, maxHeight - 2 * OuterPad));
        }

        public int Height
        {
            get { return ViewHeight + 2 * OuterPad; }
        }

        public void Dispose()
        {
            font.Dispose();
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

        /// <summary>Which row is at this y (inside the panel), or -1.</summary>
        public int RowAt(int y, int scroll)
        {
            if (Empty) return -1;
            int i = (y - OuterPad + scroll) / RowH;
            if (y < OuterPad || y >= OuterPad + ViewHeight || i < 0 || i >= items.Count) return -1;
            return i;
        }

        /// <summary>Draws the panel into a new premultiplied-alpha bitmap. The caller disposes it.</summary>
        public Bitmap Render(int hot, int scroll)
        {
            Color back = settings.BackColor;
            double transparency = settings.TransparencyPercent / 100.0;
            int panelAlpha = Math.Max(1, (int)Math.Round(255 * (1 - transparency))); // alpha 1 keeps the panel clickable
            bool lightBack = (0.299 * back.R + 0.587 * back.G + 0.114 * back.B) > 150;
            Color textColor = lightBack ? Color.FromArgb(28, 28, 28) : Color.FromArgb(245, 245, 245);
            Color shadowColor = lightBack ? Color.FromArgb(150, 255, 255, 255) : Color.FromArgb(150, 0, 0, 0);
            int edge = lightBack ? 0 : 255;

            var bmp = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            using (StringFormat fmt = TextFormat())
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = TextRenderingHint.AntiAlias;

                var panel = new Rectangle(0, 0, Width - 1, Height - 1);
                using (GraphicsPath path = RoundedRect(panel, Radius))
                {
                    using (var brush = new SolidBrush(Color.FromArgb(panelAlpha, back)))
                        g.FillPath(brush, path);

                    int borderAlpha = (int)Math.Round(70 * (1 - transparency));
                    if (borderAlpha > 2)
                        using (var pen = new Pen(Color.FromArgb(borderAlpha, edge, edge, edge), 1f))
                            g.DrawPath(pen, path);
                }

                g.SetClip(new Rectangle(0, OuterPad, Width, ViewHeight));

                if (Empty)
                {
                    var r = new RectangleF(HPad, OuterPad, Width - 2 * HPad, RowH);
                    DrawText(g, EmptyText, r, fmt, Color.FromArgb(170, textColor), shadowColor, transparency);
                }
                else
                {
                    for (int i = 0; i < items.Count; i++)
                    {
                        int y = OuterPad + i * RowH - scroll;
                        if (y + RowH < OuterPad || y > OuterPad + ViewHeight) continue;

                        if (i == hot)
                        {
                            var hl = new Rectangle(Px(4), y + Px(1), Width - 2 * Px(4), RowH - Px(2));
                            using (GraphicsPath hp = RoundedRect(hl, Px(6)))
                            using (var hb = new SolidBrush(lightBack ? Color.FromArgb(36, 0, 0, 0) : Color.FromArgb(40, 255, 255, 255)))
                                g.FillPath(hb, hp);
                        }

                        g.DrawImage(icons[i], new Rectangle(HPad, y + RowPad, IconPx, IconPx));

                        int tx = HPad + IconPx + Gap;
                        var tr = new RectangleF(tx, y, Width - tx - HPad, RowH);
                        DrawText(g, items[i].Name, tr, fmt, textColor, shadowColor, transparency);
                    }
                }
            }
            return bmp;
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
