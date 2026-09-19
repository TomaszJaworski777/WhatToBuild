namespace WhatToBuild.Data;

public class StatSheet
{
    public double Health { get; set; }
    public double Mana { get; set; }
    public double AttackDamage { get; set; }
    public double AbilityPower { get; set; }
    public double Armor { get; set; }
    public double MagicResist { get; set; }
    public double AttackSpeed { get; set; }
    public double HealthRegen { get; set; }
    public double ManaRegen { get; set; }
    public double MoveSpeed { get; set; }
    public double AttackRange { get; set; }
    public double Tenacity { get; set; }
}

public class ChampionStacking
{
    public Stat Stat { get; set; } = Stats.AttackDamage;

    public double InitialStacksPerMinute { get; set; }

    public double Max { get; set; }
}

public sealed record StackReadout(double Low, double? High);

public class StackReading
{
    public const double Tolerance = 1;

    public Stat Stat { get; set; } = Stats.AttackRange;

    public double FirstStacks { get; set; }

    public double FirstBonus { get; set; }

    public double StepStacks { get; set; }

    public double StepBonus { get; set; }

    public StackReadout? Read(double bonus)
    {
        if (Math.Abs(bonus) <= Tolerance)
        {
            return new StackReadout(0, FirstStacks - 1);
        }

        if (StepBonus <= 0 || bonus < FirstBonus - Tolerance)
        {
            return null;
        }

        var steps = Math.Round((bonus - FirstBonus) / StepBonus);
        if (Math.Abs(FirstBonus + steps * StepBonus - bonus) > Tolerance)
        {
            return null;
        }

        var low = FirstStacks + steps * StepStacks;
        return new StackReadout(low, low + StepStacks - 1);
    }
}

public class CombatBuff
{
    public Stat Stat { get; set; } = Stats.AttackSpeedPercent;

    public double Amount { get; set; }

    public double PerStack { get; set; }

    public double At(double stacks) => Amount + PerStack * stacks;
}

public class Champion
{
    public Guid Id { get; set; }

    public int RiotId { get; set; }

    public string InternalName { get; set; } = "";

    public string Name { get; set; } = "";

    public string Icon { get; set; } = "";

    public double AttackSpeedRatio { get; set; }

    public StatSheet Base { get; set; } = new();

    public StatSheet PerLevel { get; set; } = new();

    public Dictionary<string, double> Tags { get; set; } = new();

    public List<ChampionStacking> Stacking { get; set; } = new();

    public StackReading? StackReading { get; set; }

    public List<CombatBuff> CombatBuffs { get; set; } = new();

    public double Tag(string name) => Tags.GetValueOrDefault(name);

    public bool IsRanged => Tag("ranged") > 0;

    public override string ToString() => Name;
}
