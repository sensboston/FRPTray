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
