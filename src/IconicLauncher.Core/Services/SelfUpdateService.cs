using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using IconicLauncher.Core.Models;
using IconicLauncher.Core.Utils;
using Serilog;

namespace IconicLauncher.Core.Services;

public sealed class SelfUpdateService : ISelfUpdateService
{
    private readonly string _appDataDir;
    private readonly HttpClient _http;

    public SelfUpdateService(string appDataDir, HttpClient? http = null)
    {
        _appDataDir = appDataDir;
        _http = http ?? LauncherConstants.CreateHttpClient();
    }

    public UpdateCheck Check(LauncherConfig config)
    {
        var latest = config.Launcher.LatestVersion;
        var current = CurrentVersion();
        var available = IsNewer(latest, current);
        Log.Information("Update check: current {Current}, latest {Latest}, available {Available}", current, latest, available);
        return new UpdateCheck
        {
            UpdateAvailable = available,
            LatestVersion = latest,
            DownloadUrl = config.Launcher.DownloadUrl,
            Sha256 = config.Launcher.Sha256,
            Changelog = config.Launcher.Changelog
        };
    }

    public async Task<bool> ApplyAsync(UpdateCheck update, IProgress<double> progress, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(update.DownloadUrl) || string.IsNullOrWhiteSpace(update.Sha256))
            {
                Log.Error("Update is missing download url or sha256");
                return false;
            }
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exePath))
            {
                Log.Error("Cannot determine running executable path");
                return false;
            }
            var updateDir = Path.Combine(_appDataDir, "update");
            Directory.CreateDirectory(updateDir);
            var stagedPath = Path.Combine(updateDir, (update.LatestVersion ?? "latest") + ".tmp");
            await DownloadAsync(update.DownloadUrl, stagedPath, progress, ct).ConfigureAwait(false);
            var hash = await ComputeSha256Async(stagedPath, ct).ConfigureAwait(false);
            if (!string.Equals(hash, update.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                Log.Error("Update hash mismatch: expected {Expected}, got {Actual}", update.Sha256, hash);
                TryDelete(stagedPath);
                return false;
            }
            var bakPath = exePath + ".bak";
            TryDelete(bakPath);
            File.Move(exePath, bakPath);
            try
            {
                File.Copy(stagedPath, exePath, true);
            }
            catch
            {
                File.Move(bakPath, exePath);
                throw;
            }
            TryDelete(stagedPath);
            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = $"--after-update \"{bakPath}\"",
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? ""
            });
            Log.Information("Update {Version} applied, relaunching", update.LatestVersion);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Self update failed");
            return false;
        }
    }

    public static bool IsNewer(string latest, string current)
    {
        if (!Version.TryParse(latest, out var latestVersion))
        {
            return false;
        }
        if (!Version.TryParse(current, out var currentVersion))
        {
            return false;
        }
        return latestVersion > currentVersion;
    }

    public static string CurrentVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plusIndex = informational.IndexOf('+');
            return plusIndex > 0 ? informational[..plusIndex] : informational;
        }
        return assembly.GetName().Version?.ToString() ?? "0.0.0";
    }

    public static void CleanupAfterUpdate(string bakPath)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (!File.Exists(bakPath))
                {
                    return;
                }
                File.Delete(bakPath);
                return;
            }
            catch (Exception ex)
            {
                if (attempt == 4)
                {
                    Log.Warning("Failed to delete {BakPath} after update: {Message}", bakPath, ex.Message);
                }
                else
                {
                    Thread.Sleep(200);
                }
            }
        }
    }

    public static void SweepStaleFiles(string appDataDir)
    {
        try
        {
            var updateDir = Path.Combine(appDataDir, "update");
            if (!Directory.Exists(updateDir))
            {
                return;
            }
            foreach (var file in Directory.EnumerateFiles(updateDir, "*.tmp"))
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception ex)
                {
                    Log.Warning("Failed to sweep stale file {File}: {Message}", file, ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning("Stale file sweep failed: {Message}", ex.Message);
        }
    }

    private async Task DownloadAsync(string url, string destination, IProgress<double> progress, CancellationToken ct)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? 0L;
        await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
        var buffer = new byte[81920];
        long downloaded = 0;
        int read;
        while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            downloaded += read;
            if (total > 0)
            {
                progress.Report(Math.Min(1.0, (double)downloaded / total));
            }
        }
        progress.Report(1.0);
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            Log.Warning("Failed to delete {Path}: {Message}", path, ex.Message);
        }
    }
}
