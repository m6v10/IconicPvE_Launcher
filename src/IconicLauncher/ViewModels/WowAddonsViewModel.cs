using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IconicLauncher.Core.Models;
using IconicLauncher.Core.Services;
using Serilog;

namespace IconicLauncher.ViewModels;

public sealed partial class WowAddonRowViewModel : ObservableObject
{
    private readonly WowAddonsViewModel _owner;
    internal WowAddonEntry Entry { get; private set; }

    [ObservableProperty]
    private string stateText = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    private string actionLabel = "INSTALL";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    private bool canAct = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    private bool isBusy;

    [ObservableProperty]
    private double percent;

    [ObservableProperty]
    private bool isInstalled;

    public string Name => Entry.Name;
    public string Description => Entry.Description;
    public string? ImageUrl => Entry.ImageUrl;
    public string InitialLetter => string.IsNullOrEmpty(Entry.Name) ? "?" : Entry.Name[..1].ToUpperInvariant();
    public bool HasImage => !string.IsNullOrWhiteSpace(Entry.ImageUrl);

    public WowAddonRowViewModel(WowAddonsViewModel owner, WowAddonStatus status)
    {
        _owner = owner;
        Entry = status.Entry;
        ApplyStatus(status);
    }

    public void ApplyStatus(WowAddonStatus status)
    {
        Entry = status.Entry;
        IsInstalled = status.State == WowAddonState.Installed;
        switch (status.State)
        {
            case WowAddonState.Installed:
                StateText = "Installed and up to date";
                ActionLabel = "INSTALLED";
                CanAct = false;
                break;
            case WowAddonState.UpdateAvailable:
                StateText = $"Update available - {WowViewModel.FormatBytes(status.BytesToDownload)}";
                ActionLabel = "UPDATE";
                CanAct = true;
                break;
            default:
                StateText = WowViewModel.FormatBytes(status.BytesToDownload);
                ActionLabel = "INSTALL";
                CanAct = true;
                break;
        }
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(ImageUrl));
        OnPropertyChanged(nameof(InitialLetter));
        OnPropertyChanged(nameof(HasImage));
    }

    private bool CanInstall => CanAct && !IsBusy && !_owner.IsRefreshing;

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private Task InstallAsync() => _owner.InstallAsync(this);
}

public sealed partial class WowAddonsViewModel : ObservableObject
{
    private readonly MainViewModel _owner;
    private readonly IWowPatchService _patch;
    private WowConfig? _config;
    private bool _loadedOnce;

    public ObservableCollection<WowAddonRowViewModel> Addons { get; } = new();

    [ObservableProperty]
    private bool isRefreshing;

    [ObservableProperty]
    private string? statusText;

    [ObservableProperty]
    private string? errorText;

    [ObservableProperty]
    private bool clientPathValid;

    [ObservableProperty]
    private bool showRestartHint;

    public WowAddonsViewModel(MainViewModel owner, IWowPatchService patch)
    {
        _owner = owner;
        _patch = patch;
    }

    public void ApplyConfig(WowConfig? config)
    {
        _config = config;
        _loadedOnce = false;
    }

    public void OnNavigatedTo()
    {
        ClientPathValid = WowPatchService.IsValidClientRoot(_owner.SettingsService.Settings.WowClientPath);
        if (!_loadedOnce)
        {
            _ = RefreshAsync();
        }
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsRefreshing) return;
        ClientPathValid = WowPatchService.IsValidClientRoot(_owner.SettingsService.Settings.WowClientPath);
        if (_config is null || string.IsNullOrWhiteSpace(_config.AddonsUrl))
        {
            StatusText = "No optional addons are published yet";
            Addons.Clear();
            return;
        }
        if (!ClientPathValid)
        {
            StatusText = "Set your game folder on the WoW page first";
            return;
        }
        IsRefreshing = true;
        ErrorText = null;
        StatusText = "Checking addons...";
        try
        {
            var statuses = await _patch.FetchAddonsAsync(_config, _owner.SettingsService.Settings.WowClientPath!, CancellationToken.None);
            Addons.Clear();
            foreach (var status in statuses)
            {
                Addons.Add(new WowAddonRowViewModel(this, status));
            }
            _loadedOnce = true;
            StatusText = Addons.Count == 0 ? "No optional addons are published yet" : null;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Addon list fetch failed");
            ErrorText = "Could not load the addon list: " + ex.Message;
            StatusText = null;
        }
        finally
        {
            IsRefreshing = false;
            foreach (var row in Addons)
            {
                row.InstallCommand.NotifyCanExecuteChanged();
            }
        }
    }

    internal async Task InstallAsync(WowAddonRowViewModel row)
    {
        if (_config is null || !ClientPathValid) return;
        var clientRoot = _owner.SettingsService.Settings.WowClientPath!;
        row.IsBusy = true;
        row.Percent = 0;
        ErrorText = null;
        try
        {
            var progress = new Progress<WowApplyProgress>(p =>
            {
                row.Percent = p.BytesTotal > 0 ? p.BytesDone * 100.0 / p.BytesTotal : 100;
            });
            await _patch.InstallAddonAsync(_config, clientRoot, row.Entry, progress, CancellationToken.None);
            row.ApplyStatus(WowPatchService.DiffAddon(row.Entry, clientRoot));
            ShowRestartHint = true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Addon install failed for {Addon}", row.Entry.Id);
            ErrorText = $"Installing {row.Name} failed: " + ex.Message;
        }
        finally
        {
            row.IsBusy = false;
            row.InstallCommand.NotifyCanExecuteChanged();
        }
    }
}
