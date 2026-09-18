namespace WhatToBuild.Data;

public enum EffectTrigger
{
    Always,

    OnAttack,

    OnAbility,

    InCombat,

    WhenLow,

    OnTakedown,
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

public class ItemEffect
{
    public EffectTrigger Trigger { get; set; }

    public EffectKind Kind { get; set; }

    public double Amount { get; set; }

    public double Cooldown { get; set; }

    public Stat? Stat { get; set; }

    public DamageSource Versus { get; set; } = DamageSource.All;

    public List<EffectCondition> When { get; set; } = new();

    public double PerBaseAd { get; set; }
    public double PerTotalAd { get; set; }
    public double PerAp { get; set; }
    public double PerMaxHealth { get; set; }
    public double PerTargetMaxHealth { get; set; }
    public double PerTargetCurrentHealth { get; set; }
}
