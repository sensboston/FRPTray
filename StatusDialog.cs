using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace FRPTray
{
    internal sealed class StatusDialog : Form
    {
        public StatusDialog(string text)
        {
            AutoScaleMode = AutoScaleMode.Dpi;

            float dpiScale = 1.0f;
            using (var g = CreateGraphics())
            {
                dpiScale = g.DpiX / 96f;
            }

            int Scale(int value) => (int)(value * dpiScale);

            Text = "Connection status";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(Scale(640), Scale(420));
            MaximizeBox = false;
            MinimizeBox = false;

            var box = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                BackColor = Color.White,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font(FontFamily.GenericMonospace, 10f),
                Location = new Point(Scale(10), Scale(10)),
                Size = new Size(Scale(620), Scale(360)),
                Text = text
            };

            var copy = new Button
            {
                Text = "Copy",
                Location = new Point(Scale(470), Scale(380)),
                Size = new Size(Scale(75), Scale(23))
            };

            var close = new Button
            {
                Text = "Close",
                Location = new Point(Scale(550), Scale(380)),
                Size = new Size(Scale(75), Scale(23)),
                DialogResult = DialogResult.OK
            };

            copy.Click += (s, e) => {
                try
                {
                    Clipboard.SetText(box.Text);
                }
                catch
                {
                    MessageBox.Show("Cannot access clipboard.");
                }
            };

            Controls.AddRange(new Control[] { box, copy, close });
            AcceptButton = close;
            CancelButton = close;
        }
    }
}
