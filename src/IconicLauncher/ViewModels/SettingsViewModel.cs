using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    private string autoDetectedDayZPath = "Detecting...";

    [ObservableProperty]
    private string autoDetectedSteamPath = "Detecting...";

    [ObservableProperty]
    private string? saveStatus;

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
}
