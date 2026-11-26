using Gameloop.Vdf;
using Gameloop.Vdf.Linq;
using Microsoft.Win32;
using Serilog;

namespace IconicLauncher.Core.Utils;

public sealed record SteamAccount(string SteamId64, bool SignedIn, string? PersonaName);

public static class SteamIdentity
{
    private const ulong SteamId64Base = 76561197960265728UL;

    public static SteamAccount? Current(string? steamRoot)
    {
        var active = ActiveAccountId();
        if (active > 0)
        {
            var id = (SteamId64Base + active).ToString();
            return new SteamAccount(id, true, PersonaName(steamRoot, id));
        }
        var recent = MostRecentLogin(steamRoot);
        return recent is null ? null : recent with { SignedIn = false };
    }

    private static uint ActiveAccountId()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\ActiveProcess");
            return key?.GetValue("ActiveUser") switch
            {
                int i => unchecked((uint)i),
                long l => unchecked((uint)l),
                _ => 0
            };
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Reading the active Steam user failed");
            return 0;
        }
    }

    private static VProperty? LoginUsers(string? steamRoot)
    {
        if (string.IsNullOrWhiteSpace(steamRoot)) return null;
        var path = Path.Combine(steamRoot, "config", "loginusers.vdf");
        if (!File.Exists(path)) return null;
        try
        {
            return VdfConvert.Deserialize(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Reading loginusers.vdf failed");
            return null;
        }
    }

    private static string? PersonaName(string? steamRoot, string steamId)
    {
        var users = LoginUsers(steamRoot);
        if (users?.Value is not VObject obj) return null;
        return obj[steamId] is VObject user ? user["PersonaName"]?.ToString() : null;
    }

    private static SteamAccount? MostRecentLogin(string? steamRoot)
    {
        var users = LoginUsers(steamRoot);
        if (users?.Value is not VObject obj) return null;
        foreach (var prop in obj.Properties())
        {
            if (prop.Value is VObject user && user["MostRecent"]?.ToString() == "1" && prop.Key.Length == 17)
            {
                return new SteamAccount(prop.Key, false, user["PersonaName"]?.ToString());
            }
        }
        return null;
    }
}
