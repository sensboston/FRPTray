using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using Timer = System.Threading.Timer;

namespace FRPTray
{
    public sealed class TrayAppContext : ApplicationContext
    {
        #region Fields

        private readonly SynchronizationContext uiContext;

        private NotifyIcon notifyIcon;
        private ToolStripMenuItem startItem;
        private ToolStripMenuItem stopItem;
        private ToolStripMenuItem statusItem;
        private Process frpcProcess;
        private string configPath;
        private string frpcPath;
        private Icon grayIcon;
        private Icon greenIcon;

        private string statusText = "not connected";
        private readonly StringBuilder logBuffer = new StringBuilder(8192);
        private readonly object logLock = new object();

        private Timer healthTimer;
        private volatile bool userWantsRunning;
        private volatile bool shuttingDown;
        private volatile bool networkAvailable = true;
        private int reconnectBackoffMs = 1000;

        private const string FrpcKeyB64 = "qUEpGDPK/+ftBlq4ThCDQvuPOdHsQnsnp2KtEJsuN+k=";
        private const string FrpcIvB64 = "JLlT8v+R+wUJH3PvF/D1wQ==";
        private const string FrpcResourceName = "FRPTray.frpc.enc";

        // Backoff throttle
        private readonly Random rng = new Random();
        private DateTime nextAllowedStartUtc = DateTime.MinValue;

        #endregion

        #region Constructor & Initialization

        public TrayAppContext()
        {
            uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

            grayIcon = CreateStatusIcon(Color.Gray);
            greenIcon = CreateStatusIcon(Color.Lime);

            startItem = new ToolStripMenuItem("Start tunnel", null, OnStartClicked);
            stopItem = new ToolStripMenuItem("Stop tunnel", null, OnStopClicked) { Enabled = false };
            var settingsItem = new ToolStripMenuItem("Settings...", null, OnSettingsClicked);
            var copyUrlItem = new ToolStripMenuItem("Copy public URL", null, OnCopyUrlClicked);
            var showStatusItem = new ToolStripMenuItem("Show connection status", null, OnShowStatusClicked);
            statusItem = new ToolStripMenuItem("Status: not connected") { Enabled = false };

            var menu = new ContextMenuStrip();
            menu.Items.Add(startItem);
            menu.Items.Add(stopItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(settingsItem);
            menu.Items.Add(copyUrlItem);
            menu.Items.Add(showStatusItem);
            menu.Items.Add(statusItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Exit", null, OnExitClicked));

            notifyIcon = new NotifyIcon
            {
                Text = "FRPTray: not connected",
                Icon = grayIcon,
                Visible = true,
                ContextMenuStrip = menu
            };

            NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
            healthTimer = new System.Threading.Timer(OnHealthTimer, null, Timeout.Infinite, Timeout.Infinite);

            // Apply startup setting and optional auto-start tunnel
            StartupManager.Set(Properties.Settings.Default.RunOnStartup);
            if (Properties.Settings.Default.StartTunnelOnRun)
                OnUi(() => OnStartClicked(this, EventArgs.Empty));
        }

        #endregion

        #region Network availability

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

        #endregion

        #region Health monitoring

        private void OnHealthTimer(object state)
        {
            if (shuttingDown) return;
            if (!userWantsRunning) return;

            bool processOk = IsProcessRunning(frpcProcess);
            if (!processOk)
            {
                statusText = "restarting...";
                OnUi(() => UpdateStatusUi(false));
                TryStartOrBackoff();
                return;
            }

            bool portOk = ProbeRemotePort();
            if (!portOk)
            {
                AppendLog("[WARN] health check failed; restarting");
                statusText = "restarting...";
                OnUi(() => UpdateStatusUi(false));
                SafeKillProcess();
                TryStartOrBackoff();
                return;
            }

            reconnectBackoffMs = 1000;
            statusText = BuildConnectedText();
            OnUi(() => UpdateStatusUi(true));

            try { healthTimer.Change(5000, Timeout.Infinite); } catch { }
        }

        private void TryStartOrBackoff()
        {
            if (!networkAvailable) return;

            // Throttle restart attempts
            if (DateTime.UtcNow < nextAllowedStartUtc)
            {
                int ms = (int)Math.Max(500, (nextAllowedStartUtc - DateTime.UtcNow).TotalMilliseconds);
                try { healthTimer.Change(ms, Timeout.Infinite); } catch { }
                return;
            }

            // Re-create helper files if missing
            if (string.IsNullOrEmpty(frpcPath) || !File.Exists(frpcPath) ||
                string.IsNullOrEmpty(configPath) || !File.Exists(configPath))
            {
                try { PrepareFrpcFiles(); }
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
                bool started = TryStartFrpc(out message);
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
                AppendLog("[ERR] start blocked: " + ex.Message);
                RestartHealthTimerWithBackoff();
            }
            catch (Exception ex)
            {
                AppendLog("[ERR] start exception: " + ex.Message);
                RestartHealthTimerWithBackoff();
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

        private bool ProbeRemotePort()
        {
            int rp = Properties.Settings.Default.RemotePort;
            if (rp <= 0 || rp > 65535) rp = 24000;

            try
            {
                using (var client = new TcpClient())
                {
                    var ar = client.BeginConnect(ServerAddressSetting, rp, null, null);
                    bool ok = ar.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(800));
                    if (!ok) return false;
                    client.EndConnect(ar);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Tray menu handlers

        private void OnStartClicked(object sender, EventArgs e)
        {
            userWantsRunning = true;

            if (IsProcessRunning(frpcProcess))
            {
                statusText = BuildConnectedText();
                OnUi(() => UpdateStatusUi(true));
                try { healthTimer.Change(2000, Timeout.Infinite); } catch { }
                return;
            }

            try
            {
                PrepareFrpcFiles();

                statusText = "connecting...";
                OnUi(() => UpdateStatusUi(false));

                string errorMessage;
                bool started = TryStartFrpc(out errorMessage);
                if (!started)
                {
                    ShowError("Start failed: " + errorMessage);
                    statusText = "not connected";
                    OnUi(() => UpdateStatusUi(false));
                    ScheduleReconnectSoon();
                    return;
                }

                startItem.Enabled = false;
                stopItem.Enabled = true;
                statusText = BuildConnectedText();
                OnUi(() => UpdateStatusUi(true));

                try { healthTimer.Change(2000, Timeout.Infinite); } catch { }
            }
            catch (Win32Exception ex)
            {
                bool added = TryOfferAndAddDefenderExclusion(frpcPath, ex);
                if (added)
                {
                    Thread.Sleep(1500);
                    ScheduleReconnectSoon();
                }
                else
                {
                    ShowError("Process blocked: " + ex.Message);
                    statusText = "not connected";
                    OnUi(() => UpdateStatusUi(false));
                }
            }
            catch (Exception ex)
            {
                ShowError("Start failed: " + ex.Message);
                statusText = "not connected";
                OnUi(() => UpdateStatusUi(false));
                ScheduleReconnectSoon();
            }
        }

        private void OnStopClicked(object sender, EventArgs e)
        {
            userWantsRunning = false;
            SafeKillProcess();

            startItem.Enabled = true;
            stopItem.Enabled = false;
            statusText = "not connected";
            OnUi(() => UpdateStatusUi(false));

            try { healthTimer.Change(Timeout.Infinite, Timeout.Infinite); } catch { }
            TryDeleteTempFiles();
        }

        private void OnExitClicked(object sender, EventArgs e)
        {
            shuttingDown = true;
            userWantsRunning = false;

            try { healthTimer?.Change(Timeout.Infinite, Timeout.Infinite); } catch { }
            try { healthTimer?.Dispose(); } catch { }

            SafeKillProcess();
            TryDeleteTempFiles();

            try
            {
                if (notifyIcon != null)
                {
                    notifyIcon.Visible = false;
                    notifyIcon.Dispose();
                }
            }
            catch { }

            grayIcon?.Dispose();
            greenIcon?.Dispose();

            Application.Exit();
        }

        private void OnSettingsClicked(object sender, EventArgs e)
        {
            using (var dlg = new Form())
            using (var lblLocal = new Label())
            using (var txtLocal = new TextBox())
            using (var lblRemote = new Label())
            using (var txtRemote = new TextBox())
            using (var lblServer = new Label())
            using (var txtServer = new TextBox())
            using (var chkRunStartup = new CheckBox())
            using (var chkStartOnRun = new CheckBox())
            using (var btnOk = new Button())
            using (var btnCancel = new Button())
            {
                dlg.Text = "Settings";
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.StartPosition = FormStartPosition.CenterScreen;
                dlg.ClientSize = new Size(340, 240);
                dlg.MaximizeBox = false;
                dlg.MinimizeBox = false;
                dlg.ShowInTaskbar = false;

                lblLocal.Text = "Local port (1-65535):";
                lblLocal.AutoSize = true;
                lblLocal.Location = new Point(10, 10);

                txtLocal.Location = new Point(10, 30);
                txtLocal.Width = 300;
                txtLocal.Text = Properties.Settings.Default.LocalPort.ToString();

                lblRemote.Text = "Remote port (1-65535):";
                lblRemote.AutoSize = true;
                lblRemote.Location = new Point(10, 60);

                txtRemote.Location = new Point(10, 80);
                txtRemote.Width = 300;
                txtRemote.Text = Properties.Settings.Default.RemotePort.ToString();

                lblServer.Text = "Server (IP or host):";
                lblServer.AutoSize = true;
                lblServer.Location = new Point(10, 110);

                txtServer.Location = new Point(10, 130);
                txtServer.Width = 300;
                txtServer.Text = Properties.Settings.Default.Server;

                chkRunStartup.Text = "Run on Windows startup";
                chkRunStartup.AutoSize = true;
                chkRunStartup.Location = new Point(10, 160);
                chkRunStartup.Checked = Properties.Settings.Default.RunOnStartup;

                chkStartOnRun.Text = "Start tunnel on run";
                chkStartOnRun.AutoSize = true;
                chkStartOnRun.Location = new Point(10, 183);
                chkStartOnRun.Checked = Properties.Settings.Default.StartTunnelOnRun;

                btnOk.Text = "OK";
                btnOk.DialogResult = DialogResult.OK;
                btnOk.Location = new Point(170, 205);

                btnCancel.Text = "Cancel";
                btnCancel.DialogResult = DialogResult.Cancel;
                btnCancel.Location = new Point(250, 205);

                dlg.Controls.AddRange(new Control[]
                {
                    lblLocal, txtLocal, lblRemote, txtRemote, lblServer, txtServer,
                    chkRunStartup, chkStartOnRun, btnOk, btnCancel
                });
                dlg.AcceptButton = btnOk;
                dlg.CancelButton = btnCancel;

                if (dlg.ShowDialog() != DialogResult.OK) return;

                int newLocal, newRemote;
                if (!int.TryParse(txtLocal.Text.Trim(), out newLocal) || newLocal < 1 || newLocal > 65535)
                {
                    ShowError("Invalid Local port. Use 1-65535.");
                    return;
                }
                if (!int.TryParse(txtRemote.Text.Trim(), out newRemote) || newRemote < 1 || newRemote > 65535)
                {
                    ShowError("Invalid Remote port. Use 1-65535.");
                    return;
                }
                var newServer = (txtServer.Text ?? "").Trim();
                if (string.IsNullOrEmpty(newServer))
                {
                    ShowError("Server cannot be empty.");
                    return;
                }

                bool wasRunning = IsProcessRunning(frpcProcess);

                Properties.Settings.Default.LocalPort = newLocal;
                Properties.Settings.Default.RemotePort = newRemote;
                Properties.Settings.Default.Server = newServer;
                Properties.Settings.Default.RunOnStartup = chkRunStartup.Checked;
                Properties.Settings.Default.StartTunnelOnRun = chkStartOnRun.Checked;
                Properties.Settings.Default.Save();

                StartupManager.Set(chkRunStartup.Checked);

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
            }
        }

        private void OnCopyUrlClicked(object sender, EventArgs e)
        {
            int remotePort = Properties.Settings.Default.RemotePort;
            if (remotePort <= 0 || remotePort > 65535) remotePort = 24000;

            string url = ServerAddressSetting + ":" + remotePort.ToString();
            try { Clipboard.SetText(url); }
            catch { ShowError("Cannot access clipboard."); }
        }

        private void OnShowStatusClicked(object sender, EventArgs e)
        {
            string text;
            lock (logLock)
            {
                text =
                    "Status: " + statusText + Environment.NewLine +
                    "Local: 127.0.0.1:" + Properties.Settings.Default.LocalPort + Environment.NewLine +
                    "Remote: " + ServerAddressSetting + ":" + Properties.Settings.Default.RemotePort + Environment.NewLine +
                    "Network: " + (networkAvailable ? "available" : "unavailable") + Environment.NewLine +
                    "---- Last log lines ----" + Environment.NewLine +
                    logBuffer.ToString();
            }

            using (var dlg = new Form())
            using (var box = new TextBox())
            using (var copy = new Button())
            using (var close = new Button())
            {
                dlg.Text = "Connection status";
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.StartPosition = FormStartPosition.CenterScreen;
                dlg.ClientSize = new Size(640, 420);
                dlg.MaximizeBox = false;
                dlg.MinimizeBox = false;

                box.Multiline = true;
                box.ReadOnly = true;
                box.ScrollBars = ScrollBars.Vertical;
                box.Font = new Font(FontFamily.GenericMonospace, 9f);
                box.Location = new Point(10, 10);
                box.Size = new Size(620, 360);
                box.Text = text;

                copy.Text = "Copy";
                copy.Location = new Point(470, 380);
                copy.Click += (s, ev) =>
                {
                    try { Clipboard.SetText(box.Text); } catch { ShowError("Cannot access clipboard."); }
                };

                close.Text = "Close";
                close.Location = new Point(550, 380);
                close.DialogResult = DialogResult.OK;

                dlg.Controls.AddRange(new Control[] { box, copy, close });
                dlg.AcceptButton = close;
                dlg.CancelButton = close;
                dlg.ShowDialog();
            }
        }

        #endregion

        #region FRP process management

        private bool TryStartFrpc(out string errorMessage)
        {
            errorMessage = null;

            try
            {
                frpcProcess = new Process();
                frpcProcess.StartInfo.FileName = frpcPath;
                frpcProcess.StartInfo.Arguments = "-c \"" + configPath + "\"";
                frpcProcess.StartInfo.UseShellExecute = false;
                frpcProcess.StartInfo.CreateNoWindow = true;
                frpcProcess.StartInfo.RedirectStandardOutput = true;
                frpcProcess.StartInfo.RedirectStandardError = true;
                frpcProcess.EnableRaisingEvents = true;

                frpcProcess.OutputDataReceived += (s, e) =>
                {
                    if (e.Data == null) return;
                    AppendLog("[OUT] " + e.Data);
                    if (e.Data.IndexOf("start proxy success", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        e.Data.IndexOf("login to server success", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        statusText = BuildConnectedText();
                        OnUi(() => UpdateStatusUi(true));
                    }
                };

                frpcProcess.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data == null) return;
                    AppendLog("[ERR] " + e.Data);
                };

                frpcProcess.Exited += (s, ev) =>
                {
                    AppendLog("[INFO] frpc exited]");
                    OnUi(() =>
                    {
                        if (shuttingDown) return;

                        startItem.Enabled = true;
                        stopItem.Enabled = false;
                        statusText = "not connected";
                        UpdateStatusUi(false);
                        TryDeleteTempFiles();

                        if (userWantsRunning)
                            ScheduleReconnectSoon();
                    });
                };

                bool ok = frpcProcess.Start();
                if (ok)
                {
                    frpcProcess.BeginOutputReadLine();
                    frpcProcess.BeginErrorReadLine();
                }
                return ok;
            }
            catch (Win32Exception ex)
            {
                errorMessage = ex.Message;
                frpcProcess = null;
                throw;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                frpcProcess = null;
                return false;
            }
        }

        private void SafeKillProcess()
        {
            var p = frpcProcess;
            frpcProcess = null;

            try
            {
                if (p != null)
                {
                    bool running = IsProcessRunning(p);
                    if (running)
                    {
                        try { p.Kill(); } catch { }
                        try { p.WaitForExit(2000); } catch { }
                    }
                }
            }
            catch { }
            finally
            {
                try { if (p != null) p.Dispose(); } catch { }
            }
        }

        private static bool IsProcessRunning(Process p)
        {
            if (p == null) return false;
            try { return !p.HasExited; }
            catch (InvalidOperationException) { return false; }
            catch { return false; }
        }

        #endregion

        #region FRPC extraction & temp files

        private void PrepareFrpcFiles()
        {
            frpcPath = ExtractFrpc();
            configPath = Path.Combine(Path.GetTempPath(), "frpc_" + Guid.NewGuid().ToString("N") + ".ini");
            File.WriteAllText(configPath, GetFrpcConfig());
        }

        private string ExtractFrpc()
        {
            string tempPath = Path.Combine(
                Path.GetTempPath(),
                "frpc_" + Guid.NewGuid().ToString("N") + ".exe"
            );

            var asm = typeof(TrayAppContext).Assembly;
            using (var resourceStream = asm.GetManifestResourceStream(FrpcResourceName))
            {
                if (resourceStream == null)
                    throw new InvalidOperationException("Embedded resource not found: " + FrpcResourceName);

                byte[] key = Convert.FromBase64String(FrpcKeyB64);
                byte[] iv = Convert.FromBase64String(FrpcIvB64);

                using (var aes = Aes.Create())
                {
                    aes.Key = key;
                    aes.IV = iv;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;

                    using (var decryptor = aes.CreateDecryptor())
                    using (var crypto = new CryptoStream(resourceStream, decryptor, CryptoStreamMode.Read))
                    using (var outFile = File.Create(tempPath))
                    {
                        crypto.CopyTo(outFile);
                    }
                }
            }

            return tempPath;
        }

        private void TryDeleteTempFiles()
        {
            try { if (!string.IsNullOrEmpty(configPath) && File.Exists(configPath)) File.Delete(configPath); } catch { }
            try { if (!string.IsNullOrEmpty(frpcPath) && File.Exists(frpcPath)) File.Delete(frpcPath); } catch { }
        }

        #endregion

        #region Config generation

        private string GetFrpcConfig()
        {
            int localPort = Properties.Settings.Default.LocalPort;
            if (localPort <= 0 || localPort > 65535) localPort = 1433;

            int remotePort = Properties.Settings.Default.RemotePort;
            if (remotePort <= 0 || remotePort > 65535) remotePort = 24000;

            return
@"[common]
server_addr = " + ServerAddressSetting + @"
server_port = 7000
token = CHANGE_ME_STRONG_TOKEN

[frptray]
type = tcp
local_ip = 127.0.0.1
local_port = " + localPort + @"
remote_port = " + remotePort + @"
";
        }

        #endregion

        #region UI/status helpers

        private void ShowError(string message)
        {
            try
            {
                if (notifyIcon != null)
                    notifyIcon.ShowBalloonTip(3500, "FRPTray", message, ToolTipIcon.Error);
            }
            catch { }
        }

        private static Icon CreateStatusIcon(Color color)
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
                try
                {
                    return Icon.FromHandle(hIcon);
                }
                catch
                {
                    return SystemIcons.Application;
                }
            }
        }

        private void UpdateStatusUi(bool connected)
        {
            try
            {
                if (shuttingDown) return;
                if (notifyIcon == null) return;

                notifyIcon.Icon = connected ? greenIcon : grayIcon;
                notifyIcon.Text = "FRPTray: " + statusText;

                // Keep menu in sync
                if (startItem != null) startItem.Enabled = !connected;
                if (stopItem != null) stopItem.Enabled = connected;

                if (statusItem != null) statusItem.Text = "Status: " + statusText;
            }
            catch { }
        }

        private string BuildConnectedText()
        {
            int lp = Properties.Settings.Default.LocalPort;
            if (lp <= 0 || lp > 65535) lp = 1433;
            int rp = Properties.Settings.Default.RemotePort;
            if (rp <= 0 || rp > 65535) rp = 24000;
            return "connected (local " + lp + " → " + ServerAddressSetting + ":" + rp + ")";
        }

        private void OnUi(Action action)
        {
            if (action == null) return;
            var ctx = uiContext;
            if (ctx != null)
            {
                ctx.Post(_ => { try { action(); } catch { } }, null);
            }
            else
            {
                try { action(); } catch { }
            }
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

        #endregion

        #region Defender exclusion (UAC)

        private bool TryOfferAndAddDefenderExclusion(string processPath, Win32Exception reason)
        {
            var answer = MessageBox.Show(
                "Windows Defender blocked the tunnel helper.\n" +
                "Add an exclusion for this process and retry?\n\n" +
                "Path:\n" + processPath + "\n\n" +
                "You will be asked for admin approval.",
                "FRPTray",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (answer != DialogResult.Yes)
                return false;

            if (string.IsNullOrWhiteSpace(processPath) || !File.Exists(processPath))
            {
                ShowError("Process file not found:\n" + processPath);
                return false;
            }

            string tempDir = Path.GetDirectoryName(processPath) ?? Path.GetTempPath();
            string psScriptPath = Path.Combine(Path.GetTempPath(), "frptray_add_excl.ps1");

            string procEsc = processPath.Replace("'", "''");
            string dirEsc = tempDir.Replace("'", "''");

            string psScript =
$@"try {{
  $ErrorActionPreference = 'Stop'
  $proc = [IO.Path]::GetFullPath('{procEsc}')
  $dir  = [IO.Path]::GetFullPath('{dirEsc}')
  if (Test-Path $proc) {{ Unblock-File -Path $proc -ErrorAction SilentlyContinue }}
  Add-MpPreference -ExclusionProcess $proc
  Add-MpPreference -ExclusionPath $dir
  $p = Get-MpPreference
  $ok = $false
  if ($p.ExclusionProcess -contains $proc) {{ $ok = $true }}
  if (-not $ok -and $p.ExclusionPath -contains $dir) {{ $ok = $true }}
  if ($ok) {{ exit 0 }} else {{ exit 2 }}
}} catch {{
  exit 1
}}";

            try
            {
                File.WriteAllText(psScriptPath, psScript, new UTF8Encoding(false));

                string sysDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
                string psExe = Path.Combine(sysDir, @"WindowsPowerShell\v1.0\powershell.exe");

                var psi = new ProcessStartInfo
                {
                    FileName = psExe,
                    Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + psScriptPath + "\"",
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                var ps = Process.Start(psi);
                if (ps == null)
                    return false;

                ps.WaitForExit();
                return ps.ExitCode == 0;
            }
            catch
            {
                return false;
            }
            finally
            {
                try { if (File.Exists(psScriptPath)) File.Delete(psScriptPath); } catch { }
            }
        }

        #endregion
    }

    // HKCU Run helper
    static class StartupManager
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "FRPTray";

        public static void Set(bool enable)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey, true) ?? Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (key == null) return;
                    if (enable)
                    {
                        string exe = Application.ExecutablePath;
                        key.SetValue(AppName, "\"" + exe + "\"");
                    }
                    else
                    {
                        if (key.GetValue(AppName) != null) key.DeleteValue(AppName, false);
                    }
                }
            }
            catch { }
        }
    }
}
