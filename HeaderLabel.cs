using System.Drawing;
using System.Windows.Forms;

namespace FRPTray
{
    internal class HeaderLabel : ToolStripLabel
    {
        public HeaderLabel(string text) : base(text)
        {
            AutoSize = true;
            TextAlign = ContentAlignment.MiddleCenter;
            Padding = new Padding(6);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var backColor = Color.DarkSlateBlue;
            var foreColor = Color.White;

            using (var b = new SolidBrush(backColor))
                e.Graphics.FillRectangle(b, e.ClipRectangle);

            TextRenderer.DrawText(
                e.Graphics,
                Text,
                new Font(SystemFonts.MenuFont, FontStyle.Regular),
                e.ClipRectangle,
                foreColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            );
        }
    }
}