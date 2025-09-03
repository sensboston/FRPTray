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

using Microsoft.Win32;
using System.Windows.Forms;

namespace FRPTray
{
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
