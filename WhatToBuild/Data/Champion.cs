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
}

public class ChampionStacking
{
    public Stat Stat { get; set; } = Stats.AttackDamage;

    public double InitialStacksPerMinute { get; set; }

    public double Max { get; set; }
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

    public double Tag(string name) => Tags.GetValueOrDefault(name);

    public bool IsRanged => Tag("ranged") > 0;

    public override string ToString() => Name;
}
