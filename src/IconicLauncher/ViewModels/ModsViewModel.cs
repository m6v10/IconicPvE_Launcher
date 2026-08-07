using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IconicLauncher.Core.Models;
using Serilog;

namespace IconicLauncher.ViewModels;

public sealed partial class ModsViewModel : ObservableObject
{
    private readonly MainViewModel _owner;
    private readonly List<ServerCardViewModel> _cards = new();

    public ObservableCollection<ModRowViewModel> Rows { get; } = new();
    public ObservableCollection<ModRowViewModel> NeedsUpdateRows { get; } = new();
    public ObservableCollection<ModRowViewModel> UpToDateRows { get; } = new();

    public ModsViewModel(MainViewModel owner)
    {
        _owner = owner;
    }

    internal string? ContentDir => _owner.Env?.WorkshopContentDir;

    internal bool HasBusyRows => Rows.Any(r => r.IsBusy);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(RecheckCommand))]
    private bool isBusy;

    [ObservableProperty]
    private bool isUpdatingAll;

    [ObservableProperty]
    private double progressPercent;

    [ObservableProperty]
    private string? statusText;

    [ObservableProperty]
    private string headerText = "All Mods - Verified 0/0";

    [ObservableProperty]
    private string needsUpdateHeader = "NEEDS UPDATE (0)";

    [ObservableProperty]
    private string upToDateHeader = "UP TO DATE (0)";

    [ObservableProperty]
    private bool hasNeedsUpdate;

    // Nav-badge counterparts of HasNeedsUpdate. Those track the on-screen bucket, which
    // is search-filtered; the sidebar badge must reflect every mod needing attention
    // regardless of what is typed in the search box.
    [ObservableProperty]
    private int needsUpdateCount;

    [ObservableProperty]
    private bool hasAnyNeedsUpdate;

    [ObservableProperty]
    private string searchText = "";

    partial void OnSearchTextChanged(string value)
    {
        Rebucket();
    }

    partial void OnIsBusyChanged(bool value)
    {
        foreach (var row in Rows)
        {
            row.RaiseCommandStates();
        }
    }

    public void SetServers(IEnumerable<ServerCardViewModel> cards)
    {
        // Called again whenever the player adds, hides or deletes a server. Without the
        // unsubscribe, every re-registration would stack another RefreshRows handler on
        // the cards that were already in the list.
        foreach (var existing in _cards)
        {
            existing.VerificationCompleted -= RefreshRows;
        }
        _cards.Clear();
        foreach (var card in cards)
        {
            _cards.Add(card);
            card.VerificationCompleted += RefreshRows;
        }
        RefreshRows();
    }

    private void RefreshRows()
    {
        var map = new Dictionary<string, ModVerificationResult>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        foreach (var card in _cards)
        {
            foreach (var result in card.Verification)
            {
                var id = result.Mod.WorkshopId;
                if (id.Length == 0) continue;
                if (!map.TryGetValue(id, out var existing))
                {
                    map[id] = result;
                    order.Add(id);
                }
                else if (existing.State == ModState.Unknown && result.State != ModState.Unknown)
                {
                    map[id] = result;
                }
            }
        }
        foreach (var card in _cards)
        {
            foreach (var mod in card.Server.Mods)
            {
                var id = mod.WorkshopId;
                if (id.Length == 0 || map.ContainsKey(id)) continue;
                map[id] = new ModVerificationResult { Mod = mod, State = ModState.Unknown };
                order.Add(id);
            }
        }
        for (var i = Rows.Count - 1; i >= 0; i--)
        {
            if (!map.ContainsKey(Rows[i].WorkshopId)) Rows.RemoveAt(i);
        }
        for (var i = 0; i < order.Count; i++)
        {
            var id = order[i];
            var existingIndex = -1;
            for (var j = i; j < Rows.Count; j++)
            {
                if (string.Equals(Rows[j].WorkshopId, id, StringComparison.OrdinalIgnoreCase))
                {
                    existingIndex = j;
                    break;
                }
            }
            if (existingIndex < 0)
            {
                Rows.Insert(i, new ModRowViewModel(this, map[id]));
            }
            else
            {
                if (existingIndex != i) Rows.Move(existingIndex, i);
                var row = Rows[i];
                if (!row.IsBusy) row.Apply(map[id]);
            }
        }
        while (Rows.Count > order.Count)
        {
            Rows.RemoveAt(Rows.Count - 1);
        }
        UpdateHeader();
    }

    internal void UpdateHeader()
    {
        var ready = Rows.Count(r => r.State == ModState.Ready);
        HeaderText = $"All Mods - Verified {ready}/{Rows.Count}";
        Rebucket();
    }

    internal void OnRowStateChanged()
    {
        Rebucket();
    }

    private static bool NeedsUpdate(ModRowViewModel row)
    {
        return row.State is ModState.NotInstalled or ModState.Outdated or ModState.Damaged or ModState.Failed;
    }

    private void Rebucket()
    {
        SyncBucket(NeedsUpdateRows, true);
        SyncBucket(UpToDateRows, false);
        NeedsUpdateHeader = $"NEEDS UPDATE ({NeedsUpdateRows.Count})";
        UpToDateHeader = $"UP TO DATE ({UpToDateRows.Count})";
        HasNeedsUpdate = NeedsUpdateRows.Count > 0;
        NeedsUpdateCount = Rows.Count(NeedsUpdate);
        HasAnyNeedsUpdate = NeedsUpdateCount > 0;
    }

    private static bool MatchesSearch(ModRowViewModel row, string[] tokens)
    {
        if (tokens.Length == 0) return true;
        var hay = row.Name + " " + row.WorkshopId;
        return tokens.All(t => hay.Contains(t, StringComparison.OrdinalIgnoreCase));
    }

    internal bool IsFavoriteId(string id)
    {
        return _owner.SettingsService.Settings.FavoriteModIds.Contains(id, StringComparer.OrdinalIgnoreCase);
    }

    internal void ToggleFavorite(ModRowViewModel row)
    {
        var favorites = _owner.SettingsService.Settings.FavoriteModIds;
        var existing = favorites.FirstOrDefault(f => string.Equals(f, row.WorkshopId, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            favorites.Add(row.WorkshopId);
            row.IsFavorite = true;
        }
        else
        {
            favorites.Remove(existing);
            row.IsFavorite = false;
        }
        try
        {
            _owner.SettingsService.Save();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Saving favorites failed");
        }
        Rebucket();
    }

    private void SyncBucket(ObservableCollection<ModRowViewModel> bucket, bool needsUpdate)
    {
        var tokens = SearchText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var desired = Rows
            .Where(r => NeedsUpdate(r) == needsUpdate && MatchesSearch(r, tokens))
            .OrderByDescending(r => r.IsFavorite)
            .ToList();
        for (var i = bucket.Count - 1; i >= 0; i--)
        {
            if (!desired.Contains(bucket[i])) bucket.RemoveAt(i);
        }
        for (var i = 0; i < desired.Count; i++)
        {
            var index = bucket.IndexOf(desired[i]);
            if (index < 0)
            {
                bucket.Insert(i, desired[i]);
            }
            else if (index != i)
            {
                bucket.Move(index, i);
            }
        }
    }

    private bool CanOperate => !IsBusy;

    [ObservableProperty]
    private bool dayZBlocked;

    private bool BlockedByRunningGame()
    {
        if (!Core.Utils.DayZProcess.IsRunning())
        {
            DayZBlocked = false;
            return false;
        }
        DayZBlocked = true;
        StatusText = "DayZ is running - close the game first, mods can only update while the game is not running";
        return true;
    }

    [RelayCommand]
    private async Task ForceCloseDayZAsync()
    {
        StatusText = "Closing DayZ...";
        var ok = await Task.Run(Core.Utils.DayZProcess.ForceClose);
        DayZBlocked = Core.Utils.DayZProcess.IsRunning();
        StatusText = ok && !DayZBlocked ? "DayZ closed - you can update mods now" : "Could not close DayZ - please close it manually";
    }

    [RelayCommand(CanExecute = nameof(CanOperate))]
    private async Task UpdateAllAsync()
    {
        if (BlockedByRunningGame()) return;
        var env = RequireEnv();
        if (env is null) return;
        var pending = Rows
            .Where(r => r.State is ModState.NotInstalled or ModState.Outdated or ModState.Damaged or ModState.Failed)
            .Select(r => r.Result)
            .ToList();
        if (pending.Count == 0)
        {
            StatusText = "All mods are up to date";
            return;
        }
        IsBusy = true;
        IsUpdatingAll = true;
        ProgressPercent = 0;
        try
        {
            var progress = new Progress<WorkshopProgress>(OnBulkProgress);
            var ok = await _owner.Updater.EnsureModsReadyAsync(pending, env, progress);
            StatusText = ok ? null : "Some mods could not be updated";
            await RecheckAllAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Bulk mod update failed");
            StatusText = "Mod update failed: " + ex.Message;
        }
        finally
        {
            IsUpdatingAll = false;
            IsBusy = false;
            RefreshRows();
        }
    }

    [RelayCommand(CanExecute = nameof(CanOperate))]
    private async Task RecheckAsync()
    {
        IsBusy = true;
        try
        {
            await RecheckAllAsync();
        }
        finally
        {
            IsBusy = false;
            RefreshRows();
            _owner.ResetSyncCountdown();
        }
    }

    internal async Task AutoSyncAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            await RecheckAllAsync();
        }
        finally
        {
            IsBusy = false;
            RefreshRows();
        }
    }

    private async Task RecheckAllAsync()
    {
        foreach (var card in _cards.ToList())
        {
            try
            {
                await card.VerifyAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Re-check failed for {Server}", card.Name);
            }
        }
    }

    private void OnBulkProgress(WorkshopProgress p)
    {
        if (p.BytesTotal > 0)
        {
            ProgressPercent = Math.Min(100.0, p.BytesDownloaded * 100.0 / p.BytesTotal);
        }
        else if (p.TotalMods > 0)
        {
            ProgressPercent = Math.Min(100.0, p.CompletedMods * 100.0 / p.TotalMods);
        }
        var text = p.Phase is { Length: > 0 } phase && p.CurrentModName is { Length: > 0 } name
            ? $"{phase}: {name}"
            : p.Phase ?? p.CurrentModName;
        if (text is { Length: > 0 })
        {
            StatusText = text;
        }
    }

    internal async Task RunUpdateAsync(ModRowViewModel row)
    {
        if (BlockedByRunningGame()) return;
        var env = RequireEnv();
        if (env is null) return;
        row.IsBusy = true;
        row.ProgressPercent = 0;
        try
        {
            var progress = new Progress<WorkshopProgress>(row.ReportProgress);
            var ok = await _owner.ModOperations.UpdateModAsync(row.Result, env, progress);
            if (!ok) Log.Warning("Mod update failed for {Mod}", row.Name);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Mod update failed for {Mod}", row.Name);
        }
        finally
        {
            row.IsBusy = false;
        }
        await ReverifyRowAsync(row, env);
    }

    internal async Task RunRebuildAsync(ModRowViewModel row)
    {
        if (BlockedByRunningGame()) return;
        var env = RequireEnv();
        if (env is null) return;
        row.IsBusy = true;
        row.ProgressPercent = 0;
        try
        {
            var progress = new Progress<WorkshopProgress>(row.ReportProgress);
            var ok = await _owner.ModOperations.ForceRebuildAsync(row.Result, env, progress);
            if (!ok) Log.Warning("Mod rebuild failed for {Mod}", row.Name);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Mod rebuild failed for {Mod}", row.Name);
        }
        finally
        {
            row.IsBusy = false;
        }
        await ReverifyRowAsync(row, env);
    }

    internal async Task RunDeleteAsync(ModRowViewModel row)
    {
        if (BlockedByRunningGame()) return;
        var env = RequireEnv();
        if (env is null) return;
        row.IsBusy = true;
        row.ReportPhase("Deleting");
        try
        {
            var ok = await _owner.ModOperations.DeleteModAsync(row.Result, env);
            if (!ok) Log.Warning("Mod delete failed for {Mod}", row.Name);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Mod delete failed for {Mod}", row.Name);
        }
        finally
        {
            row.IsBusy = false;
        }
        await ReverifyRowAsync(row, env);
    }

    private async Task ReverifyRowAsync(ModRowViewModel row, SteamEnvironment env)
    {
        try
        {
            var single = new ServerEntry
            {
                Id = "modrow",
                Name = row.Name,
                Mods = new List<ModEntry> { row.Result.Mod }
            };
            var results = await _owner.Verifier.VerifyAsync(single, env);
            if (results.Count > 0) row.Apply(results[0]);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Re-verification failed for {Mod}", row.Name);
        }
        UpdateHeader();
    }

    private SteamEnvironment? RequireEnv()
    {
        var env = _owner.Env;
        if (env is { DayZFound: true }) return env;
        StatusText = "DayZ installation not found - set the paths in Settings";
        return null;
    }
}

public sealed partial class ModRowViewModel : ObservableObject
{
    private readonly ModsViewModel _owner;

    public ModRowViewModel(ModsViewModel owner, ModVerificationResult result)
    {
        _owner = owner;
        Result = result;
        isFavorite = owner.IsFavoriteId(result.Mod.WorkshopId);
        Apply(result);
    }

    [ObservableProperty]
    private bool isFavorite;

    [RelayCommand]
    private void ToggleFavorite()
    {
        _owner.ToggleFavorite(this);
    }

    public ModVerificationResult Result { get; private set; }

    public string WorkshopId => Result.Mod.WorkshopId;

    [ObservableProperty]
    private string name = "";

    [ObservableProperty]
    private ModState state;

    partial void OnStateChanged(ModState value)
    {
        _owner.OnRowStateChanged();
    }

    [ObservableProperty]
    private string clientDateText = "";

    [ObservableProperty]
    private string workshopDateText = "";

    [ObservableProperty]
    private string? indicatorText;

    [ObservableProperty]
    private bool needsAttention;

    [ObservableProperty]
    private bool isReady;

    [ObservableProperty]
    private bool isDamaged;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateModCommand))]
    [NotifyCanExecuteChangedFor(nameof(RebuildCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    private bool isBusy;

    public void Apply(ModVerificationResult result)
    {
        Result = result;
        Name = string.IsNullOrWhiteSpace(result.Mod.Name) ? result.Mod.WorkshopId : result.Mod.Name;
        State = result.State;
        ClientDateText = "Client " + FormatUnix(result.LocalTimeUpdated);
        WorkshopDateText = "Workshop " + FormatUnix(result.RemoteTimeUpdated);
        IsReady = result.State == ModState.Ready;
        IsDamaged = result.State == ModState.Damaged;
        NeedsAttention = result.State is ModState.NotInstalled or ModState.Outdated or ModState.Damaged or ModState.Failed;
        IndicatorText = result.State switch
        {
            ModState.NotInstalled => "not installed",
            ModState.Damaged => result.IntegrityIssue is { Length: > 0 } issue ? issue : "damaged files",
            ModState.Failed => result.FailReason is { Length: > 0 } reason ? reason : "failed",
            ModState.Subscribing => "subscribing...",
            ModState.Downloading => "downloading...",
            ModState.Verifying => "verifying...",
            _ => null
        };
        OnPropertyChanged(nameof(WorkshopId));
    }

    internal void ReportPhase(string? phase)
    {
        if (phase is { Length: > 0 }) IndicatorText = phase;
    }

    [ObservableProperty]
    private double progressPercent;

    internal void ReportProgress(WorkshopProgress p)
    {
        if (p.BytesTotal > 0)
        {
            ProgressPercent = Math.Min(100.0, p.BytesDownloaded * 100.0 / p.BytesTotal);
        }
        else if (p.TotalMods > 0 && p.CompletedMods > 0)
        {
            ProgressPercent = Math.Min(100.0, p.CompletedMods * 100.0 / p.TotalMods);
        }
        ReportPhase(p.Phase);
    }

    [RelayCommand]
    private void OpenFolder()
    {
        var path = Result.InstallPath;
        if (string.IsNullOrEmpty(path))
        {
            var contentDir = _owner.ContentDir;
            if (contentDir != null)
                path = Path.Combine(contentDir, WorkshopId);
        }
        if (path is null || !Directory.Exists(path))
        {
            IndicatorText = "folder not found";
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to open mod folder {Path}", path);
        }
    }

    internal void RaiseCommandStates()
    {
        UpdateModCommand.NotifyCanExecuteChanged();
        RebuildCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
    }

    private bool CanOperate => !IsBusy && !_owner.IsBusy;

    [RelayCommand(CanExecute = nameof(CanOperate))]
    private Task UpdateModAsync()
    {
        return _owner.RunUpdateAsync(this);
    }

    [RelayCommand(CanExecute = nameof(CanOperate))]
    private async Task RebuildAsync()
    {
        if (Confirm($"Delete the local files of \"{Name}\" and reinstall it fresh from the workshop?") != MessageBoxResult.Yes) return;
        await _owner.RunRebuildAsync(this);
    }

    [RelayCommand(CanExecute = nameof(CanOperate))]
    private async Task DeleteAsync()
    {
        if (Confirm($"Delete the files of \"{Name}\" and unsubscribe from the workshop item?") != MessageBoxResult.Yes) return;
        await _owner.RunDeleteAsync(this);
    }

    private static MessageBoxResult Confirm(string message)
    {
        var owner = Application.Current?.MainWindow;
        return owner is null
            ? MessageBox.Show(message, "Iconic PvE Launcher", MessageBoxButton.YesNo, MessageBoxImage.Warning)
            : MessageBox.Show(owner, message, "Iconic PvE Launcher", MessageBoxButton.YesNo, MessageBoxImage.Warning);
    }

    private static string FormatUnix(long unixSeconds)
    {
        if (unixSeconds <= 0) return "-";
        return DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
    }
}
