using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace IconicLauncher.Core.Services;

public enum VideoPreset
{
    Poor = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Extreme = 4
}

public enum VideoFile
{
    Cfg,
    Profile
}

public sealed record VideoKey(VideoFile File, string Key, string Label, double[] Values);

public sealed class DayZVideoState
{
    public required string CfgPath { get; init; }
    public required string ProfilePath { get; init; }
    public required IReadOnlyDictionary<string, double> Values { get; init; }
    public VideoPreset? Preset { get; init; }
    public required IReadOnlyList<VideoKey> AboveMedium { get; init; }
    public bool Windowed { get; init; }
    public bool HasBackup { get; init; }

    public bool IsAboveMedium => AboveMedium.Count > 0;

    public string PresetLabel => Preset?.ToString() ?? "Custom";
}

public static class DayZVideoPresets
{
    public static readonly IReadOnlyList<VideoKey> Keys = new[]
    {
        new VideoKey(VideoFile.Profile, "TexQuality", "textures", new double[] { 0, 1, 3, 3, 4 }),
        new VideoKey(VideoFile.Profile, "sceneComplexity", "object detail", new double[] { 200000, 300000, 500000, 750000, 1000000 }),
        new VideoKey(VideoFile.Profile, "shadowQuality", "shadows", new double[] { 0, 1, 2, 3, 4 }),
        new VideoKey(VideoFile.Profile, "shadowZDistance", "shadow distance", new double[] { 100, 125, 175, 175, 250 }),
        new VideoKey(VideoFile.Profile, "viewDistance", "view distance", new double[] { 1000, 1400, 1600, 2200, 4000 }),
        new VideoKey(VideoFile.Profile, "preferredObjectViewDistance", "object view distance", new double[] { 835, 1400, 1600, 2000, 2400 }),
        new VideoKey(VideoFile.Profile, "WaterQuality", "water reflections", new double[] { 0, 1, 1, 1, 2 }),
        new VideoKey(VideoFile.Cfg, "TerrainDetail", "terrain surface", new double[] { 0, 2, 2, 2, 3 }),
        new VideoKey(VideoFile.Cfg, "PostFX", "post-processing", new double[] { 0, 1, 2, 2, 2 }),
        new VideoKey(VideoFile.Cfg, "FXAA", "post AA", new double[] { 0, 2, 2, 3, 4 }),
        new VideoKey(VideoFile.Cfg, "FXAO", "ambient occlusion", new double[] { 0, 0, 2, 3, 4 }),
        new VideoKey(VideoFile.Cfg, "MSAA", "hardware AA", new double[] { 0, 0, 0, 2, 8 }),
        new VideoKey(VideoFile.Cfg, "AToC", "foliage AA", new double[] { 0, 0, 0, 1, 1 })
    };
}

public sealed class DayZVideoSettingsService
{
    public const string BackupSuffix = ".iconic-backup";
    private static readonly Regex LineRegex = new(@"^(\s*)([A-Za-z0-9_]+)(\s*=\s*)([^;]*)(;.*)$", RegexOptions.Compiled);

    private readonly string _dir;
    private readonly string _userName;

    public DayZVideoSettingsService(string? documentsDayZDir = null, string? userName = null)
    {
        _dir = documentsDayZDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DayZ");
        _userName = userName ?? Environment.UserName;
    }

    public string Directory => _dir;

    public static bool IsDayZRunning()
    {
        foreach (var name in new[] { "DayZ_x64", "DayZ_BE" })
        {
            var procs = Process.GetProcessesByName(name);
            var any = procs.Length > 0;
            foreach (var p in procs)
            {
                p.Dispose();
            }
            if (any)
            {
                return true;
            }
        }
        return false;
    }

    public string CfgPath => Path.Combine(_dir, "DayZ.cfg");

    public string? FindProfilePath()
    {
        var own = Path.Combine(_dir, _userName + "_settings.DayZProfile");
        if (File.Exists(own))
        {
            return own;
        }
        if (!System.IO.Directory.Exists(_dir))
        {
            return null;
        }
        return new DirectoryInfo(_dir).GetFiles("*_settings.DayZProfile")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Select(f => f.FullName)
            .FirstOrDefault();
    }

    public DayZVideoState? Read()
    {
        var profile = FindProfilePath();
        if (profile is null || !File.Exists(CfgPath))
        {
            return null;
        }
        var cfg = ParseValues(File.ReadAllText(CfgPath));
        var prof = ParseValues(File.ReadAllText(profile));
        var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in DayZVideoPresets.Keys)
        {
            var source = key.File == VideoFile.Cfg ? cfg : prof;
            if (source.TryGetValue(key.Key, out var v))
            {
                values[key.Key] = v;
            }
        }
        var windowed = cfg.TryGetValue("Windowed", out var w) && w >= 1;
        return Evaluate(CfgPath, profile, values, windowed, File.Exists(CfgPath + BackupSuffix) || File.Exists(profile + BackupSuffix));
    }

    public static DayZVideoState Evaluate(string cfgPath, string profilePath, IReadOnlyDictionary<string, double> values, bool windowed, bool hasBackup)
    {
        var medium = (int)VideoPreset.Medium;
        var above = DayZVideoPresets.Keys
            .Where(k => values.TryGetValue(k.Key, out var v) && v > k.Values[medium] + 0.0001)
            .ToList();
        VideoPreset? preset = null;
        for (var i = 0; i <= (int)VideoPreset.Extreme; i++)
        {
            var idx = i;
            if (DayZVideoPresets.Keys.All(k => values.TryGetValue(k.Key, out var v) && Math.Abs(v - k.Values[idx]) < 0.0001))
            {
                preset = (VideoPreset)i;
                break;
            }
        }
        return new DayZVideoState
        {
            CfgPath = cfgPath,
            ProfilePath = profilePath,
            Values = values,
            Preset = preset,
            AboveMedium = above,
            Windowed = windowed,
            HasBackup = hasBackup
        };
    }

    public void ApplyPreset(VideoPreset preset)
    {
        Write(k => k.Values[(int)preset], null);
    }

    public void CapAt(VideoPreset preset, DayZVideoState state)
    {
        Write(k => state.Values.TryGetValue(k.Key, out var v) && v > k.Values[(int)preset] ? k.Values[(int)preset] : null, null);
    }

    public void SetWindowed(bool windowed)
    {
        Write(_ => null, windowed);
    }

    public bool RestoreBackup()
    {
        var profile = FindProfilePath();
        var restored = false;
        foreach (var path in new[] { CfgPath, profile })
        {
            if (path is null)
            {
                continue;
            }
            var backup = path + BackupSuffix;
            if (File.Exists(backup))
            {
                File.Copy(backup, path, true);
                File.Delete(backup);
                restored = true;
            }
        }
        return restored;
    }

    private void Write(Func<VideoKey, double?> valueFor, bool? windowed)
    {
        if (IsDayZRunning())
        {
            throw new InvalidOperationException("Close DayZ first - it overwrites these settings when it exits.");
        }
        var profile = FindProfilePath() ?? throw new FileNotFoundException("DayZ settings not found. Start DayZ once, then try again.");
        if (!File.Exists(CfgPath))
        {
            throw new FileNotFoundException("DayZ.cfg not found. Start DayZ once, then try again.");
        }
        var cfgChanges = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var profChanges = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in DayZVideoPresets.Keys)
        {
            var v = valueFor(key);
            if (v is null)
            {
                continue;
            }
            (key.File == VideoFile.Cfg ? cfgChanges : profChanges)[key.Key] = Format(v.Value);
        }
        if (windowed is not null)
        {
            cfgChanges["Windowed"] = windowed.Value ? "1" : "0";
        }
        WriteFile(CfgPath, cfgChanges);
        WriteFile(profile, profChanges);
    }

    private static void WriteFile(string path, IReadOnlyDictionary<string, string> changes)
    {
        if (changes.Count == 0)
        {
            return;
        }
        var original = File.ReadAllText(path);
        var updated = ApplyChanges(original, changes);
        if (updated == original)
        {
            return;
        }
        var backup = path + BackupSuffix;
        if (!File.Exists(backup))
        {
            File.Copy(path, backup);
        }
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, updated, new UTF8Encoding(false));
        File.Move(tmp, path, true);
    }

    public static string ApplyChanges(string text, IReadOnlyDictionary<string, string> changes)
    {
        var newline = text.Contains("\r\n") ? "\r\n" : "\n";
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var trailing = lines.Count > 0 && lines[^1].Length == 0;
        if (trailing)
        {
            lines.RemoveAt(lines.Count - 1);
        }
        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < lines.Count; i++)
        {
            var m = LineRegex.Match(lines[i]);
            if (!m.Success || !changes.TryGetValue(m.Groups[2].Value, out var value))
            {
                continue;
            }
            lines[i] = m.Groups[1].Value + m.Groups[2].Value + m.Groups[3].Value + value + m.Groups[5].Value;
            done.Add(m.Groups[2].Value);
        }
        foreach (var (key, value) in changes)
        {
            if (!done.Contains(key))
            {
                lines.Add(key + "=" + value + ";");
            }
        }
        var result = string.Join(newline, lines);
        return trailing ? result + newline : result;
    }

    public static Dictionary<string, double> ParseValues(string text)
    {
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in text.Split('\n'))
        {
            var m = LineRegex.Match(raw.TrimEnd('\r'));
            if (!m.Success)
            {
                continue;
            }
            var value = m.Groups[4].Value.Trim().TrimEnd('f');
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            {
                result[m.Groups[2].Value] = d;
            }
        }
        return result;
    }

    private static string Format(double v) => v.ToString("0.########", CultureInfo.InvariantCulture);
}
