using System.IO;
using System.Net.Http;
using IconicLauncher.Core.Utils;
using Serilog;

namespace IconicLauncher.Services;

public static class HeroVideoCache
{
    public static async Task<Uri?> EnsureAsync(string url, string appDataDir, CancellationToken ct)
    {
        try
        {
            if (Path.IsPathRooted(url) && File.Exists(url)) return new Uri(url);
            var name = Path.GetFileName(new Uri(url).AbsolutePath);
            if (string.IsNullOrWhiteSpace(name) || !name.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)) return null;
            var dir = Path.Combine(appDataDir, "media");
            Directory.CreateDirectory(dir);
            var target = Path.Combine(dir, name);
            if (File.Exists(target)) return new Uri(target);
            var part = target + ".part";
            using (var http = LauncherConstants.CreateHttpClient(TimeSpan.FromMinutes(10)))
            using (var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var file = File.Create(part);
                await source.CopyToAsync(file, ct).ConfigureAwait(false);
            }
            File.Move(part, target, true);
            foreach (var old in Directory.GetFiles(dir, "dayz_hero_*.mp4"))
            {
                if (!string.Equals(old, target, StringComparison.OrdinalIgnoreCase))
                {
                    TryDelete(old);
                }
            }
            Log.Information("Hero video cached at {Path}", target);
            return new Uri(target);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Hero video download failed for {Url}", url);
            return null;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Deleting an old hero video failed");
        }
    }
}
