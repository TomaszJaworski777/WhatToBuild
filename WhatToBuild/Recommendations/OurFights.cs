using WhatToBuild.Data;
using WhatToBuild.Game;
using WhatToBuild.Modeling;
using WhatToBuild.Modeling.Simulation;
using WhatToBuild.SupportedChampions;

namespace WhatToBuild.Recommendations;

public sealed class OurFights
{
    private OurFights(GameState state, PlayerState me, NeutralRepository neutrals, ChampionKits kits)
    {
        Me = me;
        EnemyTeam = me.Team == Team.Order ? Team.Chaos : Team.Order;
        Owned = me.Items.SelectMany(i => Enumerable.Repeat(i.Item, i.Count)).ToList();
        Buffs = state.TeamBuffs(me.Team, neutrals).ToList();
        Enemies = state.Enemies.Select(p => (p, state.EntityFor(p, neutrals))).ToList();
        Ranks = kits.RanksFor(me.Champion, me.Level, state.ActivePlayerRanks);
        RanksObserved = state.ActivePlayerRanks is { } observed && observed != AbilityRanks.None;
        Supported = kits.For(me.Champion);
        Stacks = me.EstimatedStacks.FirstOrDefault()?.Stacks ?? 0;
        Scorer = new FightScorer(
            me.Champion,
            me.Level,
            Buffs,
            Enemies.Select(e => (Entity)e.Entity),
            Ranks,
            Stacks,
            () => kits.NewFight(me.Champion),
            state.ActivePlayerStats is { } observedStats
                ? StatCalculator.Adjustment(observedStats, state.EntityFor(me, neutrals).Stats)
                : null,
            state.ItemStacksFor(me));
    }

    public static OurFights? For(GameState state, NeutralRepository neutrals, ChampionKits kits) =>
        state.ActivePlayer is { } me ? new OurFights(state, me, neutrals, kits) : null;

    public PlayerState Me { get; }

    public Team EnemyTeam { get; }

    public IReadOnlyList<Item> Owned { get; }

    public IReadOnlyList<StatModifier> Buffs { get; }

    public IReadOnlyList<(PlayerState Player, ChampionState Entity)> Enemies { get; }

    public AbilityRanks Ranks { get; }

    public bool RanksObserved { get; }

    public ISupportedChampion? Supported { get; }

    public bool HasKit => Supported is not null;

    public double Stacks { get; }

    public FightScorer Scorer { get; }

    public IReadOnlyList<CastHint> Hints(Entity target) =>
        Supported?.Hints(new FightSetup(Scorer.Us(Owned), target, Ranks, Stacks)) ?? [];
}
