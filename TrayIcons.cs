using System;
using System.Drawing;

namespace FRPTray
{
    internal static class TrayIcons
    {
        public static Icon CreateStatusIcon(Color color)
        {
            int size = 16;
            using (var bmp = new Bitmap(size, size))
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                using (var b = new SolidBrush(color))
                {
                    g.FillEllipse(b, 2, 2, size - 6, size - 6);
                }
                IntPtr hIcon = bmp.GetHicon();
                try { return Icon.FromHandle(hIcon); }
                catch { return SystemIcons.Application; }
            }
        }
    }
}
