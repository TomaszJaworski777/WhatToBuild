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

    public static StatSheet ForChampion(Champion champion, int level, IEnumerable<Item> items)
    {
        var growth = GrowthMultiplier(level);
        var b = champion.Base;
        var p = champion.PerLevel;
        var itemList = items.ToList();

        var bonusAttackSpeed = p.AttackSpeed * growth + itemList.Sum(i => i.Stats.AttackSpeedPercent);

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

            foreach (var effect in item.Effects.Where(IsPermanentStatBuff))
            {
                Add(sheet, effect.Stat!, effect.Amount);
            }
        }

        return sheet;
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

    private static bool IsPermanentStatBuff(Effect effect) =>
        effect.Kind == EffectKind.StatBuff
        && effect.Trigger == EffectTrigger.Always
        && effect.When.Count == 0
        && effect.Stat is not null;

    private static void Add(StatSheet sheet, Stat stat, double amount)
    {
        if (stat == Stats.Health) sheet.Health += amount;
        else if (stat == Stats.Armor) sheet.Armor += amount;
        else if (stat == Stats.MagicResist) sheet.MagicResist += amount;
        else if (stat == Stats.AttackDamage) sheet.AttackDamage += amount;
        else if (stat == Stats.AbilityPower) sheet.AbilityPower += amount;
    }
}
