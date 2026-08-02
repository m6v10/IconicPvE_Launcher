using Serilog;

namespace IconicLauncher.Core.Services;

public sealed class ModIntegrityChecker : IModIntegrityChecker
{
    private static readonly TimeSpan ModifiedTolerance = TimeSpan.FromHours(2);

    public string? Check(string installPath, long baselineUnixTime)
    {
        if (string.IsNullOrWhiteSpace(installPath) || !DirectoryHasContent(installPath))
            return "content directory missing or empty";

        var addonsDirs = FindTopLevelDirs(installPath, "addons");
        if (addonsDirs.Count == 0)
            return null;

        var pbos = new List<string>();
        foreach (var dir in addonsDirs)
        {
            try
            {
                pbos.AddRange(Directory.EnumerateFiles(dir, "*.pbo", SearchOption.TopDirectoryOnly));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to enumerate PBO files under {Path}", dir);
            }
        }
        if (pbos.Count == 0)
            return "no PBO files in addons folder";

        foreach (var pbo in pbos)
        {
            try
            {
                var dir = Path.GetDirectoryName(pbo)!;
                var name = Path.GetFileName(pbo);
                if (!Directory.EnumerateFiles(dir, name + ".*.bisign").Any())
                    return "missing .bisign for " + name;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to check signature presence for {File}", pbo);
            }
        }

        if (baselineUnixTime > 0)
        {
            var limit = DateTimeOffset.FromUnixTimeSeconds(baselineUnixTime).UtcDateTime + ModifiedTolerance;
            foreach (var file in EnumerateTrackedFiles(installPath, addonsDirs))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) > limit)
                        return "modified after install: " + Path.GetFileName(file);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to read timestamp for {File}", file);
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateTrackedFiles(string installPath, List<string> addonsDirs)
    {
        var files = new List<string>();
        foreach (var dir in addonsDirs)
        {
            try
            {
                files.AddRange(Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly)
                    .Where(f => f.EndsWith(".pbo", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".bisign", StringComparison.OrdinalIgnoreCase)));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to enumerate files under {Path}", dir);
            }
        }
        foreach (var dir in FindTopLevelDirs(installPath, "keys"))
        {
            try
            {
                files.AddRange(Directory.EnumerateFiles(dir, "*.bikey", SearchOption.TopDirectoryOnly));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to enumerate keys under {Path}", dir);
            }
        }
        return files;
    }

    private static List<string> FindTopLevelDirs(string installPath, string name)
    {
        try
        {
            return Directory.EnumerateDirectories(installPath)
                .Where(d => string.Equals(Path.GetFileName(d), name, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to enumerate directories under {Path}", installPath);
            return new List<string>();
        }
    }

    private static bool DirectoryHasContent(string path)
    {
        try
        {
            return Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any();
        }
        catch
        {
            return false;
        }
    }
}
