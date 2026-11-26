using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace IconicLauncher.ViewModels;

public sealed partial class ProfileTab : ObservableObject
{
    public string Key { get; init; } = "";
    public string Label { get; init; } = "";
    public bool IsAllTime { get; init; }

    [ObservableProperty]
    private bool isSelected;
}

public sealed record StatTile(string Label, string Value, string Sub, string? ToolTip = null);

public sealed record StatRow(string Label, string Value, string? ToolTip = null);

public sealed record StatSection(string Title, IReadOnlyList<StatRow> Rows);

public sealed record BarRow(string Label, string Value, double Fraction, string Group);

public sealed record AchievementChip(string Title, string Description);

public sealed record HistoryRow(string Era, string Server, string Points, string Rank, string Level);

public sealed record HomeChip(string Badge, string Name, string Line, bool HasStats);

public static class Fmt
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    public static string Number(long value) => value.ToString("#,0", Culture);

    public static string Compact(long value)
    {
        var abs = Math.Abs(value);
        if (abs >= 1_000_000_000) return (value / 1_000_000_000d).ToString("0.##", Culture) + "B";
        if (abs >= 1_000_000) return (value / 1_000_000d).ToString("0.##", Culture) + "M";
        if (abs >= 100_000) return (value / 1_000d).ToString("0", Culture) + "K";
        return Number(value);
    }

    public static string Hours(long minutes)
    {
        if (minutes <= 0) return "0 h";
        if (minutes < 60) return $"{minutes} min";
        return Number(minutes / 60) + " h";
    }

    public static string HoursLong(long minutes) => minutes < 60 ? $"{minutes} min" : $"{Number(minutes / 60)} h {minutes % 60} min";

    public static string Date(long unixSeconds) =>
        unixSeconds <= 0 ? "-" : DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime().ToString("d MMM yyyy", Culture);

    public static string Ago(long unixSeconds)
    {
        if (unixSeconds <= 0) return "-";
        var span = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        if (span.TotalMinutes < 2) return "just now";
        if (span.TotalHours < 1) return $"{(int)span.TotalMinutes} min ago";
        if (span.TotalDays < 1) return $"{(int)span.TotalHours} h ago";
        if (span.TotalDays < 60) return $"{(int)span.TotalDays} days ago";
        return Date(unixSeconds);
    }

    public static string Title(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return "";
        var words = id.Replace('_', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words.Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
    }
}
