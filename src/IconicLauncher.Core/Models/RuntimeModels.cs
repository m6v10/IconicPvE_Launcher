namespace IconicLauncher.Core.Models;

public enum ConfigSource
{
    Live,
    Cached,
    Embedded
}

public sealed class ConfigResult
{
    public required LauncherConfig Config { get; init; }
    public required ConfigSource Source { get; init; }
}

public sealed class LauncherSettings
{
    public string ProfileName { get; set; } = "Survivor";
    public string ExtraLaunchParams { get; set; } = "";
    public string? DayZPathOverride { get; set; }
    public string? SteamPathOverride { get; set; }
    public string? ConfigUrlOverride { get; set; }
    public bool AutoConnect { get; set; } = true;
    public bool CloseOnLaunch { get; set; } = true;
    public bool DiscordRpcEnabled { get; set; } = true;
    public bool LaunchNoSplash { get; set; } = true;
    public bool LaunchNoPause { get; set; } = true;
    public bool LaunchWindowed { get; set; }
    public bool LaunchDoLogs { get; set; }
    public string? LastSelectedServerId { get; set; }
    public double WindowWidth { get; set; } = 1100;
    public double WindowHeight { get; set; } = 700;
    public List<string> FavoriteModIds { get; set; } = new();
}

public sealed class AdminSettings
{
    public string FtpHost { get; set; } = "";
    public int FtpPort { get; set; } = 21;
    public string FtpUser { get; set; } = "";
    public string FtpPasswordProtected { get; set; } = "";
    public string RemotePath { get; set; } = "/";
    public string? WorkshopFolderOverride { get; set; }
}

public sealed class ServerStatus
{
    public bool Online { get; init; }
    public string? Name { get; init; }
    public string? Map { get; init; }
    public int Players { get; init; }
    public int MaxPlayers { get; init; }
    public int PingMs { get; init; }
    public bool PasswordProtected { get; init; }
}

public sealed class SteamEnvironment
{
    public string? SteamRoot { get; init; }
    public string? LibraryRoot { get; init; }
    public string? DayZDir { get; init; }
    public string? WorkshopContentDir { get; init; }
    public string? AcfPath { get; init; }
    public bool DayZFound => DayZDir != null && WorkshopContentDir != null;
}

public enum ModState
{
    Unknown,
    NotInstalled,
    Outdated,
    Subscribing,
    Downloading,
    Verifying,
    Ready,
    Failed,
    Damaged
}

public sealed class ModVerificationResult
{
    public required ModEntry Mod { get; init; }
    public ModState State { get; set; }
    public string? InstallPath { get; set; }
    public string? FailReason { get; set; }
    public string? LocalManifest { get; set; }
    public string? RemoteManifest { get; set; }
    public long RemoteFileSize { get; set; }
    public ulong BytesDownloaded { get; set; }
    public ulong BytesTotal { get; set; }
    public long LocalTimeUpdated { get; set; }
    public long RemoteTimeUpdated { get; set; }
    public string? IntegrityIssue { get; set; }
}

public sealed class WorkshopProgress
{
    public int TotalMods { get; init; }
    public int CompletedMods { get; init; }
    public ulong BytesDownloaded { get; init; }
    public ulong BytesTotal { get; init; }
    public string? CurrentModName { get; init; }
    public string? Phase { get; init; }
}

public sealed class UpdateCheck
{
    public bool UpdateAvailable { get; init; }
    public string? LatestVersion { get; init; }
    public string? DownloadUrl { get; init; }
    public string? Sha256 { get; init; }
    public string? Changelog { get; init; }
}

public sealed class LaunchResult
{
    public bool Started { get; init; }
    public string? Error { get; init; }
    public int ProcessId { get; init; }
}
