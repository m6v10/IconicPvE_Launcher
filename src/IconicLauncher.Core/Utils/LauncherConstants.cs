namespace IconicLauncher.Core.Utils;

public static class LauncherConstants
{
    public const string DayZAppId = "221100";
    public const string AppDataFolderName = "IconicLauncher";
    /// <summary>
    /// Served out of the WordPress uploads folder: FTP /public_html/wp-content/uploads/launcher
    /// maps to /wp-content/uploads/launcher on the site (public_html is the web root).
    /// </summary>
    public const string DefaultConfigUrl = "https://iconic-pve.com/wp-content/uploads/launcher/launcher-config.json";

    /// <summary>
    /// Sent on every outbound request. Shared hosts commonly reject requests with no User-Agent
    /// via mod_security, which would silently degrade the launcher to cached/embedded config.
    /// </summary>
    public const string UserAgent = "IconicPvE-Launcher";

    public static HttpClient CreateHttpClient(TimeSpan? timeout = null)
    {
        var client = new HttpClient();
        if (timeout.HasValue)
        {
            client.Timeout = timeout.Value;
        }
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"{UserAgent}/{ClientVersion}");
        return client;
    }

    private static string ClientVersion =>
        System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "1.0.0";
}
