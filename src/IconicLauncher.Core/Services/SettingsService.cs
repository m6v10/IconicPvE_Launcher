using System.Text.Json;
using IconicLauncher.Core.Models;
using IconicLauncher.Core.Utils;
using Serilog;

namespace IconicLauncher.Core.Services;

public sealed class SettingsService : ISettingsService
{
    private const string SettingsFileName = "settings.json";
    private const string AdminFileName = "admin-settings.json";

    public LauncherSettings Settings { get; }
    public AdminSettings Admin { get; }
    public string AppDataDir { get; }

    public SettingsService(string? appDataDirOverride = null)
    {
        AppDataDir = appDataDirOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            LauncherConstants.AppDataFolderName);
        Directory.CreateDirectory(AppDataDir);
        Settings = LoadFile<LauncherSettings>(SettingsFileName) ?? new LauncherSettings();
        Admin = LoadFile<AdminSettings>(AdminFileName) ?? new AdminSettings();
    }

    public void Save()
    {
        WriteFile(SettingsFileName, Settings);
    }

    public void SaveAdmin()
    {
        WriteFile(AdminFileName, Admin);
    }

    private void WriteFile<T>(string fileName, T value)
    {
        var path = Path.Combine(AppDataDir, fileName);
        var json = JsonSerializer.Serialize(value, JsonDefaults.Options);
        AtomicFile.WriteAllTextAtomic(path, json);
    }

    private T? LoadFile<T>(string fileName) where T : class
    {
        var path = Path.Combine(AppDataDir, fileName);
        if (!File.Exists(path))
        {
            return null;
        }
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json, JsonDefaults.Options);
        }
        catch (Exception ex)
        {
            Log.Warning("Corrupt settings file {File}, using defaults: {Message}", fileName, ex.Message);
            QuarantineCorrupt(path);
            return null;
        }
    }

    private static void QuarantineCorrupt(string path)
    {
        try
        {
            var corruptPath = path + ".corrupt";
            if (File.Exists(corruptPath))
            {
                File.Delete(corruptPath);
            }
            File.Move(path, corruptPath);
        }
        catch (Exception ex)
        {
            Log.Warning("Failed to quarantine corrupt file {Path}: {Message}", path, ex.Message);
        }
    }
}
