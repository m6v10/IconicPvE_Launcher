using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IconicLauncher.Core.Models;
using Serilog;

namespace IconicLauncher.ViewModels;

public enum CardState
{
    Checking,
    Ready,
    UpdateRequired,
    Downloading,
    Launching,
    Running,
    Offline,
    NoModList
}

public sealed partial class ServerCardViewModel : ObservableObject
{
    private readonly MainViewModel _owner;
    private IReadOnlyList<ModVerificationResult> _verification = Array.Empty<ModVerificationResult>();
    private bool _hasPolled;
    private bool _verifying;

    public ServerEntry Server { get; }
    public string Name => Server.Name;
    public int ModCount => Server.Mods.Count;
    public bool IsCustom { get; }
    public IReadOnlyList<ModVerificationResult> Verification => _verification;

    public event Action? VerificationCompleted;

    public ServerCardViewModel(ServerEntry server, MainViewModel owner, bool isCustom = false)
    {
        Server = server;
        _owner = owner;
        IsCustom = isCustom;
        UpdateRestartCountdown();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActionLabel))]
    [NotifyPropertyChangedFor(nameof(CanJoin))]
    [NotifyPropertyChangedFor(nameof(IsOffline))]
    [NotifyPropertyChangedFor(nameof(CanJoinAnyway))]
    [NotifyPropertyChangedFor(nameof(IsDownloading))]
    [NotifyCanExecuteChangedFor(nameof(JoinCommand))]
    [NotifyCanExecuteChangedFor(nameof(JoinAnywayCommand))]
    private CardState state = CardState.Checking;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayersText))]
    [NotifyPropertyChangedFor(nameof(PingText))]
    private bool online;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayersText))]
    private int players;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayersText))]
    private int maxPlayers;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PingText))]
    private int pingMs;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActionLabel))]
    private int outdatedCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActionLabel))]
    private double downloadPercent;

    [ObservableProperty]
    private string? statusText;

    [ObservableProperty]
    private string? restartText;

    [ObservableProperty]
    private bool isRestartSoon;

    [ObservableProperty]
    private bool passwordProtected;

    [ObservableProperty]
    private bool passwordEntryOpen;

    [ObservableProperty]
    private string serverPassword = "";

    [RelayCommand]
    private void TogglePasswordEntry()
    {
        PasswordEntryOpen = !PasswordEntryOpen;
    }

    [RelayCommand]
    private void MoveUp()
    {
        _owner.MoveServerCard(this, -1);
    }

    [RelayCommand]
    private void MoveDown()
    {
        _owner.MoveServerCard(this, 1);
    }

    [RelayCommand]
    private void ConfirmPassword()
    {
        PasswordEntryOpen = false;
        StatusText = ServerPassword.Length > 0 ? "Password set - it will be used when joining" : null;
    }

    public string PlayersText => Online ? $"{Players}/{MaxPlayers}" : "-/-";
    public string PingText => Online ? $"{PingMs} ms" : "--";
    public bool IsOffline => State == CardState.Offline;
    public bool IsDownloading => State == CardState.Downloading;
    public bool CanJoin => State is CardState.Ready or CardState.UpdateRequired;
    public bool CanJoinAnyway => State is CardState.Offline or CardState.NoModList;

    public string ActionLabel => State switch
    {
        CardState.Checking => "CHECKING...",
        CardState.Ready => "JOIN SERVER",
        CardState.UpdateRequired => $"UPDATE MODS ({OutdatedCount})",
        CardState.Downloading => $"DOWNLOADING {DownloadPercent:0}%",
        CardState.Launching => "LAUNCHING...",
        CardState.Running => "RUNNING",
        CardState.NoModList => "MOD LIST UNAVAILABLE",
        _ => "OFFLINE"
    };

    public void UpdateRestartCountdown()
    {
        if (Server.RestartIntervalHours <= 0)
        {
            RestartText = null;
            IsRestartSoon = false;
            return;
        }
        var nowUtc = DateTimeOffset.UtcNow;
        var next = ComputeNextRestartUtc(nowUtc);
        if (next is null)
        {
            RestartText = null;
            IsRestartSoon = false;
            return;
        }
        var remaining = next.Value - nowUtc;
        if (remaining < TimeSpan.FromMinutes(1))
        {
            RestartText = "Restart imminent!";
            IsRestartSoon = true;
        }
        else if (remaining <= TimeSpan.FromMinutes(15))
        {
            RestartText = $"Restart in {(int)Math.Ceiling(remaining.TotalMinutes)} min!";
            IsRestartSoon = true;
        }
        else
        {
            var hours = (int)remaining.TotalHours;
            var minutes = remaining.Minutes;
            RestartText = hours > 0 ? $"next restart in {hours}h {minutes}m" : $"next restart in {minutes}m";
            IsRestartSoon = false;
        }
    }

    private DateTimeOffset? ComputeNextRestartUtc(DateTimeOffset nowUtc)
    {
        var interval = Math.Clamp(Server.RestartIntervalHours, 1, 24);
        var anchor = ((Server.RestartAnchorHour % 24) + 24) % 24;
        var tz = ResolveTimeZone();
        var hours = new SortedSet<int>();
        for (var k = 0; k < 24; k++)
        {
            hours.Add((anchor + k * interval) % 24);
        }
        var localNow = TimeZoneInfo.ConvertTime(nowUtc, tz).DateTime;
        for (var day = 0; day <= 2; day++)
        {
            var date = localNow.Date.AddDays(day);
            foreach (var hour in hours)
            {
                var candidate = new DateTime(date.Year, date.Month, date.Day, hour, 0, 0, DateTimeKind.Unspecified);
                if (candidate <= localNow) continue;
                if (tz.IsInvalidTime(candidate)) continue;
                try
                {
                    return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(candidate, tz), TimeSpan.Zero);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Restart time conversion failed for {Server}", Server.Name);
                }
            }
        }
        return null;
    }

    private TimeZoneInfo ResolveTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(Server.RestartTimeZone);
        }
        catch (Exception)
        {
        }
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");
        }
        catch (Exception)
        {
            return TimeZoneInfo.Local;
        }
    }

    public async Task RefreshStatusAsync(CancellationToken ct = default)
    {
        try
        {
            var status = await _owner.A2S.QueryAsync(Server.Ip, Server.QueryPort, ct);
            Online = status.Online;
            Players = status.Players;
            MaxPlayers = status.MaxPlayers;
            PingMs = status.PingMs;
            if (status.Online)
            {
                PasswordProtected = status.PasswordProtected;
                if (!status.PasswordProtected)
                {
                    PasswordEntryOpen = false;
                }
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "A2S query failed for {Server}", Server.Name);
            Online = false;
        }
        _hasPolled = true;
        if (!Online && State is CardState.Ready or CardState.Checking)
        {
            State = CardState.Offline;
        }
        else if (Online && State == CardState.Offline)
        {
            State = RecomputeState();
        }
    }

    public async Task VerifyAsync()
    {
        if (_verifying) return;
        if (State is CardState.Downloading or CardState.Launching or CardState.Running) return;
        var env = _owner.Env;
        if (env is not { DayZFound: true })
        {
            StatusText = "DayZ installation not found";
            VerificationCompleted?.Invoke();
            return;
        }
        _verifying = true;
        try
        {
            State = CardState.Checking;
            StatusText = null;
            if (Server.ModsFromQuery)
            {
                IReadOnlyList<ModEntry>? queried = null;
                try
                {
                    queried = await _owner.ServerModQuery.QueryModListAsync(Server.Ip, Server.QueryPort);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Live mod list query failed for {Server}", Server.Name);
                }
                if (IsLaunchState()) return;
                if (queried is null)
                {
                    _verification = Array.Empty<ModVerificationResult>();
                    OutdatedCount = 0;
                    StatusText = "Mod list unavailable - the server did not answer the query";
                    State = CardState.NoModList;
                    OnPropertyChanged(nameof(ModCount));
                    return;
                }
                Server.Mods = queried.ToList();
                OnPropertyChanged(nameof(ModCount));
            }
            try
            {
                var results = await _owner.Verifier.VerifyAsync(Server, env);
                if (IsLaunchState()) return;
                _verification = results;
                OutdatedCount = results.Count(r => r.State != ModState.Ready);
                StatusText = OutdatedCount > 0 ? $"{OutdatedCount} of {results.Count} mods need attention" : "All mods verified";
                State = RecomputeState();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Mod verification failed for {Server}", Server.Name);
                if (IsLaunchState()) return;
                StatusText = "Mod verification failed - joining may get you kicked";
                State = Online || !_hasPolled ? CardState.Ready : CardState.Offline;
            }
        }
        finally
        {
            _verifying = false;
            VerificationCompleted?.Invoke();
        }
    }

    private bool IsLaunchState()
    {
        return State is CardState.Downloading or CardState.Launching or CardState.Running;
    }

    public async Task<bool> UpdateModsAsync()
    {
        if (Core.Utils.DayZProcess.IsRunning())
        {
            StatusText = "Close DayZ first - mods can only update while the game is not running";
            return false;
        }
        var env = _owner.Env;
        if (env is not { DayZFound: true })
        {
            StatusText = "DayZ installation not found";
            return false;
        }
        if (State is CardState.Downloading or CardState.Launching or CardState.Running) return false;
        if (_verification.Count == 0)
        {
            await VerifyAsync();
            if (_verification.Count == 0) return false;
        }
        State = CardState.Downloading;
        DownloadPercent = 0;
        try
        {
            var list = _verification.ToList();
            var progress = new Progress<WorkshopProgress>(OnWorkshopProgress);
            var ok = await _owner.Updater.EnsureModsReadyAsync(list, env, progress);
            _verification = list;
            if (!ok)
            {
                StatusText = "Some mods could not be updated";
            }
            State = CardState.Checking;
            await VerifyAsync();
            return OutdatedCount == 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Workshop update failed for {Server}", Server.Name);
            StatusText = "Mod update failed: " + ex.Message;
            State = CardState.UpdateRequired;
            return false;
        }
    }

    private void OnWorkshopProgress(WorkshopProgress p)
    {
        if (p.BytesTotal > 0)
        {
            DownloadPercent = Math.Min(100.0, p.BytesDownloaded * 100.0 / p.BytesTotal);
        }
        else if (p.TotalMods > 0)
        {
            DownloadPercent = Math.Min(100.0, p.CompletedMods * 100.0 / p.TotalMods);
        }
        var text = p.Phase is { Length: > 0 } phase && p.CurrentModName is { Length: > 0 } name
            ? $"{phase}: {name}"
            : p.Phase ?? p.CurrentModName;
        if (text is { Length: > 0 })
        {
            StatusText = text;
        }
    }

    private CardState RecomputeState()
    {
        if (OutdatedCount > 0) return CardState.UpdateRequired;
        if (!Online && _hasPolled) return CardState.Offline;
        return CardState.Ready;
    }

    [RelayCommand(CanExecute = nameof(CanJoin))]
    private async Task JoinAsync()
    {
        if (State == CardState.UpdateRequired)
        {
            var ready = await UpdateModsAsync();
            if (!ready) return;
        }
        else
        {
            await VerifyAsync();
        }
        if (State == CardState.Ready && OutdatedCount == 0)
        {
            await LaunchAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(CanJoinAnyway))]
    private Task JoinAnywayAsync()
    {
        return LaunchAsync();
    }

    private async Task LaunchAsync()
    {
        var env = _owner.Env;
        if (env is not { DayZFound: true })
        {
            StatusText = "DayZ installation not found";
            return;
        }
        var previous = State;
        State = CardState.Launching;
        LaunchResult result;
        try
        {
            var password = PasswordProtected && ServerPassword.Length > 0 ? ServerPassword : null;
            result = _owner.Launcher.Launch(Server, _verification, env, _owner.SettingsService.Settings, password);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Launch failed for {Server}", Server.Name);
            StatusText = "Launch failed: " + ex.Message;
            State = previous is CardState.Offline or CardState.NoModList ? previous : RecomputeState();
            return;
        }
        if (!result.Started)
        {
            StatusText = result.Error ?? "Launch failed";
            State = previous is CardState.Offline or CardState.NoModList ? previous : RecomputeState();
            return;
        }
        var settings = _owner.SettingsService.Settings;
        settings.LastSelectedServerId = Server.Id;
        try
        {
            _owner.SettingsService.Save();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Saving settings failed");
        }
        _owner.Rpc.SetPlaying(Server.Name);
        if (settings.CloseOnLaunch)
        {
            _owner.RequestExit();
            return;
        }
        State = CardState.Running;
        StatusText = "Game is running";
        await TrackProcessAsync(result.ProcessId);
        _owner.Rpc.SetInLauncher();
        StatusText = null;
        State = previous == CardState.NoModList ? CardState.NoModList : previous == CardState.Offline && !Online ? CardState.Offline : RecomputeState();
    }

    private static async Task TrackProcessAsync(int processId)
    {
        if (processId <= 0) return;
        try
        {
            using var process = Process.GetProcessById(processId);
            await process.WaitForExitAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Tracking the game process failed");
        }
    }
}
