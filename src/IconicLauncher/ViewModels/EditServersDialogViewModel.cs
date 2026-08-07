using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace IconicLauncher.ViewModels;

public sealed partial class EditServersDialogViewModel : ObservableObject
{
    private readonly MainViewModel _owner;

    public ObservableCollection<ServerListRowViewModel> Rows { get; } = new();

    public EditServersDialogViewModel(MainViewModel owner)
    {
        _owner = owner;
        Reload();
    }

    [ObservableProperty]
    private string? errorText;

    [ObservableProperty]
    private string headerText = "";

    internal void Reload()
    {
        Rows.Clear();
        var entries = _owner.BuildServerList();
        foreach (var entry in entries)
        {
            Rows.Add(new ServerListRowViewModel(this, _owner, entry));
        }
        var shown = entries.Count(e => e.IsVisible);
        HeaderText = $"{shown} of {entries.Count} servers shown";
    }

    internal void ShowError(string? message)
    {
        ErrorText = message;
    }

    internal void RefreshHeader()
    {
        var shown = Rows.Count(r => r.IsShown);
        HeaderText = $"{shown} of {Rows.Count} servers shown";
    }

    [RelayCommand]
    private void RestoreDefaults()
    {
        var answer = MessageBox.Show(
            "Show every server that Iconic PvE ships with again? Servers you added yourself are kept.",
            "Restore default list",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }
        _owner.RestoreDefaultServerVisibility();
        ErrorText = null;
        Reload();
    }

    [RelayCommand]
    private void AddServer()
    {
        _owner.ShowAddServerCommand.Execute(null);
    }

    [RelayCommand]
    private void Close()
    {
        _owner.CloseDialogCommand.Execute(null);
    }
}

public sealed partial class ServerListRowViewModel : ObservableObject
{
    private readonly EditServersDialogViewModel _dialog;
    private readonly MainViewModel _owner;
    private bool _updating;

    public string Id { get; }
    public string Name { get; }
    public string Address { get; }
    public bool IsCustom { get; }
    public string SourceLabel { get; }
    public string ModsLabel { get; }

    public ServerListRowViewModel(EditServersDialogViewModel dialog, MainViewModel owner, Core.Services.ServerListEntry entry)
    {
        _dialog = dialog;
        _owner = owner;
        Id = entry.Server.Id;
        Name = entry.Server.Name;
        Address = $"{entry.Server.Ip}:{entry.Server.GamePort}";
        IsCustom = entry.IsCustom;
        SourceLabel = entry.IsCustom ? "YOURS" : "ICONIC";
        ModsLabel = entry.Server.ModsFromQuery
            ? "mods read from the server"
            : $"{entry.Server.Mods.Count} mods";
        isShown = entry.IsVisible;
    }

    [ObservableProperty]
    private bool isShown;

    partial void OnIsShownChanged(bool value)
    {
        if (_updating)
        {
            return;
        }
        var error = _owner.SetServerVisible(Id, value);
        if (error != null)
        {
            _dialog.ShowError(error);
            _updating = true;
            IsShown = !value;
            _updating = false;
            return;
        }
        _dialog.ShowError(null);
        _dialog.RefreshHeader();
    }

    [RelayCommand]
    private void Delete()
    {
        var answer = MessageBox.Show(
            $"Remove {Name} from your launcher for good?",
            "Delete server",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }
        var error = _owner.DeleteCustomServer(Id);
        if (error != null)
        {
            _dialog.ShowError(error);
            return;
        }
        _dialog.ShowError(null);
        _dialog.Reload();
    }
}
