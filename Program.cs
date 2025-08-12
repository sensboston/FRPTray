using System;
using System.Windows.Forms;
using Bluegrams.Application;
using FRPTray.Properties;

namespace FRPTray
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            PortableSettingsProvider.SettingsFileName = "FRPTray.config";
            PortableSettingsProvider.ApplyProvider(Settings.Default);

            if (Environment.OSVersion.Version.Major >= 6)
                SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TrayAppContext());
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool SetProcessDpiAwarenessContext(int dpiFlag);
        // DPI awareness constants
        const int DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4;
    }
}
