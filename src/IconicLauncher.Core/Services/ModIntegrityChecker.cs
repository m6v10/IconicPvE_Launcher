using System.Text.Json;
using IconicLauncher.Core.Utils;
using Serilog;

namespace IconicLauncher.Core.Services;

public sealed class IntegrityBaseline
{
    public string Manifest { get; set; } = "";
    public Dictionary<string, string> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ModIntegrityChecker : IModIntegrityChecker
{
    private readonly string? _baselinePath;
    private readonly object _sync = new();
    private Dictionary<string, IntegrityBaseline>? _baselines;

    public ModIntegrityChecker(string? baselinePath = null)
    {
        _baselinePath = baselinePath;
    }

    public string? Check(string workshopId, string installPath, string? manifest)
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

        if (string.IsNullOrWhiteSpace(workshopId) || string.IsNullOrWhiteSpace(manifest))
            return null;

        var current = Snapshot(installPath, addonsDirs);
        lock (_sync)
        {
            var baselines = LoadBaselines();
            if (!baselines.TryGetValue(workshopId, out var baseline) || baseline.Manifest != manifest)
            {
                baselines[workshopId] = new IntegrityBaseline { Manifest = manifest, Files = current };
                SaveBaselines(baselines);
                return null;
            }
            return Compare(baseline.Files, current);
        }
    }

    public void Forget(string workshopId)
    {
        if (string.IsNullOrWhiteSpace(workshopId))
            return;
        lock (_sync)
        {
            var baselines = LoadBaselines();
            if (baselines.Remove(workshopId))
                SaveBaselines(baselines);
        }
    }

    private static string? Compare(Dictionary<string, string> baseline, Dictionary<string, string> current)
    {
        foreach (var (path, stamp) in baseline)
        {
            if (!current.TryGetValue(path, out var now))
                return "file removed after install: " + Path.GetFileName(path);
            if (now != stamp)
                return "modified after install: " + Path.GetFileName(path);
        }
        foreach (var path in current.Keys)
        {
            if (!baseline.ContainsKey(path))
                return "file added after install: " + Path.GetFileName(path);
        }
        return null;
    }

    private static Dictionary<string, string> Snapshot(string installPath, List<string> addonsDirs)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in EnumerateTrackedFiles(installPath, addonsDirs))
        {
            try
            {
                var info = new FileInfo(file);
                map[Path.GetRelativePath(installPath, file)] = info.Length + ":" + info.LastWriteTimeUtc.Ticks;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to read file info for {File}", file);
            }
        }
        return map;
    }

    private Dictionary<string, IntegrityBaseline> LoadBaselines()
    {
        if (_baselines != null)
            return _baselines;
        _baselines = new Dictionary<string, IntegrityBaseline>(StringComparer.OrdinalIgnoreCase);
        if (_baselinePath == null || !File.Exists(_baselinePath))
            return _baselines;
        try
        {
            var loaded = JsonSerializer.Deserialize<Dictionary<string, IntegrityBaseline>>(File.ReadAllText(_baselinePath), JsonDefaults.Options);
            if (loaded != null)
            {
                foreach (var (id, baseline) in loaded)
                {
                    baseline.Files = new Dictionary<string, string>(baseline.Files ?? new(), StringComparer.OrdinalIgnoreCase);
                    _baselines[id] = baseline;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Integrity baseline file unreadable, starting fresh");
        }
        return _baselines;
    }

    private void SaveBaselines(Dictionary<string, IntegrityBaseline> baselines)
    {
        if (_baselinePath == null)
            return;
        try
        {
            AtomicFile.WriteAllTextAtomic(_baselinePath, JsonSerializer.Serialize(baselines, JsonDefaults.Options));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Saving the integrity baseline failed");
        }
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
