using System.Text.Json;
using IconicLauncher.Core.Models;
using IconicLauncher.Core.Utils;
using Serilog;

namespace IconicLauncher.Core.Services;

public sealed class RemoteConfigService : IRemoteConfigService
{
    public const int SupportedSchemaVersion = 1;

    private readonly string _configUrl;
    private readonly string _appDataDir;
    private readonly Func<string?> _embeddedJsonProvider;
    private readonly HttpClient _http;

    public RemoteConfigService(string configUrl, string appDataDir, Func<string?> embeddedJsonProvider, HttpClient? http = null)
    {
        _configUrl = configUrl;
        _appDataDir = appDataDir;
        _embeddedJsonProvider = embeddedJsonProvider;
        _http = http ?? LauncherConstants.CreateHttpClient(TimeSpan.FromSeconds(5));
    }

    private string CachePath => Path.Combine(_appDataDir, "cached-config.json");

    public async Task<ConfigResult> LoadAsync(CancellationToken ct = default)
    {
        var live = await TryLoadLiveAsync(ct).ConfigureAwait(false);
        if (live is not null)
        {
            return new ConfigResult { Config = live, Source = ConfigSource.Live };
        }
        var cached = TryLoadCached();
        if (cached is not null)
        {
            return new ConfigResult { Config = cached, Source = ConfigSource.Cached };
        }
        var embedded = TryLoadEmbedded();
        if (embedded is not null)
        {
            return new ConfigResult { Config = embedded, Source = ConfigSource.Embedded };
        }
        throw new InvalidOperationException("Launcher configuration unavailable from live, cached and embedded sources");
    }

    private async Task<LauncherConfig?> TryLoadLiveAsync(CancellationToken ct)
    {
        try
        {
            var separator = _configUrl.Contains('?') ? "&" : "?";
            var url = $"{_configUrl}{separator}t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
            using var response = await _http.GetAsync(url, timeoutCts.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
            var config = ParseAndValidate(json, "live");
            if (config is null)
            {
                return null;
            }
            PersistCache(json);
            Log.Information("Loaded live launcher config from {Url}", _configUrl);
            return config;
        }
        catch (Exception ex)
        {
            Log.Warning("Live config fetch failed: {Message}", ex.Message);
            return null;
        }
    }

    private LauncherConfig? TryLoadCached()
    {
        try
        {
            if (!File.Exists(CachePath))
            {
                return null;
            }
            var json = File.ReadAllText(CachePath);
            var config = ParseAndValidate(json, "cache");
            if (config is not null)
            {
                Log.Information("Loaded cached launcher config from {Path}", CachePath);
            }
            return config;
        }
        catch (Exception ex)
        {
            Log.Warning("Cached config load failed: {Message}", ex.Message);
            return null;
        }
    }

    private LauncherConfig? TryLoadEmbedded()
    {
        try
        {
            var json = _embeddedJsonProvider();
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }
            var config = ParseAndValidate(json, "embedded");
            if (config is not null)
            {
                Log.Information("Loaded embedded fallback launcher config");
            }
            return config;
        }
        catch (Exception ex)
        {
            Log.Warning("Embedded config load failed: {Message}", ex.Message);
            return null;
        }
    }

    private void PersistCache(string json)
    {
        try
        {
            AtomicFile.WriteAllTextAtomic(CachePath, json);
        }
        catch (Exception ex)
        {
            Log.Warning("Failed to persist config cache: {Message}", ex.Message);
        }
    }

    private static LauncherConfig? ParseAndValidate(string json, string source)
    {
        try
        {
            // Editing the config in Notepad or a cPanel editor can prepend a UTF-8 BOM, which
            // System.Text.Json rejects outright. Losing live config over an invisible character
            // would degrade silently to the cached copy, so strip it rather than fail.
            json = json.TrimStart('\uFEFF');
            var config = JsonSerializer.Deserialize<LauncherConfig>(json, JsonDefaults.Options);
            if (config is null)
            {
                Log.Warning("Config from {Source} deserialized to null", source);
                return null;
            }
            if (config.SchemaVersion > SupportedSchemaVersion)
            {
                Log.Warning("Config from {Source} has unsupported schemaVersion {Version}", source, config.SchemaVersion);
                return null;
            }
            if (config.Servers is null)
            {
                Log.Warning("Config from {Source} has no servers list", source);
                return null;
            }
            return config;
        }
        catch (JsonException ex)
        {
            Log.Warning("Config from {Source} failed to parse: {Message}", source, ex.Message);
            return null;
        }
    }
}
