using IconicLauncher.Core.Services;

namespace IconicLauncher.Tests;

public class DayZVideoSettingsTests : IDisposable
{
    private const string ExtremeCfg = "language=\"English\";\r\nadapter=-1;\r\n3D_Performance=100000;\r\nWindowWidth=1600;\r\nWindowHeight=900;\r\nWindowed=1;\r\nMSAA=8;\r\nPostFX=2;\r\nVSync=0;\r\nFXAO=4;\r\nAToC=1;\r\nAnisoFilter=2;\r\nTerrainDetail=3;\r\nFXAA=4;\r\n";

    private const string ExtremeProfile = "version=1;\r\nshadowQuality=4;\r\nTexQuality=4;\r\nWaterQuality=2;\r\nplayerName=\"Yellow\";\r\nsceneComplexity=1000000;\r\nshadowZDistance=250;\r\nviewDistance=4000;\r\npreferredObjectViewDistance=2400;\r\nterrainGrid=12.5;\r\nvolumeMaster=5;\r\n";

    private readonly string _dir;

    public DayZVideoSettingsTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "iconic-gfx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "DayZ.cfg"), ExtremeCfg);
        File.WriteAllText(Path.Combine(_dir, "Tester_settings.DayZProfile"), ExtremeProfile);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch
        {
        }
    }

    private DayZVideoSettingsService Service() => new(_dir, "Tester");

    [Fact]
    public void Reads_extreme_and_flags_every_key_above_medium()
    {
        var state = Service().Read();
        Assert.NotNull(state);
        Assert.Equal(VideoPreset.Extreme, state!.Preset);
        Assert.True(state.IsAboveMedium);
        Assert.True(state.Windowed);
        Assert.Contains(state.AboveMedium, k => k.Key == "MSAA");
        Assert.DoesNotContain(state.AboveMedium, k => k.Key == "PostFX");
    }

    [Fact]
    public void Apply_medium_writes_captured_values_and_keeps_other_lines()
    {
        var svc = Service();
        svc.ApplyPreset(VideoPreset.Medium);
        var state = svc.Read()!;
        Assert.Equal(VideoPreset.Medium, state.Preset);
        Assert.False(state.IsAboveMedium);
        var profile = File.ReadAllText(Path.Combine(_dir, "Tester_settings.DayZProfile"));
        Assert.Contains("playerName=\"Yellow\";\r\n", profile);
        Assert.Contains("viewDistance=1600;\r\n", profile);
        Assert.Contains("terrainGrid=12.5;", profile);
        var cfg = File.ReadAllText(Path.Combine(_dir, "DayZ.cfg"));
        Assert.Contains("WindowWidth=1600;", cfg);
        Assert.Contains("MSAA=0;", cfg);
    }

    [Fact]
    public void Cap_lowers_only_keys_above_medium()
    {
        File.WriteAllText(Path.Combine(_dir, "Tester_settings.DayZProfile"), ExtremeProfile.Replace("viewDistance=4000", "viewDistance=1000"));
        var svc = Service();
        svc.CapAt(VideoPreset.Medium, svc.Read()!);
        var values = DayZVideoSettingsService.ParseValues(File.ReadAllText(Path.Combine(_dir, "Tester_settings.DayZProfile")));
        Assert.Equal(1000, values["viewDistance"]);
        Assert.Equal(500000, values["sceneComplexity"]);
        Assert.False(svc.Read()!.IsAboveMedium);
    }

    [Fact]
    public void Backup_keeps_the_original_and_restore_brings_it_back()
    {
        var svc = Service();
        svc.ApplyPreset(VideoPreset.Medium);
        svc.ApplyPreset(VideoPreset.Low);
        Assert.True(svc.Read()!.HasBackup);
        Assert.True(svc.RestoreBackup());
        Assert.Equal(ExtremeCfg, File.ReadAllText(Path.Combine(_dir, "DayZ.cfg")));
        Assert.Equal(ExtremeProfile, File.ReadAllText(Path.Combine(_dir, "Tester_settings.DayZProfile")));
        Assert.False(svc.Read()!.HasBackup);
    }

    [Fact]
    public void Windowed_toggle_only_touches_the_windowed_line()
    {
        var svc = Service();
        svc.SetWindowed(false);
        var cfg = File.ReadAllText(Path.Combine(_dir, "DayZ.cfg"));
        Assert.Equal(ExtremeCfg.Replace("Windowed=1;", "Windowed=0;"), cfg);
    }

    [Fact]
    public void Missing_key_is_appended()
    {
        var text = DayZVideoSettingsService.ApplyChanges("a=1;\n", new Dictionary<string, string> { ["b"] = "2" });
        Assert.Equal("a=1;\nb=2;\n", text);
    }

    [Fact]
    public void Missing_files_return_null()
    {
        Assert.Null(new DayZVideoSettingsService(Path.Combine(_dir, "nope"), "Tester").Read());
    }

    [Fact]
    public void Profile_falls_back_to_any_settings_file()
    {
        Assert.NotNull(new DayZVideoSettingsService(_dir, "SomeoneElse").Read());
    }
}
