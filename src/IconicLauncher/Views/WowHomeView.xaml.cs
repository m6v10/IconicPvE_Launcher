using System.Windows;
using System.Windows.Controls;

namespace IconicLauncher.Views;

public partial class WowHomeView : UserControl
{
    public WowHomeView()
    {
        InitializeComponent();
    }

    private void OnHeroVideoLoaded(object sender, RoutedEventArgs e)
    {
        var media = (MediaElement)sender;
        if (media.Source != null)
        {
            media.Play();
        }
    }

    private void OnHeroVideoEnded(object sender, RoutedEventArgs e)
    {
        var media = (MediaElement)sender;
        media.Position = TimeSpan.Zero;
        media.Play();
    }

    private void OnHeroVideoFailed(object? sender, ExceptionRoutedEventArgs e)
    {
        if (sender is MediaElement media)
        {
            media.Visibility = Visibility.Collapsed;
        }
    }
}
