using System.Text;
using IconicLauncher.Core.Models;
using IconicLauncher.Core.Utils;

namespace IconicLauncher.Core.Services;

public sealed class LogDumpService
{
    public const string DumpHeaderMagic = "=== Iconic PvE Launcher Logdump ===";
    public const int MaxDumpBytes = 900_000;
    private const int MaxLogFiles = 3;

    public static string BuildDump(string logDir, string versionText, LauncherSettings settings, DateTime nowUtc)
    {
        var sb = new StringBuilder();
        sb.AppendLine(DumpHeaderMagic);
        sb.AppendLine($"generated: {nowUtc:yyyy-MM-dd HH:mm:ss} UTC (local {nowUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss})");
        sb.AppendLine($"launcher version: {versionText}");
        sb.AppendLine($"os: {Environment.OSVersion.VersionString} 64bit={Environment.Is64BitOperatingSystem}");
        sb.AppendLine($"runtime: {Environment.Version}");
        sb.AppendLine($"machine locale: {System.Globalization.CultureInfo.CurrentCulture.Name}");
        sb.AppendLine($"debug logging: {settings.DebugLogging}");
        sb.AppendLine($"profile name: {settings.ProfileName}");
        sb.AppendLine($"dayz path override: {settings.DayZPathOverride ?? "(none)"}");
        sb.AppendLine($"steam path override: {settings.SteamPathOverride ?? "(none)"}");
        sb.AppendLine($"config url override: {settings.ConfigUrlOverride ?? "(none)"}");
        sb.AppendLine($"launch flags: noSplash={settings.LaunchNoSplash} noPause={settings.LaunchNoPause} window={settings.LaunchWindowed} doLogs={settings.LaunchDoLogs}");
        sb.AppendLine($"extra params: {settings.ExtraLaunchParams}");
        sb.AppendLine($"last selected server: {settings.LastSelectedServerId ?? "(none)"}");
        sb.AppendLine();

        var files = ListLogFiles(logDir);
        if (files.Count == 0)
        {
            sb.AppendLine("(no log files found in " + logDir + ")");
            return sb.ToString();
        }

        var budget = MaxDumpBytes - sb.Length;
        var perFile = new List<(string Path, string Content)>();
        foreach (var file in files)
        {
            var content = SafeReadShared(file);
            perFile.Add((file, content));
        }

        var totalLen = perFile.Sum(f => f.Content.Length);
        for (var i = 0; i < perFile.Count && totalLen > budget; i++)
        {
            var (path, content) = perFile[i];
            var others = totalLen - content.Length;
            var allowed = Math.Max(0, budget - others);
            if (content.Length > allowed)
            {
                var cut = content.Length - allowed;
                var truncated = allowed == 0 ? "" : content[cut..];
                perFile[i] = (path, $"[... truncated {cut} chars, showing the most recent {allowed} ...]\r\n" + truncated);
                totalLen = others + perFile[i].Content.Length;
            }
        }

        foreach (var (path, content) in perFile)
        {
            sb.AppendLine($"----- {Path.GetFileName(path)} -----");
            sb.AppendLine(content.TrimEnd());
            sb.AppendLine();
        }
        return sb.ToString();
    }

    public static string DumpFileName(DateTime nowLocal) =>
        $"IconicPvE_Launcher_Logdump_{nowLocal:yyyy-MM-dd_HH-mm-ss}.log";

    public static string WriteToDesktop(string dump, DateTime nowLocal)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var path = Path.Combine(desktop, DumpFileName(nowLocal));
        File.WriteAllText(path, dump, new UTF8Encoding(false));
        return path;
    }

    private static List<string> ListLogFiles(string logDir)
    {
        try
        {
            if (!Directory.Exists(logDir))
                return new List<string>();
            return Directory.EnumerateFiles(logDir, "launcher-*.log")
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .TakeLast(MaxLogFiles)
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    private static string SafeReadShared(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception ex)
        {
            return $"(could not read {Path.GetFileName(path)}: {ex.Message})";
        }
    }
}
