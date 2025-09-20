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

        public static void Prepare(out string frpcPath, out string configPath, string server, string serverPort, string token, string proxyPrefix, int[] locals, int[] remotes)
        {
            frpcPath = ExtractFrpc();
            // Changed extension from .ini to .toml for modern config format
            configPath = Path.Combine(Path.GetTempPath(), "frpc_" + Guid.NewGuid().ToString("N") + ".toml");
            File.WriteAllText(configPath, BuildConfig(server, serverPort, token, proxyPrefix, locals, remotes));
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

        private static string BuildConfig(string server, string serverPort, string token, string proxyPrefix, int[] locals, int[] remotes)
        {
            // Ensure proxy prefix is valid
            if (string.IsNullOrWhiteSpace(proxyPrefix))
            {
                // Fallback to machine name if prefix is empty
                proxyPrefix = Environment.MachineName.ToLower().Replace(" ", "-");
            }

            // Sanitize the prefix to ensure it's valid for frpc
            proxyPrefix = System.Text.RegularExpressions.Regex.Replace(proxyPrefix, @"[^a-zA-Z0-9\-]", "-").ToLower();

            var sb = new StringBuilder();

            // Generate TOML format configuration
            // Server configuration section
            sb.AppendLine("serverAddr = \"" + server + "\"");
            sb.AppendLine("serverPort = " + serverPort);

            // Authentication
            sb.AppendLine("auth.method = \"token\"");
            sb.AppendLine("auth.token = \"" + token + "\"");
            sb.AppendLine();

            // Proxy configurations with unique prefix
            sb.AppendLine("[[proxies]]");

            for (int i = 0; i < locals.Length; i++)
            {
                if (i > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("[[proxies]]");
                }

                // Use prefix to create unique proxy names across different clients
                sb.AppendLine("name = \"" + proxyPrefix + "-" + (i + 1) + "\"");
                sb.AppendLine("type = \"tcp\"");
                sb.AppendLine("localIP = \"127.0.0.1\"");
                sb.AppendLine("localPort = " + locals[i]);
                sb.AppendLine("remotePort = " + remotes[i]);
            }

            return sb.ToString();
        }
    }
}