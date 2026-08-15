namespace IconicLauncher.Core.Models;

public sealed class WowConfig
{
    public string Name { get; set; } = "";
    public string ClientVersion { get; set; } = "";
    public string Ip { get; set; } = "";
    public int AuthPort { get; set; } = 3724;
    public int WorldPort { get; set; } = 8085;
    public string Realmlist { get; set; } = "";
    public string ManifestUrl { get; set; } = "";
    public string FilesBaseUrl { get; set; } = "";
    public string? StatusUrl { get; set; }
    public string? FullClientUrl { get; set; }
    public long FullClientSizeBytes { get; set; }
    public string? FullManifestUrl { get; set; }
    public string? FullFilesBaseUrl { get; set; }
    public string? AddonsUrl { get; set; }
    public string? AddonsFilesBaseUrl { get; set; }
    public List<NewsItem>? News { get; set; }
}

public sealed class WowAddonList
{
    public int SchemaVersion { get; set; } = 1;
    public string? GeneratedUtc { get; set; }
    public List<WowAddonEntry> Addons { get; set; } = new();
}

public sealed class WowAddonEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string? ImageUrl { get; set; }
    public List<WowManifestFile> Files { get; set; } = new();
}

public enum WowAddonState
{
    NotInstalled,
    UpdateAvailable,
    Installed
}

public sealed class WowAddonStatus
{
    public required WowAddonEntry Entry { get; init; }
    public required WowAddonState State { get; init; }
    public long BytesToDownload { get; init; }
}

public sealed class WowManifest
{
    public int SchemaVersion { get; set; } = 1;
    public string? GeneratedUtc { get; set; }
    public string Build { get; set; } = "";
    public List<WowManifestFile> Files { get; set; } = new();
    public List<string> Delete { get; set; } = new();
}

public sealed class WowManifestFile
{
    public string Path { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
}

public enum WowFileState
{
    Ok,
    Missing,
    Modified,
    Obsolete
}

public sealed class WowFileCheck
{
    public required string Path { get; init; }
    public required WowFileState State { get; init; }
    public long SizeBytes { get; init; }
    public string Sha256 { get; init; } = "";
}

public sealed class WowVerifyResult
{
    public required WowManifest Manifest { get; init; }
    public required IReadOnlyList<WowFileCheck> Files { get; init; }
    public IEnumerable<WowFileCheck> NeedsDownload => Files.Where(f => f.State is WowFileState.Missing or WowFileState.Modified);
    public IEnumerable<WowFileCheck> NeedsDelete => Files.Where(f => f.State == WowFileState.Obsolete);
    public long BytesToDownload => NeedsDownload.Sum(f => f.SizeBytes);
    public bool UpToDate => Files.All(f => f.State == WowFileState.Ok);
}

public sealed class WowApplyProgress
{
    public string? CurrentFile { get; init; }
    public long BytesDone { get; init; }
    public long BytesTotal { get; init; }
    public int FilesDone { get; init; }
    public int FilesTotal { get; init; }
}

public sealed class WowRealmStatus
{
    public bool AuthOnline { get; init; }
    public bool WorldOnline { get; init; }
    public int? Players { get; init; }
    public int? MaxPlayers { get; init; }
    public bool Online => AuthOnline && WorldOnline;
}
