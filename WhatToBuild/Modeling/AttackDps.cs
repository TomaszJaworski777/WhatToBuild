using WhatToBuild.Data;

namespace WhatToBuild.Modeling;

public sealed record DpsBreakdown(double Attacks, double OnHit)
{
    public double Total => Attacks + OnHit;
}

public static class AttackDps
{
    public const double BaseCritDamage = 1.75;

    public static DpsBreakdown Against(ChampionState attacker, Entity target)
    {
        var items = attacker.Items;
        var stats = attacker.Stats;
        var critChance = Math.Min(1, items.Sum(i => i.Stats.CritChance)
            + items.SelectMany(i => i.Effects).Where(e => StatCalculator.IsPermanentStatBuff(e) && e.Stat == Stats.CritChance).Sum(e => e.Amount));
        var critDamage = BaseCritDamage + items.Sum(i => i.Stats.CritDamage);

        var normal = Physical(attacker, target, stats.AttackDamage).Attack().Run().HealthDamage;
        var crit = critChance > 0
            ? Physical(attacker, target, stats.AttackDamage * critDamage).Crit().Run().HealthDamage
            : 0;

        var perAttack = normal * (1 - critChance) + crit * critChance;

        var onHit = items
            .SelectMany(i => i.Effects)
            .Where(e => e.Trigger == EffectTrigger.OnAttack && !e.Splash && target.Satisfies(e.When))
            .Sum(e => OnHitDps(e, attacker, target));

        return new DpsBreakdown(perAttack * stats.AttackSpeed, onHit);
    }

    public static double TeamAverage(ChampionState attacker, IEnumerable<Entity> targets)
    {
        var list = targets.ToList();
        return list.Count == 0 ? 0 : list.Average(t => Against(attacker, t).Total);
    }

    private static double OnHitDps(Effect effect, ChampionState attacker, Entity target)
    {
        var raw = RawAmount(effect, attacker, target) * (attacker.Champion.IsRanged ? effect.RangedMultiplier : 1);
        var calculator = effect.Kind switch
        {
            EffectKind.PhysicalDamage => Physical(attacker, target, raw),
            EffectKind.MagicDamage => Magic(attacker, target, raw),
            EffectKind.TrueDamage => DamageCalculator.Create(target).TrueDamage(raw),
            EffectKind.AdaptiveDamage => BonusAttackDamage(attacker) >= attacker.Stats.AbilityPower
                ? Physical(attacker, target, raw)
                : Magic(attacker, target, raw),
            _ => null,
        };

        if (calculator is null)
        {
            return 0;
        }

        var procsPerSecond = effect.EveryAttacks > 0
            ? attacker.Stats.AttackSpeed / effect.EveryAttacks
            : effect.Cooldown > 0
                ? Math.Min(attacker.Stats.AttackSpeed, 1 / effect.Cooldown)
                : attacker.Stats.AttackSpeed;

        return calculator.Attack().Run().HealthDamage * procsPerSecond;
    }

    private static double RawAmount(Effect effect, ChampionState attacker, Entity target) =>
        effect.Amount
        + effect.PerLevel * (attacker.Level - 1)
        + effect.PerBaseAd * BaseAttackDamage(attacker)
        + effect.PerTotalAd * attacker.Stats.AttackDamage
        + effect.PerAp * attacker.Stats.AbilityPower
        + effect.PerMaxHealth * attacker.Stats.Health
        + effect.PerTargetMaxHealth * target.MaxHealth
        + effect.PerTargetCurrentHealth * target.CurrentHealth;

    private static double BaseAttackDamage(ChampionState attacker) =>
        attacker.Champion.Base.AttackDamage
        + attacker.Champion.PerLevel.AttackDamage * StatCalculator.GrowthMultiplier(attacker.Level);

    private static double BonusAttackDamage(ChampionState attacker) =>
        attacker.Stats.AttackDamage - BaseAttackDamage(attacker);

    private static DamageCalculator Physical(ChampionState attacker, Entity target, double amount)
    {
        var calculator = DamageCalculator.Create(target)
            .AdDamage(amount)
            .Lethality(attacker.Items.Sum(i => i.Stats.ArmorPenetrationFlat))
            .AttackerItems(attacker.Items);

        foreach (var item in attacker.Items.Where(i => i.Stats.ArmorPenetrationPercent > 0))
        {
            calculator.ArmorPenetration(item.Stats.ArmorPenetrationPercent);
        }

        return calculator;
    }

    private static DamageCalculator Magic(ChampionState attacker, Entity target, double amount)
    {
        var calculator = DamageCalculator.Create(target)
            .ApDamage(amount)
            .FlatMagicPenetration(attacker.Items.Sum(i => i.Stats.MagicPenetrationFlat))
            .AttackerItems(attacker.Items);

        foreach (var item in attacker.Items.Where(i => i.Stats.MagicPenetrationPercent > 0))
        {
            calculator.MagicPenetration(item.Stats.MagicPenetrationPercent);
        }

        return calculator;
    }
}

public sealed class AttackDpsScorer : IPurchaseScorer
{
    private readonly Champion _champion;
    private readonly int _level;
    private readonly IReadOnlyList<StatModifier> _teamBuffs;
    private readonly IReadOnlyList<Entity> _targets;
    private readonly CombatAssumption _combat;

    public AttackDpsScorer(Champion champion, int level, IEnumerable<StatModifier> teamBuffs, IEnumerable<Entity> targets, CombatAssumption combat)
    {
        _combat = combat;
        _champion = champion;
        _level = level;
        _teamBuffs = teamBuffs.ToList();
        _targets = targets.ToList();
    }

    public double Score(IReadOnlyList<Item> inventory) =>
        AttackDps.TeamAverage(new ChampionState(_champion, _level, inventory, _teamBuffs, _combat), _targets);
}
