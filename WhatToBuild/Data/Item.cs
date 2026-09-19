namespace WhatToBuild.Data;

public class Item
{
    public Guid Id { get; set; }

    public int RiotId { get; set; }

    public string Name { get; set; } = "";

    public string Icon { get; set; } = "";

    public int Cost { get; set; }

    public bool Unique { get; set; }

    public List<string> Groups { get; set; } = new();

    public List<Guid> BuildPath { get; set; } = new();

    public ItemStats Stats { get; set; } = new();

    public List<Effect> Effects { get; set; } = new();

    public ItemStacking? Stacking { get; set; }

    public override string ToString() => $"{Name} ({Cost}g)";
}

public class StackGain
{
    public Stat Stat { get; set; } = Stats.Health;

    public double Amount { get; set; }

    public double PerMaxHealth { get; set; }
}

public class ItemStacking
{
    public string Per { get; set; } = "";

    public List<StackGain> Gains { get; set; } = new();

    public double Max { get; set; }

    public double RangedMultiplier { get; set; } = 1;

    public double StacksPerMinute { get; set; }

    public string RateSource { get; set; } = "";

    public double StacksAfter(double? minutesOwned, bool ranged)
    {
        var stacks = StacksPerMinute * (ranged ? RangedMultiplier : 1) * Math.Max(0, minutesOwned ?? 0);
        return Max > 0 ? Math.Min(Max, stacks) : stacks;
    }
}
