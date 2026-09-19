using WhatToBuild.Data;

namespace WhatToBuild.Modeling.Simulation;

public static class AttackerHits
{
    public const double BaseCritDamage = 1.75;

    public static double CritChance(ChampionState attacker) =>
        Math.Min(1, attacker.Items.Sum(i => i.Stats.CritChance)
                    + attacker.StackValue(Stats.CritChance)
                    + attacker.Items.SelectMany(i => i.Effects)
                        .Where(e => StatCalculator.IsPermanentStatBuff(e) && e.Stat == Stats.CritChance)
                        .Sum(e => e.Amount));

    public static double CritDamage(ChampionState attacker) =>
        BaseCritDamage + attacker.Items.Sum(i => i.Stats.CritDamage);

    public static double BaseAttackDamage(ChampionState attacker) => Base(attacker, s => s.AttackDamage);

    public static double BonusAttackDamage(ChampionState attacker) => Bonus(attacker, s => s.AttackDamage);

    public static double Base(ChampionState attacker, Func<StatSheet, double> stat) =>
        stat(attacker.Champion.Base) + stat(attacker.Champion.PerLevel) * StatCalculator.GrowthMultiplier(attacker.Level);

    public static double Bonus(ChampionState attacker, Func<StatSheet, double> stat) =>
        stat(attacker.Stats) - Base(attacker, stat);

    public static DamageCalculator Physical(ChampionState attacker, Entity target, double amount)
    {
        var calculator = DamageCalculator.Create(target)
            .AdDamage(amount)
            .Lethality(attacker.Items.Sum(i => i.Stats.ArmorPenetrationFlat))
            .AttackerItems(attacker.Items, attacker.Champion.IsRanged);

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
            .AttackerItems(attacker.Items, attacker.Champion.IsRanged);

        foreach (var item in attacker.Items.Where(i => i.Stats.MagicPenetrationPercent > 0))
        {
            calculator.MagicPenetration(item.Stats.MagicPenetrationPercent);
        }

        return calculator;
    }

    public static DamageCalculator? ForEffect(Effect effect, ChampionState attacker, Entity target)
    {
        var raw = RawAmount(effect, attacker, target) * (attacker.Champion.IsRanged ? effect.RangedMultiplier : 1)
                  * (1 + effect.MissingHealthAmp * (1 - target.HealthPercent));

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
            EffectKind.TrueDamage => DamageCalculator.Create(target).TrueDamage(raw).AttackerItems(attacker.Items, attacker.Champion.IsRanged),
            EffectKind.AdaptiveDamage => BonusAttackDamage(attacker) >= attacker.Stats.AbilityPower
                ? Physical(attacker, target, raw)
                : Magic(attacker, target, raw),
            _ => null,
        };
    }

    private static double RawAmount(Effect effect, ChampionState attacker, Entity target) =>
        OwnerAmount(effect, attacker)
        + effect.PerTargetMaxHealth * target.MaxHealth
        + effect.PerTargetCurrentHealth * target.CurrentHealth;

    public static double OwnerAmount(Effect effect, ChampionState attacker) =>
        effect.Amount
        + effect.PerLevel * Math.Max(0, attacker.Level - effect.PerLevelFrom + 1)
        + effect.PerLethality * attacker.Items.Sum(i => i.Stats.ArmorPenetrationFlat)
        + effect.PerCritChance * CritChance(attacker)
        + effect.PerBaseAd * BaseAttackDamage(attacker)
        + effect.PerTotalAd * attacker.Stats.AttackDamage
        + effect.PerBonusAd * BonusAttackDamage(attacker)
        + effect.PerAp * attacker.Stats.AbilityPower
        + effect.PerMaxHealth * attacker.Stats.Health
        + effect.PerBonusHealth * Bonus(attacker, s => s.Health)
        + effect.PerBonusArmor * Bonus(attacker, s => s.Armor)
        + effect.PerBonusMagicResist * Bonus(attacker, s => s.MagicResist);
}
