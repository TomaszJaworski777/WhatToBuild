namespace WhatToBuild.Data;

public enum EffectTrigger
{
    Always,

    OnAttack,

    OnAbility,

    OnUltimate,

    InCombat,

    WhenLow,

    OnTakedown,

    OutOfCombat,
}

public enum DamageSource
{
    All,
    Attacks,
    Abilities,
    Crit,
    Physical,
    Magic,
}

public enum ConditionSubject
{
    Self,
    Target,
}

public enum ConditionProperty
{
    HealthPercent,
    BonusHealth,
    MaxHealth,
    Armor,
    MagicResist,
    Level,
    IsMonster,
}

public enum ConditionOp
{
    AtLeast,
    AtMost,
}

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
    AdaptiveDamage,
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
/// An effect that grows with something about its subject rather than switching on at a threshold:
/// none of it at <see cref="From"/>, all of it at <see cref="To"/>, in a straight line between
/// (Lord Dominik's Giant Slayer: more bonus damage the more bonus health the target has).
/// </summary>
public class EffectScale
{
    public ConditionSubject Subject { get; set; } = ConditionSubject.Target;

    public ConditionProperty Property { get; set; }

    public double From { get; set; }

    public double To { get; set; }

    /// <summary>How much of the effect applies at this measure, from 0 to 1.</summary>
    public double Share(double measured) =>
        To <= From ? (measured >= To ? 1 : 0) : Math.Clamp((measured - From) / (To - From), 0, 1);
}

public class Effect
{
    /// <summary>When set, only this share of <see cref="Amount"/> applies; see <see cref="EffectScale"/>.</summary>
    public EffectScale? ScalesWith { get; set; }

    public EffectTrigger Trigger { get; set; }

    public EffectKind Kind { get; set; }

    public double Amount { get; set; }

    public double Cooldown { get; set; }

    public Stat? Stat { get; set; }

    public DamageSource Versus { get; set; } = DamageSource.All;

    public List<EffectCondition> When { get; set; } = new();

    public double EveryAttacks { get; set; }

    public bool Splash { get; set; }

    public bool Area { get; set; }

    public bool ByCompanion { get; set; }

    public double RangedMultiplier { get; set; } = 1;

    /// <summary>What a melee owner gets of this, where an item treats the two differently.</summary>
    public double MeleeMultiplier { get; set; } = 1;

    /// <summary>The share of this effect its owner actually gets.</summary>
    public double ShareFor(bool ranged) => ranged ? RangedMultiplier : MeleeMultiplier;

    public double PerLevel { get; set; }

    public int PerLevelFrom { get; set; } = 2;

    public double PerBaseAd { get; set; }
    public double PerTotalAd { get; set; }
    public double PerBonusAd { get; set; }
    public double PerAp { get; set; }
    public double PerMaxHealth { get; set; }
    public double PerBonusHealth { get; set; }
    public double PerBonusArmor { get; set; }
    public double PerBonusMagicResist { get; set; }
    public double PerLethality { get; set; }
    public double PerCritChance { get; set; }
    public double PerTargetMaxHealth { get; set; }
    public double PerTargetCurrentHealth { get; set; }

    public double MissingHealthAmp { get; set; }

    public int StacksTo { get; set; }

    public double PerStack { get; set; }

    public double Duration { get; set; }
}

