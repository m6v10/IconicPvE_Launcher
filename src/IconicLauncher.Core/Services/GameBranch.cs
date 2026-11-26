using IconicLauncher.Core.Models;
using IconicLauncher.Core.Utils;

namespace IconicLauncher.Core.Services;

public static class GameBranch
{
    public static bool UseExperimental(ServerEntry server, LauncherSettings settings, SteamEnvironment? env, string? serverVersion)
    {
        if (settings.ServerExperimental != null && settings.ServerExperimental.TryGetValue(server.Id, out var forced))
            return forced;
        if (server.Experimental)
            return true;
        return MatchesExperimentalOnly(env, serverVersion);
    }

    public static bool MatchesExperimentalOnly(SteamEnvironment? env, string? serverVersion)
    {
        var version = GameVersion.MajorMinor(serverVersion);
        if (version == null || env?.DayZExpVersion == null)
            return false;
        return version == env.DayZExpVersion && version != env.DayZVersion;
    }
}
