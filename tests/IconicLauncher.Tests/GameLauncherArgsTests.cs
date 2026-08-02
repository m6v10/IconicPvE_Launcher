using IconicLauncher.Core.Models;
using IconicLauncher.Core.Services;

namespace IconicLauncher.Tests;

public class GameLauncherArgsTests
{
    private static readonly string[] ModPaths =
    {
        @"C:\Steam\steamapps\workshop\content\221100\1559212036",
        @"C:\Program Files (x86)\Steam\steamapps\workshop\content\221100\3077736647"
    };

    private static ServerEntry Server() => new()
    {
        Id = "eu1",
        Name = "Iconic PvE - EU 1",
        Ip = "192.0.2.10",
        GamePort = 2302,
        QueryPort = 2303
    };

    private static LauncherSettings Settings() => new()
    {
        ProfileName = "Survivor",
        AutoConnect = true,
        ExtraLaunchParams = "",
        LaunchNoSplash = false,
        LaunchNoPause = false,
        LaunchWindowed = false,
        LaunchDoLogs = false
    };

    [Fact]
    public void BuildsFullArgumentString()
    {
        var args = GameLauncher.BuildArguments(Server(), ModPaths, Settings());
        Assert.Equal(@"0 1 1 -exe DayZ_x64.exe -name=Survivor ""-mod=C:\Steam\steamapps\workshop\content\221100\1559212036;C:\Program Files (x86)\Steam\steamapps\workshop\content\221100\3077736647"" -connect=192.0.2.10:2302:2303", args);
    }

    [Fact]
    public void AutoConnectFalseOmitsConnectToken()
    {
        var settings = Settings();
        settings.AutoConnect = false;
        var args = GameLauncher.BuildArguments(Server(), ModPaths, settings);
        Assert.DoesNotContain("-connect", args);
    }

    [Fact]
    public void ExtraLaunchParamsAppended()
    {
        var settings = Settings();
        settings.ExtraLaunchParams = "-scriptDebug";
        var args = GameLauncher.BuildArguments(Server(), ModPaths, settings);
        Assert.EndsWith("-connect=192.0.2.10:2302:2303 -scriptDebug", args);
    }

    [Fact]
    public void LaunchFlagsAppearWhenEnabled()
    {
        var settings = Settings();
        settings.LaunchWindowed = true;
        settings.LaunchNoSplash = true;
        settings.LaunchNoPause = true;
        settings.LaunchDoLogs = true;
        var args = GameLauncher.BuildArguments(Server(), ModPaths, settings);
        Assert.StartsWith("0 1 1 -exe DayZ_x64.exe -window -noSplash -noPause -doLogs -name=Survivor", args);
    }

    [Fact]
    public void DefaultSettingsEnableNoSplashAndNoPause()
    {
        var args = GameLauncher.BuildArguments(Server(), ModPaths, new LauncherSettings { ProfileName = "Survivor" });
        Assert.Contains("-noSplash", args);
        Assert.Contains("-noPause", args);
        Assert.DoesNotContain("-window", args);
        Assert.DoesNotContain("-doLogs", args);
    }

    [Fact]
    public void SpacedProfileNameIsQuoted()
    {
        var settings = Settings();
        settings.ProfileName = "Iconic Player (2)";
        var args = GameLauncher.BuildArguments(Server(), ModPaths, settings);
        Assert.Contains("\"-name=Iconic Player (2)\"", args);
    }

    [Fact]
    public void PasswordAppendedWhenProvided()
    {
        var args = GameLauncher.BuildArguments(Server(), ModPaths, Settings(), "maint2026");
        Assert.EndsWith("-connect=192.0.2.10:2302:2303 -password=maint2026", args);
    }

    [Fact]
    public void SpacedPasswordIsQuoted()
    {
        var args = GameLauncher.BuildArguments(Server(), ModPaths, Settings(), "top secret");
        Assert.Contains("\"-password=top secret\"", args);
    }

    [Fact]
    public void NoPasswordTokenWithoutPassword()
    {
        var args = GameLauncher.BuildArguments(Server(), ModPaths, Settings());
        Assert.DoesNotContain("-password", args);
    }

    [Fact]
    public void ModTokenIsWrappedInLiteralDoubleQuotes()
    {
        var args = GameLauncher.BuildArguments(Server(), ModPaths, Settings());
        Assert.Contains("\"-mod=", args);
        Assert.Contains("3077736647\"", args);
    }

    [Fact]
    public void ProfileNameFlowsIntoNameToken()
    {
        var settings = Settings();
        settings.ProfileName = "M6";
        var args = GameLauncher.BuildArguments(Server(), ModPaths, settings);
        Assert.Contains("-name=M6", args);
    }
}
