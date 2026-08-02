using System.Text.Json;
using IconicLauncher.Core.Models;
using IconicLauncher.Core.Utils;
using Serilog;

namespace IconicLauncher.Core.Services;

public sealed class AdminConfigBuilder : IAdminConfigBuilder
{
    public LauncherConfig Build(string workshopFolder, LauncherConfig template, string serverId)
    {
        var discovered = new List<ModEntry>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var directories = Directory.EnumerateDirectories(workshopFolder)
            .Where(d => Path.GetFileName(d).StartsWith('@'))
            .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase);

        foreach (var dir in directories)
        {
            var metaPath = Path.Combine(dir, "meta.cpp");
            if (!File.Exists(metaPath))
            {
                Log.Warning("AdminConfigBuilder skipping {Folder}: no meta.cpp", dir);
                continue;
            }
            MetaCppInfo? info;
            try
            {
                info = MetaCppParser.Parse(File.ReadAllText(metaPath));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "AdminConfigBuilder skipping {Folder}: meta.cpp unreadable", dir);
                continue;
            }
            if (info == null)
            {
                Log.Warning("AdminConfigBuilder skipping {Folder}: no usable publishedid", dir);
                continue;
            }
            if (!seenIds.Add(info.PublishedId))
            {
                Log.Information("AdminConfigBuilder duplicate publishedid {Id} in {Folder}, keeping first occurrence", info.PublishedId, dir);
                continue;
            }
            discovered.Add(new ModEntry { WorkshopId = info.PublishedId, Name = info.Name });
        }

        var json = JsonSerializer.Serialize(template, JsonDefaults.Options);
        var result = JsonSerializer.Deserialize<LauncherConfig>(json, JsonDefaults.Options)!;
        result.GeneratedUtc = DateTime.UtcNow.ToString("o");

        var server = result.Servers.FirstOrDefault(s => s.Id == serverId);
        if (server == null)
        {
            Log.Warning("AdminConfigBuilder: server {ServerId} not found in template, returning copy with {Count} discovered mods unapplied", serverId, discovered.Count);
            return result;
        }

        var discoveredIds = discovered.Select(m => m.WorkshopId).ToHashSet(StringComparer.Ordinal);
        var kept = server.Mods.Where(m => discoveredIds.Contains(m.WorkshopId)).ToList();
        var keptIds = kept.Select(m => m.WorkshopId).ToHashSet(StringComparer.Ordinal);
        var added = discovered
            .Where(m => !keptIds.Contains(m.WorkshopId))
            .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        server.Mods = kept.Concat(added).ToList();

        Log.Information("AdminConfigBuilder built config for {ServerId}: {Discovered} discovered, {Kept} kept, {Added} added", serverId, discovered.Count, kept.Count, added.Count);
        return result;
    }
}
