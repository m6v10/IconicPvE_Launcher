using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IconicLauncher.Core.Models;
using Serilog;

namespace IconicLauncher.ViewModels;

public sealed partial class HomeViewModel : ObservableObject
{
    public ObservableCollection<ServerCardViewModel> Servers { get; } = new();
    public ObservableCollection<NewsItem> News { get; } = new();

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
