using IconicLauncher.Core.Models;
using Serilog;

namespace IconicLauncher.Core.Services;

public sealed class ModVerificationService : IModVerificationService
{
    private readonly WorkshopWebApi _webApi;
    private readonly IModIntegrityChecker _integrity;

    public ModVerificationService(WorkshopWebApi webApi, IModIntegrityChecker integrity)
    {
        _webApi = webApi;
        _integrity = integrity;
    }

    public async Task<IReadOnlyList<ModVerificationResult>> VerifyAsync(ServerEntry server, SteamEnvironment env, CancellationToken ct = default)
    {
        var results = server.Mods.Select(m => new ModVerificationResult { Mod = m, State = ModState.Unknown }).ToList();
        var acf = env.AcfPath != null ? WorkshopAcf.Load(env.AcfPath) : null;
        if (acf == null)
            Log.Warning("Workshop ACF unavailable at {Path}, treating all mods as not installed", env.AcfPath);

        Dictionary<string, PublishedFileDetails>? remote = null;
        try
        {
            remote = await _webApi.GetDetailsAsync(results.Select(r => r.Mod.WorkshopId), ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Steam Web API unreachable, falling back to ACF latest_manifest comparison");
        }

        foreach (var r in results)
        {
            var id = r.Mod.WorkshopId;
            var installed = acf?.Installed.GetValueOrDefault(id);
            var contentDir = env.WorkshopContentDir != null ? Path.Combine(env.WorkshopContentDir, id) : null;
            var contentOk = contentDir != null && DirectoryHasContent(contentDir);
            var acfDetails = acf?.Details.GetValueOrDefault(id);
            r.LocalManifest = installed?.Manifest;
            r.LocalTimeUpdated = installed?.TimeUpdated ?? 0;

            PublishedFileDetails? details = null;
            if (remote != null)
                remote.TryGetValue(id, out details);

            if (details != null)
            {
                r.RemoteManifest = details.HContentFile;
                r.RemoteFileSize = details.FileSize;
                r.RemoteTimeUpdated = details.TimeUpdated;
                if (details.Result != 1)
                {
                    r.State = ModState.Failed;
                    r.FailReason = "removed from Workshop";
                    continue;
                }
            }
            else if (remote != null)
            {
                r.State = ModState.Failed;
                r.FailReason = "removed from Workshop";
                continue;
            }
            else
            {
                r.RemoteManifest = acfDetails?.LatestManifest;
                r.RemoteTimeUpdated = acfDetails?.LatestTimeUpdated ?? 0;
            }

            if (installed == null || !contentOk)
            {
                r.State = ModState.NotInstalled;
                continue;
            }
            if (r.RemoteManifest != null && r.RemoteManifest != installed.Manifest)
            {
                r.State = ModState.Outdated;
                continue;
            }
            r.State = ModState.Ready;
            r.InstallPath = contentDir;
            var baseline = acfDetails != null && acfDetails.TimeTouched > 0 ? acfDetails.TimeTouched : installed.TimeUpdated;
            var issue = _integrity.Check(contentDir!, baseline);
            if (issue != null)
            {
                r.State = ModState.Damaged;
                r.IntegrityIssue = issue;
                Log.Warning("Integrity issue for {WorkshopId}: {Issue}", id, issue);
            }
        }
        foreach (var r in results.Where(x => x.State != ModState.Ready))
            Log.Debug("Verify {WorkshopId} ({Name}): state={State} local={Local} remote={Remote} reason={Reason}",
                r.Mod.WorkshopId, r.Mod.Name, r.State, r.LocalManifest, r.RemoteManifest, r.FailReason ?? r.IntegrityIssue);
        return results;
    }

    private static bool DirectoryHasContent(string path)
    {
        try
        {
            return Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to inspect content directory {Path}", path);
            return false;
        }
    }
}
