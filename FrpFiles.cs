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
using System.IO;
using System.Text;

namespace FRPTray
{
    internal static class FrpFiles
    {
        private const string FrpcResourceNamePrimary = "FRPTray.frpc";
        private const string FrpcResourceNameAlt = "FRPTray.frpc.exe";

        public static void Prepare(out string frpcPath, out string configPath, string server, string serverPort, string token, int[] locals, int[] remotes)
        {
            frpcPath = ExtractFrpc();
            configPath = Path.Combine(Path.GetTempPath(), "frpc_" + Guid.NewGuid().ToString("N") + ".ini");
            File.WriteAllText(configPath, BuildConfig(server, serverPort, token, locals, remotes));
        }

        public static void TryDeleteTemp(string frpcPath, string configPath)
        {
            try { if (!string.IsNullOrEmpty(configPath) && File.Exists(configPath)) File.Delete(configPath); } catch { }
            try { if (!string.IsNullOrEmpty(frpcPath) && File.Exists(frpcPath)) File.Delete(frpcPath); } catch { }
        }

        private static string ExtractFrpc()
        {
            var tempPath = Path.Combine(Path.GetTempPath(), "frpc.exe");

            var asm = typeof(FrpFiles).Assembly;
            using (var resourceStream = asm.GetManifestResourceStream(FrpcResourceNamePrimary) ??
                                       asm.GetManifestResourceStream(FrpcResourceNameAlt))
            {
                if (resourceStream == null) throw new InvalidOperationException("Embedded resource not found: " + FrpcResourceNamePrimary);
                using (var outFile = File.Create(tempPath))
                    resourceStream.CopyTo(outFile);
            }
            return tempPath;
        }

        private static string BuildConfig(string server, string serverPort, string token, int[] locals, int[] remotes)
        {
            var sb = new StringBuilder();
            sb.AppendLine("[common]");
            sb.AppendLine("server_addr = " + server);
            sb.AppendLine("server_port = " + serverPort);
            sb.AppendLine("token = " + token);
            sb.AppendLine();

            for (int i = 0; i < locals.Length; i++)
            {
                sb.AppendLine("[frptray-" + (i + 1) + "]");
                sb.AppendLine("type = tcp");
                sb.AppendLine("local_ip = 127.0.0.1");
                sb.AppendLine("local_port = " + locals[i]);
                sb.AppendLine("remote_port = " + remotes[i]);
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }
}
