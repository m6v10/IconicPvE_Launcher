using System.IO;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    public bool IsProfileNameSurvivor => ProfileName.Trim().Equals("Survivor", StringComparison.OrdinalIgnoreCase);

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
    }

    [RelayCommand]
    private void Save()
    {
        var s = _owner.SettingsService.Settings;
        s.ProfileName = string.IsNullOrWhiteSpace(ProfileName) ? "Survivor" : ProfileName.Trim();
        s.ExtraLaunchParams = ExtraLaunchParams.Trim();
        s.DayZPathOverride = string.IsNullOrWhiteSpace(DayZPathOverride) ? null : DayZPathOverride.Trim();
        s.SteamPathOverride = string.IsNullOrWhiteSpace(SteamPathOverride) ? null : SteamPathOverride.Trim();
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
