using System.Windows;
using System.Windows.Media;

namespace IconicLauncher.Services;

public static class GameTheme
{
    private static readonly Dictionary<string, (string DayZ, string Wow)> Palette = new()
    {
        ["AccentBrush"] = ("#D4AF37", "#69CCF0"),
        ["AccentSoftBrush"] = ("#EBCF6E", "#9FDFF7"),
        ["AccentDeepBrush"] = ("#9A7B1F", "#3D93B8"),
        ["HotBrush"] = ("#FFF4CE", "#D6F2FC"),
        ["HairlineBrush"] = ("#29D4AF37", "#2969CCF0"),
        ["AccentTintBrush"] = ("#1AD4AF37", "#1A69CCF0")
    };

    public static void Apply(bool wow)
    {
        var app = Application.Current;
        if (app is null) return;
        foreach (var (key, colors) in Palette)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(wow ? colors.Wow : colors.DayZ));
            brush.Freeze();
            app.Resources[key] = brush;
        }
    }
}
