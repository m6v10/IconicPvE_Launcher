using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IconicLauncher.Core.Models;
using IconicLauncher.Core.Services;
using IconicLauncher.Core.Utils;
using Serilog;

namespace IconicLauncher.ViewModels;

public sealed partial class AdminViewModel : ObservableObject
{
    private const string FallbackWorkshopFolder = @"C:\Program Files (x86)\Steam\steamapps\common\DayZ\!Workshop";

    private readonly MainViewModel _owner;
    private readonly IAdminConfigBuilder _builder;
    private readonly IFtpPublishService _ftp;
    private LauncherConfig? _template;

    public ObservableCollection<WorkshopScanRow> ScanRows { get; } = new();
    public ObservableCollection<ServerEntry> ServerTargets { get; } = new();

    public string FtpPassword { get; set; } = "";

    [ObservableProperty]
    private string workshopFolder = FallbackWorkshopFolder;

    [ObservableProperty]
    private ServerEntry? selectedServer;

    [ObservableProperty]
    private string generatedJson = "";

    [ObservableProperty]
    private string ftpHost = "";

    [ObservableProperty]
    private int ftpPort = 21;

    [ObservableProperty]
    private string ftpUser = "";

    [ObservableProperty]
    private string remotePath = "/";

    [ObservableProperty]
    private double uploadProgress;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UploadCommand))]
    private bool isUploading;

    [ObservableProperty]
    private string? adminStatus;

    public AdminViewModel(MainViewModel owner, IAdminConfigBuilder builder, IFtpPublishService ftp)
    {
        _owner = owner;
        _builder = builder;
        _ftp = ftp;
        var admin = owner.SettingsService.Admin;
        FtpHost = admin.FtpHost;
        FtpPort = admin.FtpPort;
        FtpUser = admin.FtpUser;
        RemotePath = admin.RemotePath;
        if (!string.IsNullOrWhiteSpace(admin.WorkshopFolderOverride))
        {
            WorkshopFolder = admin.WorkshopFolderOverride;
        }
        if (!string.IsNullOrEmpty(admin.FtpPasswordProtected))
        {
            try
            {
                FtpPassword = DpapiProtector.Unprotect(admin.FtpPasswordProtected) ?? "";
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "FTP password decryption failed");
            }
        }
    }

    public void SetTemplate(LauncherConfig config)
    {
        _template = config;
        ServerTargets.Clear();
        foreach (var server in config.Servers)
        {
            ServerTargets.Add(server);
        }
        SelectedServer ??= ServerTargets.FirstOrDefault();
    }

    public void OnEnvironmentChanged(SteamEnvironment? env)
    {
        if (!string.IsNullOrWhiteSpace(_owner.SettingsService.Admin.WorkshopFolderOverride)) return;
        if (env?.DayZDir is { Length: > 0 } dayZDir && WorkshopFolder == FallbackWorkshopFolder)
        {
            WorkshopFolder = Path.Combine(dayZDir, "!Workshop");
        }
    }

    [RelayCommand]
    private void Scan()
    {
        ScanRows.Clear();
        var rows = new List<WorkshopScanRow>();
        try
        {
            if (!Directory.Exists(WorkshopFolder))
            {
                AdminStatus = "Workshop folder not found";
                return;
            }
            var seen = new HashSet<string>();
            foreach (var dir in Directory.EnumerateDirectories(WorkshopFolder))
            {
                var folderName = Path.GetFileName(dir);
                if (!folderName.StartsWith('@')) continue;
                var metaPath = Path.Combine(dir, "meta.cpp");
                if (!File.Exists(metaPath)) continue;
                MetaCppInfo? info = null;
                try
                {
                    info = MetaCppParser.Parse(File.ReadAllText(metaPath));
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "meta.cpp parse failed in {Folder}", folderName);
                }
                if (info is null) continue;
                var duplicate = !seen.Add(info.PublishedId);
                rows.Add(new WorkshopScanRow(folderName, info.Name, info.PublishedId, duplicate));
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Workshop scan failed");
            AdminStatus = "Scan failed: " + ex.Message;
            return;
        }
        var templateOrder = SelectedServer?.Mods.Select(m => m.WorkshopId).ToList() ?? new List<string>();
        int OrderOf(WorkshopScanRow row)
        {
            var index = templateOrder.IndexOf(row.PublishedId);
            return index < 0 ? int.MaxValue : index;
        }
        foreach (var row in rows.OrderBy(OrderOf).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase))
        {
            ScanRows.Add(row);
        }
        AdminStatus = $"{ScanRows.Count} mod folders scanned";
    }

    [RelayCommand]
    private void GenerateJson()
    {
        if (_template is null || SelectedServer is null)
        {
            AdminStatus = "No configuration template loaded";
            return;
        }
        try
        {
            var config = _builder.Build(WorkshopFolder, _template, SelectedServer.Id);
            GeneratedJson = JsonSerializer.Serialize(config, JsonDefaults.Options);
            AdminStatus = "Configuration generated";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Config generation failed");
            AdminStatus = "Generation failed: " + ex.Message;
        }
    }

    public bool SaveGeneratedJson(string path)
    {
        if (string.IsNullOrEmpty(GeneratedJson))
        {
            AdminStatus = "Generate the configuration first";
            return false;
        }
        try
        {
            File.WriteAllText(path, GeneratedJson);
            AdminStatus = "Saved to " + path;
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Saving the config file failed");
            AdminStatus = "Save failed: " + ex.Message;
            return false;
        }
    }

    private bool CanUpload => !IsUploading;

    [RelayCommand(CanExecute = nameof(CanUpload))]
    private async Task UploadAsync()
    {
        if (string.IsNullOrEmpty(GeneratedJson))
        {
            AdminStatus = "Generate the configuration first";
            return;
        }
        IsUploading = true;
        UploadProgress = 0;
        try
        {
            PersistAdminSettings();
            var localPath = Path.Combine(_owner.SettingsService.AppDataDir, "generated-config.json");
            await File.WriteAllTextAsync(localPath, GeneratedJson);
            var progress = new Progress<double>(v => UploadProgress = Math.Clamp(v <= 1.0 ? v * 100.0 : v, 0.0, 100.0));
            await _ftp.UploadAsync(_owner.SettingsService.Admin, localPath, "launcher-config.json", progress);
            AdminStatus = "Upload complete";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "FTP upload failed");
            AdminStatus = "Upload failed: " + ex.Message;
        }
        finally
        {
            IsUploading = false;
        }
    }

    private void PersistAdminSettings()
    {
        var admin = _owner.SettingsService.Admin;
        admin.FtpHost = FtpHost.Trim();
        admin.FtpPort = FtpPort;
        admin.FtpUser = FtpUser.Trim();
        admin.RemotePath = string.IsNullOrWhiteSpace(RemotePath) ? "/" : RemotePath.Trim();
        admin.WorkshopFolderOverride = string.IsNullOrWhiteSpace(WorkshopFolder) ? null : WorkshopFolder.Trim();
        try
        {
            admin.FtpPasswordProtected = string.IsNullOrEmpty(FtpPassword) ? "" : DpapiProtector.Protect(FtpPassword);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "FTP password encryption failed");
        }
        _owner.SettingsService.SaveAdmin();
    }
}

public sealed record WorkshopScanRow(string Folder, string Name, string PublishedId, bool Duplicate);
