using System.Drawing;
using System.Windows.Forms;

namespace FRPTray
{
    internal sealed class StatusDialog : Form
    {
        public StatusDialog(string text)
        {
            Text = "Connection status";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(640, 420);
            MaximizeBox = false; MinimizeBox = false;

            var box = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font(FontFamily.GenericMonospace, 9f), Location = new Point(10, 10), Size = new Size(620, 360), Text = text };
            var copy = new Button { Text = "Copy", Location = new Point(470, 380) };
            var close = new Button { Text = "Close", Location = new Point(550, 380), DialogResult = DialogResult.OK };

            copy.Click += (s, e) => { try { Clipboard.SetText(box.Text); } catch { MessageBox.Show("Cannot access clipboard."); } };

            Controls.AddRange(new Control[] { box, copy, close });
            AcceptButton = close; CancelButton = close;
        }
    }
}
