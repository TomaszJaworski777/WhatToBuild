using System.Collections.Concurrent;

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
    private readonly ConcurrentDictionary<string, ModelSettings.ObjectiveWeights> _weights = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Items in the core, not counting shoes. 1 to 5, three by default.</summary>
    public int CoreItems
    {
        get => _coreItems;
        set => _coreItems = Math.Clamp(value, MinItems, MaxItems);
    }

    /// <summary>
    /// Objective weights you chose, keyed like <c>objectives.champions</c> in model.json
    /// (<c>Kindred</c>, <c>Kayn/Darkin</c>); an entry not here plays the model's own weights.
    /// </summary>
    public IReadOnlyDictionary<string, ModelSettings.ObjectiveWeights> Weights =>
        new Dictionary<string, ModelSettings.ObjectiveWeights>(_weights, StringComparer.OrdinalIgnoreCase);

    public void SetWeights(string key, ModelSettings.ObjectiveWeights? weights)
    {
        if (weights is null)
        {
            _weights.TryRemove(key, out _);
        }
        else
        {
            _weights[key] = weights.Clamped();
        }
    }

    public string Key =>
        CoreItems + string.Concat(_weights.OrderBy(w => w.Key, StringComparer.OrdinalIgnoreCase).Select(w => $"|{w.Key}:{w.Value.Key}"));

    public string Label => $"{CoreItems} item{(CoreItems == 1 ? "" : "s")} + shoes";
}
