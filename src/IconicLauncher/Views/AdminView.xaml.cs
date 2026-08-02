using System.Windows;
using System.Windows.Controls;
using IconicLauncher.ViewModels;
using Microsoft.Win32;

namespace IconicLauncher.Views;

public partial class AdminView : UserControl
{
    public AdminView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is AdminViewModel vm && PwBox.Password.Length == 0 && vm.FtpPassword.Length > 0)
        {
            PwBox.Password = vm.FtpPassword;
        }
    }

    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is AdminViewModel vm)
        {
            vm.FtpPassword = PwBox.Password;
        }
    }

    private void OnSaveToFile(object sender, RoutedEventArgs e)
    {
        if (DataContext is not AdminViewModel vm || string.IsNullOrEmpty(vm.GeneratedJson)) return;
        var dialog = new SaveFileDialog
        {
            FileName = "launcher-config.json",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog() == true)
        {
            vm.SaveGeneratedJson(dialog.FileName);
        }
    }
}
