using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IconicLauncher.Core.Models;
using IconicLauncher.Core.Services;
using Microsoft.Win32;
using Serilog;

namespace IconicLauncher.ViewModels;

public sealed class WowFileRow
{
    public required string Path { get; init; }
    public required string StateText { get; init; }
    public bool IsProblem { get; init; }
}

public sealed partial class WowViewModel : ObservableObject
{
    private readonly MainViewModel _owner;
    private readonly IWowPatchService _patch;
    private readonly IWowStatusService _statusService;
    private WowConfig? _config;
    private WowVerifyResult? _verify;
    private CancellationTokenSource? _applyCts;
    private bool _statusRunning;

    public ObservableCollection<WowFileRow> Files { get; } = new();
    public ObservableCollection<NewsItem> News { get; } = new();

    [ObservableProperty]
    private bool isConfigured;

    [ObservableProperty]
    private string realmName = "Iconic WoW";

    [ObservableProperty]
    private string clientVersionText = "";

    [ObservableProperty]
    private bool online;

    [ObservableProperty]
    private string playersText = "-";

    [ObservableProperty]
    private string botsText = "-";

    [ObservableProperty]
    private string? statusDetailText;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PrimaryActionCommand))]
    [NotifyCanExecuteChangedFor(nameof(RepairCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    private bool clientPathValid;

    [ObservableProperty]
    private string clientPathText = "";

    [ObservableProperty]
    private string patchStateText = "Set your game folder to get started";

    [ObservableProperty]
    private string buildText = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PrimaryActionCommand))]
    [NotifyCanExecuteChangedFor(nameof(RepairCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    private bool isVerifying;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PrimaryActionCommand))]
    [NotifyCanExecuteChangedFor(nameof(RepairCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelDownloadCommand))]
    private bool isApplying;

    [ObservableProperty]
    private double applyPercent;

    [ObservableProperty]
    private string? applyDetailText;

    [ObservableProperty]
    private string actionLabel = "SET GAME FOLDER";

    [ObservableProperty]
    private string? errorText;

    public string BarText => IsApplying && !string.IsNullOrEmpty(ApplyDetailText) ? ApplyDetailText! : PatchStateText;

    partial void OnIsApplyingChanged(bool value) => OnPropertyChanged(nameof(BarText));
    partial void OnApplyDetailTextChanged(string? value) => OnPropertyChanged(nameof(BarText));
    partial void OnPatchStateTextChanged(string value) => OnPropertyChanged(nameof(BarText));

    [ObservableProperty]
    private bool updateNeeded;

    [ObservableProperty]
    private string? fullClientUrl;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PrimaryActionCommand))]
    [NotifyCanExecuteChangedFor(nameof(RepairCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    private bool isDetecting;

    [ObservableProperty]
    private Uri? heroVideoUri;

    public WowViewModel(MainViewModel owner, IWowPatchService patch, IWowStatusService statusService)
    {
        _owner = owner;
        _patch = patch;
        _statusService = statusService;
        HeroVideoUri = ExtractHeroVideo(owner.SettingsService.AppDataDir);
    }

    private static Uri? ExtractHeroVideo(string appDataDir)
    {
        try
        {
            using var stream = System.Reflection.Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("IconicLauncher.Assets.wow_hero.mp4");
            if (stream is null) return null;
            var target = Path.Combine(appDataDir, "wow_hero.mp4");
            if (!File.Exists(target) || new FileInfo(target).Length != stream.Length)
            {
                using var file = File.Create(target);
                stream.CopyTo(file);
            }
            return new Uri(target);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Extracting the hero video failed");
            return null;
        }
    }

    public void ApplyConfig(WowConfig? config)
    {
        _config = config;
        IsConfigured = config != null;
        if (config is null)
        {
            PatchStateText = "The WoW realm is not configured yet";
            return;
        }
        RealmName = string.IsNullOrWhiteSpace(config.Name) ? "Iconic WoW" : config.Name;
        ClientVersionText = config.ClientVersion;
        FullClientUrl = string.IsNullOrWhiteSpace(config.FullClientUrl) ? null : config.FullClientUrl;
        var news = config.News;
        if (news != null)
        {
            News.Clear();
            foreach (var item in news) News.Add(item);
        }
        RefreshClientPath();
    }

    public void RefreshClientPath()
    {
        var path = _owner.SettingsService.Settings.WowClientPath;
        ClientPathValid = WowPatchService.IsValidClientRoot(path);
        ClientPathText = ClientPathValid ? path! : "Not set - select the folder that contains Wow.exe";
        _owner.SettingsPage.SyncWowPathFromSettings();
        UpdateActionState();
    }

    public async Task InitializeAsync()
    {
        if (_config is null) return;
        await RefreshStatusSafeAsync(CancellationToken.None);
        if (!ClientPathValid)
        {
            await DetectAsync(true);
        }
        if (ClientPathValid)
        {
            await VerifySafeAsync();
        }
    }

    [RelayCommand]
    private Task DetectManuallyAsync() => DetectAsync(false);

    public async Task<bool> DetectAsync(bool quiet)
    {
        if (IsDetecting) return false;
        IsDetecting = true;
        if (!quiet) PatchStateText = "Searching your PC for a WoW 3.3.5a client...";
        try
        {
            var candidates = await WowClientLocator.ScanAsync(TimeSpan.FromSeconds(quiet ? 6 : 15), CancellationToken.None);
            if (candidates.Count == 0)
            {
                if (!quiet)
                {
                    PatchStateText = "No WoW client found - use GAME FOLDER to pick it manually";
                }
                return false;
            }
            _owner.SettingsService.Settings.WowClientPath = candidates[0];
            try
            {
                _owner.SettingsService.Save();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Saving the detected WoW client path failed");
            }
            RefreshClientPath();
            PatchStateText = "Found your game: " + candidates[0];
            await VerifySafeAsync();
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "WoW client detection failed");
            if (!quiet) PatchStateText = "Search failed - use GAME FOLDER to pick it manually";
            return false;
        }
        finally
        {
            IsDetecting = false;
        }
    }

    public async Task RefreshStatusSafeAsync(CancellationToken ct)
    {
        if (_config is null || _statusRunning) return;
        _statusRunning = true;
        try
        {
            var status = await _statusService.QueryAsync(_config, ct);
            Online = status.Online;
            if (status.Online)
            {
                var (fakePlayers, fakeBots) = FakeCounts(DateTime.Now);
                PlayersText = status.Players is > 0 ? status.Players.Value.ToString() : fakePlayers.ToString();
                BotsText = fakeBots.ToString();
            }
            else
            {
                PlayersText = "-";
                BotsText = "-";
            }
            StatusDetailText = status.Online
                ? null
                : status.AuthOnline
                    ? "World server is down - login will hang at 'Connected'"
                    : "Realm offline";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log.Debug("WoW status refresh failed: {Message}", ex.Message);
        }
        finally
        {
            _statusRunning = false;
        }
    }

    private bool CanVerify => IsConfigured && ClientPathValid && !IsVerifying && !IsApplying;

    [RelayCommand(CanExecute = nameof(CanVerify))]
    private Task RefreshAsync() => VerifySafeAsync();

    public async Task VerifySafeAsync()
    {
        if (_config is null || !ClientPathValid || IsVerifying || IsApplying) return;
        IsVerifying = true;
        ErrorText = null;
        PatchStateText = "Checking game files...";
        try
        {
            _verify = await _patch.VerifyAsync(_config, _owner.SettingsService.Settings.WowClientPath!, CancellationToken.None);
            RebuildFileRows();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "WoW verify failed");
            _verify = null;
            ErrorText = "Could not check the game files: " + ex.Message;
            PatchStateText = "File check failed";
        }
        finally
        {
            IsVerifying = false;
            UpdateActionState();
            if (!IsApplying)
            {
                ApplyPercent = _verify?.UpToDate == true ? 100 : 0;
            }
        }
    }

    private void RebuildFileRows()
    {
        Files.Clear();
        if (_verify is null) return;
        foreach (var check in _verify.Files)
        {
            var text = check.State switch
            {
                WowFileState.Ok => "OK",
                WowFileState.Missing => "MISSING",
                WowFileState.Modified => "UPDATE",
                WowFileState.Obsolete => "REMOVE",
                _ => ""
            };
            Files.Add(new WowFileRow { Path = check.Path, StateText = text, IsProblem = check.State != WowFileState.Ok });
        }
    }

    private bool CanFullDownload => _config != null
        && !string.IsNullOrWhiteSpace(_config.FullManifestUrl)
        && !string.IsNullOrWhiteSpace(_config.FullFilesBaseUrl);

    private void UpdateActionState()
    {
        if (!ClientPathValid)
        {
            UpdateNeeded = false;
            if (CanFullDownload)
            {
                var size = _config!.FullClientSizeBytes;
                ActionLabel = size > 0 ? $"DOWNLOAD ({FormatBytes(size)})" : "DOWNLOAD";
                if (IsConfigured) PatchStateText = "No game found - download the full client, or use GAME FOLDER if you already have it";
            }
            else
            {
                ActionLabel = "SET GAME FOLDER";
                if (IsConfigured) PatchStateText = "Set your game folder to get started";
            }
            return;
        }
        if (_verify is null)
        {
            UpdateNeeded = false;
            ActionLabel = "START";
            return;
        }
        BuildText = string.IsNullOrWhiteSpace(_verify.Manifest.Build) ? "" : $"Client build {_verify.Manifest.Build}";
        if (_verify.UpToDate)
        {
            UpdateNeeded = false;
            ActionLabel = "START";
            PatchStateText = "All game files are up to date";
        }
        else
        {
            UpdateNeeded = true;
            var count = _verify.NeedsDownload.Count() + _verify.NeedsDelete.Count();
            ActionLabel = $"UPDATE ({FormatBytes(_verify.BytesToDownload)})";
            PatchStateText = $"{count} file(s) need updating - {FormatBytes(_verify.BytesToDownload)} to download";
        }
        PrimaryActionCommand.NotifyCanExecuteChanged();
    }

    private bool CanPrimaryAction => IsConfigured && !IsVerifying && !IsApplying && !IsDetecting;

    [RelayCommand(CanExecute = nameof(CanPrimaryAction))]
    private async Task PrimaryActionAsync()
    {
        if (!ClientPathValid)
        {
            if (CanFullDownload)
            {
                await FullDownloadAsync();
            }
            else
            {
                Browse();
            }
            return;
        }
        await VerifySafeAsync();
        if (_verify is null) return;
        if (UpdateNeeded)
        {
            var ok = await ApplyAsync(false);
            if (!ok || UpdateNeeded) return;
        }
        Launch();
    }

    private bool CanRepair => IsConfigured && ClientPathValid && !IsVerifying && !IsApplying;

    [RelayCommand(CanExecute = nameof(CanRepair))]
    private async Task RepairAsync()
    {
        if (_verify is null)
        {
            await VerifySafeAsync();
        }
        if (_verify is null) return;
        await ApplyAsync(true);
    }

    private async Task<bool> ApplyAsync(bool repair)
    {
        if (_config is null || _verify is null) return false;
        var clientRoot = _owner.SettingsService.Settings.WowClientPath!;
        IsApplying = true;
        ApplyPercent = 0;
        ApplyDetailText = repair ? "Repairing all managed files..." : "Downloading updates...";
        ErrorText = null;
        _applyCts = new CancellationTokenSource();
        try
        {
            var progress = new Progress<WowApplyProgress>(p =>
            {
                ApplyPercent = p.BytesTotal > 0 ? p.BytesDone * 100.0 / p.BytesTotal : 100;
                ApplyDetailText = $"{p.CurrentFile}  ({p.FilesDone}/{p.FilesTotal} files, {FormatBytes(p.BytesDone)} / {FormatBytes(p.BytesTotal)})";
            });
            if (repair)
            {
                await _patch.RepairAsync(_config, clientRoot, _verify, progress, _applyCts.Token);
            }
            else
            {
                await _patch.ApplyAsync(_config, clientRoot, _verify, progress, _applyCts.Token);
            }
            ApplyDetailText = null;
            await VerifySafeAsync();
            return true;
        }
        catch (OperationCanceledException)
        {
            ApplyDetailText = "Download cancelled";
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "WoW patch apply failed");
            ErrorText = "Update failed: " + ex.Message;
            ApplyDetailText = null;
            return false;
        }
        finally
        {
            IsApplying = false;
            _applyCts?.Dispose();
            _applyCts = null;
            UpdateActionState();
        }
    }

    private async Task FullDownloadAsync()
    {
        if (_config is null) return;
        string targetDir;
        var existing = _owner.SettingsService.Settings.WowClientPath;
        if (!string.IsNullOrWhiteSpace(existing) && Directory.Exists(existing) && !WowPatchService.IsValidClientRoot(existing))
        {
            targetDir = existing;
        }
        else
        {
            var dialog = new OpenFolderDialog { Title = "Choose where to install the game (a subfolder 'Iconic WoW' will be created)" };
            if (dialog.ShowDialog() != true) return;
            targetDir = Path.Combine(dialog.FolderName, "Iconic WoW");
            if (File.Exists(Path.Combine(dialog.FolderName, "Wow.exe")))
            {
                targetDir = dialog.FolderName;
            }
        }
        var fullConfig = new WowConfig
        {
            ManifestUrl = _config.FullManifestUrl!,
            FilesBaseUrl = _config.FullFilesBaseUrl!,
            Realmlist = _config.Realmlist
        };
        IsApplying = true;
        ApplyPercent = 0;
        ApplyDetailText = "Preparing the full download...";
        ErrorText = null;
        _applyCts = new CancellationTokenSource();
        try
        {
            Directory.CreateDirectory(targetDir);
            var verify = await _patch.VerifyAsync(fullConfig, targetDir, _applyCts.Token);
            var needed = verify.BytesToDownload;
            var drive = new DriveInfo(Path.GetPathRoot(targetDir)!);
            if (drive.AvailableFreeSpace < needed + 2L * 1024 * 1024 * 1024)
            {
                ErrorText = $"Not enough disk space on {drive.Name} - the download needs {FormatBytes(needed)} plus headroom";
                ApplyDetailText = null;
                return;
            }
            _owner.SettingsService.Settings.WowClientPath = targetDir;
            try
            {
                _owner.SettingsService.Save();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Saving the install path failed");
            }
            var progress = new Progress<WowApplyProgress>(p =>
            {
                ApplyPercent = p.BytesTotal > 0 ? p.BytesDone * 100.0 / p.BytesTotal : 100;
                ApplyDetailText = $"Downloading the game  ({p.FilesDone}/{p.FilesTotal} files, {FormatBytes(p.BytesDone)} / {FormatBytes(p.BytesTotal)})";
            });
            await _patch.ApplyAsync(fullConfig, targetDir, verify, progress, _applyCts.Token);
            ApplyDetailText = null;
            RefreshClientPath();
            await VerifySafeAsync();
            PatchStateText = "Download complete - ready to play";
        }
        catch (OperationCanceledException)
        {
            ApplyDetailText = "Download paused - press DOWNLOAD again to resume where it stopped";
            RefreshClientPath();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Full client download failed");
            ErrorText = "Download failed: " + ex.Message + " - press DOWNLOAD again to resume";
            ApplyDetailText = null;
            RefreshClientPath();
        }
        finally
        {
            IsApplying = false;
            _applyCts?.Dispose();
            _applyCts = null;
            UpdateActionState();
        }
    }

    private bool CanCancelDownload => IsApplying;

    [RelayCommand(CanExecute = nameof(CanCancelDownload))]
    private void CancelDownload()
    {
        _applyCts?.Cancel();
    }

    [RelayCommand]
    private void Browse()
    {
        var dialog = new OpenFolderDialog { Title = "Select your WoW 3.3.5a folder (contains Wow.exe)" };
        if (dialog.ShowDialog() != true) return;
        var path = dialog.FolderName;
        if (!WowPatchService.IsValidClientRoot(path))
        {
            ErrorText = "That folder does not contain Wow.exe and a Data folder";
            return;
        }
        ErrorText = null;
        _owner.SettingsService.Settings.WowClientPath = path;
        try
        {
            _owner.SettingsService.Save();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Saving the WoW client path failed");
        }
        RefreshClientPath();
        _ = VerifySafeAsync();
    }

    private void Launch()
    {
        if (_config is null) return;
        var clientRoot = _owner.SettingsService.Settings.WowClientPath!;
        try
        {
            _patch.EnsureRealmlist(_config, clientRoot);
            var exe = Path.Combine(clientRoot, "Wow.exe");
            Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = clientRoot, UseShellExecute = true });
            Log.Information("WoW client launched from {Path}", clientRoot);
            if (_owner.SettingsService.Settings.CloseOnLaunch)
            {
                _owner.RequestExit();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "WoW launch failed");
            ErrorText = "Could not start the game: " + ex.Message;
        }
    }

    [RelayCommand]
    private void OpenFullClient()
    {
        var url = FullClientUrl;
        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Opening the full client link failed");
        }
    }

    [RelayCommand]
    private void OpenNewsLink(NewsItem? item)
    {
        var url = item?.Url;
        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Opening news link failed");
        }
    }

    private static (int Players, int Bots) FakeCounts(DateTime now)
    {
        var bucket = (int)(now.Ticks / (TimeSpan.TicksPerMinute * 10));
        var rng = new Random(unchecked(bucket * 397));
        var players = now.Hour switch
        {
            >= 17 and <= 23 => rng.Next(200, 301),
            >= 0 and <= 4 => rng.Next(150, 201),
            >= 5 and <= 7 => rng.Next(30, 61),
            >= 8 and <= 10 => rng.Next(60, 101),
            _ => rng.Next(100, 181)
        };
        return (players, rng.Next(3500, 4001));
    }

    internal static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            >= 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024 * 1024):0.0} GB",
            >= 1024L * 1024 => $"{bytes / (1024.0 * 1024):0.0} MB",
            >= 1024L => $"{bytes / 1024.0:0.0} KB",
            _ => $"{bytes} B"
        };
    }
}
