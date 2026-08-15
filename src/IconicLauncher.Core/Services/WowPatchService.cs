using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using IconicLauncher.Core.Models;
using IconicLauncher.Core.Utils;
using Serilog;

namespace IconicLauncher.Core.Services;

public sealed class WowPatchService : IWowPatchService
{
    private static readonly string[] ProtectedTopDirs = { "WTF", "Cache", "Screenshots", "Logs" };
    private static readonly Regex LocaleDirRegex = new(@"^[a-z]{2}[A-Z]{2}$", RegexOptions.Compiled);

    private readonly HttpClient _http;

    public WowPatchService(HttpClient? http = null)
    {
        _http = http ?? LauncherConstants.CreateHttpClient(TimeSpan.FromMinutes(10));
    }

    public static string? ValidateRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "empty path";
        if (path.Contains(':') || path.StartsWith('/') || path.StartsWith('\\')) return "absolute path not allowed";
        var segments = path.Replace('\\', '/').Split('/', StringSplitOptions.None);
        if (segments.Any(s => s.Length == 0)) return "empty path segment";
        if (segments.Any(s => s == "." || s == "..")) return "path traversal not allowed";
        if (ProtectedTopDirs.Any(d => string.Equals(segments[0], d, StringComparison.OrdinalIgnoreCase)))
            return $"'{segments[0]}' is a protected folder";
        return null;
    }

    public static string ToLocalPath(string clientRoot, string relativePath)
    {
        var combined = Path.GetFullPath(Path.Combine(clientRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var root = Path.GetFullPath(clientRoot);
        if (!combined.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Path escapes the game folder: {relativePath}");
        return combined;
    }

    public static bool IsValidClientRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        return File.Exists(Path.Combine(path, "Wow.exe")) && Directory.Exists(Path.Combine(path, "Data"));
    }

    public async Task<WowVerifyResult> VerifyAsync(WowConfig config, string clientRoot, CancellationToken ct = default)
    {
        var manifest = await FetchManifestAsync(config.ManifestUrl, ct).ConfigureAwait(false);
        var checks = await Task.Run(() => Diff(manifest, clientRoot), ct).ConfigureAwait(false);
        Log.Information("WoW verify: build {Build}, {Total} files, {Needed} to download ({Bytes} bytes), {Obsolete} obsolete",
            manifest.Build, checks.Count(c => c.State != WowFileState.Obsolete),
            checks.Count(c => c.State is WowFileState.Missing or WowFileState.Modified),
            checks.Where(c => c.State is WowFileState.Missing or WowFileState.Modified).Sum(c => c.SizeBytes),
            checks.Count(c => c.State == WowFileState.Obsolete));
        return new WowVerifyResult { Manifest = manifest, Files = checks };
    }

    public static IReadOnlyList<WowFileCheck> Diff(WowManifest manifest, string clientRoot)
    {
        var checks = new List<WowFileCheck>();
        foreach (var file in manifest.Files)
        {
            var invalid = ValidateRelativePath(file.Path);
            if (invalid != null)
            {
                Log.Warning("WoW manifest entry {Path} skipped: {Reason}", file.Path, invalid);
                continue;
            }
            var local = ToLocalPath(clientRoot, file.Path);
            var state = WowFileState.Ok;
            var info = new FileInfo(local);
            if (!info.Exists)
            {
                state = WowFileState.Missing;
            }
            else if (info.Length != file.SizeBytes || !string.Equals(HashFile(local), file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                state = WowFileState.Modified;
            }
            checks.Add(new WowFileCheck { Path = file.Path, State = state, SizeBytes = file.SizeBytes, Sha256 = file.Sha256 });
        }
        foreach (var path in manifest.Delete)
        {
            var invalid = ValidateRelativePath(path);
            if (invalid != null)
            {
                Log.Warning("WoW manifest delete entry {Path} skipped: {Reason}", path, invalid);
                continue;
            }
            if (File.Exists(ToLocalPath(clientRoot, path)))
            {
                checks.Add(new WowFileCheck { Path = path, State = WowFileState.Obsolete });
            }
        }
        return checks;
    }

    public Task ApplyAsync(WowConfig config, string clientRoot, WowVerifyResult result, IProgress<WowApplyProgress> progress, CancellationToken ct = default)
    {
        return ApplyInternalAsync(config, clientRoot, result, result.NeedsDownload.ToList(), progress, ct);
    }

    public Task RepairAsync(WowConfig config, string clientRoot, WowVerifyResult result, IProgress<WowApplyProgress> progress, CancellationToken ct = default)
    {
        var all = result.Files.Where(f => f.State != WowFileState.Obsolete).ToList();
        return ApplyInternalAsync(config, clientRoot, result, all, progress, ct);
    }

    private async Task<bool> DownloadSetAsync(string baseUrl, string clientRoot, List<WowFileCheck> downloads, IProgress<WowApplyProgress> progress, CancellationToken ct)
    {
        var totalBytes = downloads.Sum(f => f.SizeBytes);
        long doneBytes = 0;
        var doneFiles = 0;
        var changed = false;
        foreach (var file in downloads)
        {
            ct.ThrowIfCancellationRequested();
            var local = ToLocalPath(clientRoot, file.Path);
            var tmp = local + ".iconic-tmp";
            Directory.CreateDirectory(Path.GetDirectoryName(local)!);
            try
            {
                var url = BuildFileUrl(baseUrl, file.Path);
                var baseDone = doneBytes;
                await DownloadAsync(url, tmp, file, p =>
                {
                    progress.Report(new WowApplyProgress
                    {
                        CurrentFile = file.Path,
                        BytesDone = baseDone + p,
                        BytesTotal = totalBytes,
                        FilesDone = doneFiles,
                        FilesTotal = downloads.Count
                    });
                }, ct).ConfigureAwait(false);
                var hash = HashFile(tmp);
                if (!string.Equals(hash, file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Hash mismatch for {file.Path}");
                }
                File.Move(tmp, local, true);
                changed = true;
            }
            catch
            {
                TryDelete(tmp);
                throw;
            }
            doneBytes += file.SizeBytes;
            doneFiles++;
            progress.Report(new WowApplyProgress { CurrentFile = file.Path, BytesDone = doneBytes, BytesTotal = totalBytes, FilesDone = doneFiles, FilesTotal = downloads.Count });
        }
        return changed;
    }

    private async Task ApplyInternalAsync(WowConfig config, string clientRoot, WowVerifyResult result, List<WowFileCheck> downloads, IProgress<WowApplyProgress> progress, CancellationToken ct)
    {
        var changed = await DownloadSetAsync(config.FilesBaseUrl, clientRoot, downloads, progress, ct).ConfigureAwait(false);
        var doneFiles = downloads.Count;
        foreach (var file in result.NeedsDelete)
        {
            var local = ToLocalPath(clientRoot, file.Path);
            try
            {
                if (File.Exists(local))
                {
                    File.Delete(local);
                    changed = true;
                    Log.Information("WoW patch removed obsolete file {Path}", file.Path);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not remove obsolete file {Path}", file.Path);
            }
        }
        EnsureRealmlist(config, clientRoot);
        if (changed)
        {
            WipeCache(clientRoot);
        }
        Log.Information("WoW patch applied: build {Build}, {Files} files downloaded", result.Manifest.Build, doneFiles);
    }

    public async Task<IReadOnlyList<WowAddonStatus>> FetchAddonsAsync(WowConfig config, string clientRoot, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(config.AddonsUrl))
        {
            return Array.Empty<WowAddonStatus>();
        }
        var separator = config.AddonsUrl.Contains('?') ? "&" : "?";
        var url = $"{config.AddonsUrl}{separator}t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = (await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false)).TrimStart('\uFEFF');
        var list = JsonSerializer.Deserialize<WowAddonList>(json, JsonDefaults.Options)
            ?? throw new InvalidOperationException("The addon list deserialized to null");
        var statuses = await Task.Run(() => list.Addons.Select(a => DiffAddon(a, clientRoot)).ToList(), ct).ConfigureAwait(false);
        Log.Information("WoW addons: {Total} listed, {Installed} installed, {Updatable} with updates",
            statuses.Count, statuses.Count(s => s.State == WowAddonState.Installed), statuses.Count(s => s.State == WowAddonState.UpdateAvailable));
        return statuses;
    }

    public static WowAddonStatus DiffAddon(WowAddonEntry addon, string clientRoot)
    {
        var anyPresent = false;
        long bytesNeeded = 0;
        foreach (var file in addon.Files)
        {
            if (ValidateRelativePath(file.Path) != null) continue;
            var info = new FileInfo(ToLocalPath(clientRoot, file.Path));
            if (!info.Exists)
            {
                bytesNeeded += file.SizeBytes;
                continue;
            }
            anyPresent = true;
            if (info.Length != file.SizeBytes || !string.Equals(HashFile(info.FullName), file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                bytesNeeded += file.SizeBytes;
            }
        }
        var state = bytesNeeded == 0
            ? WowAddonState.Installed
            : anyPresent ? WowAddonState.UpdateAvailable : WowAddonState.NotInstalled;
        return new WowAddonStatus { Entry = addon, State = state, BytesToDownload = bytesNeeded };
    }

    public async Task InstallAddonAsync(WowConfig config, string clientRoot, WowAddonEntry addon, IProgress<WowApplyProgress> progress, CancellationToken ct = default)
    {
        var baseUrl = string.IsNullOrWhiteSpace(config.AddonsFilesBaseUrl) ? config.FilesBaseUrl : config.AddonsFilesBaseUrl;
        var downloads = new List<WowFileCheck>();
        foreach (var file in addon.Files)
        {
            var invalid = ValidateRelativePath(file.Path);
            if (invalid != null)
            {
                Log.Warning("Addon {Addon} entry {Path} skipped: {Reason}", addon.Id, file.Path, invalid);
                continue;
            }
            var info = new FileInfo(ToLocalPath(clientRoot, file.Path));
            if (info.Exists && info.Length == file.SizeBytes && string.Equals(HashFile(info.FullName), file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            downloads.Add(new WowFileCheck { Path = file.Path, State = info.Exists ? WowFileState.Modified : WowFileState.Missing, SizeBytes = file.SizeBytes, Sha256 = file.Sha256 });
        }
        await DownloadSetAsync(baseUrl, clientRoot, downloads, progress, ct).ConfigureAwait(false);
        Log.Information("WoW addon {Addon} installed/updated: {Files} files", addon.Id, downloads.Count);
    }

    public bool EnsureRealmlist(WowConfig config, string clientRoot)
    {
        if (string.IsNullOrWhiteSpace(config.Realmlist)) return false;
        var dataDir = Path.Combine(clientRoot, "Data");
        if (!Directory.Exists(dataDir)) return false;
        var content = config.Realmlist.Trim() + Environment.NewLine;
        var changed = false;
        foreach (var dir in Directory.EnumerateDirectories(dataDir))
        {
            var name = Path.GetFileName(dir);
            if (!LocaleDirRegex.IsMatch(name)) continue;
            var target = Path.Combine(dir, "realmlist.wtf");
            try
            {
                if (File.Exists(target) && string.Equals(File.ReadAllText(target).Trim(), config.Realmlist.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                AtomicFile.WriteAllTextAtomic(target, content);
                changed = true;
                Log.Information("Wrote realmlist for locale {Locale}", name);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Writing realmlist for locale {Locale} failed", name);
            }
        }
        return changed;
    }

    public static void WipeCache(string clientRoot)
    {
        var cache = Path.Combine(clientRoot, "Cache");
        if (!Directory.Exists(cache)) return;
        try
        {
            Directory.Delete(cache, true);
            Log.Information("WoW client cache wiped");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Wiping the WoW client cache failed");
        }
    }

    public static string BuildFileUrl(string baseUrl, string relativePath)
    {
        var segments = relativePath.Replace('\\', '/').Split('/').Select(Uri.EscapeDataString);
        return baseUrl.TrimEnd('/') + "/" + string.Join('/', segments);
    }

    private async Task<WowManifest> FetchManifestAsync(string manifestUrl, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(manifestUrl))
        {
            throw new InvalidOperationException("The WoW manifest URL is not configured");
        }
        var separator = manifestUrl.Contains('?') ? "&" : "?";
        var url = $"{manifestUrl}{separator}t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = (await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false)).TrimStart('\uFEFF');
        var manifest = JsonSerializer.Deserialize<WowManifest>(json, JsonDefaults.Options)
            ?? throw new InvalidOperationException("The WoW manifest deserialized to null");
        if (manifest.Files.Count == 0)
        {
            throw new InvalidOperationException("The WoW manifest lists no files");
        }
        return manifest;
    }

    private async Task DownloadAsync(string url, string targetPath, WowFileCheck file, Action<long> onBytes, CancellationToken ct)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var target = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
        var buffer = new byte[81920];
        long total = 0;
        long lastReport = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            total += read;
            if (total - lastReport >= 262144)
            {
                lastReport = total;
                onBytes(Math.Min(total, file.SizeBytes));
            }
        }
    }

    private static string HashFile(string path)
    {
        using var sha = SHA256.Create();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
        return Convert.ToHexString(sha.ComputeHash(stream));
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
        }
    }
}
