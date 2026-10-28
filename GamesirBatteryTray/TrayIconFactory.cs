using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace GamesirBatteryTray;

/// <summary>Draws a tray icon: a battery gauge whose fill reflects the charge level.</summary>
internal static class TrayIconFactory
{
    private static readonly Color HighColor = Color.FromArgb(255, 61, 214, 140);
    private static readonly Color MediumColor = Color.FromArgb(255, 255, 176, 32);
    private static readonly Color LowColor = Color.FromArgb(255, 255, 90, 90);
    private static readonly Color EmptyColor = Color.FromArgb(255, 140, 146, 154);

    /// <param name="accent">Shell colour, used to tell controllers apart.</param>
    public static Icon Create(int? percent, bool connected, Color accent)
    {
        using var bitmap = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using var shellPen = new Pen(accent, 2f);
            using var body = RoundedRect(new RectangleF(2.5f, 9f, 21f, 14f), 3.5f);
            g.DrawPath(shellPen, body);

            using var nubBrush = new SolidBrush(accent);
            g.FillRectangle(nubBrush, 24f, 13f, 4f, 6f);

            if (connected && percent is int value)
            {
                var level = Math.Clamp(value, 0, 100);
                var width = 16.5f * level / 100f;
                if (width >= 1f)
                {
                    using var fillBrush = new SolidBrush(level >= 50 ? HighColor : level >= 20 ? MediumColor : LowColor);
                    g.FillRectangle(fillBrush, 5f, 11.5f, width, 9f);
                }
                else
                {
                    using var emptyBrush = new SolidBrush(EmptyColor);
                    g.FillRectangle(emptyBrush, 5f, 11.5f, 1.5f, 9f);
                }
            }
            else
            {
                using var slashPen = new Pen(Color.FromArgb(255, 130, 136, 144), 2f);
                g.DrawLine(slashPen, 7f, 25f, 27f, 9f);
            }
        }

        return ToIcon(bitmap);
    }

    private static GraphicsPath RoundedRect(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2f;
        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>Wraps a bitmap as an Icon without leaking GDI handles (single PNG-compressed ICO entry).</summary>
    private static Icon ToIcon(Bitmap bitmap)
    {
        using var png = new MemoryStream();
        bitmap.Save(png, ImageFormat.Png);
        var pngBytes = png.ToArray();

        using var ico = new MemoryStream();
        using (var writer = new BinaryWriter(ico, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((ushort)0);          // reserved
            writer.Write((ushort)1);          // type: icon
            writer.Write((ushort)1);          // image count
            writer.Write((byte)32);           // width
            writer.Write((byte)32);           // height
            writer.Write((byte)0);            // palette
            writer.Write((byte)0);            // reserved
            writer.Write((ushort)1);          // planes
            writer.Write((ushort)32);         // bits per pixel
            writer.Write(pngBytes.Length);    // size of image data
            writer.Write(22);                 // offset of image data (6 + 16)
            writer.Write(pngBytes);
        }

        ico.Position = 0;
        return new Icon(ico);
    }
}
