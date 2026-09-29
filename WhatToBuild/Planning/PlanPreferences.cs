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

    /// <summary>The most steps a burst sequence you write can have.</summary>
    public const int MaxBurstSteps = 12;

    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _burstCombos = new(StringComparer.OrdinalIgnoreCase);
    private volatile IReadOnlySet<string> _burstExcluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private string? _game;

    /// <summary>The burst sequence you chose for a champion, by name; none plays the kit's own.</summary>
    public IReadOnlyList<string>? BurstCombo(string champion) => _burstCombos.GetValueOrDefault(champion);

    public void SetBurstCombo(string champion, IReadOnlyList<string>? steps)
    {
        if (steps is not { Count: > 0 })
        {
            _burstCombos.TryRemove(champion, out _);
        }
        else
        {
            _burstCombos[champion] = steps.Take(MaxBurstSteps).ToList();
        }
    }

    /// <summary>Enemies (by champion name) the burst leaves out. They belong to one game: a new lobby clears them.</summary>
    public IReadOnlySet<string> BurstExcluded
    {
        get => _burstExcluded;
        set => _burstExcluded = new HashSet<string>(value, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The game being planned; when it is a new one, the enemies left out of the last one are let back in.</summary>
    public void StartGame(string game)
    {
        if (_game is not null && _game != game)
        {
            BurstExcluded = new HashSet<string>();
        }

        _game = game;
    }

    public string Key =>
        CoreItems + string.Concat(_weights.OrderBy(w => w.Key, StringComparer.OrdinalIgnoreCase).Select(w => $"|{w.Key}:{w.Value.Key}"))
        + string.Concat(_burstCombos.OrderBy(c => c.Key, StringComparer.OrdinalIgnoreCase).Select(c => $"|{c.Key}>{string.Join(',', c.Value)}"))
        + (_burstExcluded.Count > 0 ? "|-" + string.Join(',', _burstExcluded.Order(StringComparer.OrdinalIgnoreCase)) : "");

    public string Label => $"{CoreItems} item{(CoreItems == 1 ? "" : "s")} + shoes";
}
