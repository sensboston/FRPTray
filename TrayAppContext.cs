using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Timer = System.Threading.Timer;

namespace FRPTray
{
    public sealed class TrayAppContext : ApplicationContext
    {
        private readonly SynchronizationContext uiContext;

        private NotifyIcon notifyIcon;
        private ToolStripMenuItem startItem;
        private ToolStripMenuItem stopItem;
        private ToolStripMenuItem statusItem;
        private ToolStripMenuItem copyUrlRootItem;

        private Icon grayIcon;
        private Icon greenIcon;

        private Process frpcProcess;
        private string frpcPath;
        private string configPath;

        private string statusText = "not connected";
        private readonly StringBuilder logBuffer = new StringBuilder(8192);
        private readonly object logLock = new object();

        private Timer healthTimer;
        private volatile bool userWantsRunning;
        private volatile bool shuttingDown;
        private volatile bool networkAvailable = true;

        private readonly Random rng = new Random();
        private int reconnectBackoffMs = 1000;
        private DateTime nextAllowedStartUtc = DateTime.MinValue;

        public TrayAppContext()
        {
            FrpProcess.KillStaleFrpcProcesses();
            uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

            grayIcon = TrayIcons.CreateStatusIcon(Color.Gray);
            greenIcon = TrayIcons.CreateStatusIcon(Color.Lime);

            startItem = new ToolStripMenuItem("Start tunnel", null, OnStartClicked);
            stopItem = new ToolStripMenuItem("Stop tunnel", null, OnStopClicked) { Enabled = false };
            var settingsItem = new ToolStripMenuItem("Settings...", null, OnSettingsClicked);
            copyUrlRootItem = new ToolStripMenuItem("Copy public URL");
            var showStatusItem = new ToolStripMenuItem("Show connection status...", null, OnShowStatusClicked);
            statusItem = new ToolStripMenuItem("Status: not connected") { Enabled = false };

            var menu = new ContextMenuStrip();
            var placeholderItem = new ToolStripMenuItem(" ") { Enabled = false, Padding = new Padding(5) };
            menu.Items.Add(placeholderItem);
            menu.Items.Add(startItem);
            menu.Items.Add(stopItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(settingsItem);
            menu.Items.Add(copyUrlRootItem);
            menu.Items.Add(showStatusItem);
            menu.Items.Add(statusItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Exit", null, OnExitClicked));

            menu.Paint += (sender, e) =>
            {
                var headerRect = new Rectangle(0, 0, menu.Width, statusItem.Height + 5);
                using (var brush = new SolidBrush(Color.DarkSlateBlue))
                    e.Graphics.FillRectangle(brush, headerRect);

                using (var scaledFont = new Font(SystemFonts.MenuFont.FontFamily, SystemFonts.MenuFont.Size))
                {
                    TextRenderer.DrawText(e.Graphics, GetVersionText(),
                        scaledFont, headerRect, Color.White,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            };

            notifyIcon = new NotifyIcon
            {
                Text = "FRPTray: not connected",
                Icon = grayIcon,
                Visible = true,
                ContextMenuStrip = menu
            };

            notifyIcon.MouseDoubleClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    if (FrpProcess.IsProcessRunning(frpcProcess)) OnStopClicked(this, EventArgs.Empty);
                    else OnStartClicked(this, EventArgs.Empty);
                }
            };

            NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
            healthTimer = new Timer(OnHealthTimer, null, Timeout.Infinite, Timeout.Infinite);

            StartupManager.Set(Properties.Settings.Default.RunOnStartup);
            RebuildCopyMenu();

            if (Properties.Settings.Default.StartTunnelOnRun)
                OnUi(() => OnStartClicked(this, EventArgs.Empty));
        }

        private void OnNetworkAvailabilityChanged(object sender, NetworkAvailabilityEventArgs e)
        {
            networkAvailable = e.IsAvailable;
            if (!userWantsRunning) return;

            if (networkAvailable)
            {
                reconnectBackoffMs = 2000;
                nextAllowedStartUtc = DateTime.UtcNow;
                try { healthTimer.Change(1500, Timeout.Infinite); } catch { }
            }
            else
            {
                try { healthTimer.Change(Timeout.Infinite, Timeout.Infinite); } catch { }
                statusText = "network unavailable";
                OnUi(() => UpdateStatusUi(false));
            }
        }

        private void OnHealthTimer(object _)
        {
            if (shuttingDown || !userWantsRunning) return;

            bool processOk = FrpProcess.IsProcessRunning(frpcProcess);
            if (!processOk)
            {
                statusText = "restarting...";
                OnUi(() => UpdateStatusUi(false));
                TryStartOrBackoff();
                return;
            }

            if (!ProbeRemotePorts())
            {
                AppendLog("[WARN] health check failed; restarting");
                statusText = "restarting...";
                OnUi(() => UpdateStatusUi(false));
                FrpProcess.SafeKill(frpcProcess);
                TryStartOrBackoff();
                return;
            }

            reconnectBackoffMs = 1000;
            statusText = BuildConnectedText();
            OnUi(() => UpdateStatusUi(true));
            try { healthTimer.Change(5000, Timeout.Infinite); } catch { }
        }

        private bool ExistsSafe(string path) => !string.IsNullOrEmpty(path) && File.Exists(path);

        private void TryStartOrBackoff()
        {
            if (!networkAvailable) return;

            if (DateTime.UtcNow < nextAllowedStartUtc)
            {
                int ms = (int)Math.Max(500, (nextAllowedStartUtc - DateTime.UtcNow).TotalMilliseconds);
                try { healthTimer.Change(ms, Timeout.Infinite); } catch { }
                return;
            }

            if (!ExistsSafe(frpcPath) || !ExistsSafe(configPath))
            {
                try
                {
                    int[] locals, remotes;
                    Ports.GetFromSettings(out locals, out remotes);
                    FrpFiles.Prepare(out frpcPath, out configPath, ServerAddressSetting, ServerPortSetting, TokenSetting, locals, remotes);
                }
                catch (Exception prepEx)
                {
                    AppendLog("[ERR] prepare failed: " + prepEx.Message);
                    RestartHealthTimerWithBackoff();
                    return;
                }
            }

            try
            {
                string message;
                bool started = FrpProcess.TryStart(frpcPath, configPath, OnFrpcOut, OnFrpcErr, OnFrpcExit, out frpcProcess, out message);
                if (started)
                {
                    reconnectBackoffMs = 1000;
                    statusText = BuildConnectedText();
                    OnUi(() => UpdateStatusUi(true));
                    try { healthTimer.Change(2000, Timeout.Infinite); } catch { }
                }
                else
                {
                    AppendLog("[WARN] start failed: " + message);
                    RestartHealthTimerWithBackoff();
                }
            }
            catch (Win32Exception ex)
            {
                bool added = DefenderExclusion.TryOfferAndAdd(frpcPath, ex, ShowError);
                if (added)
                {
                    Thread.Sleep(1500);
                    ScheduleReconnectSoon();
                }
                else
                {
                    ShowError("Process blocked: " + ex.Message);
                    statusText = "error";
                    OnUi(() => UpdateStatusUi(false));
                }
            }
            catch (Exception ex)
            {
                ShowError("Start failed: " + ex.Message);
                statusText = "error";
                OnUi(() => UpdateStatusUi(false));
                ScheduleReconnectSoon();
            }
        }

        private void RestartHealthTimerWithBackoff()
        {
            reconnectBackoffMs = Math.Min(reconnectBackoffMs * 2, 60000);
            int jitter = rng.Next(0, Math.Max(1, reconnectBackoffMs / 3));
            int delay = reconnectBackoffMs + jitter;
            nextAllowedStartUtc = DateTime.UtcNow.AddMilliseconds(delay);
            try { healthTimer.Change(delay, Timeout.Infinite); } catch { }
        }

        private bool ProbeRemotePorts()
        {
            int[] locals, remotes;
            try { Ports.GetFromSettings(out locals, out remotes); } catch { return false; }

            for (int i = 0; i < remotes.Length; i++)
            {
                int rp = remotes[i];
                try
                {
                    using (var client = new TcpClient())
                    {
                        var ar = client.BeginConnect(ServerAddressSetting, rp, null, null);
                        bool ok = ar.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(800));
                        if (!ok) return false;
                        client.EndConnect(ar);
                    }
                }
                catch { return false; }
            }
            return true;
        }

        private void OnStartClicked(object sender, EventArgs e)
        {
            userWantsRunning = true;

            if (FrpProcess.IsProcessRunning(frpcProcess))
            {
                statusText = BuildConnectedText();
                OnUi(() => UpdateStatusUi(true));
                try { healthTimer.Change(2000, Timeout.Infinite); } catch { }
                return;
            }

            try
            {
                int[] locals, remotes;
                Ports.GetFromSettings(out locals, out remotes);
                FrpFiles.Prepare(out frpcPath, out configPath, ServerAddressSetting, ServerPortSetting, TokenSetting, locals, remotes);

                statusText = "connecting...";
                OnUi(() => UpdateStatusUi(false));

                string errorMessage;
                bool started = FrpProcess.TryStart(frpcPath, configPath, OnFrpcOut, OnFrpcErr, OnFrpcExit, out frpcProcess, out errorMessage);
                if (!started)
                {
                    ShowError("Start failed: " + errorMessage);
                    statusText = "error";
                    OnUi(() => UpdateStatusUi(false));
                    ScheduleReconnectSoon();
                    return;
                }

                startItem.Enabled = false;
                stopItem.Enabled = true;
                statusText = BuildConnectedText();
                OnUi(() => UpdateStatusUi(true));

                RebuildCopyMenu();
                try { healthTimer.Change(2000, Timeout.Infinite); } catch { }
            }
            catch (Win32Exception ex)
            {
                bool added = DefenderExclusion.TryOfferAndAdd(frpcPath, ex, ShowError);
                if (added)
                {
                    Thread.Sleep(1500);
                    ScheduleReconnectSoon();
                }
                else
                {
                    ShowError("Process blocked: " + ex.Message);
                    statusText = "error";
                    OnUi(() => UpdateStatusUi(false));
                }
            }
            catch (Exception ex)
            {
                ShowError("Start failed: " + ex.Message);
                statusText = "error";
                OnUi(() => UpdateStatusUi(false));
                ScheduleReconnectSoon();
            }
        }

        private void OnStopClicked(object sender, EventArgs e)
        {
            userWantsRunning = false;
            FrpProcess.SafeKill(frpcProcess);

            startItem.Enabled = true;
            stopItem.Enabled = false;
            statusText = "not connected";
            OnUi(() => UpdateStatusUi(false));

            try { healthTimer.Change(Timeout.Infinite, Timeout.Infinite); } catch { }
            FrpFiles.TryDeleteTemp(frpcPath, configPath);
            RebuildCopyMenu();
        }

        private void OnExitClicked(object sender, EventArgs e)
        {
            shuttingDown = true;
            userWantsRunning = false;

            try { healthTimer?.Change(Timeout.Infinite, Timeout.Infinite); } catch { }
            try { healthTimer?.Dispose(); } catch { }

            FrpProcess.SafeKill(frpcProcess);
            FrpFiles.TryDeleteTemp(frpcPath, configPath);

            try { if (notifyIcon != null) { notifyIcon.Visible = false; notifyIcon.Dispose(); } } catch { }
            grayIcon?.Dispose();
            greenIcon?.Dispose();
            Application.Exit();
        }

        private void OnSettingsClicked(object sender, EventArgs e)
        {
            using (var dlg = new SettingsDialog())
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;

                bool wasRunning = FrpProcess.IsProcessRunning(frpcProcess);

                StartupManager.Set(Properties.Settings.Default.RunOnStartup);

                if (wasRunning)
                {
                    OnStopClicked(this, EventArgs.Empty);
                    OnStartClicked(this, EventArgs.Empty);
                }
                else
                {
                    statusText = "not connected";
                    OnUi(() => UpdateStatusUi(false));
                }

                RebuildCopyMenu();
            }
        }

        private string RemoveAnsiEscapes(string text)
        {
            return Regex.Replace(text, @"\x1B\[[0-9;]*[mK]", "");
        }

        public string GetCurrentLogText()
        {
            string mapping;
            try
            {
                int[] locals, remotes;
                Ports.GetFromSettings(out locals, out remotes);
                var mb = new StringBuilder();
                for (int i = 0; i < locals.Length; i++)
                    mb.AppendLine("  127.0.0.1:" + locals[i] + " → " + ServerAddressSetting + ":" + remotes[i]);
                mapping = mb.ToString().TrimEnd();
            }
            catch { mapping = "(not configured)"; }

            lock (logLock)
            {
                return "Status: " + statusText + Environment.NewLine +
                       "Tunnels:" + Environment.NewLine +
                       mapping + Environment.NewLine +
                       "Network: " + (networkAvailable ? "available" : "unavailable") +
                       Environment.NewLine + Environment.NewLine +
                       "---- Last log lines ----" + Environment.NewLine +
                       RemoveAnsiEscapes(logBuffer.ToString());
            }
        }

        private void OnShowStatusClicked(object sender, EventArgs e)
        {
            string initialText = GetCurrentLogText();
            var dlg = new StatusDialog(initialText, this); 
            dlg.Show();
        }

        private void RebuildCopyMenu()
        {
            if (copyUrlRootItem == null) return;
            copyUrlRootItem.DropDownItems.Clear();
            copyUrlRootItem.Click -= OnCopySingleRootClicked;
            copyUrlRootItem.Tag = null;

            int[] locals, remotes;
            try { Ports.GetFromSettings(out locals, out remotes); }
            catch
            {
                copyUrlRootItem.Text = "Copy public URL";
                copyUrlRootItem.Enabled = false;
                return;
            }

            if (remotes.Length == 1)
            {
                int rp = remotes[0];
                int lp = locals[0];
                copyUrlRootItem.Text = "Copy public URL for port " + rp;
                copyUrlRootItem.Enabled = true;
                copyUrlRootItem.Tag = new PortPair(lp, rp);
                copyUrlRootItem.Click += OnCopySingleRootClicked;
            }
            else
            {
                copyUrlRootItem.Text = "Copy public URLs";
                copyUrlRootItem.Enabled = true;

                for (int i = 0; i < remotes.Length; i++)
                {
                    int rp = remotes[i];
                    int lp = locals[i];
                    var item = new ToolStripMenuItem(UrlFormatter.Format(ServerAddressSetting, lp, rp));
                    item.Tag = new PortPair(lp, rp);
                    item.Click += (s, e) =>
                    {
                        var pair = (PortPair)((ToolStripMenuItem)s).Tag;
                        CopyUrlToClipboard(UrlFormatter.Format(ServerAddressSetting, pair.Local, pair.Remote));
                    };
                    copyUrlRootItem.DropDownItems.Add(item);
                }
            }
        }

        private void OnCopySingleRootClicked(object sender, EventArgs e)
        {
            var pair = (PortPair)((ToolStripMenuItem)sender).Tag;
            CopyUrlToClipboard(UrlFormatter.Format(ServerAddressSetting, pair.Local, pair.Remote));
        }

        private void CopyUrlToClipboard(string url)
        {
            try { Clipboard.SetText(url); } catch { ShowError("Cannot access clipboard."); }
        }

        private void OnFrpcOut(string line)
        {
            if (line == null) return;
            AppendLog("[OUT] " + line);
            if (line.IndexOf("start proxy success", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("login to server success", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                statusText = BuildConnectedText();
                OnUi(() => UpdateStatusUi(true));
            }
        }

        private void OnFrpcErr(string line)
        {
            if (line == null) return;
            AppendLog("[ERR] " + line);
        }

        private void OnFrpcExit()
        {
            AppendLog("[INFO] frpc exited]");
            OnUi(() =>
            {
                if (shuttingDown) return;

                startItem.Enabled = true;
                stopItem.Enabled = false;
                statusText = "not connected";
                UpdateStatusUi(false);
                FrpFiles.TryDeleteTemp(frpcPath, configPath);

                if (userWantsRunning) ScheduleReconnectSoon();
            });
        }

        private void ShowError(string message)
        {
            try { notifyIcon?.ShowBalloonTip(3500, "FRPTray", message, ToolTipIcon.Error); } catch { }
        }

        private void UpdateStatusUi(bool connected)
        {
            try
            {
                if (shuttingDown || notifyIcon == null) return;
                notifyIcon.Icon = connected ? greenIcon : grayIcon;
                notifyIcon.Text = "FRPTray: " + statusText;
                if (startItem != null) startItem.Enabled = !connected;
                if (stopItem != null) stopItem.Enabled = connected;
                if (statusItem != null) statusItem.Text = "Status: " + statusText;
            }
            catch { }
        }

        private string BuildConnectedText()
        {
            try { int[] l, r; Ports.GetFromSettings(out l, out r); return "connected to " + ServerAddressSetting; }
            catch { return "not connected"; }
        }

        private string GetVersionText()
        {
            try
            {
                var asm = typeof(TrayAppContext).Assembly;
                var verInfo = FileVersionInfo.GetVersionInfo(asm.Location);
                string ver = !string.IsNullOrEmpty(verInfo.ProductVersion)
                    ? verInfo.ProductVersion
                    : (!string.IsNullOrEmpty(verInfo.FileVersion) ? verInfo.FileVersion : asm.GetName().Version.ToString());
                var buildTime = System.IO.File.GetLastWriteTime(asm.Location);
                return "  FRPTray " + ver + "  ";
            }
            catch { return "  FRPTray  "; }
        }

        private void OnUi(Action action)
        {
            if (action == null) return;
            var ctx = uiContext;
            if (ctx != null) ctx.Post(_ => { try { action(); } catch { } }, null);
            else { try { action(); } catch { } }
        }

        private void ScheduleReconnectSoon()
        {
            if (shuttingDown) return;
            reconnectBackoffMs = Math.Min(reconnectBackoffMs, 15000);
            try { healthTimer.Change(Math.Max(reconnectBackoffMs, 1000), Timeout.Infinite); } catch { }
        }

        private void AppendLog(string line)
        {
            lock (logLock)
            {
                logBuffer.AppendLine(DateTime.Now.ToString("HH:mm:ss ") + line);
                if (logBuffer.Length > 8000)
                {
                    int cut = logBuffer.Length - 6000;
                    if (cut > 0) logBuffer.Remove(0, cut);
                }
            }
        }

        private string ServerAddressSetting
        {
            get
            {
                var s = Properties.Settings.Default.Server;
                return string.IsNullOrWhiteSpace(s) ? "127.0.0.1" : s.Trim();
            }
        }

        private string ServerPortSetting
        {
            get
            {
                var s = Properties.Settings.Default.ServerPort;
                return string.IsNullOrWhiteSpace(s) ? "7000" : s.Trim();
            }
        }

        private string TokenSetting
        {
            get
            {
                var s = Properties.Settings.Default.Token;
                return string.IsNullOrWhiteSpace(s) ? "CHANGE_ME_STRONG_TOKEN" : s.Trim();
            }
        }

        private sealed class PortPair
        {
            public int Local; public int Remote;
            public PortPair(int local, int remote) { Local = local; Remote = remote; }
        }
    }
}
