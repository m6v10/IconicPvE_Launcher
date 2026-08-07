using IconicLauncher.Core.Models;

namespace IconicLauncher.Core.Services;

public sealed class ServerListEntry
{
    public required ServerEntry Server { get; init; }
    public bool IsCustom { get; init; }
    public bool IsOptional { get; init; }
    public bool IsVisible { get; init; }
}

/// <summary>
/// Merges the shipped config servers with the player's own additions and applies the
/// per-player visibility overrides and saved ordering. Pure - no I/O, no UI.
/// </summary>
public static class ServerListBuilder
{
    public static IReadOnlyList<ServerListEntry> BuildAll(LauncherConfig config, LauncherSettings settings)
    {
        var visibility = settings.ServerVisibility ?? new Dictionary<string, bool>();
        var order = settings.ServerOrder ?? new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<ServerListEntry>();
        foreach (var server in config.Servers ?? new List<ServerEntry>())
        {
            if (string.IsNullOrWhiteSpace(server.Id) || !seen.Add(server.Id))
            {
                continue;
            }
            entries.Add(new ServerListEntry
            {
                Server = server,
                IsCustom = false,
                IsOptional = server.Optional,
                // An optional server ships hidden: the player opts into it instead of
                // being handed its mod list (and its update prompts) by default.
                IsVisible = ResolveVisibility(visibility, server.Id, !server.Optional)
            });
        }
        foreach (var server in settings.CustomServers ?? new List<ServerEntry>())
        {
            if (string.IsNullOrWhiteSpace(server.Id) || !seen.Add(server.Id))
            {
                continue;
            }
            entries.Add(new ServerListEntry
            {
                Server = server,
                IsCustom = true,
                IsOptional = false,
                IsVisible = ResolveVisibility(visibility, server.Id, true)
            });
        }
        // OrderBy is stable, so ids the player never reordered keep their source order.
        return entries.OrderBy(e => OrderIndex(order, e.Server.Id)).ToList();
    }

    public static List<ServerEntry> BuildVisible(LauncherConfig config, LauncherSettings settings)
    {
        return BuildAll(config, settings).Where(e => e.IsVisible).Select(e => e.Server).ToList();
    }

    public static bool DefaultVisible(ServerListEntry entry)
    {
        return entry.IsCustom || !entry.IsOptional;
    }

    private static bool ResolveVisibility(Dictionary<string, bool> visibility, string id, bool fallback)
    {
        foreach (var pair in visibility)
        {
            if (string.Equals(pair.Key, id, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }
        return fallback;
    }

    private static int OrderIndex(List<string> order, string id)
    {
        for (var i = 0; i < order.Count; i++)
        {
            if (string.Equals(order[i], id, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return int.MaxValue;
    }
}
