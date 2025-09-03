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
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace FRPTray
{
    internal static class FrpProcess
    {
        public static bool TryStart(string frpcPath, string configPath, Action<string> onOut, Action<string> onErr,
                                    Action onExit, out Process process, out string errorMessage)
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
            finally { try { proc?.Dispose(); } catch { } }
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
                var list = Process.GetProcessesByName("frpc");

                foreach (var p in list)
                {
                    try
                    {
                        var mm = p.MainModule;
                        if (mm == null) continue;

                        string exePath = mm.FileName ?? string.Empty;
                        string dirFull;
                        try { dirFull = Path.GetFullPath(Path.GetDirectoryName(exePath) ?? "").TrimEnd('\\'); }
                        catch { continue; }

                        if (!exePath.EndsWith("frpc.exe", StringComparison.OrdinalIgnoreCase)) continue;
                        if (!dirFull.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase)) continue;

                        try { p.Kill(); } catch { }
                        try { p.WaitForExit(1500); } catch { }
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
    }
}
