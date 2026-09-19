using WhatToBuild.Data;

namespace WhatToBuild.Modeling.Simulation;

public static class AttackerHits
{
    public const double BaseCritDamage = 1.75;

    public static double CritChance(ChampionState attacker) =>
        Math.Min(1, attacker.Items.Sum(i => i.Stats.CritChance)
                    + attacker.Items.SelectMany(i => i.Effects)
                        .Where(e => StatCalculator.IsPermanentStatBuff(e) && e.Stat == Stats.CritChance)
                        .Sum(e => e.Amount));

    public static double CritDamage(ChampionState attacker) =>
        BaseCritDamage + attacker.Items.Sum(i => i.Stats.CritDamage);

    public static double BaseAttackDamage(ChampionState attacker) =>
        attacker.Champion.Base.AttackDamage
        + attacker.Champion.PerLevel.AttackDamage * StatCalculator.GrowthMultiplier(attacker.Level);

    public static double BonusAttackDamage(ChampionState attacker) =>
        attacker.Stats.AttackDamage - BaseAttackDamage(attacker);

    public static DamageCalculator Physical(ChampionState attacker, Entity target, double amount)
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

    public static DamageCalculator Magic(ChampionState attacker, Entity target, double amount)
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

    public static DamageCalculator? ForEffect(Effect effect, ChampionState attacker, Entity target)
    {
        var raw = RawAmount(effect, attacker, target) * (attacker.Champion.IsRanged ? effect.RangedMultiplier : 1);

        if (effect.ByCompanion)
        {
            return effect.Kind switch
            {
                EffectKind.PhysicalDamage => DamageCalculator.Create(target).AdDamage(raw),
                EffectKind.MagicDamage => DamageCalculator.Create(target).ApDamage(raw),
                EffectKind.TrueDamage => DamageCalculator.Create(target).TrueDamage(raw),
                _ => null,
            };
        }

        return effect.Kind switch
        {
            EffectKind.PhysicalDamage => Physical(attacker, target, raw),
            EffectKind.MagicDamage => Magic(attacker, target, raw),
            EffectKind.TrueDamage => DamageCalculator.Create(target).TrueDamage(raw).AttackerItems(attacker.Items),
            EffectKind.AdaptiveDamage => BonusAttackDamage(attacker) >= attacker.Stats.AbilityPower
                ? Physical(attacker, target, raw)
                : Magic(attacker, target, raw),
            _ => null,
        };
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
}
