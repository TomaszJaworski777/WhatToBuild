using WhatToBuild.Data;

namespace WhatToBuild.Modeling;

public static class StatCalculator
{
    public const int MinLevel = 1;
    public const int MaxLevel = 18;
    public const double AttackSpeedCap = 2.5;

    public static double GrowthMultiplier(int level)
    {
        var steps = Math.Clamp(level, MinLevel, MaxLevel) - 1;
        return steps * (0.7025 + 0.0175 * steps);
    }

    public static StatSheet ForChampion(
        Champion champion,
        int level,
        IEnumerable<Item> items,
        IEnumerable<StatModifier>? modifiers = null)
    {
        var growth = GrowthMultiplier(level);
        var b = champion.Base;
        var p = champion.PerLevel;
        var itemList = items.ToList();
        var modifierList = (modifiers ?? []).ToList();

        var bonusAttackSpeed = p.AttackSpeed * growth
                               + itemList.Sum(i => i.Stats.AttackSpeedPercent)
                               + itemList.SelectMany(i => i.Effects).Where(e => IsPermanentStatBuff(e) && e.Stat == Stats.AttackSpeedPercent).Sum(e => e.Amount)
                               + modifierList.Where(m => m.Stat == Stats.AttackSpeedPercent).Sum(m => m.Flat);

        var sheet = new StatSheet
        {
            Health = b.Health + p.Health * growth,
            Mana = b.Mana + p.Mana * growth,
            AttackDamage = b.AttackDamage + p.AttackDamage * growth,
            AbilityPower = b.AbilityPower,
            Armor = b.Armor + p.Armor * growth,
            MagicResist = b.MagicResist + p.MagicResist * growth,
            AttackSpeed = Math.Min(AttackSpeedCap, b.AttackSpeed + champion.AttackSpeedRatio * bonusAttackSpeed),
            HealthRegen = b.HealthRegen + p.HealthRegen * growth,
            ManaRegen = b.ManaRegen + p.ManaRegen * growth,
            MoveSpeed = b.MoveSpeed,
            AttackRange = b.AttackRange,
        };

        foreach (var item in itemList)
        {
            sheet.Health += item.Stats.Health;
            sheet.Mana += item.Stats.Mana;
            sheet.AttackDamage += item.Stats.AttackDamage;
            sheet.AbilityPower += item.Stats.AbilityPower;
            sheet.Armor += item.Stats.Armor;
            sheet.MagicResist += item.Stats.MagicResist;
            sheet.AttackRange += item.Stats.AttackRange;
            sheet.AbilityHaste += item.Stats.AbilityHaste;

            foreach (var effect in item.Effects.Where(IsPermanentStatBuff))
            {
                Add(sheet, effect.Stat!, effect.Amount);
            }
        }

        foreach (var modifier in modifierList)
        {
            Add(sheet, modifier.Stat, modifier.Flat);
        }

        foreach (var group in modifierList.Where(m => m.Percent != 0).GroupBy(m => m.Stat))
        {
            Scale(sheet, group.Key, 1 + group.Sum(m => m.Percent));
        }

        sheet.Tenacity = StackMultiplicatively(TenacitySources(itemList, modifierList));

        return sheet;
    }

    public static StatSheet Adjustment(StatSheet observed, StatSheet rebuilt) => new()
    {
        Health = observed.Health - rebuilt.Health,
        AttackDamage = observed.AttackDamage - rebuilt.AttackDamage,
        AbilityPower = observed.AbilityPower - rebuilt.AbilityPower,
        Armor = observed.Armor - rebuilt.Armor,
        MagicResist = observed.MagicResist - rebuilt.MagicResist,
        AbilityHaste = observed.AbilityHaste - rebuilt.AbilityHaste,
    };

    public static void Apply(StatSheet sheet, StatSheet? adjustment)
    {
        if (adjustment is null)
        {
            return;
        }

        sheet.Health += adjustment.Health;
        sheet.AttackDamage += adjustment.AttackDamage;
        sheet.AbilityPower += adjustment.AbilityPower;
        sheet.Armor += adjustment.Armor;
        sheet.MagicResist += adjustment.MagicResist;
        sheet.AbilityHaste += adjustment.AbilityHaste;
    }

    public static double StackMultiplicatively(IEnumerable<double> sources) =>
        1 - sources.Aggregate(1.0, (remaining, source) => remaining * (1 - source));

    private static IEnumerable<double> TenacitySources(List<Item> items, List<StatModifier> modifiers)
    {
        foreach (var item in items)
        {
            if (item.Stats.TenacityPercent > 0)
            {
                yield return item.Stats.TenacityPercent;
            }

            foreach (var effect in item.Effects.Where(e => IsPermanentStatBuff(e) && e.Stat == Stats.TenacityPercent))
            {
                yield return effect.Amount;
            }
        }

        foreach (var modifier in modifiers.Where(m => m.Stat == Stats.TenacityPercent && m.Flat > 0))
        {
            yield return modifier.Flat;
        }
    }

    public static StatSheet ForNeutral(Neutral neutral, NeutralScaling scaling, double gameTimeSeconds)
    {
        var multiplier = scaling.MultiplierAt(gameTimeSeconds);
        var b = neutral.Base;

        return new StatSheet
        {
            Health = b.Health * multiplier,
            AttackDamage = b.AttackDamage * multiplier,
            Armor = b.Armor,
            MagicResist = b.MagicResist,
            AttackSpeed = b.AttackSpeed,
            MoveSpeed = b.MoveSpeed,
            AttackRange = b.AttackRange,
        };
    }

    public static bool IsPermanentStatBuff(Effect effect) =>
        effect.Kind == EffectKind.StatBuff
        && effect.Trigger == EffectTrigger.Always
        && effect.When.Count == 0
        && effect.Stat is not null;

    private static void Scale(StatSheet sheet, Stat stat, double factor)
    {
        if (stat == Stats.Health) sheet.Health *= factor;
        else if (stat == Stats.Armor) sheet.Armor *= factor;
        else if (stat == Stats.MagicResist) sheet.MagicResist *= factor;
        else if (stat == Stats.AttackDamage) sheet.AttackDamage *= factor;
        else if (stat == Stats.AbilityPower) sheet.AbilityPower *= factor;
    }

    private static void Add(StatSheet sheet, Stat stat, double amount)
    {
        if (stat == Stats.Health) sheet.Health += amount;
        else if (stat == Stats.Armor) sheet.Armor += amount;
        else if (stat == Stats.MagicResist) sheet.MagicResist += amount;
        else if (stat == Stats.AttackDamage) sheet.AttackDamage += amount;
        else if (stat == Stats.AbilityPower) sheet.AbilityPower += amount;
        else if (stat == Stats.AbilityHaste) sheet.AbilityHaste += amount;
    }
}
