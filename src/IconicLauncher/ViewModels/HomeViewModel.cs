using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IconicLauncher.Core.Models;
using Serilog;

namespace IconicLauncher.ViewModels;

public sealed partial class HomeViewModel : ObservableObject
{
    private readonly MainViewModel? _owner;
    private bool _userPicked;

    public ObservableCollection<ServerCardViewModel> Servers { get; } = new();
    public ObservableCollection<NewsItem> News { get; } = new();

    [ObservableProperty]
    private ServerCardViewModel? selectedServer;

    [ObservableProperty]
    private bool isPickerOpen;

    public HomeViewModel(MainViewModel? owner = null)
    {
        _owner = owner;
        Servers.CollectionChanged += OnServersChanged;
    }

    partial void OnSelectedServerChanged(ServerCardViewModel? oldValue, ServerCardViewModel? newValue)
    {
        if (oldValue != null) oldValue.IsSelected = false;
        if (newValue != null) newValue.IsSelected = true;
        _owner?.Profile.SetFocusServer(newValue);
    }

    [RelayCommand]
    private void TogglePicker()
    {
        IsPickerOpen = !IsPickerOpen;
    }

    [RelayCommand]
    private void PickServer(ServerCardViewModel? card)
    {
        IsPickerOpen = false;
        if (card is null) return;
        _userPicked = true;
        SelectedServer = card;
        _owner?.RememberSelectedServer(card.Server.Id);
    }

    private void OnServersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Move) return;
        var saved = _owner?.SettingsService.Settings.LastSelectedServerId;
        if (SelectedServer != null && Servers.Contains(SelectedServer))
        {
            if (_userPicked || SelectedServer.Server.Id == saved) return;
            var preferred = Servers.FirstOrDefault(s => s.Server.Id == saved);
            if (preferred != null) SelectedServer = preferred;
            return;
        }
        SelectedServer = Servers.FirstOrDefault(s => s.Server.Id == saved) ?? Servers.FirstOrDefault();
    }

    [RelayCommand]
    private void OpenNewsLink(NewsItem? item)
    {
        if (item?.Url is not { Length: > 0 } url) return;
        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Opening news link failed");
        }
    }
}
