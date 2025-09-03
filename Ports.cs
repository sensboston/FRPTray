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
using System.Collections.Generic;

namespace FRPTray
{
    internal static class Ports
    {
        public static int[] ParseCsv(string csv)
        {
            if (string.IsNullOrWhiteSpace(csv)) return new int[0];
            var parts = csv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            var list = new List<int>(parts.Length);
            for (int i = 0; i < parts.Length; i++)
            {
                var s = parts[i].Trim();
                if (!int.TryParse(s, out int v) || v < 1 || v > 65535)
                    throw new ArgumentException("Invalid port in list: " + s);
                list.Add(v);
            }
            return list.ToArray();
        }

        public static void GetFromSettings(out int[] locals, out int[] remotes)
        {
            var lc = Properties.Settings.Default.LocalPort ?? "";
            var rc = Properties.Settings.Default.RemotePort ?? "";

            locals = ParseCsv(lc);
            remotes = ParseCsv(rc);

            if (locals.Length == 0 || remotes.Length == 0)
                throw new InvalidOperationException("Ports are not configured.");

            if (locals.Length != remotes.Length)
                throw new InvalidOperationException("Local/Remote ports count mismatch.");
        }
    }
}
