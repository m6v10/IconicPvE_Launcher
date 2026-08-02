using System.Windows;
using System.Windows.Controls;
using IconicLauncher.ViewModels;

namespace IconicLauncher.Views;

public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
    }

    private void OnServerPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox box && box.DataContext is ServerCardViewModel vm)
        {
            vm.ServerPassword = box.Password;
        }
    }
}
