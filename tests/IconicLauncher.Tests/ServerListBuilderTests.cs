using IconicLauncher.Core.Models;
using IconicLauncher.Core.Services;
using Xunit;

namespace IconicLauncher.Tests;

public class ServerListBuilderTests
{
    private static ServerEntry Server(string id, bool optional = false)
    {
        return new ServerEntry { Id = id, Name = id, Ip = "1.2.3.4", GamePort = 2302, QueryPort = 2303, Optional = optional };
    }

    private static LauncherConfig Config(params ServerEntry[] servers)
    {
        return new LauncherConfig { Servers = servers.ToList() };
    }

    [Fact]
    public void OptionalServersAreHiddenUntilThePlayerOptsIn()
    {
        var config = Config(Server("eumain"), Server("eutest", optional: true));
        var settings = new LauncherSettings();

        var visible = ServerListBuilder.BuildVisible(config, settings);

        Assert.Equal(new[] { "eumain" }, visible.Select(s => s.Id));
    }

    [Fact]
    public void VisibilityOverrideBeatsTheConfigDefault()
    {
        var config = Config(Server("eumain"), Server("eutest", optional: true));
        var settings = new LauncherSettings
        {
            ServerVisibility = new Dictionary<string, bool> { ["eutest"] = true, ["eumain"] = false }
        };

        var visible = ServerListBuilder.BuildVisible(config, settings);

        Assert.Equal(new[] { "eutest" }, visible.Select(s => s.Id));
    }

    [Fact]
    public void CustomServersAppendAfterConfigServersAndAreVisibleByDefault()
    {
        var config = Config(Server("eumain"));
        var settings = new LauncherSettings { CustomServers = { Server("custom-9-9-9-9-2302") } };

        var all = ServerListBuilder.BuildAll(config, settings);

        Assert.Equal(new[] { "eumain", "custom-9-9-9-9-2302" }, all.Select(e => e.Server.Id));
        Assert.False(all[0].IsCustom);
        Assert.True(all[1].IsCustom);
        Assert.True(all[1].IsVisible);
    }

    [Fact]
    public void SavedOrderWinsAndUnknownIdsKeepConfigOrderAtTheEnd()
    {
        var config = Config(Server("eumain"), Server("eulite"), Server("usmain"));
        var settings = new LauncherSettings { ServerOrder = { "usmain", "eulite" } };

        var all = ServerListBuilder.BuildAll(config, settings);

        Assert.Equal(new[] { "usmain", "eulite", "eumain" }, all.Select(e => e.Server.Id));
    }

    [Fact]
    public void DuplicateIdsAreCollapsedAndCustomNeverShadowsAConfigServer()
    {
        var config = Config(Server("eumain"));
        var settings = new LauncherSettings { CustomServers = { Server("EUMAIN") } };

        var all = ServerListBuilder.BuildAll(config, settings);

        Assert.Single(all);
        Assert.False(all[0].IsCustom);
    }

    [Fact]
    public void HidingEveryServerYieldsAnEmptyListRatherThanThrowing()
    {
        var config = Config(Server("eumain"));
        var settings = new LauncherSettings
        {
            ServerVisibility = new Dictionary<string, bool> { ["EuMain"] = false }
        };

        Assert.Empty(ServerListBuilder.BuildVisible(config, settings));
    }
}
