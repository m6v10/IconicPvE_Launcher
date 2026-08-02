using Gameloop.Vdf;
using Gameloop.Vdf.Linq;
using IconicLauncher.Core.Models;
using IconicLauncher.Core.Utils;
using Microsoft.Win32;
using Serilog;

namespace IconicLauncher.Core.Services;

public sealed class SteamLibraryLocator : ISteamLocator
{
    public SteamEnvironment Locate(LauncherSettings settings)
    {
        var steamRoot = ResolveSteamRoot(settings);
        string? libraryRoot = null;
        string? dayzDir = null;
        string? workshopContentDir = null;
        string? acfPath = null;

        if (steamRoot != null)
        {
            var vdfPath = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdfPath))
            {
                try
                {
                    libraryRoot = FindLibraryPathForApp(File.ReadAllText(vdfPath), LauncherConstants.DayZAppId);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to read libraryfolders.vdf at {Path}", vdfPath);
                }
            }
            else
            {
                Log.Warning("libraryfolders.vdf not found at {Path}", vdfPath);
            }
        }
        else
        {
            Log.Warning("Steam installation not found via settings or registry");
        }

        if (libraryRoot != null)
        {
            var steamApps = Path.Combine(libraryRoot, "steamapps");
            dayzDir = Path.Combine(steamApps, "common", "DayZ");
            workshopContentDir = Path.Combine(steamApps, "workshop", "content", LauncherConstants.DayZAppId);
            acfPath = Path.Combine(steamApps, "workshop", "appworkshop_" + LauncherConstants.DayZAppId + ".acf");
            if (!Directory.Exists(dayzDir))
            {
                Log.Warning("DayZ directory missing on disk at {Path}", dayzDir);
                dayzDir = null;
            }
        }

        if (!string.IsNullOrWhiteSpace(settings.DayZPathOverride))
        {
            if (Directory.Exists(settings.DayZPathOverride))
                dayzDir = Path.GetFullPath(settings.DayZPathOverride);
            else
                Log.Warning("DayZ path override does not exist: {Path}", settings.DayZPathOverride);
        }

        Log.Information("Steam environment: root={SteamRoot} library={LibraryRoot} dayz={DayZDir}", steamRoot, libraryRoot, dayzDir);
        return new SteamEnvironment
        {
            SteamRoot = steamRoot,
            LibraryRoot = libraryRoot,
            DayZDir = dayzDir,
            WorkshopContentDir = workshopContentDir,
            AcfPath = acfPath
        };
    }

    public static string? FindLibraryPathForApp(string vdfContent, string appId)
    {
        try
        {
            var root = VdfConvert.Deserialize(vdfContent);
            if (root.Value is not VObject folders)
                return null;
            foreach (var prop in folders.Properties())
            {
                if (prop.Value is not VObject lib)
                    continue;
                if (!lib.TryGetValue("apps", out var appsToken) || appsToken is not VObject apps)
                    continue;
                if (!apps.ContainsKey(appId))
                    continue;
                if (!lib.TryGetValue("path", out var pathToken))
                    continue;
                var path = pathToken.ToString();
                if (!string.IsNullOrWhiteSpace(path))
                    return path;
            }
            return null;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to parse libraryfolders.vdf content");
            return null;
        }
    }

    private static string? ResolveSteamRoot(LauncherSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.SteamPathOverride))
        {
            if (Directory.Exists(settings.SteamPathOverride))
                return Path.GetFullPath(settings.SteamPathOverride);
            Log.Warning("Steam path override does not exist: {Path}", settings.SteamPathOverride);
        }
        var hkcu = ReadRegistryString(Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath");
        if (hkcu != null && Directory.Exists(hkcu))
            return Path.GetFullPath(hkcu);
        var hklm = ReadRegistryString(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");
        if (hklm != null && Directory.Exists(hklm))
            return Path.GetFullPath(hklm);
        return null;
    }

    private static string? ReadRegistryString(RegistryKey hive, string subKey, string valueName)
    {
        try
        {
            using var key = hive.OpenSubKey(subKey);
            var value = key?.GetValue(valueName) as string;
            return string.IsNullOrWhiteSpace(value) ? null : value.Replace('/', '\\');
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Registry read failed for {SubKey}\\{Value}", subKey, valueName);
            return null;
        }
    }
}
