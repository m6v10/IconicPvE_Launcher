using IconicLauncher.Core.Models;

namespace IconicLauncher.Core.Services;

public interface IRemoteConfigService
{
    Task<ConfigResult> LoadAsync(CancellationToken ct = default);
}

public interface ISettingsService
{
    LauncherSettings Settings { get; }
    AdminSettings Admin { get; }
    string AppDataDir { get; }
    void Save();
    void SaveAdmin();
}

public interface ISteamLocator
{
    SteamEnvironment Locate(LauncherSettings settings);
}

public interface IModVerificationService
{
    Task<IReadOnlyList<ModVerificationResult>> VerifyAsync(ServerEntry server, SteamEnvironment env, CancellationToken ct = default);
}

public interface IWorkshopUpdateService
{
    Task<bool> EnsureModsReadyAsync(IList<ModVerificationResult> mods, SteamEnvironment env, IProgress<WorkshopProgress> progress, CancellationToken ct = default);
}

public interface IGameLauncher
{
    LaunchResult Launch(ServerEntry server, IReadOnlyList<ModVerificationResult> mods, SteamEnvironment env, LauncherSettings settings, string? password = null);
}

public interface IA2SQueryService
{
    Task<ServerStatus> QueryAsync(string ip, int queryPort, CancellationToken ct = default);
}

public interface ISelfUpdateService
{
    UpdateCheck Check(LauncherConfig config);
    Task<bool> ApplyAsync(UpdateCheck update, IProgress<double> progress, CancellationToken ct = default);
}

public interface IAdminConfigBuilder
{
    LauncherConfig Build(string workshopFolder, LauncherConfig template, string serverId);
}

public interface IFtpPublishService
{
    Task UploadAsync(AdminSettings admin, string localPath, string remoteFileName, IProgress<double> progress, CancellationToken ct = default);
}

public interface IServerModQueryService
{
    Task<IReadOnlyList<ModEntry>?> QueryModListAsync(string ip, int queryPort, CancellationToken ct = default);
}

public interface IModOperationsService
{
    Task<bool> UpdateModAsync(ModVerificationResult mod, SteamEnvironment env, IProgress<WorkshopProgress> progress, CancellationToken ct = default);
    Task<bool> ForceRebuildAsync(ModVerificationResult mod, SteamEnvironment env, IProgress<WorkshopProgress> progress, CancellationToken ct = default);
    Task<bool> DeleteModAsync(ModVerificationResult mod, SteamEnvironment env, CancellationToken ct = default);
}

public interface IModIntegrityChecker
{
    string? Check(string installPath, long baselineUnixTime);
}
