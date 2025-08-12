using System;
using System.Drawing;
using System.Windows.Forms;

namespace FRPTray
{
    internal sealed class SettingsDialog : Form
    {
        public SettingsDialog()
        {
            Text = "Settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(340, 240);
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;

            var lblLocal = new Label { Text = "Local ports (CSV, 1-65535):", AutoSize = true, Location = new Point(10, 10) };
            var txtLocal = new TextBox { Location = new Point(10, 30), Width = 300, Text = Properties.Settings.Default.LocalPort ?? "" };

            var lblRemote = new Label { Text = "Remote ports (CSV, 1-65535):", AutoSize = true, Location = new Point(10, 60) };
            var txtRemote = new TextBox { Location = new Point(10, 80), Width = 300, Text = Properties.Settings.Default.RemotePort ?? "" };

            var lblServer = new Label { Text = "Server (IP or host):", AutoSize = true, Location = new Point(10, 110) };
            var txtServer = new TextBox { Location = new Point(10, 130), Width = 300, Text = Properties.Settings.Default.Server };

            var chkRunStartup = new CheckBox { Text = "Run on Windows startup", AutoSize = true, Location = new Point(10, 160), Checked = Properties.Settings.Default.RunOnStartup };
            var chkStartOnRun = new CheckBox { Text = "Start tunnel on run", AutoSize = true, Location = new Point(10, 183), Checked = Properties.Settings.Default.StartTunnelOnRun };

            var btnOk = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(170, 205) };
            var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(250, 205) };

            Controls.AddRange(new Control[] { lblLocal, txtLocal, lblRemote, txtRemote, lblServer, txtServer, chkRunStartup, chkStartOnRun, btnOk, btnCancel });
            AcceptButton = btnOk; CancelButton = btnCancel;

            btnOk.Click += (s, e) =>
            {
                int[] locals, remotes;
                try
                {
                    locals = Ports.ParseCsv((txtLocal.Text ?? "").Trim());
                    remotes = Ports.ParseCsv((txtRemote.Text ?? "").Trim());
                    if (locals.Length == 0) { MessageBox.Show("Invalid Local ports."); DialogResult = DialogResult.None; return; }
                    if (remotes.Length == 0) { MessageBox.Show("Invalid Remote ports."); DialogResult = DialogResult.None; return; }
                    if (locals.Length != remotes.Length) { MessageBox.Show("Local/Remote ports count mismatch."); DialogResult = DialogResult.None; return; }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                    DialogResult = DialogResult.None; return;
                }

                var newServer = (txtServer.Text ?? "").Trim();
                if (string.IsNullOrEmpty(newServer))
                {
                    MessageBox.Show("Server cannot be empty.");
                    DialogResult = DialogResult.None; return;
                }

                Properties.Settings.Default.LocalPort = (txtLocal.Text ?? "").Trim();
                Properties.Settings.Default.RemotePort = (txtRemote.Text ?? "").Trim();
                Properties.Settings.Default.Server = newServer;
                Properties.Settings.Default.RunOnStartup = chkRunStartup.Checked;
                Properties.Settings.Default.StartTunnelOnRun = chkStartOnRun.Checked;
                Properties.Settings.Default.Save();
            };
        }
    }
}
