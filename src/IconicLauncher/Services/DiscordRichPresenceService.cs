using DiscordRPC;
using Serilog;

namespace IconicLauncher.Services;

public sealed class DiscordRichPresenceService : IDisposable
{
    private DiscordRpcClient? _client;

    public void Initialize(string? applicationId)
    {
        if (_client != null) return;
        if (string.IsNullOrWhiteSpace(applicationId)) return;
        try
        {
            _client = new DiscordRpcClient(applicationId);
            _client.Initialize();
            Log.Information("Discord Rich Presence initialized");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Discord Rich Presence init failed");
            _client = null;
        }
    }

    public void SetInLauncher()
    {
        SetPresence("In Launcher", null);
    }

    public void SetPlaying(string serverName)
    {
        SetPresence($"Playing on {serverName}", "Iconic PvE");
    }

    private void SetPresence(string details, string? state)
    {
        try
        {
            _client?.SetPresence(new RichPresence
            {
                Details = details,
                State = state,
                Timestamps = Timestamps.Now
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Discord Rich Presence update failed");
        }
    }

    public void Dispose()
    {
        try
        {
            _client?.ClearPresence();
            _client?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Discord Rich Presence dispose failed");
        }
        _client = null;
    }
}
