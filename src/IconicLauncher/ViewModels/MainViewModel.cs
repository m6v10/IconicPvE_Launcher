using System.Diagnostics;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IconicLauncher.Core.Models;
using IconicLauncher.Core.Services;
using IconicLauncher.Services;
using Serilog;

namespace IconicLauncher.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IRemoteConfigService _configService;
    private readonly ISteamLocator _steamLocator;
    private readonly ISelfUpdateService _selfUpdate;
    private const int SyncIntervalSeconds = 60;
    private readonly CancellationTokenSource _cts = new();
    private readonly DispatcherTimer _restartTimer;
    private readonly DispatcherTimer _syncTimer;
    private readonly DispatcherTimer _updateCheckTimer;
    private LauncherConfig? _config;
    private UpdateCheck? _updateCheck;
    private bool _pollLoopStarted;
    private int _syncSecondsRemaining = SyncIntervalSeconds;
    private bool _syncRunning;
    private bool _updateCheckRunning;

    internal ISettingsService SettingsService { get; }
    internal IModVerificationService Verifier { get; }
    internal IWorkshopUpdateService Updater { get; }
    internal IModOperationsService ModOperations { get; }
    internal IGameLauncher Launcher { get; }
    internal IA2SQueryService A2S { get; }
    internal IServerModQueryService ServerModQuery { get; }
    internal DiscordRichPresenceService Rpc { get; }
    internal SteamEnvironment? Env { get; private set; }

    public HomeViewModel Home { get; }
    public ModsViewModel Mods { get; }
    public SettingsViewModel SettingsPage { get; }
    public AdminViewModel? Admin { get; }
    public bool IsAdminMode { get; }
    public string VersionLabel { get; }

    public event Action? ExitRequested;

    [ObservableProperty]
    private object? currentViewModel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOfflineData))]
    private ConfigSource configSource = ConfigSource.Live;

    public bool IsOfflineData => ConfigSource != ConfigSource.Live;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunSelfUpdateCommand))]
    private bool updateAvailable;

    [ObservableProperty]
    private string updateBannerText = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunSelfUpdateCommand))]
    private bool isUpdating;

    [ObservableProperty]
    private double updateProgress;

    [ObservableProperty]
    private string? statusBanner;

    [ObservableProperty]
    private string? websiteUrl;

    [ObservableProperty]
    private string syncCountdownText = $"Syncing server status and mods automatically in {SyncIntervalSeconds}s";

    public MainViewModel(
        ISettingsService settingsService,
        IRemoteConfigService configService,
        ISteamLocator steamLocator,
        IModVerificationService verifier,
        IWorkshopUpdateService updater,
        IModOperationsService modOperations,
        IGameLauncher launcher,
        IA2SQueryService a2s,
        IServerModQueryService serverModQuery,
        ISelfUpdateService selfUpdate,
        DiscordRichPresenceService rpc,
        IAdminConfigBuilder adminConfigBuilder,
        IFtpPublishService ftpPublishService,
        bool adminMode,
        string version)
    {
        SettingsService = settingsService;
        _configService = configService;
        _steamLocator = steamLocator;
        Verifier = verifier;
        Updater = updater;
        ModOperations = modOperations;
        Launcher = launcher;
        A2S = a2s;
        ServerModQuery = serverModQuery;
        _selfUpdate = selfUpdate;
        Rpc = rpc;
        IsAdminMode = adminMode;
        VersionLabel = $"v{version}";
        Home = new HomeViewModel();
        Mods = new ModsViewModel(this);
        SettingsPage = new SettingsViewModel(this);
        Admin = adminMode ? new AdminViewModel(this, adminConfigBuilder, ftpPublishService) : null;
        CurrentViewModel = Home;
        _restartTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _restartTimer.Tick += OnRestartTimerTick;
        _restartTimer.Start();
        _syncTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _syncTimer.Tick += OnSyncTimerTick;
        _syncTimer.Start();
        _updateCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        _updateCheckTimer.Tick += OnUpdateCheckTimerTick;
        _updateCheckTimer.Start();
    }

    private void OnSyncTimerTick(object? sender, EventArgs e)
    {
        if (_syncRunning) return;
        _syncSecondsRemaining--;
        if (_syncSecondsRemaining > 0)
        {
            SyncCountdownText = $"Syncing server status and mods automatically in {_syncSecondsRemaining}s";
            return;
        }
        if (IsSyncBlocked())
        {
            ResetSyncCountdown();
            return;
        }
        _ = RunAutoSyncAsync();
    }

    private bool IsSyncBlocked()
    {
        if (IsUpdating || Mods.IsBusy || Mods.HasBusyRows) return true;
        return Home.Servers.Any(c => c.State is CardState.Checking or CardState.Downloading or CardState.Launching);
    }

    private async Task RunAutoSyncAsync()
    {
        _syncRunning = true;
        SyncCountdownText = "Syncing...";
        try
        {
            await Mods.AutoSyncAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Auto sync failed");
        }
        finally
        {
            _syncRunning = false;
            ResetSyncCountdown();
        }
    }

    internal void ResetSyncCountdown()
    {
        _syncSecondsRemaining = SyncIntervalSeconds;
        SyncCountdownText = $"Syncing server status and mods automatically in {SyncIntervalSeconds}s";
    }

    private async void OnUpdateCheckTimerTick(object? sender, EventArgs e)
    {
        if (_updateCheckRunning) return;
        _updateCheckRunning = true;
        try
        {
            var result = await _configService.LoadAsync(_cts.Token);
            ConfigSource = result.Source;
            if (result.Source == ConfigSource.Live)
            {
                _config = result.Config;
                RefreshLiveContent(result.Config);
                var check = _selfUpdate.Check(result.Config);
                if (check.UpdateAvailable)
                {
                    _updateCheck = check;
                    UpdateBannerText = $"Launcher update {check.LatestVersion} is available";
                    UpdateAvailable = true;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Periodic update check failed");
        }
        finally
        {
            _updateCheckRunning = false;
        }
    }

    private void OnRestartTimerTick(object? sender, EventArgs e)
    {
        foreach (var card in Home.Servers)
        {
            card.UpdateRestartCountdown();
        }
    }

    public async Task InitializeAsync()
    {
        try
        {
            await LoadConfigAsync();
            CheckForUpdate();
            LocateSteam();
            InitializeRpc();
            StartPollLoop();
            await VerifyAllAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Startup sequence failed");
            StatusBanner = "Startup error: " + ex.Message;
        }
    }

    private async Task LoadConfigAsync()
    {
        StatusBanner = "Loading configuration...";
        try
        {
            var result = await _configService.LoadAsync(_cts.Token);
            _config = result.Config;
            ConfigSource = result.Source;
            StatusBanner = null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Config load failed");
            StatusBanner = "Could not load any configuration";
            return;
        }
        ApplyConfig(_config!);
    }

    private void ApplyConfig(LauncherConfig config)
    {
        WebsiteUrl = string.IsNullOrWhiteSpace(config.WebsiteUrl) ? null : config.WebsiteUrl.Trim();
        Home.Servers.Clear();
        Home.News.Clear();
        foreach (var server in config.Servers)
        {
            Home.Servers.Add(new ServerCardViewModel(server, this));
        }
        // "news": null in a hand-edited config deserializes to null, not an empty list.
        foreach (var item in config.News ?? new List<NewsItem>())
        {
            Home.News.Add(item);
        }
        Mods.SetServers(Home.Servers);
        Admin?.SetTemplate(config);
    }

    /// <summary>
    /// Applies the parts of a freshly fetched live config that are safe to swap under a
    /// running launcher. Called on the periodic update-check tick.
    /// </summary>
    private void RefreshLiveContent(LauncherConfig config)
    {
        WebsiteUrl = string.IsNullOrWhiteSpace(config.WebsiteUrl) ? null : config.WebsiteUrl.Trim();

        // Home.Servers is deliberately NOT rebuilt here. Recreating the cards would throw
        // away ServerCardViewModels that may be mid-verification or mid-launch, which is the
        // same class of race the auto-sync guard already exists to prevent. Server list
        // changes still land on the next launcher start.
        var incoming = config.News ?? new List<NewsItem>();
        if (NewsMatches(incoming))
        {
            return;
        }
        // Only rebuilt when something actually changed - clearing the collection on every
        // tick would reset the NEWS scroll position and flicker for no reason.
        Home.News.Clear();
        foreach (var item in incoming)
        {
            Home.News.Add(item);
        }
        Log.Information("News refreshed from live config: {Count} item(s)", incoming.Count);
    }

    private bool NewsMatches(List<NewsItem> incoming)
    {
        if (incoming.Count != Home.News.Count)
        {
            return false;
        }
        for (var i = 0; i < incoming.Count; i++)
        {
            var current = Home.News[i];
            var candidate = incoming[i];
            if (current.Id != candidate.Id
                || current.Date != candidate.Date
                || current.Title != candidate.Title
                || current.Body != candidate.Body
                || current.Url != candidate.Url)
            {
                return false;
            }
        }
        return true;
    }

    private void CheckForUpdate()
    {
        if (_config is null) return;
        try
        {
            var check = _selfUpdate.Check(_config);
            if (check.UpdateAvailable)
            {
                _updateCheck = check;
                UpdateBannerText = $"Launcher update {check.LatestVersion} is available";
                UpdateAvailable = true;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Update check failed");
        }
    }

    internal void LocateSteam()
    {
        try
        {
            Env = _steamLocator.Locate(SettingsService.Settings);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Steam detection failed");
            Env = null;
        }
        if (Env is not { DayZFound: true })
        {
            StatusBanner = "DayZ installation not found - set the paths in Settings";
        }
        SettingsPage.RefreshDetected();
        Admin?.OnEnvironmentChanged(Env);
    }

    private void InitializeRpc()
    {
        if (!SettingsService.Settings.DiscordRpcEnabled || _config is null) return;
        Rpc.Initialize(_config.Discord.ApplicationId);
        Rpc.SetInLauncher();
    }

    private void StartPollLoop()
    {
        if (_pollLoopStarted) return;
        _pollLoopStarted = true;
        _ = PollLoopAsync(_cts.Token);
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var cards = Home.Servers.ToList();
            try
            {
                await Task.WhenAll(cards.Select(c => c.RefreshStatusAsync(ct)));
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Server status poll failed");
            }
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task VerifyAllAsync()
    {
        var cards = Home.Servers.ToList();
        if (cards.Count == 0) return;
        var selected = cards.FirstOrDefault(c => c.Server.Id == SettingsService.Settings.LastSelectedServerId) ?? cards[0];
        await selected.VerifyAsync();
        foreach (var card in cards)
        {
            if (ReferenceEquals(card, selected)) continue;
            await card.VerifyAsync();
        }
    }

    private async Task VerifyAllSafeAsync()
    {
        try
        {
            await VerifyAllAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Re-verification failed");
        }
    }

    [RelayCommand]
    private void Navigate(string? section)
    {
        switch (section)
        {
            case "MODS":
                CurrentViewModel = Mods;
                break;
            case "SETTINGS":
                CurrentViewModel = SettingsPage;
                break;
            case "ADMIN":
                if (Admin != null) CurrentViewModel = Admin;
                break;
            default:
                CurrentViewModel = Home;
                break;
        }
    }

    [RelayCommand]
    private async Task RefreshStatusesAsync()
    {
        var cards = Home.Servers.ToList();
        try
        {
            await Task.WhenAll(cards.Select(c => c.RefreshStatusAsync(_cts.Token)));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Manual status refresh failed");
        }
    }

    [RelayCommand]
    private void OpenWebsite()
    {
        var url = WebsiteUrl;
        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Opening website failed");
            StatusBanner = "Could not open the website";
        }
    }

    [RelayCommand]
    private void OpenDiscord()
    {
        var url = _config?.Discord.InviteUrl;
        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            StatusBanner = "Discord invite is not configured yet";
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Opening Discord failed");
            StatusBanner = "Could not open the Discord invite";
        }
    }

    private bool CanRunSelfUpdate => UpdateAvailable && !IsUpdating;

    [RelayCommand(CanExecute = nameof(CanRunSelfUpdate))]
    private async Task RunSelfUpdateAsync()
    {
        if (_updateCheck is null) return;
        IsUpdating = true;
        UpdateProgress = 0;
        try
        {
            var progress = new Progress<double>(v => UpdateProgress = Math.Clamp(v <= 1.0 ? v * 100.0 : v, 0.0, 100.0));
            var ok = await _selfUpdate.ApplyAsync(_updateCheck, progress, _cts.Token);
            if (ok)
            {
                ExitRequested?.Invoke();
                return;
            }
            StatusBanner = "Update failed - try again later or download it manually";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Self update failed");
            StatusBanner = "Update failed: " + ex.Message;
        }
        finally
        {
            IsUpdating = false;
        }
    }

    internal void OnSettingsSaved()
    {
        StatusBanner = null;
        LocateSteam();
        InitializeRpc();
        _ = VerifyAllSafeAsync();
    }

    internal void RequestExit()
    {
        ExitRequested?.Invoke();
    }

    public void Dispose()
    {
        _restartTimer.Stop();
        _syncTimer.Stop();
        _updateCheckTimer.Stop();
        _cts.Cancel();
        _cts.Dispose();
    }
}
