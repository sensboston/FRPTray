using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace FRPTray
{
    internal static class DefenderExclusion
    {
        public static bool TryOfferAndAdd(string processPath, Win32Exception reason, Action<string> showError)
        {
            var answer = MessageBox.Show(
                "Windows Defender blocked the tunnel helper.\n" +
                "Add an exclusion for this process and retry?\n\n" +
                "Path:\n" + processPath + "\n\n" +
                "You will be asked for admin approval.",
                "FRPTray",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (answer != DialogResult.Yes) return false;

            if (string.IsNullOrWhiteSpace(processPath) || !File.Exists(processPath))
            {
                if (showError != null) showError("Process file not found:\n" + processPath);
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
                string psExe = System.IO.Path.Combine(sysDir, @"WindowsPowerShell\v1.0\powershell.exe");

                var psi = new ProcessStartInfo
                {
                    FileName = psExe,
                    Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + psScriptPath + "\"",
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                var ps = Process.Start(psi);
                if (ps == null) return false;

                ps.WaitForExit();
                return ps.ExitCode == 0;
            }
            catch { return false; }
            finally { try { if (File.Exists(psScriptPath)) File.Delete(psScriptPath); } catch { } }
        }
    }
}
