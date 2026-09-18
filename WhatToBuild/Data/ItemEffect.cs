namespace WhatToBuild.Data;

/// <summary>Roughly when an effect happens. See GameData/README.md.</summary>
public enum EffectTrigger
{
    /// <summary>Permanently on.</summary>
    Always,

    /// <summary>Happens through attacking, rate-limited by <see cref="ItemEffect.Cooldown"/>.</summary>
    OnAttack,

    /// <summary>Happens through casting abilities.</summary>
    OnAbility,

    /// <summary>Repeats while fighting, every <see cref="ItemEffect.Cooldown"/> seconds.</summary>
    InCombat,

    /// <summary>Triggers when you drop low on health.</summary>
    WhenLow,

    /// <summary>Triggers on a kill or assist.</summary>
    OnTakedown,
}

/// <summary>
/// What a mitigation or shield applies to. Randuin's 30% only counts against
/// critical strikes, and Maw's shield only absorbs magic damage; treating either as
/// blanket mitigation would overrate them against the comps they are not bought for.
/// </summary>
public enum DamageSource
{
    All,
    Attacks,
    Abilities,
    Crit,
    Physical,
    Magic,
}

/// <summary>Whose state a condition looks at.</summary>
public enum ConditionSubject
{
    Self,
    Target,
}

/// <summary>What a condition measures. Health fractions are 0-1; the rest are game units.</summary>
public enum ConditionProperty
{
    HealthPercent,
    BonusHealth,
    MaxHealth,
    Armor,
    MagicResist,
    Level,
}

public enum ConditionOp
{
    AtLeast,
    AtMost,
}

/// <summary>
/// One requirement for an effect to count. Lord Dominik's is only worth its 15%
/// against something that actually stacked health, so the evaluator has to be able
/// to ask that question rather than assume the bonus always applies.
/// </summary>
public class EffectCondition
{
    public ConditionSubject Subject { get; set; }

    public ConditionProperty Property { get; set; }

    public ConditionOp Op { get; set; }

    public double Value { get; set; }

    public bool IsMet(double measured) =>
        Op == ConditionOp.AtLeast ? measured >= Value : measured <= Value;
}

public enum EffectKind
{
    PhysicalDamage,
    MagicDamage,
    TrueDamage,
    Heal,
    Shield,
    DamageReduction,
    StatBuff,
    ArmorShred,
    MagicResistShred,
    GrievousWounds,
    ShieldReduction,
    Execute,
    Revive,
}

/// <summary>
/// A ballpark of what an item does: how much, and how often. Proc mechanics are
/// deliberately not modelled - "every third attack" becomes a cooldown in seconds.
/// </summary>
public class ItemEffect
{
    public EffectTrigger Trigger { get; set; }

    public EffectKind Kind { get; set; }

    /// <summary>
    /// Flat magnitude per occurrence. Damage, heal and shield are game units;
    /// <see cref="EffectKind.DamageReduction"/>, <see cref="EffectKind.GrievousWounds"/>,
    /// <see cref="EffectKind.ArmorShred"/>, <see cref="EffectKind.MagicResistShred"/>,
    /// <see cref="EffectKind.ShieldReduction"/> and <see cref="EffectKind.Execute"/>
    /// are fractions.
    /// </summary>
    public double Amount { get; set; }

    /// <summary>Seconds between occurrences. 0 means every time the trigger happens.</summary>
    public double Cooldown { get; set; }

    /// <summary>For StatBuff and shreds: which stat, by <see cref="ItemStats"/> field name.</summary>
    public string? Stat { get; set; }

    /// <summary>
    /// For DamageReduction and Shield: what it applies to. Defaults to everything,
    /// which is right for most items and wrong for the ones that matter most.
    /// </summary>
    public DamageSource Versus { get; set; } = DamageSource.All;

    /// <summary>
    /// Requirements that must all hold for this effect to count. Empty means it
    /// always applies.
    /// </summary>
    public List<EffectCondition> When { get; set; } = new();

    // Scaling. All default to 0, so anything absent simply contributes nothing.

    public double PerBaseAd { get; set; }
    public double PerTotalAd { get; set; }
    public double PerAp { get; set; }
    public double PerMaxHealth { get; set; }
    public double PerTargetMaxHealth { get; set; }
    public double PerTargetCurrentHealth { get; set; }
}
