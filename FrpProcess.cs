using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace FRPTray
{
    internal static class FrpProcess
    {
        public static bool TryStart(
            string frpcPath,
            string configPath,
            Action<string> onOut,
            Action<string> onErr,
            Action onExit,
            out Process process,
            out string errorMessage)
        {
            errorMessage = null;
            process = null;

            try
            {
                var p = new Process();
                p.StartInfo.FileName = frpcPath;
                p.StartInfo.Arguments = "-c \"" + configPath + "\"";
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardError = true;
                p.EnableRaisingEvents = true;

                p.OutputDataReceived += (s, e) => { if (e.Data != null) onOut?.Invoke(e.Data); };
                p.ErrorDataReceived += (s, e) => { if (e.Data != null) onErr?.Invoke(e.Data); };
                p.Exited += (s, e) => { try { onExit?.Invoke(); } catch { } };

                bool ok = p.Start();
                if (ok)
                {
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    process = p;
                }
                return ok;
            }
            catch (Win32Exception ex)
            {
                errorMessage = ex.Message;
                throw;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        public static void SafeKill(Process p)
        {
            var proc = p;
            try
            {
                if (proc != null && IsProcessRunning(proc))
                {
                    try { proc.Kill(); } catch { }
                    try { proc.WaitForExit(2000); } catch { }
                }
            }
            catch { }
            finally { try { if (proc != null) proc.Dispose(); } catch { } }
        }

        public static bool IsProcessRunning(Process p)
        {
            if (p == null) return false;
            try { return !p.HasExited; }
            catch { return false; }
        }

        public static void KillStaleFrpcProcesses()
        {
            try
            {
                string tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd('\\');
                var list = Process.GetProcesses();

                foreach (var p in list)
                {
                    bool kill = false;
                    try
                    {
                        string name = null;
                        try { name = p.ProcessName; } catch { }

                        if (!IsFrpcGuidName(name))
                            continue;

                        // Try to verify the executable path lives under %TEMP%
                        try
                        {
                            var mm = p.MainModule; // may throw for protected/system processes
                            if (mm != null && IsUnderTempFrpcExe(mm.FileName, tempRoot))
                                kill = true;
                            else
                                kill = true; // fallback by name only
                        }
                        catch
                        {
                            kill = true; // fallback by name only
                        }

                        if (kill)
                        {
                            try { p.Kill(); } catch { }
                            try { p.WaitForExit(1500); } catch { }
                        }
                    }
                    catch { }
                    finally
                    {
                        try { p.Dispose(); } catch { }
                    }
                }
            }
            catch { }
        }

        private static bool IsFrpcGuidName(string procName)
        {
            if (string.IsNullOrEmpty(procName)) return false;
            if (!procName.StartsWith("frpc_", StringComparison.OrdinalIgnoreCase)) return false;

            string core = procName.Substring("frpc_".Length);
            if (core.Length != 32) return false;

            for (int i = 0; i < core.Length; i++)
            {
                char c = core[i];
                bool hex = (c >= '0' && c <= '9') ||
                           (c >= 'a' && c <= 'f') ||
                           (c >= 'A' && c <= 'F');
                if (!hex) return false;
            }
            return true;
        }

        private static bool IsUnderTempFrpcExe(string exePath, string tempRoot)
        {
            if (string.IsNullOrEmpty(exePath)) return false;

            string dir, file, dirFull;
            try
            {
                dir = Path.GetDirectoryName(exePath) ?? "";
                file = Path.GetFileName(exePath) ?? "";
                dirFull = Path.GetFullPath(dir).TrimEnd('\\');
            }
            catch { return false; }

            if (!dirFull.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase)) return false;
            if (!file.StartsWith("frpc_", StringComparison.OrdinalIgnoreCase)) return false;
            if (!file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return false;

            int coreLen = file.Length - "frpc_".Length - ".exe".Length;
            if (coreLen != 32) return false;

            for (int i = 0; i < 32; i++)
            {
                char c = file["frpc_".Length + i];
                bool hex = (c >= '0' && c <= '9') ||
                           (c >= 'a' && c <= 'f') ||
                           (c >= 'A' && c <= 'F');
                if (!hex) return false;
            }
            return true;
        }
    }
}
