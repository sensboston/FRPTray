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
                int v;
                if (!int.TryParse(s, out v) || v < 1 || v > 65535)
                    throw new ArgumentException("Invalid port in list: " + s);
                list.Add(v);
            }
            return list.ToArray();
        }

        public static void GetFromSettings(out int[] locals, out int[] remotes)
        {
            string lc = Properties.Settings.Default.LocalPort ?? "";
            string rc = Properties.Settings.Default.RemotePort ?? "";

            locals = ParseCsv(lc);
            remotes = ParseCsv(rc);

            if (locals.Length == 0 || remotes.Length == 0)
                throw new InvalidOperationException("Ports are not configured.");

            if (locals.Length != remotes.Length)
                throw new InvalidOperationException("Local/Remote ports count mismatch.");
        }
    }
}
