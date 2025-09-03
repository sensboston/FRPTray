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

namespace FRPTray
{
    internal static class UrlFormatter
    {
        public static string Format(string server, int localPort, int remotePort)
        {
            string scheme = "";
            if (localPort == 80) scheme = "http://";
            else if (localPort == 443) scheme = "https://";
            return scheme + server + ":" + remotePort;
        }
    }
}
