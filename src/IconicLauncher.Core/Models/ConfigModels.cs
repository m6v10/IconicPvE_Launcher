namespace IconicLauncher.Core.Models;

public sealed class LauncherConfig
{
    public int SchemaVersion { get; set; } = 1;
    public string? GeneratedUtc { get; set; }
    public string WebsiteUrl { get; set; } = "";
    public LauncherInfo Launcher { get; set; } = new();
    public DiscordInfo Discord { get; set; } = new();
    public List<ServerEntry> Servers { get; set; } = new();
    public List<NewsItem> News { get; set; } = new();
}

public sealed class LauncherInfo
{
    public string LatestVersion { get; set; } = "1.0.0";
    public string DownloadUrl { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string Changelog { get; set; } = "";
}

public sealed class DiscordInfo
{
    public string InviteUrl { get; set; } = "";
    public string ApplicationId { get; set; } = "";
}

public sealed class ServerEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Ip { get; set; } = "";
    public int GamePort { get; set; }
    public int QueryPort { get; set; }
    public bool ModsFromQuery { get; set; }
    public bool Optional { get; set; }
    public int RestartIntervalHours { get; set; }
    public int RestartAnchorHour { get; set; } = 1;
    public string RestartTimeZone { get; set; } = "Europe/Zurich";
    public List<ModEntry> Mods { get; set; } = new();
}

public sealed class ModEntry
{
    public string WorkshopId { get; set; } = "";
    public string Name { get; set; } = "";
}

public sealed class NewsItem
{
    public string Id { get; set; } = "";
    public string Date { get; set; } = "";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string? Url { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsNew { get; set; }
}
