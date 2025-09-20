/*
 * This file is part of FRPTray project: a lightweight 
 * Windows tray app for managing FRP (frpc) tunnels
 * 
 * https://github.com/sensboston/FRPTray
 *
 * Copyright (c) 2013-2025 SeNSSoFT
 * SPDX-License-Identifier: MIT
 *
 */

using System;
using System.Drawing;
using System.Windows.Forms;

namespace FRPTray
{
    internal sealed class SettingsDialog : Form
    {
        public SettingsDialog()
        {
            AutoScaleMode = AutoScaleMode.Dpi;

            float dpiScale = 1.0f;
            using (var g = CreateGraphics())
            {
                dpiScale = g.DpiX / 96f;
            }

            int Scale(int value) => (int)(value * dpiScale);

            Text = "Settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(Scale(340), Scale(370));  // Increased height for new field
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;

            var lblLocal = new Label { Text = "Local ports (CSV, 1-65535):", AutoSize = true, Location = new Point(Scale(10), Scale(10)) };
            var txtLocal = new TextBox { Location = new Point(Scale(10), Scale(30)), Width = Scale(300), Text = Properties.Settings.Default.LocalPort ?? "" };

            var lblRemote = new Label { Text = "Remote ports (CSV, 1-65535):", AutoSize = true, Location = new Point(Scale(10), Scale(60)) };
            var txtRemote = new TextBox { Location = new Point(Scale(10), Scale(80)), Width = Scale(300), Text = Properties.Settings.Default.RemotePort ?? "" };

            var lblServer = new Label { Text = "Server (IP or host):", AutoSize = true, Location = new Point(Scale(10), Scale(110)) };
            var txtServer = new TextBox { Location = new Point(Scale(10), Scale(130)), Width = Scale(300), Text = Properties.Settings.Default.Server ?? "" };

            var lblServerPort = new Label { Text = "Server port:", AutoSize = true, Location = new Point(Scale(10), Scale(160)) };
            var txtServerPort = new TextBox { Location = new Point(Scale(10), Scale(180)), Width = Scale(300), Text = Properties.Settings.Default.ServerPort ?? "" };

            var lblToken = new Label { Text = "Token:", AutoSize = true, Location = new Point(Scale(10), Scale(210)) };
            var txtToken = new TextBox { Location = new Point(Scale(10), Scale(230)), Width = Scale(300), Text = Properties.Settings.Default.Token ?? "" };

            // New proxy prefix field
            var lblProxyPrefix = new Label { Text = "Proxy prefix (unique per client):", AutoSize = true, Location = new Point(Scale(10), Scale(260)) };

            // Get default prefix - use computer name if not set
            string defaultPrefix = Properties.Settings.Default.ProxyPrefix;
            if (string.IsNullOrWhiteSpace(defaultPrefix))
            {
                defaultPrefix = Environment.MachineName.ToLower().Replace(" ", "-");
            }

            var txtProxyPrefix = new TextBox
            {
                Location = new Point(Scale(10), Scale(280)),
                Width = Scale(300),
                Text = defaultPrefix,
                MaxLength = 50  // Limit prefix length
            };

            var chkRunStartup = new CheckBox { Text = "Run on Windows startup", AutoSize = true, Location = new Point(Scale(10), Scale(310)), Checked = Properties.Settings.Default.RunOnStartup };
            var chkStartOnRun = new CheckBox { Text = "Start tunnel on run", AutoSize = true, Location = new Point(Scale(10), Scale(333)), Checked = Properties.Settings.Default.StartTunnelOnRun };

            var btnOk = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(Scale(170), Scale(340)), Size = new Size(Scale(75), Scale(23)) };
            var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(Scale(250), Scale(340)), Size = new Size(Scale(75), Scale(23)) };

            Controls.AddRange(new Control[]
            {
                lblLocal, txtLocal,
                lblRemote, txtRemote,
                lblServer, txtServer,
                lblServerPort, txtServerPort,
                lblToken, txtToken,
                lblProxyPrefix, txtProxyPrefix,  // Add new controls
                chkRunStartup, chkStartOnRun,
                btnOk, btnCancel
            });

            AcceptButton = btnOk;
            CancelButton = btnCancel;

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

                var newServerPort = (txtServerPort.Text ?? "").Trim();
                if (string.IsNullOrEmpty(newServerPort))
                {
                    MessageBox.Show("Server port cannot be empty.");
                    DialogResult = DialogResult.None; return;
                }

                var newToken = (txtToken.Text ?? "").Trim();
                if (string.IsNullOrEmpty(newToken))
                {
                    MessageBox.Show("Token cannot be empty.");
                    DialogResult = DialogResult.None; return;
                }

                // Validate proxy prefix
                var newProxyPrefix = (txtProxyPrefix.Text ?? "").Trim();
                if (string.IsNullOrEmpty(newProxyPrefix))
                {
                    MessageBox.Show("Proxy prefix cannot be empty. Use a unique name for this client.");
                    DialogResult = DialogResult.None; return;
                }

                // Sanitize prefix - allow only alphanumeric and dash
                var sanitizedPrefix = System.Text.RegularExpressions.Regex.Replace(newProxyPrefix, @"[^a-zA-Z0-9\-]", "-").ToLower();
                if (sanitizedPrefix != newProxyPrefix.ToLower())
                {
                    var result = MessageBox.Show(
                        $"Proxy prefix will be sanitized to: {sanitizedPrefix}\nOnly letters, numbers and dashes are allowed.\n\nContinue?",
                        "Prefix Sanitization",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);
                    if (result != DialogResult.Yes)
                    {
                        DialogResult = DialogResult.None;
                        return;
                    }
                }

                Properties.Settings.Default.LocalPort = (txtLocal.Text ?? "").Trim();
                Properties.Settings.Default.RemotePort = (txtRemote.Text ?? "").Trim();
                Properties.Settings.Default.Server = newServer;
                Properties.Settings.Default.ServerPort = newServerPort;
                Properties.Settings.Default.Token = newToken;
                Properties.Settings.Default.ProxyPrefix = sanitizedPrefix;  // Save sanitized prefix
                Properties.Settings.Default.RunOnStartup = chkRunStartup.Checked;
                Properties.Settings.Default.StartTunnelOnRun = chkStartOnRun.Checked;
                Properties.Settings.Default.Save();
            };
        }
    }
}