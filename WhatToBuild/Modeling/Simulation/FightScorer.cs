using WhatToBuild.Data;

namespace WhatToBuild.Modeling.Simulation;

public sealed class FightScorer : IPurchaseScorer
{
    public static readonly double[] AttackPhases = [0, 0.25, 0.5, 0.75];

    private readonly Champion _champion;
    private readonly int _level;
    private readonly IReadOnlyList<StatModifier> _teamBuffs;
    private readonly IReadOnlyList<Entity> _targets;
    private readonly AbilityRanks _ranks;
    private readonly double _stacks;
    private readonly Func<IChampionKit?> _newKit;
    private readonly StatSheet? _adjustment;
    private readonly IReadOnlyDictionary<Guid, double>? _itemStacks;

    public FightScorer(
        Champion champion,
        int level,
        IEnumerable<StatModifier> teamBuffs,
        IEnumerable<Entity> targets,
        AbilityRanks ranks,
        double stacks,
        Func<IChampionKit?> newKit,
        StatSheet? adjustment = null,
        IReadOnlyDictionary<Guid, double>? itemStacks = null)
    {
        _adjustment = adjustment;
        _itemStacks = itemStacks;
        _champion = champion;
        _level = level;
        _teamBuffs = teamBuffs.ToList();
        _targets = targets.ToList();
        _ranks = ranks;
        _stacks = stacks;
        _newKit = newKit;
    }

    public ChampionState Us(IEnumerable<Item> inventory, IReadOnlyDictionary<Guid, double>? projectedStacks = null) =>
        new(_champion, _level, inventory, _teamBuffs, _adjustment, Merge(_itemStacks, projectedStacks));

    private static IReadOnlyDictionary<Guid, double>? Merge(IReadOnlyDictionary<Guid, double>? owned, IReadOnlyDictionary<Guid, double>? projected)
    {
        if (projected is null || projected.Count == 0)
        {
            return owned;
        }

        var merged = new Dictionary<Guid, double>(owned ?? new Dictionary<Guid, double>());
        foreach (var (id, stacks) in projected)
        {
            merged.TryAdd(id, stacks);
        }

        return merged;
    }

    public FightResult Against(IEnumerable<Item> inventory, Entity target, IReadOnlyDictionary<Guid, double>? projectedStacks = null)
    {
        var us = Us(inventory, projectedStacks);

        return FightResult.Average(AttackPhases
            .Select(phase => FightSimulator.Run(new FightSetup(us, target, _ranks, _stacks, AttackPhase: phase), _newKit()))
            .ToList());
    }

    public double Score(IReadOnlyList<Item> inventory) =>
        _targets.Count == 0 ? 0 : _targets.Average(t => Against(inventory, t).EffectiveDps);
}
