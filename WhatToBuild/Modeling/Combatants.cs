using WhatToBuild.Data;

namespace WhatToBuild.Modeling;

public sealed record Shield(double Amount, DamageSource Versus = DamageSource.All);

public sealed record Mitigation(DamageSource Versus, double Percent = 0, double Flat = 0);

public sealed class Attacker
{
    public double ArmorPenetrationPercent { get; init; }

    public double Lethality { get; init; }

    public double MagicPenetrationPercent { get; init; }

    public double MagicPenetrationFlat { get; init; }

    public double DamageAmp { get; init; }

    public double ShieldReduction { get; init; }
}

public sealed class Defender
{
    public double Armor { get; init; }

    public double MagicResist { get; init; }

    public double ArmorReductionFlat { get; init; }

    public double ArmorReductionPercent { get; init; }

    public double MagicResistReductionFlat { get; init; }

    public double MagicResistReductionPercent { get; init; }

    public IReadOnlyList<Mitigation> Mitigations { get; init; } = [];

    public IReadOnlyList<Shield> Shields { get; init; } = [];
}
