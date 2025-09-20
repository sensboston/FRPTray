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
    internal sealed class StatusDialog : Form
    {
        private readonly TextBox logTextBox;
        private readonly Timer updateTimer;
        private readonly TrayAppContext parentContext;

        // Store current selection position to restore after update
        private int savedSelectionStart = 0;
        private int savedSelectionLength = 0;

        public StatusDialog(string initialText, TrayAppContext context = null)
        {
            parentContext = context;
            AutoScaleMode = AutoScaleMode.Dpi;

            var dpiScale = GetDpiScale();
            int Scale(int value) => dpiScale == 1.0f ? value : (int)(value * dpiScale);

            Text = "Connection status";
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(Scale(640), Scale(420));
            MaximizeBox = true;
            MinimizeBox = true;
            ShowInTaskbar = true;

            logTextBox = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                BackColor = Color.White,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font(FontFamily.GenericMonospace, 10f, FontStyle.Bold),
                Location = new Point(Scale(10), Scale(10)),
                Size = new Size(Scale(620), Scale(360)),
                Text = initialText,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
            };

            var copy = new Button
            {
                Text = "Copy",
                Location = new Point(Scale(470), Scale(380)),
                Size = new Size(Scale(75), Scale(23)),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };

            var close = new Button
            {
                Text = "Close",
                Location = new Point(Scale(550), Scale(380)),
                Size = new Size(Scale(75), Scale(23)),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };

            copy.Click += (s, e) => {
                try
                {
                    Clipboard.SetText(logTextBox.Text);
                }
                catch
                {
                    MessageBox.Show("Cannot access clipboard.");
                }
            };

            close.Click += (s, e) => Close();

            Controls.AddRange(new Control[] { logTextBox, copy, close });

            // Use Shown event instead of Load for proper scroll
            // Shown fires after the form is displayed and all controls are rendered
            Shown += (s, e) => {
                // Use BeginInvoke to ensure the scroll happens after all layout is complete
                BeginInvoke(new Action(() => {
                    ScrollToBottom();
                }));
            };

            // Start update timer if context provided
            if (parentContext != null)
            {
                updateTimer = new Timer();
                updateTimer.Interval = 1000; // Update every second
                updateTimer.Tick += UpdateTimer_Tick;
                updateTimer.Start();
            }

            FormClosed += (s, e) =>
            {
                updateTimer?.Stop();
                updateTimer?.Dispose();
            };
        }

        private void UpdateTimer_Tick(object sender, EventArgs e)
        {
            if (parentContext != null)
            {
                string newText = parentContext.GetCurrentLogText();
                if (logTextBox.Text != newText)
                {
                    // Save current position
                    savedSelectionStart = logTextBox.SelectionStart;
                    savedSelectionLength = logTextBox.SelectionLength;

                    // Update text
                    logTextBox.Text = newText;

                    // Restore position - maintain user's view
                    if (savedSelectionStart <= logTextBox.Text.Length)
                    {
                        logTextBox.SelectionStart = savedSelectionStart;
                        logTextBox.SelectionLength = savedSelectionLength;
                        logTextBox.ScrollToCaret();
                    }
                }
            }
        }

        private void ScrollToBottom()
        {
            logTextBox.SelectionStart = logTextBox.Text.Length;
            logTextBox.ScrollToCaret();
        }

        private float GetDpiScale()
        {
            using (var graphics = Graphics.FromHwnd(IntPtr.Zero))
            {
                float scale = graphics.DpiX / 96f;
                return Math.Abs(scale - 1.0f) < 0.01f ? 1.0f : scale;
            }
        }
    }
}