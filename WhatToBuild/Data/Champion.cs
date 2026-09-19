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

public class StackStep
{
    public double Stacks { get; set; }

    public double Bonus { get; set; }
}

public sealed record StackReadout(double Low, double? High);

public class StackReading
{
    public const double Tolerance = 1;

    public Stat Stat { get; set; } = Stats.AttackRange;

    public List<StackStep> Steps { get; set; } = new();

    public StackReadout? Read(double bonus)
    {
        var steps = Steps.OrderBy(s => s.Stacks).ToList();

        if (steps.Count == 0)
        {
            return null;
        }

        if (Math.Abs(bonus) <= Tolerance)
        {
            return new StackReadout(0, steps[0].Stacks - 1);
        }

        for (var i = 0; i < steps.Count; i++)
        {
            if (Math.Abs(bonus - steps[i].Bonus) <= Tolerance)
            {
                return new StackReadout(steps[i].Stacks, i + 1 < steps.Count ? steps[i + 1].Stacks - 1 : null);
            }
        }

        return null;
    }
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

    public double Tag(string name) => Tags.GetValueOrDefault(name);

    public bool IsRanged => Tag("ranged") > 0;

    public override string ToString() => Name;
}
