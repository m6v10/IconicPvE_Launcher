using System.Diagnostics;
using System.Text.RegularExpressions;

namespace IconicLauncher.Core.Utils;

public static class GameVersion
{
    private static readonly Regex MajorMinorRegex = new(@"(\d+)\.(\d+)", RegexOptions.Compiled);

    public static string? MajorMinor(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return null;
        var match = MajorMinorRegex.Match(version);
        if (!match.Success)
            return null;
        return int.Parse(match.Groups[1].Value) + "." + int.Parse(match.Groups[2].Value).ToString("00");
    }

    public static string? ReadMajorMinor(string? gameDir)
    {
        if (gameDir == null)
            return null;
        try
        {
            var exe = Path.Combine(gameDir, "DayZ_x64.exe");
            if (!File.Exists(exe))
                return null;
            var info = FileVersionInfo.GetVersionInfo(exe);
            return MajorMinor(info.ProductVersion) ?? MajorMinor(info.FileVersion);
        }
        catch
        {
            return null;
        }
    }
}
