namespace WhatToBuild.Planning;

/// <summary>
/// How big a build to aim at, as one knob you can move while playing. The plan works out the
/// best core of this many items plus shoes and builds toward it as a package; once you own it,
/// there is nothing left to save for, so every later purchase is simply the best item at the
/// moment you buy it.
/// </summary>
public sealed class PlanPreferences
{
    public const int MinItems = 1;
    public const int MaxItems = 5;

    private int _coreItems = 3;

    /// <summary>Items in the core, not counting shoes. 1 to 5, three by default.</summary>
    public int CoreItems
    {
        get => _coreItems;
        set => _coreItems = Math.Clamp(value, MinItems, MaxItems);
    }

    public string Key => CoreItems.ToString();

    public string Label => $"{CoreItems} item{(CoreItems == 1 ? "" : "s")} + shoes";
}
