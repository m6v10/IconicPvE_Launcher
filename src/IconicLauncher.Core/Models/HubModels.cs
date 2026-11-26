namespace IconicLauncher.Core.Models;

public sealed class HubPlayerFile
{
    public int V { get; set; }
    public string Sid { get; set; } = "";
    public string Name { get; set; } = "";
    public string Box { get; set; } = "";
    public string Era { get; set; } = "";
    public List<HubServerStats> Servers { get; set; } = new();
    public HubAllTime AllTime { get; set; } = new();
    public List<HubHistoryEntry> History { get; set; } = new();
}

public sealed class HubServerStats
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public long Rank { get; set; }
    public long Ranked { get; set; }
    public long Points { get; set; }
    public long Zombies { get; set; }
    public long Players { get; set; }
    public long Animals { get; set; }
    public long Deaths { get; set; }
    public long WorldBosses { get; set; }
    public long DynBosses { get; set; }
    public long RareAnimals { get; set; }
    public List<HubKill> Kills { get; set; } = new();
    public long Level { get; set; }
    public long Xp { get; set; }
    public long Prestige { get; set; }
    public long FishCaught { get; set; }
    public long FishLevel { get; set; }
    public List<HubProfession> Professions { get; set; } = new();
    public HubSeasonPass SeasonPass { get; set; } = new();
    public long PlaytimeMin { get; set; }
    public long Quests { get; set; }
    public long Bank { get; set; }
    public HubCoins Coins { get; set; } = new();
    public List<string> Achievements { get; set; } = new();
    public string Title { get; set; } = "";
    public long Transmog { get; set; }
    public string Group { get; set; } = "";
    public long FirstJoin { get; set; }
    public long LastOnline { get; set; }
    public long Sessions { get; set; }
}

public sealed class HubKill
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string Group { get; set; } = "";
    public long Count { get; set; }
}

public sealed class HubProfession
{
    public string Id { get; set; } = "";
    public long Skill { get; set; }
    public long Rank { get; set; }
}

public sealed class HubSeasonPass
{
    public long Level { get; set; }
    public long Prestige { get; set; }
    public long Xp { get; set; }
    public long Premium { get; set; }
    public long LoginStreak { get; set; }
    public long BestStreak { get; set; }
}

public sealed class HubCoins
{
    public long Balance { get; set; }
    public long Spent { get; set; }
    public long Orders { get; set; }
}

public sealed class HubAllTime
{
    public long Points { get; set; }
    public long Zombies { get; set; }
    public long Players { get; set; }
    public long Animals { get; set; }
    public long Deaths { get; set; }
    public long WorldBosses { get; set; }
    public long DynBosses { get; set; }
    public long RareAnimals { get; set; }
    public long FishCaught { get; set; }
    public long PlaytimeMin { get; set; }
    public long Quests { get; set; }
    public long Level { get; set; }
    public long Prestige { get; set; }
    public List<HubKill> Kills { get; set; } = new();
    public List<string> Eras { get; set; } = new();
}

public sealed class HubHistoryEntry
{
    public string Era { get; set; } = "";
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public long Points { get; set; }
    public long Rank { get; set; }
    public long Ranked { get; set; }
    public long Level { get; set; }
}

public sealed class HubIndex
{
    public int V { get; set; }
    public string Box { get; set; } = "";
    public string Era { get; set; } = "";
    public long Updated { get; set; }
    public List<HubIndexServer> Servers { get; set; } = new();
}

public sealed class HubIndexServer
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public long Players { get; set; }
    public long Ranked { get; set; }
}

public sealed class HubAchievementInfo
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
}

public sealed class HubBoxData
{
    public string Box { get; set; } = "";
    public HubIndex? Index { get; set; }
    public HubPlayerFile? Player { get; set; }
}

public sealed class PlayerHubSnapshot
{
    public string SteamId { get; set; } = "";
    public List<HubBoxData> Boxes { get; set; } = new();
    public DateTime FetchedUtc { get; set; }
    public bool AnyReachable { get; set; }

    public IEnumerable<HubPlayerFile> Players => Boxes.Where(b => b.Player != null).Select(b => b.Player!);
}
