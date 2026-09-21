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

public class Effect
{
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

