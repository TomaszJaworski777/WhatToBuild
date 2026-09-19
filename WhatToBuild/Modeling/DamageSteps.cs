namespace WhatToBuild.Modeling;

public sealed class AmplificationStep : IDamageStep
{
    public string Name => "Amplification";

    public void Apply(DamageContext context)
    {
        context.Damage *= 1 + context.Attacker.DamageAmp;
    }
}

public sealed class ResistanceStep : IDamageStep
{
    public string Name => "Resistance";

    public void Apply(DamageContext context)
    {
        context.EffectiveResist = EffectiveResist(context.Hit.Type, context.Attacker, context.Defender);
        context.Damage *= Multiplier(context.EffectiveResist);
    }

    public static double Multiplier(double resist) =>
        resist >= 0 ? 100 / (100 + resist) : 2 - 100 / (100 - resist);

    public static double EffectiveResist(DamageType type, Attacker attacker, Defender defender) => type switch
    {
        DamageType.Physical => ReduceThenPenetrate(
            defender.Armor,
            defender.ArmorReductionFlat,
            defender.ArmorReductionPercent,
            attacker.ArmorPenetrationPercent,
            attacker.Lethality),

        DamageType.Magic => ReduceThenPenetrate(
            defender.MagicResist,
            defender.MagicResistReductionFlat,
            defender.MagicResistReductionPercent,
            attacker.MagicPenetrationPercent,
            attacker.MagicPenetrationFlat),

        _ => 0,
    };

    private static double ReduceThenPenetrate(
        double resist,
        double flatReduction,
        double percentReduction,
        double percentPenetration,
        double flatPenetration)
    {
        resist -= flatReduction;

        if (resist > 0)
        {
            resist *= 1 - percentReduction;
        }

        if (resist > 0)
        {
            resist *= 1 - percentPenetration;
            resist = Math.Max(0, resist - flatPenetration);
        }

        return resist;
    }
}

public sealed class MitigationStep : IDamageStep
{
    public string Name => "Mitigation";

    public void Apply(DamageContext context)
    {
        var applicable = context.Defender.Mitigations
            .Where(m => context.Hit.Matches(m.Versus))
            .ToList();

        foreach (var mitigation in applicable)
        {
            context.Damage *= 1 - mitigation.Percent;
        }

        context.Damage = Math.Max(0, context.Damage - applicable.Sum(m => m.Flat));
    }
}

public sealed class ShieldStep : IDamageStep
{
    public string Name => "Shield";

    public void Apply(DamageContext context)
    {
        var usable = context.Defender.Shields
            .Where(s => context.Hit.Matches(s.Versus))
            .Sum(s => s.Amount) * (1 - context.Attacker.ShieldReduction);

        var absorbed = Math.Min(context.Damage, Math.Max(0, usable));

        context.ShieldAbsorbed = absorbed;
        context.Damage -= absorbed;
    }
}
