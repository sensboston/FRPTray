using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace FRPTray
{
    internal static class FrpFiles
    {
        private const string FrpcKeyB64 = "qUEpGDPK/+ftBlq4ThCDQvuPOdHsQnsnp2KtEJsuN+k=";
        private const string FrpcIvB64 = "JLlT8v+R+wUJH3PvF/D1wQ==";
        private const string FrpcResourceName = "FRPTray.frpc.enc";

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
            string tempPath = Path.Combine(Path.GetTempPath(), "frpc_" + Guid.NewGuid().ToString("N") + ".exe");

            var asm = typeof(FrpFiles).Assembly;
            using (var resourceStream = asm.GetManifestResourceStream(FrpcResourceName))
            {
                if (resourceStream == null) throw new InvalidOperationException("Embedded resource not found: " + FrpcResourceName);

                byte[] key = Convert.FromBase64String(FrpcKeyB64);
                byte[] iv = Convert.FromBase64String(FrpcIvB64);

                using (var aes = Aes.Create())
                {
                    aes.Key = key; aes.IV = iv; aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;
                    using (var decryptor = aes.CreateDecryptor())
                    using (var crypto = new CryptoStream(resourceStream, decryptor, CryptoStreamMode.Read))
                    using (var outFile = File.Create(tempPath))
                        crypto.CopyTo(outFile);
                }
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
