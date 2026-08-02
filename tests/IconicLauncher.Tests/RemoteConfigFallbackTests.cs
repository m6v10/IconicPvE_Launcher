using IconicLauncher.Core.Models;
using IconicLauncher.Core.Services;

namespace IconicLauncher.Tests;

public sealed class RemoteConfigFallbackTests : IDisposable
{
    private const string UnreachableUrl = "http://127.0.0.1:1/launcher-config.json";
    private readonly string _dir;

    public RemoteConfigFallbackTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "IconicLauncherTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string ConfigJson(int schemaVersion) => $$"""
        {
          "schemaVersion": {{schemaVersion}},
          "generatedUtc": "2026-08-01T00:00:00Z",
          "launcher": {
            "latestVersion": "1.0.0",
            "downloadUrl": "",
            "sha256": "",
            "changelog": ""
          },
          "discord": {
            "inviteUrl": "",
            "applicationId": ""
          },
          "servers": [
            {
              "id": "eu1",
              "name": "Iconic PvE - EU 1",
              "ip": "0.0.0.0",
              "gamePort": 2302,
              "queryPort": 2303,
              "mods": [
                { "workshopId": "1559212036", "name": "CF" },
                { "workshopId": "3077736647", "name": "Iconic Server Pack Core" }
              ]
            },
            {
              "id": "test",
              "name": "Iconic PvE - Test Server",
              "ip": "0.0.0.0",
              "gamePort": 2702,
              "queryPort": 2703,
              "mods": []
            }
          ],
          "news": [
            {
              "id": "welcome",
              "date": "2026-08-01",
              "title": "Welcome to the Iconic PvE Launcher",
              "body": "Verify your mods and join with one click.",
              "url": null
            }
          ]
        }
        """;

    private string CachePath => Path.Combine(_dir, "cached-config.json");

    [Fact]
    public async Task UnreachableUrlWithValidCacheReturnsCached()
    {
        File.WriteAllText(CachePath, ConfigJson(1));
        var svc = new RemoteConfigService(UnreachableUrl, _dir, () => null);
        var result = await svc.LoadAsync();
        Assert.Equal(ConfigSource.Cached, result.Source);
        Assert.Equal(2, result.Config.Servers.Count);
        Assert.Equal("eu1", result.Config.Servers[0].Id);
    }

    [Fact]
    public async Task UnreachableUrlWithoutCacheFallsToEmbedded()
    {
        var svc = new RemoteConfigService(UnreachableUrl, _dir, () => ConfigJson(1));
        var result = await svc.LoadAsync();
        Assert.Equal(ConfigSource.Embedded, result.Source);
        Assert.Equal(2, result.Config.Servers.Count);
        Assert.Equal("1559212036", result.Config.Servers[0].Mods[0].WorkshopId);
    }

    [Fact]
    public async Task AllThreeSourcesMissingThrows()
    {
        var svc = new RemoteConfigService(UnreachableUrl, _dir, () => null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.LoadAsync());
    }

    [Fact]
    public async Task UnsupportedSchemaVersionInCacheFallsToEmbedded()
    {
        File.WriteAllText(CachePath, ConfigJson(99));
        var svc = new RemoteConfigService(UnreachableUrl, _dir, () => ConfigJson(1));
        var result = await svc.LoadAsync();
        Assert.Equal(ConfigSource.Embedded, result.Source);
        Assert.Equal(RemoteConfigService.SupportedSchemaVersion, result.Config.SchemaVersion);
    }

    [Fact]
    public async Task ConfigStringWithUtf8BomStillParses()
    {
        // Notepad and cPanel's editor happily prepend a BOM on save, and
        // HttpContent.ReadAsStringAsync hands the resulting U+FEFF straight through.
        // System.Text.Json rejects it outright, which would silently drop a perfectly
        // good config and strand every player on the cached copy.
        // Note this goes through the embedded provider on purpose: the cache path reads
        // via File.ReadAllText, which strips a BOM itself, so it cannot exercise this.
        var svc = new RemoteConfigService(UnreachableUrl, _dir, () => "\uFEFF" + ConfigJson(1));
        var result = await svc.LoadAsync();
        Assert.Equal(ConfigSource.Embedded, result.Source);
        Assert.Equal(2, result.Config.Servers.Count);
        Assert.Equal("eu1", result.Config.Servers[0].Id);
    }
}
