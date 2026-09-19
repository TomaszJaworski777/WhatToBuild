using WhatToBuild.Data;
using WhatToBuild.Modeling;
using WhatToBuild.Modeling.Simulation;

namespace WhatToBuild.Game;

public enum Team
{
    Order,
    Chaos,
}

public sealed record OwnedItem(Item Item, int Count, int Slot = -1);

public sealed record StackEstimate(Stat Stat, double Stacks, double Max = 0, bool Observed = false, double? High = null);

public sealed class PlayerState
{
    public required Champion Champion { get; init; }

    public required Team Team { get; init; }

    public required int Level { get; init; }

    public IReadOnlyList<OwnedItem> Items { get; init; } = [];

    public IReadOnlyList<StackEstimate> EstimatedStacks { get; init; } = [];

    public int Kills { get; init; }

    public int Deaths { get; init; }

    public int Assists { get; init; }

    public int CreepScore { get; init; }

    public bool IsDead { get; init; }

    public string Position { get; init; } = "";

    public bool IsActivePlayer { get; init; }

    public int ItemValue => Items.Sum(i => i.Item.Cost * i.Count);

    public ChampionState ToEntity(IEnumerable<StatModifier>? teamBuffs = null, IReadOnlyDictionary<Guid, double>? itemStacks = null) =>
        new(Champion, Level, Items.SelectMany(i => Enumerable.Repeat(i.Item, i.Count)), teamBuffs, itemStacks: itemStacks);
}

public sealed class ObjectiveCounts
{
    public Dictionary<string, int> Dragons { get; } = new();

    public int Elders { get; set; }

    public int Barons { get; set; }

    public int Heralds { get; set; }

    public int Turrets { get; set; }

    public int Inhibitors { get; set; }

    public int TotalDragons => Dragons.Values.Sum();

    public string? SoulType { get; set; }

    public bool HasSoul => SoulType is not null;
}

public sealed class GameState
{
    public required double GameTime { get; init; }

    public required double CurrentGold { get; init; }

    public required IReadOnlyList<PlayerState> Players { get; init; }

    public required IReadOnlyDictionary<Team, ObjectiveCounts> Objectives { get; init; }

    public StatSheet? ActivePlayerStats { get; init; }

    public AbilityRanks? ActivePlayerRanks { get; init; }

    public IReadOnlyDictionary<string, string> ActivePlayerAbilityIds { get; init; } = new Dictionary<string, string>();

    public double? ActivePlayerCurrentHealth { get; init; }

    public IReadOnlyList<int> UnknownItemIds { get; init; } = [];

    public IReadOnlyList<string> UnknownChampions { get; init; } = [];

    public PlayerState? ActivePlayer => Players.FirstOrDefault(p => p.IsActivePlayer);

    public Team? ActiveTeam => ActivePlayer?.Team;

    public IEnumerable<PlayerState> Allies => ActiveTeam is { } team ? Players.Where(p => p.Team == team) : [];

    public IEnumerable<PlayerState> Enemies => ActiveTeam is { } team ? Players.Where(p => p.Team != team) : [];

    public double GoldEarned => CurrentGold + (ActivePlayer?.ItemValue ?? 0);

    public PlayerState? Find(Champion champion) => Players.FirstOrDefault(p => p.Champion.Id == champion.Id);

    public IEnumerable<StatModifier> TeamBuffs(Team team, NeutralRepository neutrals) =>
        Objectives[team].Dragons
            .SelectMany(d => neutrals.DragonFor(d.Key)?.BuffsFor(d.Value) ?? []);

    public double TeamTag(Team team, string tag, NeutralRepository neutrals)
    {
        var objectives = Objectives[team];

        var fromStacks = objectives.Dragons.Sum(d => neutrals.DragonFor(d.Key)?.StackTag(tag, d.Value) ?? 0);
        var fromSoul = neutrals.SoulFor(objectives.SoulType)?.Tag(tag) ?? 0;

        return fromStacks + fromSoul;
    }

    public IReadOnlyDictionary<(string Champion, int Item), double> ItemFirstSeen { get; set; } =
        new Dictionary<(string Champion, int Item), double>();

    public IReadOnlyDictionary<Guid, double> ItemStacksFor(PlayerState player) =>
        player.Items
            .Where(i => i.Item.Stacking is not null)
            .GroupBy(i => i.Item.Id)
            .ToDictionary(g => g.Key, g => g.First().Item.Stacking!.StacksAfter(MinutesOwned(player, g.First().Item), player.Champion.IsRanged));

    public double? MinutesOwned(PlayerState player, Item item) =>
        ItemFirstSeen.TryGetValue((player.Champion.Name, item.RiotId), out var since) ? (GameTime - since) / 60 : null;

    public ChampionState EntityFor(PlayerState player, NeutralRepository neutrals) =>
        player.ToEntity(TeamBuffs(player.Team, neutrals), ItemStacksFor(player));
}
