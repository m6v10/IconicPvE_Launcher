using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Serilog;

namespace IconicLauncher.Core.Services;

public static class WowClientLocator
{
    private static readonly Regex NameHint = new(@"wow|warcraft|wotlk|lich|3\.3\.5", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] SkipDirNames =
    {
        "windows", "$recycle.bin", "system volume information", "programdata", "appdata",
        "node_modules", ".git", "windowsapps", "winsxs", "temp", "cache", "onedrive",
        "steamapps", "$windows.~bt", "recovery", "perflogs", "intel", "amd", "nvidia", "drivers"
    };

    public static Task<IReadOnlyList<string>> ScanAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        return Task.Run<IReadOnlyList<string>>(() => Scan(DateTime.UtcNow + timeout, ct), ct);
    }

    private static List<string> Scan(DateTime deadline, CancellationToken ct)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            foreach (var subKey in new[] { @"SOFTWARE\Blizzard Entertainment\World of Warcraft", @"SOFTWARE\WOW6432Node\Blizzard Entertainment\World of Warcraft" })
            {
                TryAdd(found, ReadRegistryString(hive, subKey, "InstallPath"));
            }
        }

        var roots = new List<string>();
        foreach (var folder in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) is { Length: > 0 } up ? Path.Combine(up, "Downloads") : null,
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        })
        {
            if (!string.IsNullOrWhiteSpace(folder)) roots.Add(folder!);
        }
        foreach (var drive in SafeGetFixedDrives())
        {
            roots.Add(drive);
            roots.Add(Path.Combine(drive, "Games"));
            roots.Add(Path.Combine(drive, "Spiele"));
        }

        foreach (var root in roots)
        {
            if (DateTime.UtcNow > deadline || ct.IsCancellationRequested) break;
            ScanShallow(root, found, hintOnly: false, deadline, ct);
        }

        foreach (var drive in SafeGetFixedDrives())
        {
            if (DateTime.UtcNow > deadline || ct.IsCancellationRequested) break;
            ScanBfs(drive, found, maxDepth: 4, deadline, ct);
        }

        var ranked = found
            .OrderByDescending(IsWotlkExe)
            .ThenBy(p => p.Length)
            .ToList();
        Log.Information("WoW client scan found {Count} candidate(s): {First}", ranked.Count, ranked.FirstOrDefault());
        return ranked;
    }

    private static void ScanShallow(string root, HashSet<string> found, bool hintOnly, DateTime deadline, CancellationToken ct)
    {
        TryAdd(found, root);
        foreach (var dir in SafeEnumDirs(root))
        {
            if (DateTime.UtcNow > deadline || ct.IsCancellationRequested) return;
            var name = Path.GetFileName(dir);
            if (hintOnly && !NameHint.IsMatch(name)) continue;
            TryAdd(found, dir);
            if (NameHint.IsMatch(name))
            {
                foreach (var sub in SafeEnumDirs(dir))
                {
                    TryAdd(found, sub);
                }
            }
        }
    }

    private static void ScanBfs(string root, HashSet<string> found, int maxDepth, DateTime deadline, CancellationToken ct)
    {
        var queue = new Queue<(string path, int depth)>();
        queue.Enqueue((root, 0));
        while (queue.Count > 0)
        {
            if (DateTime.UtcNow > deadline || ct.IsCancellationRequested) return;
            var (path, depth) = queue.Dequeue();
            foreach (var dir in SafeEnumDirs(path))
            {
                var name = Path.GetFileName(dir);
                if (SkipDirNames.Contains(name.ToLowerInvariant())) continue;
                var info = new DirectoryInfo(dir);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                TryAdd(found, dir);
                if (depth + 1 < maxDepth)
                {
                    queue.Enqueue((dir, depth + 1));
                }
            }
        }
    }

    private static bool IsWotlkExe(string clientRoot)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(Path.Combine(clientRoot, "Wow.exe"));
            var version = info.FileVersion ?? "";
            return version.StartsWith("3.3.5") || version.StartsWith("3, 3, 5");
        }
        catch
        {
            return false;
        }
    }

    private static void TryAdd(HashSet<string> found, string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && WowPatchService.IsValidClientRoot(path))
            {
                found.Add(Path.GetFullPath(path));
            }
        }
        catch
        {
        }
    }

    private static IEnumerable<string> SafeEnumDirs(string path)
    {
        try
        {
            if (!Directory.Exists(path)) return Array.Empty<string>();
            return Directory.EnumerateDirectories(path);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static IEnumerable<string> SafeGetFixedDrives()
    {
        var drives = new List<string>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (drive.DriveType == DriveType.Fixed && drive.IsReady)
                    {
                        drives.Add(drive.RootDirectory.FullName);
                    }
                }
                catch
                {
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Enumerating drives failed");
        }
        return drives;
    }

    private static string? ReadRegistryString(RegistryKey hive, string subKey, string valueName)
    {
        try
        {
            using var key = hive.OpenSubKey(subKey);
            return key?.GetValue(valueName) as string;
        }
        catch
        {
            return null;
        }
    }
}
