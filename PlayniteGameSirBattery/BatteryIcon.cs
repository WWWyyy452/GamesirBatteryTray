using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameSirBattery
{
    /// <summary>Renders the battery gauge as a frozen bitmap for the top panel button.</summary>
    internal static class BatteryIcon
    {
        private static readonly Color ShellColor = Color.FromRgb(0xA8, 0xAE, 0xB6);
        private static readonly Color HighColor = Color.FromRgb(0x3D, 0xD6, 0x8C);
        private static readonly Color MediumColor = Color.FromRgb(0xFF, 0xB0, 0x20);
        private static readonly Color LowColor = Color.FromRgb(0xFF, 0x5A, 0x5A);
        private static readonly Color EmptyColor = Color.FromRgb(0x82, 0x88, 0x90);

        public static BitmapSource CreateSource(int? level)
        {
            var visual = new DrawingVisual();

            using (var dc = visual.RenderOpen())
            {
                var shellBrush = new SolidColorBrush(ShellColor);
                var shellPen = new Pen(shellBrush, 2);
                dc.DrawRoundedRectangle(null, shellPen, new Rect(3, 8, 20, 16), 3, 3);
                dc.DrawRectangle(shellBrush, null, new Rect(24, 13, 4, 6));

                if (level.HasValue)
                {
                    var clamped = Math.Max(0, Math.Min(100, level.Value));
                    var color = clamped >= 50 ? HighColor : clamped >= 20 ? MediumColor : LowColor;
                    var width = 16.0 * clamped / 100.0;
                    if (width >= 1)
                    {
                        dc.DrawRectangle(new SolidColorBrush(color), null, new Rect(5, 10, width, 12));
                    }
                }
                else
                {
                    dc.DrawLine(new Pen(new SolidColorBrush(EmptyColor), 2), new Point(8, 25), new Point(26, 9));
                }
            }

            var bitmap = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }
    }
}
