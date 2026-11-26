using System.Net;
using System.Text.Json;
using IconicLauncher.Core.Models;
using IconicLauncher.Core.Utils;
using Serilog;

namespace IconicLauncher.Core.Services;

public interface IPlayerHubService
{
    Task<PlayerHubSnapshot> LoadAsync(string steamId, string baseUrl, IReadOnlyList<string> boxes, CancellationToken ct);
    Task<Dictionary<string, Dictionary<string, HubAchievementInfo>>> LoadAchievementsAsync(string baseUrl, string box, CancellationToken ct);
}

public sealed class PlayerHubService : IPlayerHubService
{
    private readonly HttpClient _http = LauncherConstants.CreateHttpClient(TimeSpan.FromSeconds(15));

    public async Task<PlayerHubSnapshot> LoadAsync(string steamId, string baseUrl, IReadOnlyList<string> boxes, CancellationToken ct)
    {
        var snapshot = new PlayerHubSnapshot { SteamId = steamId, FetchedUtc = DateTime.UtcNow };
        var tasks = boxes.Select(async box =>
        {
            var data = new HubBoxData { Box = box };
            var index = await FetchAsync<HubIndex>(baseUrl, $"{box}/index.json", ct).ConfigureAwait(false);
            data.Index = index.Value;
            if (index.Reachable)
            {
                var player = await FetchAsync<HubPlayerFile>(baseUrl, $"{box}/{steamId}.json", ct).ConfigureAwait(false);
                data.Player = player.Value;
            }
            return (data, index.Reachable);
        }).ToList();
        foreach (var (data, reachable) in await Task.WhenAll(tasks).ConfigureAwait(false))
        {
            snapshot.Boxes.Add(data);
            snapshot.AnyReachable |= reachable;
        }
        return snapshot;
    }

    public async Task<Dictionary<string, Dictionary<string, HubAchievementInfo>>> LoadAchievementsAsync(string baseUrl, string box, CancellationToken ct)
    {
        var result = await FetchAsync<Dictionary<string, Dictionary<string, HubAchievementInfo>>>(baseUrl, $"{box}/achievements.json", ct).ConfigureAwait(false);
        return result.Value ?? new Dictionary<string, Dictionary<string, HubAchievementInfo>>();
    }

    private async Task<(T? Value, bool Reachable)> FetchAsync<T>(string baseUrl, string relative, CancellationToken ct) where T : class
    {
        try
        {
            if (IsLocal(baseUrl, out var root))
            {
                var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path)) return (null, Directory.Exists(Path.GetDirectoryName(path)));
                await using var file = File.OpenRead(path);
                return (await JsonSerializer.DeserializeAsync<T>(file, JsonDefaults.Options, ct).ConfigureAwait(false), true);
            }
            using var response = await _http.GetAsync(baseUrl.TrimEnd('/') + "/" + relative, ct).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound) return (null, true);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return (await JsonSerializer.DeserializeAsync<T>(stream, JsonDefaults.Options, ct).ConfigureAwait(false), true);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Player hub fetch failed for {Relative}", relative);
            return (null, false);
        }
    }

    private static bool IsLocal(string baseUrl, out string root)
    {
        if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            root = uri.LocalPath;
            return true;
        }
        root = baseUrl;
        return Path.IsPathRooted(baseUrl) && !baseUrl.Contains("://", StringComparison.Ordinal);
    }
}
