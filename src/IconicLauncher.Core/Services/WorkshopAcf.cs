using Gameloop.Vdf;
using Gameloop.Vdf.Linq;
using Serilog;

namespace IconicLauncher.Core.Services;

public sealed class AcfInstalledItem
{
    public string Id = "";
    public string Manifest = "";
    public long TimeUpdated;
    public long Size;
}

public sealed class AcfDetailsItem
{
    public string Id = "";
    public string Manifest = "";
    public string? LatestManifest;
    public long TimeUpdated;
    public long LatestTimeUpdated;
    public long TimeTouched;
}

public sealed class WorkshopAcf
{
    public Dictionary<string, AcfInstalledItem> Installed { get; } = new();
    public Dictionary<string, AcfDetailsItem> Details { get; } = new();

    public static WorkshopAcf Parse(string content)
    {
        var acf = new WorkshopAcf();
        var root = VdfConvert.Deserialize(content);
        if (!string.Equals(root.Key, "AppWorkshop", StringComparison.OrdinalIgnoreCase) || root.Value is not VObject app)
            throw new FormatException("Not an appworkshop ACF document");

        if (app.TryGetValue("WorkshopItemsInstalled", out var installedToken) && installedToken is VObject installed)
        {
            foreach (var prop in installed.Properties())
            {
                if (prop.Value is not VObject item)
                    continue;
                acf.Installed[prop.Key] = new AcfInstalledItem
                {
                    Id = prop.Key,
                    Manifest = GetString(item, "manifest"),
                    TimeUpdated = GetLong(item, "timeupdated"),
                    Size = GetLong(item, "size")
                };
            }
        }

        if (app.TryGetValue("WorkshopItemDetails", out var detailsToken) && detailsToken is VObject details)
        {
            foreach (var prop in details.Properties())
            {
                if (prop.Value is not VObject item)
                    continue;
                acf.Details[prop.Key] = new AcfDetailsItem
                {
                    Id = prop.Key,
                    Manifest = GetString(item, "manifest"),
                    LatestManifest = GetOptionalString(item, "latest_manifest"),
                    TimeUpdated = GetLong(item, "timeupdated"),
                    LatestTimeUpdated = GetLong(item, "latest_timeupdated"),
                    TimeTouched = GetLong(item, "timetouched")
                };
            }
        }

        return acf;
    }

    public static WorkshopAcf? Load(string acfPath)
    {
        if (!File.Exists(acfPath))
        {
            Log.Warning("ACF file not found at {Path}", acfPath);
            return null;
        }
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var stream = new FileStream(acfPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                return Parse(reader.ReadToEnd());
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "ACF load attempt {Attempt} of 3 failed for {Path}", attempt, acfPath);
                if (attempt < 3)
                    Thread.Sleep(200);
            }
        }
        return null;
    }

    private static string GetString(VObject obj, string key) =>
        obj.TryGetValue(key, out var token) ? token.ToString() : "";

    private static string? GetOptionalString(VObject obj, string key) =>
        obj.TryGetValue(key, out var token) ? token.ToString() : null;

    private static long GetLong(VObject obj, string key) =>
        obj.TryGetValue(key, out var token) && long.TryParse(token.ToString(), out var value) ? value : 0;
}
