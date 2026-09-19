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

    public FightScorer(
        Champion champion,
        int level,
        IEnumerable<StatModifier> teamBuffs,
        IEnumerable<Entity> targets,
        AbilityRanks ranks,
        double stacks,
        Func<IChampionKit?> newKit,
        StatSheet? adjustment = null)
    {
        _adjustment = adjustment;
        _champion = champion;
        _level = level;
        _teamBuffs = teamBuffs.ToList();
        _targets = targets.ToList();
        _ranks = ranks;
        _stacks = stacks;
        _newKit = newKit;
    }

    public ChampionState Us(IEnumerable<Item> inventory) => new(_champion, _level, inventory, _teamBuffs, _adjustment);

    public FightResult Against(IEnumerable<Item> inventory, Entity target)
    {
        var us = Us(inventory);

        return FightResult.Average(AttackPhases
            .Select(phase => FightSimulator.Run(new FightSetup(us, target, _ranks, _stacks, AttackPhase: phase), _newKit()))
            .ToList());
    }

    public double Score(IReadOnlyList<Item> inventory) =>
        _targets.Count == 0 ? 0 : _targets.Average(t => Against(inventory, t).EffectiveDps);
}
