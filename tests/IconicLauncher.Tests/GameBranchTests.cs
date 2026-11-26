using System.Text;
using IconicLauncher.Core.Models;
using IconicLauncher.Core.Services;
using IconicLauncher.Core.Utils;

namespace IconicLauncher.Tests;

public class GameBranchTests
{
    private static SteamEnvironment Env(string? stable, string? exp) => new()
    {
        DayZDir = @"C:\DayZ",
        DayZExpDir = exp == null ? null : @"C:\DayZ Exp",
        DayZVersion = stable,
        DayZExpVersion = exp,
        WorkshopContentDir = @"C:\workshop"
    };

    [Theory]
    [InlineData("1.30.164014", "1.30")]
    [InlineData("1.29.0.163709", "1.29")]
    [InlineData("1.30", "1.30")]
    [InlineData("v1.28.161464", "1.28")]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("unknown", null)]
    public void MajorMinorNormalisesVersions(string? input, string? expected)
    {
        Assert.Equal(expected, GameVersion.MajorMinor(input));
    }

    [Fact]
    public void ServerOnExperimentalOnlyVersionUsesExp()
    {
        var server = new ServerEntry { Id = "a" };
        Assert.True(GameBranch.UseExperimental(server, new LauncherSettings(), Env("1.29", "1.30"), "1.30.164014"));
    }

    [Fact]
    public void ServerOnStableVersionUsesStable()
    {
        var server = new ServerEntry { Id = "a" };
        Assert.False(GameBranch.UseExperimental(server, new LauncherSettings(), Env("1.29", "1.30"), "1.29.163709"));
    }

    [Fact]
    public void SameVersionOnBothBranchesPrefersStable()
    {
        var server = new ServerEntry { Id = "a" };
        Assert.False(GameBranch.UseExperimental(server, new LauncherSettings(), Env("1.30", "1.30"), "1.30.164014"));
    }

    [Fact]
    public void NoExpInstallNeverAutoSelectsExp()
    {
        var server = new ServerEntry { Id = "a" };
        Assert.False(GameBranch.UseExperimental(server, new LauncherSettings(), Env("1.29", null), "1.30.164014"));
    }

    [Fact]
    public void UnknownServerVersionUsesStable()
    {
        var server = new ServerEntry { Id = "a" };
        Assert.False(GameBranch.UseExperimental(server, new LauncherSettings(), Env("1.29", "1.30"), null));
    }

    [Fact]
    public void ConfigFlagForcesExp()
    {
        var server = new ServerEntry { Id = "a", Experimental = true };
        Assert.True(GameBranch.UseExperimental(server, new LauncherSettings(), Env("1.29", "1.30"), null));
    }

    [Fact]
    public void PlayerOverrideWinsBothWays()
    {
        var settings = new LauncherSettings();
        settings.ServerExperimental["a"] = false;
        settings.ServerExperimental["b"] = true;
        Assert.False(GameBranch.UseExperimental(new ServerEntry { Id = "a", Experimental = true }, settings, Env("1.29", "1.30"), "1.30.1"));
        Assert.True(GameBranch.UseExperimental(new ServerEntry { Id = "b" }, settings, Env("1.29", "1.30"), "1.29.1"));
    }

    [Fact]
    public void A2SInfoParserReadsTheVersionString()
    {
        var bytes = new List<byte> { 0xFF, 0xFF, 0xFF, 0xFF, 0x49, 0x11 };
        void Str(string v)
        {
            bytes.AddRange(Encoding.UTF8.GetBytes(v));
            bytes.Add(0);
        }
        Str("Iconic PvE - Test");
        Str("chernarusplus");
        Str("dayz");
        Str("DayZ");
        bytes.AddRange(new byte[] { 0xDC, 0x5F });
        bytes.AddRange(new byte[] { 12, 60, 0, (byte)'d', (byte)'w', 1, 0 });
        Str("1.30.164014");
        var status = A2SQueryService.ParseInfo(bytes.ToArray(), 25);
        Assert.True(status.Online);
        Assert.Equal(12, status.Players);
        Assert.Equal(60, status.MaxPlayers);
        Assert.True(status.PasswordProtected);
        Assert.Equal("1.30.164014", status.Version);
    }

    [Fact]
    public void GameProcessGetsTheAppIdOfItsBranch()
    {
        Environment.SetEnvironmentVariable("SteamAppId", "221100");
        Environment.SetEnvironmentVariable("SteamGameId", "221100");
        var exp = GameLauncher.BuildStartInfo(@"C:\DayZ Exp\DayZ_BE.exe", "", @"C:\DayZ Exp", true);
        Assert.Equal("1024020", exp.Environment["SteamAppId"]);
        Assert.Equal("1024020", exp.Environment["SteamGameId"]);
        var stable = GameLauncher.BuildStartInfo(@"C:\DayZ\DayZ_BE.exe", "", @"C:\DayZ", false);
        Assert.Equal("221100", stable.Environment["SteamAppId"]);
        Assert.Equal("221100", stable.Environment["SteamGameId"]);
    }

    [Fact]
    public void ExperimentalLaunchWithoutExpInstallFailsWithAClearError()
    {
        var env = Env("1.29", null);
        var mods = new List<ModVerificationResult>();
        var result = new GameLauncher().Launch(new ServerEntry { Id = "a", Name = "x", Ip = "1.2.3.4", GamePort = 2302, QueryPort = 2303 },
            mods, env, new LauncherSettings(), null, true);
        Assert.False(result.Started);
        Assert.Contains("Experimental", result.Error);
    }
}
