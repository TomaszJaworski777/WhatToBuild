namespace WhatToBuild.Data;

public class Item
{
    public Guid Id { get; set; }

    public int RiotId { get; set; }

    public string Name { get; set; } = "";

    public string Icon { get; set; } = "";

    public int Cost { get; set; }

    public List<Guid> BuildPath { get; set; } = new();

    public ItemStats Stats { get; set; } = new();

    public List<Effect> Effects { get; set; } = new();

    public override string ToString() => $"{Name} ({Cost}g)";
}
