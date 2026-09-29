using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using WhatToBuild.Data;
using WhatToBuild.Modeling.Simulation;

namespace WhatToBuild.Planning;

/// <summary>
/// Everything an evaluator works out that holds for as long as the game looks the same: the
/// battlefields, the evaluations, and the fights under them (yours against each enemy, phase by
/// phase, and each enemy's against you). One cache serves every evaluator made while its
/// <see cref="Key"/> holds, so a re-plan, a deeper search and the second-by-second refresh all
/// start from what the last one already paid for.
/// <para>
/// Times are snapped to a grid anchored at <see cref="Anchor"/>, the moment the cache was made,
/// not at each evaluator's own now; otherwise the grid slid every second and nothing matched.
/// </para>
/// </summary>
public sealed class EvaluationCache
{
    public EvaluationCache(string key, double anchor)
    {
        Key = key;
        Anchor = anchor;
    }

    /// <summary>What the cached results depend on: the game as the planner sees it, your settings, and the time bucket.</summary>
    public string Key { get; }

    /// <summary>The game time the snapping grid is anchored at.</summary>
    public double Anchor { get; }

    internal ConcurrentDictionary<string, Lazy<Battlefield>> Battlefields { get; } = new();

    internal ConcurrentDictionary<string, Lazy<Evaluation>> Evaluations { get; } = new();

    /// <summary>Your fight against one enemy in one attack phase: the same whichever search mode asks for it.</summary>
    internal ConcurrentDictionary<string, Lazy<FightResult>> Fights { get; } = new();

    /// <summary>One enemy's damage on you: it does not depend on the search mode at all.</summary>
    internal ConcurrentDictionary<string, object> Incoming { get; } = new();

    /// <summary>Walked build orders, by evaluator and order prefix: see <see cref="BuildPlanner"/>.</summary>
    internal ConditionalWeakTable<BuildEvaluator, Dictionary<string, object?>> Walks { get; } = new();

    public int EvaluationCount => Evaluations.Count;

    /// <summary>
    /// The key for a moment of the game: every player's champion, level, items, takedowns and
    /// pace, the objectives (the trends signature), the choices on the page, your ranks and
    /// measured stats, and which <paramref name="bucketSeconds"/> window of game time it is in.
    /// Buying anything, a level, a kill or a dragon makes a new key and so a fresh cache.
    /// </summary>
    public static string KeyFor(BuildContext context, string preferences, double bucketSeconds)
    {
        var key = new StringBuilder()
            .Append(context.Forecaster.Trends.Signature).Append('#')
            .Append(preferences).Append('#')
            .Append(context.State.ActivePlayerRanks?.ToString()).Append('#')
            .Append(context.DetectedForm).Append('#')
            .Append(string.Join(',', context.Trinkets.Select(i => i.RiotId))).Append('#')
            .Append(Sheet(context.Adjustment)).Append('#')
            .Append(Math.Floor(context.Now / Math.Max(1, bucketSeconds)).ToString(CultureInfo.InvariantCulture));

        return key.ToString();
    }

    private static string Sheet(StatSheet? sheet) =>
        sheet is null
            ? ""
            : string.Join(',', typeof(StatSheet).GetProperties()
                .Where(p => p.PropertyType == typeof(double))
                .Select(p => ((double)p.GetValue(sheet)!).ToString("0.#", CultureInfo.InvariantCulture)));
}
