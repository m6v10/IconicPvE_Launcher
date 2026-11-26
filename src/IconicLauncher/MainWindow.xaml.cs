using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace IconicLauncher;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        StateChanged += (_, _) => SyncVideo();
        Activated += (_, _) => (DataContext as ViewModels.MainViewModel)?.SettingsPage.RefreshGraphics();
        DependencyPropertyDescriptor.FromProperty(MediaElement.SourceProperty, typeof(MediaElement))
            .AddValueChanged(DayZVideo, (_, _) => SyncVideo());
    }

    private bool VideoShouldPlay => DayZVideo.Source != null && DayZBackdrop.IsVisible && WindowState != WindowState.Minimized;

    private void SyncVideo()
    {
        if (VideoShouldPlay) DayZVideo.Play();
        else if (DayZVideo.Source != null) DayZVideo.Pause();
    }

    private void OnBackdropVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        SyncVideo();
    }

    private void OnDayZVideoLoaded(object sender, RoutedEventArgs e)
    {
        SyncVideo();
    }

    private void OnDayZVideoEnded(object sender, RoutedEventArgs e)
    {
        DayZVideo.Position = TimeSpan.Zero;
        SyncVideo();
    }

    private void OnDayZVideoFailed(object? sender, ExceptionRoutedEventArgs e)
    {
        DayZVideo.Visibility = Visibility.Collapsed;
    }
}
