namespace WhatToBuild.Data;

public class Item
{
    public Guid Id { get; set; }

    /// <summary>Riot's item id. Used to match live game inventory and to refresh stats.</summary>
    public int RiotId { get; set; }

    public string Name { get; set; } = "";

    public string Icon { get; set; } = "";

    /// <summary>Total gold, components included.</summary>
    public int Cost { get; set; }

    public List<Guid> BuildPath { get; set; } = new();

    public ItemStats Stats { get; set; } = new();

    public List<ItemEffect> Effects { get; set; } = new();

    public override string ToString() => $"{Name} ({Cost}g)";
}
