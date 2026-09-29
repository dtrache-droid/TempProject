// The choices for the taskbar button's icon, plus loading a user's own image.

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace AppLauncher
{
    internal static class ButtonIcons
    {
        public sealed class Entry
        {
            public readonly string Id, Label;
            public readonly bool Mask; // white-on-transparent: tinted to match the taskbar

            public Entry(string id, string label, bool mask)
            {
                Id = id;
                Label = label;
                Mask = mask;
            }
        }

        public static readonly Entry[] BuiltIn =
        {
            new Entry("savy", "Savy S", false),
            new Entry("grid", "Color tiles", false),
            new Entry("dots", "Dots", true),
            new Entry("list", "List", true),
            new Entry("sparkle", "Sparkle", true),
        };

        public static bool CustomExists
        {
            get { return File.Exists(Settings.CustomIconPath); }
        }

        /// <summary>Loads an icon by id ("custom" = the user's own). Returns null if it can't be loaded.</summary>
        public static Image Load(string id, out bool mask)
        {
            mask = false;
            try
            {
                if (id == "custom")
                {
                    using (var fs = File.OpenRead(Settings.CustomIconPath))
                    using (Image img = Image.FromStream(fs))
                        return new Bitmap(img);
                }

                foreach (Entry entry in BuiltIn)
                {
                    if (entry.Id != id) continue;
                    mask = entry.Mask;
                    using (Stream s = typeof(ButtonIcons).Assembly.GetManifestResourceStream("icon-" + id + ".png"))
                    {
                        if (s == null) return null;
                        using (Image img = Image.FromStream(s))
                            return new Bitmap(img);
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>The icon chosen in the settings; falls back to the Savy S if it is missing.</summary>
        public static Image LoadCurrent(Settings settings, out bool mask)
        {
            Image img = Load(settings.ButtonIcon, out mask);
            if (img == null)
            {
                settings.ButtonIcon = "savy";
                img = Load("savy", out mask);
            }
            return img;
        }

        /// <summary>A square preview tile: the icon on a dark taskbar-coloured rounded square.</summary>
        public static Bitmap Tile(string id, int px)
        {
            var tile = new Bitmap(px, px, PixelFormat.Format32bppPArgb);
            bool mask;
            using (Image icon = Load(id, out mask))
            using (Graphics g = Graphics.FromImage(tile))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                using (GraphicsPath path = Theme.Round(new RectangleF(0.5f, 0.5f, px - 1f, px - 1f), px * 0.18f))
                using (var brush = new SolidBrush(Color.FromArgb(32, 32, 34)))
                    g.FillPath(brush, path);

                if (icon != null)
                {
                    int inset = (int)(px * 0.22f);
                    var target = new Rectangle(inset, inset, px - 2 * inset, px - 2 * inset);
                    using (ImageAttributes attrs = mask ? Tint(1f) : null)
                        Draw(g, icon, target, attrs);
                }
            }
            return tile;
        }

        public static void Draw(Graphics g, Image image, Rectangle target, ImageAttributes attrs)
        {
            if (attrs == null)
                g.DrawImage(image, target);
            else
                g.DrawImage(image, target, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attrs);
        }

        /// <summary>Recolours a white-on-transparent mask: t = 1 keeps it white, small t makes it dark.</summary>
        public static ImageAttributes Tint(float t)
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

        /// <summary>Copies a picture the user chose into the settings folder as a 256 x 256 PNG. Throws on failure.</summary>
        public static void ImportCustom(string file)
        {
            Bitmap square;
            if (Path.GetExtension(file).ToLowerInvariant() == ".ico")
            {
                using (var ico = new Icon(file, 256, 256))
                    square = Shell.Fit(ico.ToBitmap(), 256);
            }
            else
            {
                using (Image img = Image.FromFile(file))
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
        }
    }
}
