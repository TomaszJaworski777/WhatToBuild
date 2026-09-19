using System.Globalization;
using WhatToBuild.Data;
using WhatToBuild.Modeling;
using WhatToBuild.Modeling.Simulation;

namespace WhatToBuild.Dtos;

public static class ItemDescriber
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private static readonly (Func<ItemStats, double> Get, string Label, bool Percent)[] StatFields =
    [
        (s => s.AttackDamage, "Attack Damage", false),
        (s => s.AbilityPower, "Ability Power", false),
        (s => s.AttackSpeedPercent, "Attack Speed", true),
        (s => s.CritChance, "Critical Strike Chance", true),
        (s => s.CritDamage, "Critical Strike Damage", true),
        (s => s.ArmorPenetrationFlat, "Lethality", false),
        (s => s.ArmorPenetrationPercent, "Armor Penetration", true),
        (s => s.MagicPenetrationFlat, "Magic Penetration", false),
        (s => s.MagicPenetrationPercent, "Magic Penetration", true),
        (s => s.Health, "Health", false),
        (s => s.Armor, "Armor", false),
        (s => s.MagicResist, "Magic Resist", false),
        (s => s.AbilityHaste, "Ability Haste", false),
        (s => s.LifeStealPercent, "Life Steal", true),
        (s => s.OmnivampPercent, "Omnivamp", true),
        (s => s.TenacityPercent, "Tenacity", true),
        (s => s.HealAndShieldPowerPercent, "Heal and Shield Power", true),
        (s => s.MoveSpeedFlat, "Move Speed", false),
        (s => s.MoveSpeedPercent, "Move Speed", true),
        (s => s.Mana, "Mana", false),
        (s => s.HealthRegen, "Health Regen", false),
        (s => s.ManaRegen, "Mana Regen", false),
        (s => s.BaseHealthRegenPercent, "Base Health Regen", true),
        (s => s.BaseManaRegenPercent, "Base Mana Regen", true),
        (s => s.AttackRange, "Attack Range", false),
    ];

    public static IReadOnlyList<string> StatLines(ItemStats stats) =>
        StatFields
            .Where(f => f.Get(stats) != 0)
            .Select(f => f.Percent ? $"+{Percent(f.Get(stats))} {f.Label}" : $"+{Number(f.Get(stats))} {f.Label}")
            .ToList();

    public static IReadOnlyList<string> EffectLines(IEnumerable<Effect> effects, ChampionState? owner = null) =>
        effects.Select(e => Describe(e, owner)).ToList();

    public static string Describe(Effect effect, ChampionState? owner = null)
    {
        var trigger = effect.EveryAttacks > 0 ? $"Every {Number(effect.EveryAttacks)} attacks" : Trigger(effect.Trigger);
        var text = $"{trigger}: {What(effect, owner)}";

        if (effect.Splash)
        {
            text += " to enemies around the target";
        }

        if (effect.Area)
        {
            text += " to the target and every enemy around it";
        }

        if (effect.ByCompanion)
        {
            text += ", dealt by the companion";
        }

        if (effect.Versus != DamageSource.All)
        {
            text += $" vs {Versus(effect.Versus)}";
        }

        if (effect.When.Count > 0)
        {
            text += $" when {string.Join(" and ", effect.When.Select(Condition))}";
        }

        if (effect.Cooldown > 0)
        {
            text += $" ({Number(effect.Cooldown)}s cooldown)";
        }

        if (effect.RangedMultiplier != 1 && owner is null)
        {
            text += $" (ranged: {Percent(effect.RangedMultiplier)})";
        }

        return text;
    }

    private static string What(Effect e, ChampionState? owner) => e.Kind switch
    {
        EffectKind.PhysicalDamage => $"{Magnitude(e, owner)} physical damage",
        EffectKind.MagicDamage => $"{Magnitude(e, owner)} magic damage",
        EffectKind.TrueDamage => $"{Magnitude(e, owner)} true damage",
        EffectKind.AdaptiveDamage => $"{Magnitude(e, owner)} adaptive damage",
        EffectKind.Heal => $"heal {Magnitude(e, owner)}",
        EffectKind.Shield => $"shield {Magnitude(e, owner)}",
        EffectKind.DamageReduction => e.Amount < 1 ? $"{Percent(e.Amount)} damage reduction" : $"{Number(e.Amount)} damage blocked",
        EffectKind.StatBuff => $"+{BuffText(e, owner)} {StatName(e.Stat)}",
        EffectKind.ArmorShred => $"-{Percent(e.Amount)} target armor",
        EffectKind.MagicResistShred => $"-{Percent(e.Amount)} target magic resist",
        EffectKind.GrievousWounds => $"{Percent(e.Amount)} Grievous Wounds",
        EffectKind.ShieldReduction => $"-{Percent(e.Amount)} enemy shields",
        EffectKind.Execute => $"execute below {Percent(e.Amount)} health",
        EffectKind.Revive => $"revive with {Percent(e.Amount)} health",
        _ => e.Kind.ToString(),
    };

    private static string Magnitude(Effect e, ChampionState? owner)
    {
        if (owner is not null)
        {
            return OwnerMagnitude(e, owner);
        }

        var parts = new List<string>();

        if (e.Amount != 0) parts.Add(Number(e.Amount));
        if (e.PerLevel != 0) parts.Add(e.PerLevelFrom > 2 ? $"{Number(e.PerLevel)} per level from {e.PerLevelFrom}" : $"{Number(e.PerLevel)} per level");
        if (e.PerLethality != 0) parts.Add($"{Percent(e.PerLethality)} lethality");
        if (e.PerCritChance != 0) parts.Add($"{Number(e.PerCritChance)} per 100% crit chance");
        if (e.PerBaseAd != 0) parts.Add($"{Percent(e.PerBaseAd)} base AD");
        if (e.PerTotalAd != 0) parts.Add($"{Percent(e.PerTotalAd)} AD");
        if (e.PerBonusAd != 0) parts.Add($"{Percent(e.PerBonusAd)} bonus AD");
        if (e.PerAp != 0) parts.Add($"{Percent(e.PerAp)} AP");
        if (e.PerMaxHealth != 0) parts.Add($"{Percent(e.PerMaxHealth)} max health");
        if (e.PerBonusHealth != 0) parts.Add($"{Percent(e.PerBonusHealth)} bonus health");
        if (e.PerBonusArmor != 0) parts.Add($"{Percent(e.PerBonusArmor)} bonus armor");
        if (e.PerBonusMagicResist != 0) parts.Add($"{Percent(e.PerBonusMagicResist)} bonus magic resist");
        if (e.PerTargetMaxHealth != 0) parts.Add($"{Percent(e.PerTargetMaxHealth)} target max health");
        if (e.PerTargetCurrentHealth != 0) parts.Add($"{Percent(e.PerTargetCurrentHealth)} target current health");

        return parts.Count == 0 ? "0" : string.Join(" + ", parts);
    }

    public static string? StackLine(Item item, ChampionState? owner)
    {
        if (item.Stacking is not { } s)
        {
            return null;
        }

        var gains = string.Join(", ", s.Gains.Select(g => $"+{GainText(g)} {StatName(g.Stat)}"));
        var ranged = owner?.Champion.IsRanged ?? false;
        var rate = s.StacksPerMinute * (ranged ? s.RangedMultiplier : 1);
        var text = $"Stacks: {gains} per {s.Per}, ~{Number(Math.Round(rate, 2))} per minute";

        if (s.RangedMultiplier != 1 && owner is null) text += $" ({Percent(s.RangedMultiplier)} when ranged)";
        if (s.Max > 0) text += $", max {Number(s.Max)}";
        if (owner is not null) text += $", ~{Number(Math.Round(owner.StacksOf(item)))} now";

        return text;
    }

    private static string GainText(StackGain g)
    {
        var amount = g.Stat.IsFraction ? Percent(g.Amount) : Number(g.Amount);
        if (g.PerMaxHealth != 0) amount += $" + {Percent(g.PerMaxHealth)} max health";
        return amount;
    }

    private static string BuffText(Effect e, ChampionState? owner)
    {
        string Format(double value) => e.Stat?.IsFraction == true ? Percent(value) : Number(Math.Round(value, 1));

        if (owner is not null)
        {
            return Format(StatCalculator.BuffAmount(e, AttackerHits.BaseAttackDamage(owner), owner.Items.Sum(i => i.Stats.Health)));
        }

        var parts = new List<string>();
        if (e.Amount != 0 || (e.PerBaseAd == 0 && e.PerBonusHealth == 0)) parts.Add(Format(e.Amount));
        if (e.PerBaseAd != 0) parts.Add($"{Percent(e.PerBaseAd)} base AD");
        if (e.PerBonusHealth != 0) parts.Add($"{Percent(e.PerBonusHealth)} bonus health");

        return string.Join(" + ", parts);
    }

    private static string OwnerMagnitude(Effect e, ChampionState owner)
    {
        var ranged = owner.Champion.IsRanged ? e.RangedMultiplier : 1;
        var flat = AttackerHits.OwnerAmount(e, owner) * ranged;
        var parts = new List<string>();

        if (Math.Round(flat) != 0 || (e.PerTargetMaxHealth == 0 && e.PerTargetCurrentHealth == 0)) parts.Add(Number(Math.Round(flat)));
        if (e.PerTargetMaxHealth != 0) parts.Add($"{Percent(e.PerTargetMaxHealth * ranged)} target max health");
        if (e.PerTargetCurrentHealth != 0) parts.Add($"{Percent(e.PerTargetCurrentHealth * ranged)} target current health");

        return string.Join(" + ", parts);
    }

    private static string Trigger(EffectTrigger trigger) => trigger switch
    {
        EffectTrigger.Always => "Passive",
        EffectTrigger.OnAttack => "On attack",
        EffectTrigger.OnAbility => "On ability",
        EffectTrigger.InCombat => "In combat",
        EffectTrigger.WhenLow => "When low",
        EffectTrigger.OnTakedown => "On takedown",
        EffectTrigger.OutOfCombat => "Out of combat",
        _ => trigger.ToString(),
    };

    private static string Versus(DamageSource source) => source switch
    {
        DamageSource.Attacks => "attacks",
        DamageSource.Abilities => "abilities",
        DamageSource.Crit => "critical strikes",
        DamageSource.Physical => "physical damage",
        DamageSource.Magic => "magic damage",
        _ => "all damage",
    };

    private static string Condition(EffectCondition c)
    {
        if (c.Property == ConditionProperty.IsMonster)
        {
            return "the target is a monster";
        }

        var who = c.Subject == ConditionSubject.Target ? "target" : "you";
        var op = c.Op == ConditionOp.AtLeast ? "≥" : "≤";
        var value = c.Property == ConditionProperty.HealthPercent ? Percent(c.Value) : Number(c.Value);

        var property = c.Property switch
        {
            ConditionProperty.HealthPercent => "health",
            ConditionProperty.BonusHealth => "bonus health",
            ConditionProperty.MaxHealth => "max health",
            ConditionProperty.MagicResist => "magic resist",
            _ => c.Property.ToString().ToLowerInvariant(),
        };

        return $"{who} {property} {op} {value}";
    }

    private static string StatName(Stat? stat) => stat?.Name switch
    {
        null => "stat",
        "attackDamage" => "Attack Damage",
        "abilityPower" => "Ability Power",
        "attackSpeedPercent" => "Attack Speed",
        "critChance" => "Critical Strike Chance",
        "omnivampPercent" => "Omnivamp",
        "damageAmp" => "damage",
        "abilityPowerAmp" => "Ability Power",
        "healAndShieldPowerPercent" => "Heal and Shield Power",
        "armorPenetrationPercent" => "Armor Penetration",
        "enemyAttackSpeedPercent" => "enemy Attack Speed",
        "enemyMagicDamageAmp" => "magic damage taken by enemies",
        "health" => "Health",
        "mana" => "Mana",
        "gold" => "gold",
        "armor" => "Armor",
        "magicResist" => "Magic Resist",
        "attackSpeed" => "Attack Speed",
        "abilityHaste" => "Ability Haste",
        var other => other!,
    };

    private static string Number(double value) =>
        Math.Round(value, 2).ToString("0.##", Invariant);

    private static string Percent(double fraction) =>
        (fraction * 100).ToString("0.#", Invariant) + "%";
}
