using System.IO;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IconicLauncher.Core.Models;
using IconicLauncher.Core.Services;
using IconicLauncher.Core.Utils;
using Serilog;

namespace IconicLauncher.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private static readonly Regex DuplicateSuffixRegex = new(@"\((\d+)\)", RegexOptions.Compiled);

    private readonly MainViewModel _owner;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProfileNameSurvivor))]
    [NotifyPropertyChangedFor(nameof(HasDuplicateSuffix))]
    [NotifyPropertyChangedFor(nameof(DuplicateSuffixHintText))]
    [NotifyPropertyChangedFor(nameof(IsProfileNameSuspicious))]
    private string profileName = "";

    [ObservableProperty]
    private string extraLaunchParams = "";

    [ObservableProperty]
    private string dayZPathOverride = "";

    [ObservableProperty]
    private string steamPathOverride = "";

    [ObservableProperty]
    private string dayZExpPathOverride = "";

    [ObservableProperty]
    private string autoDetectedDayZExpPath = "Detecting...";

    [ObservableProperty]
    private string? gameVersionsText;

    [ObservableProperty]
    private bool autoUpdateMods;

    [ObservableProperty]
    private bool autoConnect;

    [ObservableProperty]
    private bool closeOnLaunch;

    [ObservableProperty]
    private bool discordRpcEnabled;

    [ObservableProperty]
    private bool launchNoSplash;

    [ObservableProperty]
    private bool launchNoPause;

    [ObservableProperty]
    private bool launchWindowed;

    [ObservableProperty]
    private bool launchDoLogs;

    [ObservableProperty]
    private string defaultGame = "last";

    [ObservableProperty]
    private string wowClientPath = "";

    [ObservableProperty]
    private string autoDetectedDayZPath = "Detecting...";

    [ObservableProperty]
    private string autoDetectedSteamPath = "Detecting...";

    [ObservableProperty]
    private string? saveStatus;

    [ObservableProperty]
    private bool debugLogging;

    [ObservableProperty]
    private string? dumpStatus;

    [ObservableProperty]
    private string? sendStatus;

    public SettingsViewModel(MainViewModel owner)
    {
        _owner = owner;
        var s = owner.SettingsService.Settings;
        ProfileName = s.ProfileName;
        ExtraLaunchParams = s.ExtraLaunchParams;
        DayZPathOverride = s.DayZPathOverride ?? "";
        SteamPathOverride = s.SteamPathOverride ?? "";
        DayZExpPathOverride = s.DayZExpPathOverride ?? "";
        AutoUpdateMods = s.AutoUpdateMods;
        AutoConnect = s.AutoConnect;
        CloseOnLaunch = s.CloseOnLaunch;
        DiscordRpcEnabled = s.DiscordRpcEnabled;
        LaunchNoSplash = s.LaunchNoSplash;
        LaunchNoPause = s.LaunchNoPause;
        LaunchWindowed = s.LaunchWindowed;
        LaunchDoLogs = s.LaunchDoLogs;
        DebugLogging = s.DebugLogging;
        DefaultGame = string.IsNullOrWhiteSpace(s.DefaultGame) ? "last" : s.DefaultGame.ToLowerInvariant();
        WowClientPath = s.WowClientPath ?? "";
    }

    [ObservableProperty]
    private string? wowDetectStatus;

    internal void SyncWowPathFromSettings()
    {
        WowClientPath = _owner.SettingsService.Settings.WowClientPath ?? "";
    }

    [RelayCommand]
    private async Task DetectWowAsync()
    {
        WowDetectStatus = "Searching your drives...";
        var ok = await _owner.Wow.DetectAsync(false);
        WowClientPath = _owner.SettingsService.Settings.WowClientPath ?? "";
        WowDetectStatus = ok ? "Found it" : "Not found - enter the path manually";
    }

    [RelayCommand]
    private void SetDefaultGame(string? value)
    {
        DefaultGame = value switch
        {
            "dayz" => "dayz",
            "wow" => "wow",
            _ => "last"
        };
    }

    partial void OnDebugLoggingChanged(bool value)
    {
        LogLevelController.SetDebug(value);
        Log.Information("Debug logging {State}", value ? "enabled" : "disabled");
        var s = _owner.SettingsService.Settings;
        if (s.DebugLogging == value)
        {
            return;
        }
        s.DebugLogging = value;
        try
        {
            _owner.SettingsService.Save();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Persisting the debug logging toggle failed");
        }
    }

    [ObservableProperty]
    private string graphicsPreset = "";

    [ObservableProperty]
    private string? graphicsStatusText;

    [ObservableProperty]
    private string? graphicsAboveText;

    [ObservableProperty]
    private bool graphicsAvailable;

    [ObservableProperty]
    private bool graphicsRisky;

    [ObservableProperty]
    private bool graphicsAlert;

    [ObservableProperty]
    private bool hasGraphicsBackup;

    [ObservableProperty]
    private string? graphicsSaveStatus;

    [ObservableProperty]
    private string displayMode = "fullscreen";

    [ObservableProperty]
    private bool advisoryEnabled;

    [ObservableProperty]
    private string? advisoryTitle;

    [ObservableProperty]
    private string? advisoryMessage;

    public void RefreshGraphics()
    {
        var advisory = _owner.Advisory;
        AdvisoryEnabled = advisory.Enabled;
        AdvisoryTitle = advisory.Title;
        AdvisoryMessage = advisory.Message;
        DayZVideoState? state = null;
        try
        {
            state = _owner.Video.Read();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Reading DayZ graphics settings failed");
        }
        GraphicsAvailable = state is not null;
        if (state is null)
        {
            GraphicsPreset = "";
            GraphicsStatusText = "No DayZ settings found yet. Start DayZ once, then come back here.";
            GraphicsAboveText = null;
            GraphicsRisky = false;
            HasGraphicsBackup = false;
        }
        else
        {
            GraphicsPreset = state.Preset?.ToString().ToLowerInvariant() ?? "custom";
            GraphicsStatusText = "Current: " + state.PresetLabel.ToUpperInvariant();
            GraphicsRisky = state.IsAboveMedium;
            GraphicsAboveText = state.IsAboveMedium
                ? "Above Medium: " + string.Join(", ", state.AboveMedium.Select(k => k.Label))
                : null;
            HasGraphicsBackup = state.HasBackup;
        }
        GraphicsAlert = advisory.Enabled
            && _owner.SettingsService.Settings.GraphicsAdvisoryMutedId != advisory.Id
            && state is { IsAboveMedium: true };
        DisplayMode = state?.Windowed == true || _owner.SettingsService.Settings.LaunchWindowed ? "windowed" : "fullscreen";
        OnPropertyChanged(nameof(GraphicsPreset));
        OnPropertyChanged(nameof(DisplayMode));
    }

    [RelayCommand]
    private void SetGraphicsPreset(string? value)
    {
        if (!Enum.TryParse<VideoPreset>(value, true, out var preset))
        {
            return;
        }
        RunGraphicsChange(() => _owner.Video.ApplyPreset(preset), $"Set to {preset}. DayZ uses it the next time it starts.");
    }

    [RelayCommand]
    private void CapGraphicsAtMedium()
    {
        RunGraphicsChange(() =>
        {
            var state = _owner.Video.Read() ?? throw new FileNotFoundException("DayZ settings not found. Start DayZ once, then try again.");
            _owner.Video.CapAt(VideoPreset.Medium, state);
        }, "Everything above Medium was lowered to Medium.");
    }

    [RelayCommand]
    private void RestoreGraphics()
    {
        RunGraphicsChange(() => _owner.Video.RestoreBackup(), "Your previous DayZ settings are back.");
    }

    [RelayCommand]
    private void SetDisplayMode(string? value)
    {
        var windowed = string.Equals(value, "windowed", StringComparison.OrdinalIgnoreCase);
        RunGraphicsChange(() =>
        {
            if (_owner.Video.Read() is not null)
            {
                _owner.Video.SetWindowed(windowed);
            }
            var s = _owner.SettingsService.Settings;
            s.LaunchWindowed = windowed;
            LaunchWindowed = windowed;
            _owner.SettingsService.Save();
        }, windowed ? "DayZ starts in a window." : "DayZ starts in fullscreen.");
    }

    private void RunGraphicsChange(Action change, string success)
    {
        if (DayZVideoSettingsService.IsDayZRunning())
        {
            GraphicsSaveStatus = "Close DayZ first - it overwrites these settings when it exits.";
            RefreshGraphics();
            return;
        }
        try
        {
            change();
            GraphicsSaveStatus = success;
            Log.Information("DayZ graphics change: {Result}", success);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Changing DayZ graphics settings failed");
            GraphicsSaveStatus = "Failed: " + ex.Message;
        }
        RefreshGraphics();
    }

    public bool IsProfileNameSurvivor =>ProfileName.Trim().Equals("Survivor", StringComparison.OrdinalIgnoreCase);

    public bool HasDuplicateSuffix => DuplicateSuffixRegex.IsMatch(ProfileName);

    public string DuplicateSuffixHintText
    {
        get
        {
            var match = DuplicateSuffixRegex.Match(ProfileName);
            return match.Success
                ? $"The ({match.Groups[1].Value}) in your name causes admin confusion and duplicate identities, which can lead to character corruption. Remove it."
                : "";
        }
    }

    public bool IsProfileNameSuspicious => IsProfileNameSurvivor || HasDuplicateSuffix;

    public void RefreshDetected()
    {
        var env = _owner.Env;
        AutoDetectedDayZPath = env?.DayZDir ?? "Not detected - enter the DayZ folder path";
        AutoDetectedSteamPath = env?.SteamRoot ?? "Not detected - enter the Steam folder path";
        AutoDetectedDayZExpPath = env?.DayZExpDir ?? "Not installed - optional, only needed for experimental servers";
        GameVersionsText = env is null
            ? null
            : $"Stable {env.DayZVersion ?? "not found"}  |  Experimental {env.DayZExpVersion ?? "not found"}";
    }

    [RelayCommand]
    private void Save()
    {
        var s = _owner.SettingsService.Settings;
        s.ProfileName = string.IsNullOrWhiteSpace(ProfileName) ? "Survivor" : ProfileName.Trim();
        s.ExtraLaunchParams = ExtraLaunchParams.Trim();
        s.DayZPathOverride = string.IsNullOrWhiteSpace(DayZPathOverride) ? null : DayZPathOverride.Trim();
        s.SteamPathOverride = string.IsNullOrWhiteSpace(SteamPathOverride) ? null : SteamPathOverride.Trim();
        s.DayZExpPathOverride = string.IsNullOrWhiteSpace(DayZExpPathOverride) ? null : DayZExpPathOverride.Trim();
        s.AutoUpdateMods = AutoUpdateMods;
        s.AutoConnect = AutoConnect;
        s.CloseOnLaunch = CloseOnLaunch;
        s.DiscordRpcEnabled = DiscordRpcEnabled;
        s.LaunchNoSplash = LaunchNoSplash;
        s.LaunchNoPause = LaunchNoPause;
        s.LaunchWindowed = LaunchWindowed;
        s.LaunchDoLogs = LaunchDoLogs;
        s.DebugLogging = DebugLogging;
        s.DefaultGame = DefaultGame;
        s.WowClientPath = string.IsNullOrWhiteSpace(WowClientPath) ? null : WowClientPath.Trim();
        try
        {
            _owner.SettingsService.Save();
            SaveStatus = "Settings saved";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Saving settings failed");
            SaveStatus = "Saving failed: " + ex.Message;
        }
        _owner.OnSettingsSaved();
    }

    [RelayCommand]
    private void DumpLogs()
    {
        try
        {
            Log.Information("Building logdump for desktop");
            var path = LogDumpService.WriteToDesktop(BuildDump(), DateTime.Now);
            DumpStatus = "Saved to Desktop: " + Path.GetFileName(path);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Logdump to desktop failed");
            DumpStatus = "Failed: " + ex.Message;
        }
    }

    [RelayCommand]
    private async Task SendLogsAsync()
    {
        var settings = _owner.SettingsService.Settings;
        var limitMessage = LogUploadService.CheckRateLimit(settings, DateTime.UtcNow);
        if (limitMessage != null)
        {
            SendStatus = limitMessage;
            return;
        }
        SendStatus = "Sending...";
        try
        {
            Log.Information("Building logdump for upload");
            var (ok, message) = await new LogUploadService().UploadAsync(BuildDump());
            if (ok)
            {
                LogUploadService.RecordUpload(settings, DateTime.UtcNow);
                _owner.SettingsService.Save();
            }
            SendStatus = message;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Logdump upload failed");
            SendStatus = "Failed: " + ex.Message;
        }
    }

    private string BuildDump()
    {
        var logDir = Path.Combine(_owner.SettingsService.AppDataDir, "logs");
        return LogDumpService.BuildDump(logDir, _owner.VersionLabel, _owner.SettingsService.Settings, DateTime.UtcNow);
    }
}
