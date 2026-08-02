using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using IconicLauncher.Core.Models;

namespace IconicLauncher.Converters;

public sealed class BoolToBrushConverter : IValueConverter
{
    public Brush? TrueBrush { get; set; }
    public Brush? FalseBrush { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? TrueBrush ?? Brushes.Transparent : FalseBrush ?? Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}

public sealed class ModStateToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Green = CreateFrozen(0x4C, 0xAF, 0x50);
    private static readonly SolidColorBrush Amber = CreateFrozen(0xFF, 0xB3, 0x00);
    private static readonly SolidColorBrush Red = CreateFrozen(0xE5, 0x39, 0x35);
    private static readonly SolidColorBrush Gray = CreateFrozen(0x60, 0x7D, 0x8B);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not ModState state) return Gray;
        return state switch
        {
            ModState.Ready => Green,
            ModState.Failed => Red,
            ModState.Unknown => Gray,
            _ => Amber
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }

    private static SolidColorBrush CreateFrozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null) return Visibility.Collapsed;
        if (value is string s && s.Length == 0) return Visibility.Collapsed;
        return Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}

public sealed class EmptyStringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
