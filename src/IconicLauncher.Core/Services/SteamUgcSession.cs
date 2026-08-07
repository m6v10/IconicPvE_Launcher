using IconicLauncher.Core.Models;
using IconicLauncher.Core.Utils;
using Serilog;
using Steamworks;

namespace IconicLauncher.Core.Services;

public sealed class SteamUgcSession : IWorkshopUpdateService, IModOperationsService
{
    private const int PumpMs = 50;
    private const int PollMs = 250;
    private const int SubscribeBatchSize = 10;
    private const int DeleteAttempts = 3;
    private static readonly TimeSpan DeleteRetryDelay = TimeSpan.FromMilliseconds(500);
    private static readonly uint AppIdNumeric = uint.Parse(LauncherConstants.DayZAppId);
    private static readonly TimeSpan SubscribeBatchTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StallThreshold = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan DownloadWatchdog = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan DrainPollInterval = TimeSpan.FromSeconds(2);

    public async Task<bool> EnsureModsReadyAsync(IList<ModVerificationResult> mods, SteamEnvironment env, IProgress<WorkshopProgress> progress, CancellationToken ct = default)
    {
        if (env.WorkshopContentDir == null || env.AcfPath == null)
        {
            Log.Error("Workshop environment incomplete, cannot start UGC session");
            return false;
        }

        foreach (var mod in mods)
        {
            if ((mod.State == ModState.NotInstalled || mod.State == ModState.Outdated) && !ulong.TryParse(mod.Mod.WorkshopId, out _))
            {
                mod.State = ModState.Failed;
                mod.FailReason = "invalid workshop id";
            }
        }
        var targets = mods.Where(m => m.State == ModState.NotInstalled || m.State == ModState.Outdated).ToList();
        if (targets.Count == 0)
            return mods.All(m => m.State == ModState.Ready);

        if (!TryInitSteam())
            return false;
        Log.Information("SteamAPI initialized as app {AppId} for {Count} workshop items", LauncherConstants.DayZAppId, targets.Count);

        var steamAlive = true;
        var completed = new HashSet<ulong>();
        var failed = new Dictionary<ulong, EResult>();
        Callback<DownloadItemResult_t>? downloadCallback = null;
        try
        {
            downloadCallback = Callback<DownloadItemResult_t>.Create(result =>
            {
                if (result.m_unAppID.m_AppId != AppIdNumeric)
                    return;
                var id = result.m_nPublishedFileId.m_PublishedFileId;
                if (result.m_eResult == EResult.k_EResultOK)
                    completed.Add(id);
                else
                    failed[id] = result.m_eResult;
                Log.Information("DownloadItemResult for {WorkshopId}: {Result}", id, result.m_eResult);
            });

            await SubscribeMissingAsync(targets, progress, ct).ConfigureAwait(false);
            var finishedInSession = await DownloadAllAsync(targets, progress, completed, failed, ct).ConfigureAwait(false);
            if (!finishedInSession)
            {
                downloadCallback.Dispose();
                downloadCallback = null;
                SteamAPI.Shutdown();
                steamAlive = false;
                await DrainViaSteamClientAsync(targets, env, progress, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            downloadCallback?.Dispose();
            if (steamAlive)
                SteamAPI.Shutdown();
        }

        FinalizeVerification(targets, env, progress);
        var allReady = mods.All(m => m.State == ModState.Ready);
        Log.Information("UGC session finished, all ready: {AllReady}", allReady);
        return allReady;
    }

    private static async Task SubscribeMissingAsync(List<ModVerificationResult> targets, IProgress<WorkshopProgress> progress, CancellationToken ct)
    {
        var toSubscribe = targets.Where(m => m.State == ModState.NotInstalled).ToList();
        if (toSubscribe.Count == 0)
            return;
        Log.Information("Subscribing {Count} missing workshop items", toSubscribe.Count);
        for (var offset = 0; offset < toSubscribe.Count; offset += SubscribeBatchSize)
        {
            ct.ThrowIfCancellationRequested();
            var batch = toSubscribe.Skip(offset).Take(SubscribeBatchSize).ToList();
            var pending = new List<(ModVerificationResult Mod, CallResult<RemoteStorageSubscribePublishedFileResult_t> Call, TaskCompletionSource<EResult> Tcs)>();
            foreach (var mod in batch)
            {
                mod.State = ModState.Subscribing;
                var tcs = new TaskCompletionSource<EResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                CallResult<RemoteStorageSubscribePublishedFileResult_t>.APIDispatchDelegate handler =
                    (result, ioFailure) => tcs.TrySetResult(ioFailure ? EResult.k_EResultIOFailure : result.m_eResult);
                var call = CallResult<RemoteStorageSubscribePublishedFileResult_t>.Create(handler);
                var handle = SteamUGC.SubscribeItem(new PublishedFileId_t(ulong.Parse(mod.Mod.WorkshopId)));
                call.Set(handle, handler);
                pending.Add((mod, call, tcs));
            }
            Report(progress, targets, "subscribing");
            var deadline = DateTime.UtcNow + SubscribeBatchTimeout;
            while (pending.Any(p => !p.Tcs.Task.IsCompleted) && DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                SteamAPI.RunCallbacks();
                await Task.Delay(PumpMs, ct).ConfigureAwait(false);
            }
            foreach (var p in pending)
            {
                var result = p.Tcs.Task.IsCompletedSuccessfully ? p.Tcs.Task.Result : EResult.k_EResultTimeout;
                if (result != EResult.k_EResultOK)
                    Log.Warning("SubscribeItem for {WorkshopId} returned {Result}", p.Mod.Mod.WorkshopId, result);
                p.Call.Dispose();
            }
        }
    }

    private static async Task<bool> DownloadAllAsync(List<ModVerificationResult> targets, IProgress<WorkshopProgress> progress, HashSet<ulong> completed, Dictionary<ulong, EResult> failed, CancellationToken ct)
    {
        foreach (var mod in targets.Where(m => m.State != ModState.Failed))
        {
            var pid = new PublishedFileId_t(ulong.Parse(mod.Mod.WorkshopId));
            if (!SteamUGC.DownloadItem(pid, true))
            {
                mod.State = ModState.Failed;
                mod.FailReason = "Steam refused download request";
                Log.Warning("DownloadItem refused for {WorkshopId}", mod.Mod.WorkshopId);
                continue;
            }
            mod.State = ModState.Downloading;
            if (mod.BytesTotal == 0 && mod.RemoteFileSize > 0)
                mod.BytesTotal = (ulong)mod.RemoteFileSize;
        }
        Report(progress, targets, "downloading");

        var lastProgressAt = DateTime.UtcNow;
        var lastKey = ProgressKey(targets);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            for (var i = 0; i < PollMs / PumpMs; i++)
            {
                SteamAPI.RunCallbacks();
                await Task.Delay(PumpMs, ct).ConfigureAwait(false);
            }

            var anyPendingAtZero = false;
            foreach (var mod in targets.Where(m => m.State == ModState.Downloading))
            {
                var id = ulong.Parse(mod.Mod.WorkshopId);
                var pid = new PublishedFileId_t(id);
                var state = (EItemState)SteamUGC.GetItemState(pid);
                if (SteamUGC.GetItemDownloadInfo(pid, out var downloaded, out var total) && total > 0)
                {
                    mod.BytesDownloaded = downloaded;
                    mod.BytesTotal = total;
                }
                if (failed.TryGetValue(id, out var error))
                {
                    mod.State = ModState.Failed;
                    mod.FailReason = "download failed: " + error;
                    continue;
                }
                var doneByCallback = completed.Contains(id);
                var doneByState = state.HasFlag(EItemState.k_EItemStateInstalled)
                    && !state.HasFlag(EItemState.k_EItemStateNeedsUpdate)
                    && !state.HasFlag(EItemState.k_EItemStateDownloading)
                    && !state.HasFlag(EItemState.k_EItemStateDownloadPending);
                if (doneByCallback || doneByState)
                {
                    if (mod.BytesTotal > 0)
                        mod.BytesDownloaded = mod.BytesTotal;
                    mod.State = ModState.Verifying;
                    continue;
                }
                if (state.HasFlag(EItemState.k_EItemStateDownloadPending) && mod.BytesDownloaded == 0)
                    anyPendingAtZero = true;
            }

            var key = ProgressKey(targets);
            if (key != lastKey)
            {
                lastKey = key;
                lastProgressAt = DateTime.UtcNow;
            }
            Report(progress, targets, "downloading");

            if (targets.All(m => m.State != ModState.Downloading))
                return true;
            var stalledFor = DateTime.UtcNow - lastProgressAt;
            if (anyPendingAtZero && stalledFor > StallThreshold)
            {
                Log.Warning("Workshop downloads pending at zero bytes for {Seconds}s, switching to Steam client drain", (int)stalledFor.TotalSeconds);
                return false;
            }
            if (stalledFor > DownloadWatchdog)
            {
                Log.Warning("No workshop download progress for {Minutes} minutes, abandoning in-session wait", (int)DownloadWatchdog.TotalMinutes);
                return true;
            }
        }
    }

    private static async Task DrainViaSteamClientAsync(List<ModVerificationResult> targets, SteamEnvironment env, IProgress<WorkshopProgress> progress, CancellationToken ct)
    {
        Log.Information("Steam API released, polling ACF while the Steam client drains the download queue");
        var workshopRoot = Path.GetDirectoryName(env.AcfPath)!;
        var downloadsRoot = Path.Combine(workshopRoot, "downloads", LauncherConstants.DayZAppId);
        var deadline = DateTime.UtcNow + DrainTimeout;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var acf = WorkshopAcf.Load(env.AcfPath!);
            foreach (var mod in targets.Where(m => m.State == ModState.Downloading))
            {
                var id = mod.Mod.WorkshopId;
                var installed = acf?.Installed.GetValueOrDefault(id);
                var contentDir = Path.Combine(env.WorkshopContentDir!, id);
                var done = mod.RemoteManifest != null
                    ? installed?.Manifest == mod.RemoteManifest
                    : installed != null && DirectoryHasContent(contentDir);
                if (done)
                {
                    if (mod.BytesTotal > 0)
                        mod.BytesDownloaded = mod.BytesTotal;
                    mod.State = ModState.Verifying;
                    continue;
                }
                var partial = GetDirectorySize(Path.Combine(downloadsRoot, id));
                if (partial > 0)
                    mod.BytesDownloaded = (ulong)partial;
            }
            Report(progress, targets, "steam-drain");
            if (targets.All(m => m.State != ModState.Downloading))
                return;
            await Task.Delay(DrainPollInterval, ct).ConfigureAwait(false);
        }
        Log.Warning("Steam client drain timed out after {Minutes} minutes", (int)DrainTimeout.TotalMinutes);
    }

    private static void FinalizeVerification(List<ModVerificationResult> targets, SteamEnvironment env, IProgress<WorkshopProgress> progress)
    {
        foreach (var mod in targets.Where(m => m.State != ModState.Failed))
            mod.State = ModState.Verifying;
        Report(progress, targets, "verifying");
        var acf = WorkshopAcf.Load(env.AcfPath!);
        foreach (var mod in targets.Where(m => m.State == ModState.Verifying))
        {
            var id = mod.Mod.WorkshopId;
            var dir = Path.Combine(env.WorkshopContentDir!, id);
            var installed = acf?.Installed.GetValueOrDefault(id);
            mod.LocalManifest = installed?.Manifest;
            var manifestOk = mod.RemoteManifest != null ? installed?.Manifest == mod.RemoteManifest : installed != null;
            if (manifestOk && DirectoryHasContent(dir))
            {
                mod.State = ModState.Ready;
                mod.InstallPath = dir;
            }
            else
            {
                mod.State = ModState.Failed;
                mod.FailReason = installed == null
                    ? "not registered in workshop manifest after download"
                    : manifestOk
                        ? "content directory missing or empty"
                        : "manifest mismatch after download";
                Log.Warning("Verification failed for {WorkshopId}: {Reason}", id, mod.FailReason);
            }
        }
        Report(progress, targets, "verifying");
    }

    public async Task<bool> UpdateModAsync(ModVerificationResult mod, SteamEnvironment env, IProgress<WorkshopProgress> progress, CancellationToken ct = default)
    {
        if (env.WorkshopContentDir == null || env.AcfPath == null)
        {
            Log.Error("Workshop environment incomplete, cannot update {WorkshopId}", mod.Mod.WorkshopId);
            return false;
        }
        if (!ulong.TryParse(mod.Mod.WorkshopId, out _))
        {
            mod.State = ModState.Failed;
            mod.FailReason = "invalid workshop id";
            return false;
        }
        ReportSingle(progress, mod, "updating");

        var removed = await RefreshRemoteDetailsAsync(mod, ct).ConfigureAwait(false);
        if (removed)
        {
            mod.State = ModState.Failed;
            mod.FailReason = "removed from Workshop";
            ReportSingle(progress, mod, "updating");
            return false;
        }

        var acf = WorkshopAcf.Load(env.AcfPath);
        var installed = acf?.Installed.GetValueOrDefault(mod.Mod.WorkshopId);
        var contentDir = Path.Combine(env.WorkshopContentDir, mod.Mod.WorkshopId);
        var manifestDiffers = mod.RemoteManifest != null && installed?.Manifest != mod.RemoteManifest;
        var needsWork = manifestDiffers
            || mod.State is ModState.Outdated or ModState.NotInstalled
            || installed == null
            || !DirectoryHasContent(contentDir);
        Log.Debug("UpdateModAsync {WorkshopId}: state={State} localManifest={Local} remoteManifest={Remote} acfManifest={Acf} manifestDiffers={Differs} dirHasContent={HasContent} needsWork={NeedsWork}",
            mod.Mod.WorkshopId, mod.State, mod.LocalManifest, mod.RemoteManifest, installed?.Manifest, manifestDiffers, DirectoryHasContent(contentDir), needsWork);
        if (!needsWork)
        {
            mod.State = ModState.Ready;
            mod.InstallPath = contentDir;
            ReportSingle(progress, mod, "updating");
            return true;
        }

        if (!TryInitSteam())
        {
            mod.State = ModState.Failed;
            mod.FailReason = "SteamAPI init failed";
            return false;
        }
        try
        {
            return await DownloadAndVerifySingleAsync(mod, env, progress, "updating", ct).ConfigureAwait(false);
        }
        finally
        {
            SteamAPI.Shutdown();
        }
    }

    public async Task<bool> ForceRebuildAsync(ModVerificationResult mod, SteamEnvironment env, IProgress<WorkshopProgress> progress, CancellationToken ct = default)
    {
        if (env.WorkshopContentDir == null || env.AcfPath == null)
        {
            Log.Error("Workshop environment incomplete, cannot rebuild {WorkshopId}", mod.Mod.WorkshopId);
            return false;
        }
        if (!ulong.TryParse(mod.Mod.WorkshopId, out _))
        {
            mod.State = ModState.Failed;
            mod.FailReason = "invalid workshop id";
            return false;
        }
        ReportSingle(progress, mod, "rebuilding");

        var contentDir = mod.InstallPath ?? Path.Combine(env.WorkshopContentDir, mod.Mod.WorkshopId);
        if (!await DeleteDirectoryWithRetriesAsync(contentDir, ct).ConfigureAwait(false))
        {
            mod.State = ModState.Failed;
            mod.FailReason = "could not remove content directory";
            return false;
        }
        var downloadsDir = Path.Combine(Path.GetDirectoryName(env.AcfPath)!, "downloads", LauncherConstants.DayZAppId, mod.Mod.WorkshopId);
        await DeleteDirectoryWithRetriesAsync(downloadsDir, ct).ConfigureAwait(false);

        mod.State = ModState.NotInstalled;
        mod.InstallPath = null;
        mod.LocalManifest = null;
        mod.BytesDownloaded = 0;
        ReportSingle(progress, mod, "rebuilding");

        if (!TryInitSteam())
        {
            mod.State = ModState.Failed;
            mod.FailReason = "SteamAPI init failed";
            return false;
        }
        try
        {
            return await DownloadAndVerifySingleAsync(mod, env, progress, "rebuilding", ct).ConfigureAwait(false);
        }
        finally
        {
            SteamAPI.Shutdown();
        }
    }

    public async Task<bool> DeleteModAsync(ModVerificationResult mod, SteamEnvironment env, CancellationToken ct = default)
    {
        if (env.WorkshopContentDir == null)
        {
            Log.Error("Workshop environment incomplete, cannot delete {WorkshopId}", mod.Mod.WorkshopId);
            return false;
        }
        if (!ulong.TryParse(mod.Mod.WorkshopId, out var id))
        {
            mod.State = ModState.Failed;
            mod.FailReason = "invalid workshop id";
            return false;
        }

        var unsubscribed = false;
        if (TryInitSteam())
        {
            try
            {
                var result = await UnsubscribeSingleAsync(new PublishedFileId_t(id), ct).ConfigureAwait(false);
                unsubscribed = result == EResult.k_EResultOK;
                if (!unsubscribed)
                    Log.Warning("UnsubscribeItem for {WorkshopId} returned {Result}", id, result);
            }
            finally
            {
                SteamAPI.Shutdown();
            }
        }

        var contentDir = mod.InstallPath ?? Path.Combine(env.WorkshopContentDir, mod.Mod.WorkshopId);
        var deleted = await DeleteDirectoryWithRetriesAsync(contentDir, ct).ConfigureAwait(false);

        mod.State = ModState.NotInstalled;
        mod.InstallPath = null;
        mod.LocalManifest = null;
        mod.LocalTimeUpdated = 0;
        mod.BytesDownloaded = 0;
        Log.Information("Delete for {WorkshopId}: unsubscribed={Unsubscribed} deleted={Deleted}", id, unsubscribed, deleted);
        return unsubscribed && deleted;
    }

    private static bool TryInitSteam()
    {
        Environment.SetEnvironmentVariable("SteamAppId", LauncherConstants.DayZAppId);
        Environment.SetEnvironmentVariable("SteamGameId", LauncherConstants.DayZAppId);
        try
        {
            if (!SteamAPI.Init())
            {
                Log.Warning("SteamAPI.Init failed, in-process workshop session unavailable");
                return false;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SteamAPI.Init threw, Steam integration unavailable");
            return false;
        }
        return true;
    }

    private static async Task<bool> RefreshRemoteDetailsAsync(ModVerificationResult mod, CancellationToken ct)
    {
        try
        {
            var api = new WorkshopWebApi();
            var details = await api.GetDetailsAsync(new[] { mod.Mod.WorkshopId }, ct).ConfigureAwait(false);
            if (details.TryGetValue(mod.Mod.WorkshopId, out var d))
            {
                if (d.Result != 1)
                    return true;
                mod.RemoteManifest = d.HContentFile;
                mod.RemoteFileSize = d.FileSize;
                mod.RemoteTimeUpdated = d.TimeUpdated;
                Log.Debug("Remote details for {WorkshopId}: manifest={Manifest} size={Size} timeUpdated={TimeUpdated}", mod.Mod.WorkshopId, d.HContentFile, d.FileSize, d.TimeUpdated);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Remote details refresh failed for {WorkshopId}, using last known manifest", mod.Mod.WorkshopId);
        }
        return false;
    }

    private static async Task<bool> DownloadAndVerifySingleAsync(ModVerificationResult mod, SteamEnvironment env, IProgress<WorkshopProgress> progress, string phase, CancellationToken ct)
    {
        var id = ulong.Parse(mod.Mod.WorkshopId);
        var pid = new PublishedFileId_t(id);
        var completed = false;
        var failure = EResult.k_EResultOK;
        using var callback = Callback<DownloadItemResult_t>.Create(result =>
        {
            if (result.m_unAppID.m_AppId != AppIdNumeric || result.m_nPublishedFileId.m_PublishedFileId != id)
                return;
            if (result.m_eResult == EResult.k_EResultOK)
                completed = true;
            else
                failure = result.m_eResult;
            Log.Information("DownloadItemResult for {WorkshopId}: {Result}", id, result.m_eResult);
        });

        var subscribeResult = await SubscribeSingleAsync(pid, ct).ConfigureAwait(false);
        if (subscribeResult != EResult.k_EResultOK)
            Log.Warning("SubscribeItem for {WorkshopId} returned {Result}", id, subscribeResult);
        else
            Log.Debug("SubscribeItem OK for {WorkshopId}", id);

        Log.Debug("Item state for {WorkshopId} before DownloadItem: {State}", id, (EItemState)SteamUGC.GetItemState(pid));
        if (!SteamUGC.DownloadItem(pid, true))
        {
            mod.State = ModState.Failed;
            mod.FailReason = "Steam refused download request";
            ReportSingle(progress, mod, phase);
            return false;
        }
        mod.State = ModState.Downloading;
        if (mod.BytesTotal == 0 && mod.RemoteFileSize > 0)
            mod.BytesTotal = (ulong)mod.RemoteFileSize;
        ReportSingle(progress, mod, phase);

        var lastProgressAt = DateTime.UtcNow;
        var lastBytes = mod.BytesDownloaded;
        var lastLoggedState = (EItemState)(-1);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            for (var i = 0; i < PollMs / PumpMs; i++)
            {
                SteamAPI.RunCallbacks();
                await Task.Delay(PumpMs, ct).ConfigureAwait(false);
            }
            var state = (EItemState)SteamUGC.GetItemState(pid);
            if (state != lastLoggedState)
            {
                Log.Debug("Item state changed for {WorkshopId}: {State}, downloaded={Downloaded}/{Total}", id, state, mod.BytesDownloaded, mod.BytesTotal);
                lastLoggedState = state;
            }
            if (SteamUGC.GetItemDownloadInfo(pid, out var downloaded, out var total) && total > 0)
            {
                mod.BytesDownloaded = downloaded;
                mod.BytesTotal = total;
            }
            if (failure != EResult.k_EResultOK)
            {
                mod.State = ModState.Failed;
                mod.FailReason = "download failed: " + failure;
                ReportSingle(progress, mod, phase);
                return false;
            }
            var doneByState = state.HasFlag(EItemState.k_EItemStateInstalled)
                && !state.HasFlag(EItemState.k_EItemStateNeedsUpdate)
                && !state.HasFlag(EItemState.k_EItemStateDownloading)
                && !state.HasFlag(EItemState.k_EItemStateDownloadPending);
            if (completed || doneByState)
            {
                if (mod.BytesTotal > 0)
                    mod.BytesDownloaded = mod.BytesTotal;
                mod.State = ModState.Verifying;
                ReportSingle(progress, mod, phase);
                break;
            }
            if (mod.BytesDownloaded != lastBytes)
            {
                lastBytes = mod.BytesDownloaded;
                lastProgressAt = DateTime.UtcNow;
            }
            ReportSingle(progress, mod, phase);
            if (DateTime.UtcNow - lastProgressAt > DownloadWatchdog)
            {
                Log.Warning("No download progress for {WorkshopId} in {Minutes} minutes, verifying current state", id, (int)DownloadWatchdog.TotalMinutes);
                mod.State = ModState.Verifying;
                break;
            }
        }

        var acf = WorkshopAcf.Load(env.AcfPath!);
        var installed = acf?.Installed.GetValueOrDefault(mod.Mod.WorkshopId);
        var dir = Path.Combine(env.WorkshopContentDir!, mod.Mod.WorkshopId);
        mod.LocalManifest = installed?.Manifest;
        var manifestOk = mod.RemoteManifest != null ? installed?.Manifest == mod.RemoteManifest : installed != null;
        if (manifestOk && DirectoryHasContent(dir))
        {
            mod.State = ModState.Ready;
            mod.InstallPath = dir;
            if (installed != null)
                mod.LocalTimeUpdated = installed.TimeUpdated;
            ReportSingle(progress, mod, phase);
            return true;
        }
        mod.State = ModState.Failed;
        mod.FailReason = installed == null
            ? "not registered in workshop manifest after download"
            : manifestOk
                ? "content directory missing or empty"
                : "manifest mismatch after download";
        Log.Warning("Verification failed for {WorkshopId}: {Reason}", id, mod.FailReason);
        ReportSingle(progress, mod, phase);
        return false;
    }

    private static async Task<EResult> SubscribeSingleAsync(PublishedFileId_t pid, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<EResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        CallResult<RemoteStorageSubscribePublishedFileResult_t>.APIDispatchDelegate handler =
            (result, ioFailure) => tcs.TrySetResult(ioFailure ? EResult.k_EResultIOFailure : result.m_eResult);
        var call = CallResult<RemoteStorageSubscribePublishedFileResult_t>.Create(handler);
        call.Set(SteamUGC.SubscribeItem(pid), handler);
        try
        {
            return await PumpUntilResultAsync(tcs, ct).ConfigureAwait(false);
        }
        finally
        {
            call.Dispose();
        }
    }

    private static async Task<EResult> UnsubscribeSingleAsync(PublishedFileId_t pid, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<EResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        CallResult<RemoteStorageUnsubscribePublishedFileResult_t>.APIDispatchDelegate handler =
            (result, ioFailure) => tcs.TrySetResult(ioFailure ? EResult.k_EResultIOFailure : result.m_eResult);
        var call = CallResult<RemoteStorageUnsubscribePublishedFileResult_t>.Create(handler);
        call.Set(SteamUGC.UnsubscribeItem(pid), handler);
        try
        {
            return await PumpUntilResultAsync(tcs, ct).ConfigureAwait(false);
        }
        finally
        {
            call.Dispose();
        }
    }

    private static async Task<EResult> PumpUntilResultAsync(TaskCompletionSource<EResult> tcs, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + SubscribeBatchTimeout;
        while (!tcs.Task.IsCompleted && DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            SteamAPI.RunCallbacks();
            await Task.Delay(PumpMs, ct).ConfigureAwait(false);
        }
        return tcs.Task.IsCompletedSuccessfully ? tcs.Task.Result : EResult.k_EResultTimeout;
    }

    private static async Task<bool> DeleteDirectoryWithRetriesAsync(string path, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= DeleteAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (!Directory.Exists(path))
                    return true;
                Directory.Delete(path, true);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Delete attempt {Attempt} of {Max} failed for {Path}", attempt, DeleteAttempts, path);
                if (attempt < DeleteAttempts)
                    await Task.Delay(DeleteRetryDelay, ct).ConfigureAwait(false);
            }
        }
        return !Directory.Exists(path);
    }

    private static void ReportSingle(IProgress<WorkshopProgress> progress, ModVerificationResult mod, string phase)
    {
        var total = mod.BytesTotal;
        if (total == 0 && mod.RemoteFileSize > 0)
            total = (ulong)mod.RemoteFileSize;
        if (total < mod.BytesDownloaded)
            total = mod.BytesDownloaded;
        progress.Report(new WorkshopProgress
        {
            TotalMods = 1,
            CompletedMods = mod.State is ModState.Ready or ModState.Failed ? 1 : 0,
            BytesDownloaded = mod.BytesDownloaded,
            BytesTotal = total,
            CurrentModName = mod.Mod.Name,
            Phase = phase
        });
    }

    private static void Report(IProgress<WorkshopProgress> progress, List<ModVerificationResult> targets, string phase)
    {
        ulong downloadedSum = 0;
        ulong totalSum = 0;
        foreach (var mod in targets)
        {
            downloadedSum += mod.BytesDownloaded;
            var total = mod.BytesTotal;
            if (total == 0 && mod.RemoteFileSize > 0)
                total = (ulong)mod.RemoteFileSize;
            if (total < mod.BytesDownloaded)
                total = mod.BytesDownloaded;
            totalSum += total;
        }
        var current = targets.FirstOrDefault(m => m.State == ModState.Downloading && m.BytesDownloaded > 0)
            ?? targets.FirstOrDefault(m => m.State == ModState.Downloading || m.State == ModState.Subscribing);
        progress.Report(new WorkshopProgress
        {
            TotalMods = targets.Count,
            CompletedMods = targets.Count(m => m.State is ModState.Verifying or ModState.Ready or ModState.Failed),
            BytesDownloaded = downloadedSum,
            BytesTotal = totalSum,
            CurrentModName = current?.Mod.Name,
            Phase = phase
        });
    }

    private static string ProgressKey(List<ModVerificationResult> targets)
    {
        ulong bytes = 0;
        var active = 0;
        foreach (var mod in targets)
        {
            bytes += mod.BytesDownloaded;
            if (mod.State == ModState.Downloading)
                active++;
        }
        return bytes + ":" + active;
    }

    private static bool DirectoryHasContent(string path)
    {
        try
        {
            return Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any();
        }
        catch
        {
            return false;
        }
    }

    private static long GetDirectorySize(string path)
    {
        try
        {
            if (!Directory.Exists(path))
                return 0;
            long size = 0;
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                size += new FileInfo(file).Length;
            return size;
        }
        catch
        {
            return 0;
        }
    }
}
