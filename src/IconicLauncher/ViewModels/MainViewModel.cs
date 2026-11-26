using System.Diagnostics;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IconicLauncher.Core.Models;
using IconicLauncher.Core.Services;
using IconicLauncher.Core.Utils;
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
    public ProfileViewModel Profile { get; }
    public ModsViewModel Mods { get; }
    public SettingsViewModel SettingsPage { get; }
    public AdminViewModel? Admin { get; }
    public WowViewModel Wow { get; }
    public WowAddonsViewModel WowAddons { get; }
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

    [ObservableProperty]
    private object? dialog;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDayZActive))]
    [NotifyPropertyChangedFor(nameof(IsWowActive))]
    [NotifyPropertyChangedFor(nameof(GameSuffixText))]
    private string activeGame = "DAYZ";

    public bool IsDayZActive => ActiveGame == "DAYZ";
    public bool IsWowActive => ActiveGame == "WOW";
    public string GameSuffixText => IsWowActive ? " WoW" : " PvE";

    partial void OnActiveGameChanged(string value)
    {
        GameTheme.Apply(IsWowActive);
        OnPropertyChanged(nameof(ShowDayZBackdrop));
    }

    public bool IsHomeChecked => CurrentViewModel is HomeViewModel or WowViewModel;
    public bool IsProfileChecked => CurrentViewModel is ProfileViewModel;
    public bool ShowDayZBackdrop => IsDayZActive && CurrentViewModel is HomeViewModel or ProfileViewModel;
    public bool IsProfilePage => CurrentViewModel is ProfileViewModel;

    [ObservableProperty]
    private Uri? dayZHeroVideoUri;

    public string HubBaseUrl =>
        Environment.GetEnvironmentVariable("ICONIC_HUB_BASE") is { Length: > 0 } env ? env
        : string.IsNullOrWhiteSpace(_config?.HubBaseUrl) ? LauncherConstants.DefaultHubBaseUrl : _config!.HubBaseUrl!.Trim();
    public bool IsModsChecked => CurrentViewModel is ModsViewModel;
    public bool IsAddonsChecked => CurrentViewModel is WowAddonsViewModel;
    public bool IsSettingsChecked => CurrentViewModel is SettingsViewModel;
    public bool IsAdminChecked => CurrentViewModel is AdminViewModel;

    partial void OnCurrentViewModelChanged(object? value)
    {
        OnPropertyChanged(nameof(IsHomeChecked));
        OnPropertyChanged(nameof(IsProfileChecked));
        OnPropertyChanged(nameof(ShowDayZBackdrop));
        OnPropertyChanged(nameof(IsProfilePage));
        OnPropertyChanged(nameof(IsModsChecked));
        OnPropertyChanged(nameof(IsAddonsChecked));
        OnPropertyChanged(nameof(IsSettingsChecked));
        OnPropertyChanged(nameof(IsAdminChecked));
    }

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
        IWowPatchService wowPatchService,
        IWowStatusService wowStatusService,
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
        Profile = new ProfileViewModel(this, new PlayerHubService());
        Home = new HomeViewModel(this);
        Mods = new ModsViewModel(this);
        SettingsPage = new SettingsViewModel(this);
        Admin = adminMode ? new AdminViewModel(this, adminConfigBuilder, ftpPublishService) : null;
        Wow = new WowViewModel(this, wowPatchService, wowStatusService);
        WowAddons = new WowAddonsViewModel(this, wowPatchService);
        ActiveGame = settingsService.Settings.DefaultGame?.ToLowerInvariant() switch
        {
            "dayz" => "DAYZ",
            "wow" => "WOW",
            _ => string.Equals(settingsService.Settings.LastGame, "WOW", StringComparison.OrdinalIgnoreCase) ? "WOW" : "DAYZ"
        };
        CurrentViewModel = IsWowActive ? Wow : Home;
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
            if (SettingsService.Settings.AutoUpdateMods)
            {
                await Mods.AutoUpdateAsync();
            }
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
            if (Environment.GetEnvironmentVariable("ICONIC_LAUNCHER_START_PAGE") is { Length: > 0 } startPage)
            {
                if (startPage == "GFXWARN")
                {
                    _ = CheckGraphicsBeforeLaunchAsync();
                }
                else
                {
                    Navigate(startPage);
                }
            }
            _ = Profile.LoadAsync();
            _ = LoadHeroVideoAsync();
            InitializeRpc();
            StartPollLoop();
            _ = Wow.InitializeAsync();
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
        foreach (var entry in BuildServerList())
        {
            if (!entry.IsVisible) continue;
            Home.Servers.Add(new ServerCardViewModel(entry.Server, this, entry.IsCustom));
        }
        // "news": null in a hand-edited config deserializes to null, not an empty list.
        var news = config.News ?? new List<NewsItem>();
        MarkNewNews(news);
        foreach (var item in news)
        {
            Home.News.Add(item);
        }
        Mods.SetServers(Home.Servers);
        Admin?.SetTemplate(config);
        Wow.ApplyConfig(config.Wow);
        WowAddons.ApplyConfig(config.Wow);
        SettingsPage.RefreshGraphics();
    }

    public void MoveServerCard(ServerCardViewModel card, int delta)
    {
        var index = Home.Servers.IndexOf(card);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= Home.Servers.Count)
        {
            return;
        }
        Home.Servers.Move(index, target);
        SettingsService.Settings.ServerOrder = Home.Servers.Select(c => c.Server.Id).ToList();
        try
        {
            SettingsService.Save();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Persisting the server order failed");
        }
    }

    internal IReadOnlyList<ServerListEntry> BuildServerList()
    {
        return ServerListBuilder.BuildAll(_config ?? new LauncherConfig(), SettingsService.Settings);
    }

    internal bool IsKnownServerId(string id)
    {
        return BuildServerList().Any(e => string.Equals(e.Server.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Adds a player-defined server. Returns null on success, otherwise the reason.
    /// </summary>
    internal string? AddCustomServer(ServerEntry server)
    {
        if (IsKnownServerId(server.Id))
        {
            return "That server is already in your list.";
        }
        var settings = SettingsService.Settings;
        settings.CustomServers ??= new List<ServerEntry>();
        settings.CustomServers.Add(server);
        settings.ServerVisibility ??= new Dictionary<string, bool>();
        settings.ServerVisibility[server.Id] = true;
        SaveSettingsSafe("Saving the custom server failed");
        AddCard(server, true);
        return null;
    }

    /// <summary>
    /// Shows or hides a server card. Returns null on success, otherwise the reason.
    /// </summary>
    internal string? SetServerVisible(string id, bool visible)
    {
        var entry = BuildServerList().FirstOrDefault(e => string.Equals(e.Server.Id, id, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            return "That server is no longer in the list.";
        }
        if (!visible)
        {
            var blocked = BlockReason(id);
            if (blocked != null)
            {
                return blocked;
            }
        }
        var settings = SettingsService.Settings;
        settings.ServerVisibility ??= new Dictionary<string, bool>();
        settings.ServerVisibility[id] = visible;
        SaveSettingsSafe("Saving the server visibility failed");
        if (visible)
        {
            AddCard(entry.Server, entry.IsCustom);
        }
        else
        {
            RemoveCard(id);
        }
        return null;
    }

    /// <summary>
    /// Deletes a player-defined server for good. Returns null on success, otherwise the reason.
    /// </summary>
    internal string? DeleteCustomServer(string id)
    {
        var settings = SettingsService.Settings;
        var custom = settings.CustomServers?.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
        if (custom is null)
        {
            return "Only servers you added yourself can be deleted.";
        }
        var blocked = BlockReason(id);
        if (blocked != null)
        {
            return blocked;
        }
        settings.CustomServers!.Remove(custom);
        settings.ServerVisibility?.Remove(id);
        settings.ServerPasswords?.Remove(id);
        settings.ServerCache?.Remove(id);
        settings.ServerExperimental?.Remove(id);
        settings.ServerOrder?.RemoveAll(s => string.Equals(s, id, StringComparison.OrdinalIgnoreCase));
        SaveSettingsSafe("Deleting the custom server failed");
        RemoveCard(id);
        return null;
    }

    /// <summary>
    /// Drops every visibility override, so the list falls back to what the config ships.
    /// </summary>
    internal void RestoreDefaultServerVisibility()
    {
        var settings = SettingsService.Settings;
        settings.ServerVisibility = new Dictionary<string, bool>();
        SaveSettingsSafe("Restoring the default server list failed");
        foreach (var entry in BuildServerList())
        {
            if (entry.IsVisible)
            {
                AddCard(entry.Server, entry.IsCustom);
            }
            else
            {
                RemoveCard(entry.Server.Id);
            }
        }
    }

    private string? BlockReason(string id)
    {
        var card = FindCard(id);
        if (card is null)
        {
            return null;
        }
        return card.State is CardState.Downloading or CardState.Launching or CardState.Running
            ? "That server is busy right now - wait until it finishes."
            : null;
    }

    private ServerCardViewModel? FindCard(string id)
    {
        return Home.Servers.FirstOrDefault(c => string.Equals(c.Server.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    private void AddCard(ServerEntry server, bool isCustom)
    {
        if (FindCard(server.Id) != null)
        {
            return;
        }
        var card = new ServerCardViewModel(server, this, isCustom);
        Home.Servers.Insert(ComputeInsertIndex(server.Id), card);
        Mods.SetServers(Home.Servers);
        _ = InitializeCardAsync(card);
    }

    private void RemoveCard(string id)
    {
        var card = FindCard(id);
        if (card is null)
        {
            return;
        }
        Home.Servers.Remove(card);
        // Re-registering the remaining cards also drops the removed card's verification
        // subscription, so its mods disappear from the MODS union.
        Mods.SetServers(Home.Servers);
    }

    private int ComputeInsertIndex(string id)
    {
        var index = 0;
        foreach (var entry in BuildServerList())
        {
            if (string.Equals(entry.Server.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return Math.Min(index, Home.Servers.Count);
            }
            if (FindCard(entry.Server.Id) != null)
            {
                index++;
            }
        }
        return Home.Servers.Count;
    }

    private async Task InitializeCardAsync(ServerCardViewModel card)
    {
        try
        {
            await card.RefreshStatusAsync(_cts.Token);
            await card.VerifyAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Initializing the new server card failed");
        }
    }

    internal string? LoadServerPassword(string id)
    {
        var map = SettingsService.Settings.ServerPasswords;
        if (map == null || !map.TryGetValue(id, out var protectedValue) || string.IsNullOrEmpty(protectedValue))
        {
            return null;
        }
        return DpapiProtector.Unprotect(protectedValue);
    }

    internal void SaveServerPassword(string id, string? password)
    {
        var settings = SettingsService.Settings;
        settings.ServerPasswords ??= new Dictionary<string, string>();
        if (string.IsNullOrEmpty(password))
        {
            if (!settings.ServerPasswords.Remove(id))
            {
                return;
            }
        }
        else
        {
            settings.ServerPasswords[id] = DpapiProtector.Protect(password);
        }
        SaveSettingsSafe("Saving the server password failed");
    }

    internal ServerCacheEntry? GetServerCache(string id)
    {
        return SettingsService.Settings.ServerCache?.GetValueOrDefault(id);
    }

    internal (List<ModEntry> Mods, DateTime? Utc)? GetKnownMods(ServerEntry server)
    {
        var cache = GetServerCache(server.Id);
        if (cache?.Mods is { Count: > 0 })
        {
            return (cache.Mods, cache.ModsUtc);
        }
        if (server.Mods is { Count: > 0 })
        {
            return (server.Mods, null);
        }
        return null;
    }

    private ServerCacheEntry GetOrCreateServerCache(string id)
    {
        var settings = SettingsService.Settings;
        settings.ServerCache ??= new Dictionary<string, ServerCacheEntry>();
        if (!settings.ServerCache.TryGetValue(id, out var entry))
        {
            entry = new ServerCacheEntry();
            settings.ServerCache[id] = entry;
        }
        return entry;
    }

    internal void RememberServerMods(string id, List<ModEntry> mods)
    {
        if (mods.Count == 0)
        {
            return;
        }
        var entry = GetOrCreateServerCache(id);
        entry.Mods ??= new List<ModEntry>();
        var same = entry.Mods.Count == mods.Count
            && entry.Mods.Zip(mods).All(p => p.First.WorkshopId == p.Second.WorkshopId && p.First.Name == p.Second.Name);
        var fresh = entry.ModsUtc is { } utc && DateTime.UtcNow - utc < TimeSpan.FromHours(1);
        if (same && fresh)
        {
            return;
        }
        entry.Mods = mods.Select(m => new ModEntry { WorkshopId = m.WorkshopId, Name = m.Name }).ToList();
        entry.ModsUtc = DateTime.UtcNow;
        SaveSettingsSafe("Saving the server mod list failed");
    }

    internal void RememberServerVersion(string id, string version)
    {
        var entry = GetOrCreateServerCache(id);
        if (entry.Version == version)
        {
            return;
        }
        entry.Version = version;
        SaveSettingsSafe("Saving the server version failed");
    }

    internal bool IsServerExperimental(ServerEntry server)
    {
        return FindCard(server.Id)?.IsExperimental
            ?? GameBranch.UseExperimental(server, SettingsService.Settings, Env, GetServerCache(server.Id)?.Version);
    }

    internal void SetServerExperimental(string id, bool experimental)
    {
        var settings = SettingsService.Settings;
        settings.ServerExperimental ??= new Dictionary<string, bool>();
        settings.ServerExperimental[id] = experimental;
        SaveSettingsSafe("Saving the experimental setting failed");
        FindCard(id)?.RefreshBranch();
    }

    private void SaveSettingsSafe(string failureMessage)
    {
        try
        {
            SettingsService.Save();
        }
        catch (Exception ex)
        {
            Log.Error(ex, failureMessage);
            StatusBanner = failureMessage;
        }
    }

    [RelayCommand]
    private void ShowAddServer()
    {
        Dialog = new AddServerDialogViewModel(this);
    }

    [RelayCommand]
    private void ShowEditServers()
    {
        Dialog = new EditServersDialogViewModel(this);
    }

    [RelayCommand]
    private void CloseDialog()
    {
        if (Dialog is GraphicsWarningDialogViewModel graphics)
        {
            graphics.Complete(GraphicsChoice.Cancel);
        }
        Dialog = null;
    }

    public DayZVideoSettingsService Video { get; } = new(
        Environment.GetEnvironmentVariable("ICONIC_LAUNCHER_DEV_DIR") is { Length: > 0 } devDir ? System.IO.Path.Combine(devDir, "DayZDocs") : null);

    public GraphicsAdvisory Advisory => _config?.GraphicsAdvisory ?? new GraphicsAdvisory();

    public async Task<bool> CheckGraphicsBeforeLaunchAsync()
    {
        var advisory = Advisory;
        if (!advisory.Enabled || SettingsService.Settings.GraphicsAdvisoryMutedId == advisory.Id)
        {
            return true;
        }
        DayZVideoState? state;
        try
        {
            state = Video.Read();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Reading DayZ graphics settings failed");
            return true;
        }
        if (state is not { IsAboveMedium: true } || DayZVideoSettingsService.IsDayZRunning())
        {
            return true;
        }
        var dialog = new GraphicsWarningDialogViewModel(this, state, advisory);
        Dialog = dialog;
        var choice = await dialog.Result;
        if (Dialog == dialog)
        {
            Dialog = null;
        }
        if (dialog.DontShowAgain && choice != GraphicsChoice.Cancel)
        {
            SettingsService.Settings.GraphicsAdvisoryMutedId = advisory.Id;
            SaveSettingsSafe("Saving settings failed");
            SettingsPage.RefreshGraphics();
        }
        switch (choice)
        {
            case GraphicsChoice.ApplyMedium:
                try
                {
                    Video.CapAt(VideoPreset.Medium, state);
                    Log.Information("Graphics capped at Medium before launch (was {Preset})", state.PresetLabel);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Capping graphics at Medium failed");
                    StatusBanner = "Could not change the graphics settings: " + ex.Message;
                    return false;
                }
                SettingsPage.RefreshGraphics();
                return true;
            case GraphicsChoice.PlayAnyway:
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Applies the parts of a freshly fetched live config that are safe to swap under a
    /// running launcher. Called on the periodic update-check tick.
    /// </summary>
    private void RefreshLiveContent(LauncherConfig config)
    {
        WebsiteUrl = string.IsNullOrWhiteSpace(config.WebsiteUrl) ? null : config.WebsiteUrl.Trim();
        if (!Wow.IsApplying && !Wow.IsVerifying)
        {
            Wow.ApplyConfig(config.Wow);
            WowAddons.ApplyConfig(config.Wow);
        }
        SettingsPage.RefreshGraphics();

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
        MarkNewNews(incoming);
        Home.News.Clear();
        foreach (var item in incoming)
        {
            Home.News.Add(item);
        }
        Log.Information("News refreshed from live config: {Count} item(s)", incoming.Count);
    }

    private readonly HashSet<string> _sessionNewNews = new();

    private void MarkNewNews(List<NewsItem> items)
    {
        var seen = SettingsService.Settings.SeenNewsIds ??= new List<string>();
        var dirty = false;
        foreach (var item in items)
        {
            var key = string.IsNullOrWhiteSpace(item.Id) ? item.Date + "|" + item.Title : item.Id;
            if (!seen.Contains(key))
            {
                seen.Add(key);
                _sessionNewNews.Add(key);
                dirty = true;
            }
            item.IsNew = _sessionNewNews.Contains(key);
        }
        if (seen.Count > 200)
        {
            seen.RemoveRange(0, seen.Count - 200);
            dirty = true;
        }
        if (!dirty)
        {
            return;
        }
        try
        {
            SettingsService.Save();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Persisting seen news ids failed");
        }
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
        foreach (var card in Home.Servers)
        {
            card.RefreshBranch();
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
            await Wow.RefreshStatusSafeAsync(ct);
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

    internal void RememberSelectedServer(string id)
    {
        var settings = SettingsService.Settings;
        if (settings.LastSelectedServerId == id) return;
        settings.LastSelectedServerId = id;
        SaveSettingsSafe("Persisting the selected server failed");
    }

    private async Task LoadHeroVideoAsync()
    {
        var url = Environment.GetEnvironmentVariable("ICONIC_DAYZ_VIDEO") is { Length: > 0 } env ? env
            : string.IsNullOrWhiteSpace(_config?.DayZHeroVideoUrl) ? LauncherConstants.DefaultDayZHeroVideoUrl : _config!.DayZHeroVideoUrl!.Trim();
        var uri = await HeroVideoCache.EnsureAsync(url, SettingsService.AppDataDir, _cts.Token);
        if (uri != null) DayZHeroVideoUri = uri;
    }

    [RelayCommand]
    private void Navigate(string? section)
    {
        switch (section)
        {
            case "PROFILE":
                CurrentViewModel = IsWowActive ? Wow : Profile;
                if (IsDayZActive) Profile.OnNavigatedTo();
                break;
            case "MODS":
                CurrentViewModel = Mods;
                break;
            case "ADDONS":
                CurrentViewModel = WowAddons;
                WowAddons.OnNavigatedTo();
                break;
            case "SETTINGS":
                CurrentViewModel = SettingsPage;
                SettingsPage.RefreshGraphics();
                break;
            case "ADMIN":
                if (Admin != null) CurrentViewModel = Admin;
                break;
            default:
                CurrentViewModel = IsWowActive ? Wow : Home;
                break;
        }
    }

    [RelayCommand]
    private void SwitchGame(string? game)
    {
        var target = string.Equals(game, "WOW", StringComparison.OrdinalIgnoreCase) ? "WOW" : "DAYZ";
        if (ActiveGame != target)
        {
            ActiveGame = target;
            var settings = SettingsService.Settings;
            if (!string.Equals(settings.LastGame, target, StringComparison.OrdinalIgnoreCase))
            {
                settings.LastGame = target;
                SaveSettingsSafe("Persisting the game selection failed");
            }
        }
        CurrentViewModel = IsWowActive ? Wow : Home;
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
        Wow.RefreshClientPath();
        _ = VerifyAllSafeAsync();
        _ = Wow.VerifySafeAsync();
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
