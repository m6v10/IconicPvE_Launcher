using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IconicLauncher.Core.Models;
using IconicLauncher.Core.Services;
using IconicLauncher.Core.Utils;
using Serilog;

namespace IconicLauncher.ViewModels;

public sealed partial class ProfileViewModel : ObservableObject
{
    private static readonly Regex DerivedKey = new("^(eu|us)(main|lite)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private readonly MainViewModel _owner;
    private readonly IPlayerHubService _hub;
    private readonly DispatcherTimer _refreshTimer;
    private readonly Dictionary<string, Dictionary<string, HubAchievementInfo>> _catalog = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _catalogBoxes = new(StringComparer.OrdinalIgnoreCase);
    private PlayerHubSnapshot? _snapshot;
    private SteamAccount? _account;
    private string? _focusKey;
    private bool _loading;

    public ObservableCollection<ProfileTab> Tabs { get; } = new();
    public ObservableCollection<StatTile> Tiles { get; } = new();
    public ObservableCollection<StatSection> Sections { get; } = new();
    public ObservableCollection<BarRow> BossKills { get; } = new();
    public ObservableCollection<StatRow> Professions { get; } = new();
    public ObservableCollection<AchievementChip> Achievements { get; } = new();
    public ObservableCollection<HistoryRow> History { get; } = new();

    [ObservableProperty]
    private bool hasData;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string emptyTitle = "Loading your stats";

    [ObservableProperty]
    private string emptyText = "";

    [ObservableProperty]
    private string playerName = "";

    [ObservableProperty]
    private string steamIdText = "";

    [ObservableProperty]
    private string subtitleText = "";

    [ObservableProperty]
    private string groupTag = "";

    [ObservableProperty]
    private string titleText = "";

    [ObservableProperty]
    private string achievementsText = "";

    [ObservableProperty]
    private double achievementsFraction;

    [ObservableProperty]
    private bool showAchievements;

    [ObservableProperty]
    private bool showProfessions;

    [ObservableProperty]
    private bool showBossKills;

    [ObservableProperty]
    private bool showHistory;

    [ObservableProperty]
    private string bossKillsTitle = "BOSS KILLS";

    [ObservableProperty]
    private HomeChip homeSummary = new("?", "Your stats", "Loading...", false);

    public ProfileViewModel(MainViewModel owner, IPlayerHubService hub)
    {
        _owner = owner;
        _hub = hub;
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        _refreshTimer.Tick += async (_, _) => await LoadAsync();
        _refreshTimer.Start();
    }

    public static string? HubKeyFor(ServerEntry server)
    {
        if (!string.IsNullOrWhiteSpace(server.HubKey)) return server.HubKey.Trim();
        var m = DerivedKey.Match(server.Id ?? "");
        return m.Success ? $"{m.Groups[1].Value.ToLowerInvariant()}-{m.Groups[2].Value.ToLowerInvariant()}" : null;
    }

    public void SetFocusServer(ServerCardViewModel? card)
    {
        _focusKey = card is null ? null : HubKeyFor(card.Server);
        UpdateHomeSummary();
    }

    [RelayCommand]
    private async Task Refresh()
    {
        await LoadAsync();
    }

    [RelayCommand]
    private void SelectTab(ProfileTab? tab)
    {
        if (tab is null) return;
        foreach (var t in Tabs) t.IsSelected = ReferenceEquals(t, tab);
        Render(tab);
    }

    [RelayCommand]
    private void CopySteamId()
    {
        if (_account is null) return;
        try
        {
            Clipboard.SetText(_account.SteamId64);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Copying the SteamID failed");
        }
    }

    public void OnNavigatedTo()
    {
        if (_snapshot is null || DateTime.UtcNow - _snapshot.FetchedUtc > TimeSpan.FromMinutes(2))
        {
            _ = LoadAsync();
        }
    }

    public async Task LoadAsync()
    {
        if (_loading) return;
        _loading = true;
        IsLoading = true;
        try
        {
            _account = SteamIdentity.Current(_owner.Env?.SteamRoot);
            if (_account is null)
            {
                ShowEmpty("Start Steam to see your stats", "Your stats are linked to the Steam account you play DayZ with. Open Steam and sign in, then refresh.");
                HomeSummary = new HomeChip("?", "Your stats", "Start Steam to load them", false);
                return;
            }
            SteamIdText = _account.SteamId64;
            var boxes = Boxes();
            _snapshot = await _hub.LoadAsync(_account.SteamId64, _owner.HubBaseUrl, boxes, CancellationToken.None);
            if (!_snapshot.AnyReachable)
            {
                ShowEmpty("Stats are unreachable right now", "The stats service did not answer. Check your connection and refresh in a minute.");
                HomeSummary = new HomeChip("?", Persona(), "Stats unreachable", false);
                return;
            }
            if (!_snapshot.Players.Any())
            {
                ShowEmpty("No stats for this account yet", "Join one of our servers and play for a while. Stats update every 15 minutes.");
                HomeSummary = new HomeChip(Initial(Persona()), Persona(), "No stats yet", false);
                return;
            }
            BuildTabs();
            HasData = true;
            UpdateHomeSummary();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Loading player hub data failed");
            ShowEmpty("Stats could not be loaded", "Something went wrong while reading your stats. Refresh to try again.");
        }
        finally
        {
            _loading = false;
            IsLoading = false;
        }
    }

    private IReadOnlyList<string> Boxes()
    {
        var keys = _owner.Home.Servers.Select(c => HubKeyFor(c.Server))
            .Where(k => k != null && k.Contains('-'))
            .Select(k => k!.Split('-')[0].ToLowerInvariant())
            .Distinct()
            .ToList();
        foreach (var box in new[] { "eu", "us" })
        {
            if (!keys.Contains(box)) keys.Add(box);
        }
        return keys;
    }

    private string Persona()
    {
        var fromFiles = _snapshot?.Players.Select(p => p.Name).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
        return fromFiles ?? _account?.PersonaName ?? "Survivor";
    }

    private static string Initial(string name) => string.IsNullOrWhiteSpace(name) ? "?" : name.Trim()[..1].ToUpperInvariant();

    private void ShowEmpty(string title, string text)
    {
        HasData = false;
        EmptyTitle = title;
        EmptyText = text;
        Tabs.Clear();
    }

    private IEnumerable<HubServerStats> AllServers() =>
        _snapshot?.Players.SelectMany(p => p.Servers) ?? Enumerable.Empty<HubServerStats>();

    private void BuildTabs()
    {
        var previous = Tabs.FirstOrDefault(t => t.IsSelected)?.Key;
        Tabs.Clear();
        foreach (var s in AllServers().OrderBy(s => s.Key.StartsWith("eu", StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(s => s.Key.EndsWith("main", StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(s => s.Key))
        {
            Tabs.Add(new ProfileTab { Key = s.Key, Label = s.Label.ToUpperInvariant() });
        }
        Tabs.Add(new ProfileTab { Key = "*", Label = "ALL TIME", IsAllTime = true });
        PlayerName = Persona();
        var era = _snapshot!.Players.Select(p => p.Era).FirstOrDefault(e => e.Length > 0) ?? "";
        var updated = _snapshot.Boxes.Where(b => b.Index != null).Select(b => b.Index!.Updated).DefaultIfEmpty(0).Max();
        var parts = new List<string>();
        if (era.Length > 0) parts.Add($"Wipe {era}");
        if (updated > 0) parts.Add($"updated {Fmt.Ago(updated)}");
        if (_account is { SignedIn: false }) parts.Add("last signed-in Steam account");
        SubtitleText = string.Join("  ·  ", parts);
        var pick = Tabs.FirstOrDefault(t => t.Key == previous)
                   ?? Tabs.FirstOrDefault(t => t.Key == _focusKey)
                   ?? Tabs.Where(t => !t.IsAllTime).OrderByDescending(t => AllServers().First(s => s.Key == t.Key).Points).FirstOrDefault()
                   ?? Tabs[0];
        SelectTab(pick);
    }

    private void UpdateHomeSummary()
    {
        if (_snapshot is null || !_snapshot.Players.Any()) return;
        var name = Persona();
        var stats = AllServers().FirstOrDefault(s => string.Equals(s.Key, _focusKey, StringComparison.OrdinalIgnoreCase));
        if (stats is null)
        {
            var best = AllServers().OrderByDescending(s => s.Level).FirstOrDefault();
            HomeSummary = new HomeChip(best is null ? Initial(name) : best.Level.ToString(), name, "No stats on this server yet", false);
            return;
        }
        var rank = stats.Rank > 0 ? $"#{Fmt.Number(stats.Rank)} of {Fmt.Number(stats.Ranked)}" : "Unranked";
        var activity = stats.PlaytimeMin > 0 ? $"{Fmt.Hours(stats.PlaytimeMin)} played" : $"{Fmt.Number(stats.Sessions)} sessions";
        HomeSummary = new HomeChip(stats.Level.ToString(), name, $"{rank}  ·  Bank {Fmt.Compact(stats.Bank)}  ·  {activity}", true);
    }

    private void Render(ProfileTab tab)
    {
        Tiles.Clear();
        Sections.Clear();
        BossKills.Clear();
        Professions.Clear();
        Achievements.Clear();
        History.Clear();
        if (tab.IsAllTime) RenderAllTime();
        else
        {
            var s = AllServers().FirstOrDefault(x => x.Key == tab.Key);
            if (s != null) RenderServer(s);
        }
        var history = _snapshot?.Players.SelectMany(p => p.History).ToList() ?? new List<HubHistoryEntry>();
        var past = history.Where(h => !_snapshot!.Players.Any(p => p.Era == h.Era)).ToList();
        foreach (var h in past.OrderByDescending(h => h.Era).ThenBy(h => h.Key))
        {
            History.Add(new HistoryRow(h.Era, h.Label, Fmt.Number(h.Points), h.Rank > 0 ? $"#{h.Rank} of {h.Ranked}" : "-", h.Level.ToString()));
        }
        ShowHistory = History.Count > 0;
        ShowBossKills = BossKills.Count > 0;
        ShowProfessions = Professions.Count > 0;
    }

    private void RenderServer(HubServerStats s)
    {
        GroupTag = s.Group;
        TitleText = s.Title;
        Tiles.Add(new StatTile("RANK", s.Rank > 0 ? $"#{Fmt.Number(s.Rank)}" : "-", s.Rank > 0 ? $"of {Fmt.Number(s.Ranked)} players" : "Not ranked yet"));
        Tiles.Add(new StatTile("POINTS", Fmt.Number(s.Points), "this wipe"));
        Tiles.Add(new StatTile("LEVEL", s.Level.ToString(), s.Prestige > 0 ? $"Prestige {s.Prestige}" : $"{Fmt.Compact(s.Xp)} XP", $"{Fmt.Number(s.Xp)} XP"));
        Tiles.Add(new StatTile("BANK", Fmt.Compact(s.Bank), "personal account", Fmt.Number(s.Bank)));
        if (s.PlaytimeMin > 0 || s.Sessions <= 0)
            Tiles.Add(new StatTile("PLAYTIME", Fmt.Hours(s.PlaytimeMin), s.Sessions > 0 ? $"{Fmt.Number(s.Sessions)} sessions" : "this wipe", Fmt.HoursLong(s.PlaytimeMin)));
        else
            Tiles.Add(new StatTile("SESSIONS", Fmt.Number(s.Sessions), s.FirstJoin > 0 ? $"since {Fmt.Date(s.FirstJoin)}" : "this wipe"));

        Sections.Add(new StatSection("COMBAT", new List<StatRow>
        {
            new("Zombies killed", Fmt.Number(s.Zombies)),
            new("Animals killed", Fmt.Number(s.Animals)),
            new("Players killed", Fmt.Number(s.Players)),
            new("World bosses", Fmt.Number(s.WorldBosses)),
            new("Dynamic bosses", Fmt.Number(s.DynBosses)),
            new("Rare animals", Fmt.Number(s.RareAnimals)),
            new("Deaths", Fmt.Number(s.Deaths)),
        }));
        var sp = s.SeasonPass;
        var progress = new List<StatRow>
        {
            new("Season pass", sp.Level > 0 || sp.Prestige > 0 ? $"Level {sp.Level}{(sp.Prestige > 0 ? $" · Prestige {sp.Prestige}" : "")}{(sp.Premium > 0 ? " · Premium" : "")}" : "-"),
            new("Login streak", sp.LoginStreak > 0 ? $"{sp.LoginStreak} days (best {Math.Max(sp.BestStreak, sp.LoginStreak)})" : "-"),
            new("Quests done", Fmt.Number(s.Quests)),
            new("Fishing", s.FishCaught > 0 || s.FishLevel > 0 ? $"{Fmt.Number(s.FishCaught)} caught · level {s.FishLevel}" : "-"),
            new("Shop coins", $"{Fmt.Number(s.Coins.Balance)} ({Fmt.Number(s.Coins.Spent)} spent)"),
            new("Transmog pieces", Fmt.Number(s.Transmog)),
            new("First joined", Fmt.Date(s.FirstJoin)),
            new("Last online", Fmt.Ago(s.LastOnline)),
        };
        Sections.Add(new StatSection("PROGRESS", progress.Where(r => r.Value != "-").ToList()));
        AddKills(s.Kills, "BOSS KILLS");
        foreach (var p in s.Professions)
        {
            Professions.Add(new StatRow(Fmt.Title(p.Id), Fmt.Number(p.Skill), p.Rank > 0 ? $"Rank {p.Rank}" : null));
        }
        _ = RenderAchievementsAsync(s);
    }

    private void RenderAllTime()
    {
        GroupTag = "";
        TitleText = "";
        ShowAchievements = false;
        var all = _snapshot!.Players.Select(p => p.AllTime).ToList();
        long Sum(Func<HubAllTime, long> f) => all.Sum(f);
        var eras = all.SelectMany(a => a.Eras).Distinct().Count();
        Tiles.Add(new StatTile("POINTS", Fmt.Number(Sum(a => a.Points)), "all servers"));
        Tiles.Add(new StatTile("BEST LEVEL", all.Max(a => a.Level).ToString(), all.Max(a => a.Prestige) > 0 ? $"Prestige {all.Max(a => a.Prestige)}" : "highest reached"));
        if (Sum(a => a.PlaytimeMin) > 0)
            Tiles.Add(new StatTile("PLAYTIME", Fmt.Hours(Sum(a => a.PlaytimeMin)), "all servers", Fmt.HoursLong(Sum(a => a.PlaytimeMin))));
        else
            Tiles.Add(new StatTile("BOSS KILLS", Fmt.Number(Sum(a => a.WorldBosses) + Sum(a => a.DynBosses)), "world + dynamic"));
        Tiles.Add(new StatTile("ZOMBIES", Fmt.Compact(Sum(a => a.Zombies)), "killed", Fmt.Number(Sum(a => a.Zombies))));
        Tiles.Add(new StatTile("WIPES", eras.ToString(), eras == 1 ? "played" : "played on"));
        Sections.Add(new StatSection("COMBAT", new List<StatRow>
        {
            new("Zombies killed", Fmt.Number(Sum(a => a.Zombies))),
            new("Animals killed", Fmt.Number(Sum(a => a.Animals))),
            new("Players killed", Fmt.Number(Sum(a => a.Players))),
            new("World bosses", Fmt.Number(Sum(a => a.WorldBosses))),
            new("Dynamic bosses", Fmt.Number(Sum(a => a.DynBosses))),
            new("Rare animals", Fmt.Number(Sum(a => a.RareAnimals))),
            new("Deaths", Fmt.Number(Sum(a => a.Deaths))),
        }));
        Sections.Add(new StatSection("PROGRESS", new List<StatRow>
        {
            new("Quests done", Fmt.Number(Sum(a => a.Quests))),
            new("Fish caught", Fmt.Number(Sum(a => a.FishCaught))),
            new("Wipes", string.Join(", ", all.SelectMany(a => a.Eras).Distinct())),
        }));
        var kills = all.SelectMany(a => a.Kills)
            .GroupBy(k => k.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new HubKill { Key = g.Key, Label = g.First().Label, Group = g.First().Group, Count = g.Sum(k => k.Count) })
            .OrderByDescending(k => k.Count)
            .ToList();
        AddKills(kills, "BOSS KILLS, ALL TIME");
    }

    private void AddKills(IEnumerable<HubKill> kills, string title)
    {
        BossKillsTitle = title;
        var list = kills.Where(k => k.Count > 0).OrderByDescending(k => k.Count).Take(12).ToList();
        var top = list.Count > 0 ? list[0].Count : 1;
        foreach (var k in list)
        {
            BossKills.Add(new BarRow(k.Label, Fmt.Number(k.Count), (double)k.Count / top, k.Group));
        }
    }

    private async Task RenderAchievementsAsync(HubServerStats s)
    {
        var unlocked = s.Achievements;
        ShowAchievements = true;
        var box = s.Key.Split('-')[0];
        if (!_catalogBoxes.Contains(box))
        {
            var catalog = await _hub.LoadAchievementsAsync(_owner.HubBaseUrl, box, CancellationToken.None);
            foreach (var kv in catalog) _catalog[kv.Key] = kv.Value;
            if (catalog.Count > 0) _catalogBoxes.Add(box);
        }
        if (!Tabs.Any(t => t.IsSelected && t.Key == s.Key)) return;
        _catalog.TryGetValue(s.Key, out var info);
        var total = info?.Count ?? 0;
        AchievementsText = total > 0 ? $"{unlocked.Count} of {total} unlocked" : unlocked.Count > 0 ? $"{unlocked.Count} unlocked" : "None unlocked yet";
        AchievementsFraction = total > 0 ? Math.Clamp((double)unlocked.Count / total, 0, 1) : 0;
        Achievements.Clear();
        foreach (var name in unlocked)
        {
            HubAchievementInfo? a = null;
            info?.TryGetValue(name, out a);
            var title = string.IsNullOrWhiteSpace(a?.Title) ? name : a!.Title;
            Achievements.Add(new AchievementChip(title, a?.Description?.Replace("\\n", " ").Replace("\n", " ").Trim() ?? name));
        }
    }
}
