using WhatToBuild.Data;

namespace WhatToBuild.Modeling;

public enum DamageType
{
    Physical,
    Magic,
    True,
}

[Flags]
public enum HitFlags
{
    None = 0,
    Attack = 1,
    Ability = 2,
    Crit = 4,
}

public sealed record Hit(DamageType Type, double Raw, HitFlags Flags = HitFlags.None)
{
    public bool IsAttack => Flags.HasFlag(HitFlags.Attack);

    public bool IsAbility => Flags.HasFlag(HitFlags.Ability);

    public bool IsCrit => Flags.HasFlag(HitFlags.Crit);

    public bool Matches(DamageSource source) => source switch
    {
        DamageSource.All => true,
        DamageSource.Attacks => IsAttack,
        DamageSource.Abilities => IsAbility,
        DamageSource.Crit => IsCrit,
        DamageSource.Physical => Type == DamageType.Physical,
        DamageSource.Magic => Type == DamageType.Magic,
        _ => false,
    };

    public static Hit Attack(double raw, bool crit = false) =>
        new(DamageType.Physical, raw, HitFlags.Attack | (crit ? HitFlags.Crit : HitFlags.None));

    public static Hit Ability(DamageType type, double raw) =>
        new(type, raw, HitFlags.Ability);
}
