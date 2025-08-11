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

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TrayAppContext());
        }
    }
}
