using System.Net.Sockets;
using System.Text.Json;
using IconicLauncher.Core.Models;
using IconicLauncher.Core.Utils;
using Serilog;

namespace IconicLauncher.Core.Services;

public sealed class WowStatusService : IWowStatusService
{
    private readonly HttpClient _http;

    public WowStatusService(HttpClient? http = null)
    {
        _http = http ?? LauncherConstants.CreateHttpClient(TimeSpan.FromSeconds(5));
    }

    public async Task<WowRealmStatus> QueryAsync(WowConfig config, CancellationToken ct = default)
    {
        var authTask = ProbeAsync(config.Ip, config.AuthPort, ct);
        var worldTask = ProbeAsync(config.Ip, config.WorldPort, ct);
        var countsTask = FetchCountsAsync(config.StatusUrl, ct);
        await Task.WhenAll(authTask, worldTask, countsTask).ConfigureAwait(false);
        var counts = countsTask.Result;
        return new WowRealmStatus
        {
            AuthOnline = authTask.Result,
            WorldOnline = worldTask.Result,
            Players = counts.players,
            MaxPlayers = counts.maxPlayers
        };
    }

    private static async Task<bool> ProbeAsync(string host, int port, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(host) || port <= 0) return false;
        try
        {
            using var client = new TcpClient();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(2));
            await client.ConnectAsync(host, port, timeoutCts.Token).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<(int? players, int? maxPlayers)> FetchCountsAsync(string? statusUrl, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(statusUrl)) return (null, null);
        try
        {
            var separator = statusUrl.Contains('?') ? "&" : "?";
            var json = await _http.GetStringAsync($"{statusUrl}{separator}t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}", ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json.TrimStart('\uFEFF'));
            int? players = doc.RootElement.TryGetProperty("players", out var p) && p.TryGetInt32(out var pv) ? pv : null;
            int? max = doc.RootElement.TryGetProperty("maxPlayers", out var m) && m.TryGetInt32(out var mv) ? mv : null;
            return (players, max);
        }
        catch (Exception ex)
        {
            Log.Debug("WoW status url fetch failed: {Message}", ex.Message);
            return (null, null);
        }
    }
}
