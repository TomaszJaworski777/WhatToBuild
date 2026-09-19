using WhatToBuild.Data;
using WhatToBuild.Modeling.Simulation;
using WhatToBuild.SupportedChampions.Kindred;

namespace WhatToBuild.SupportedChampions;

public sealed record CastAddition(string When, double Damage);

/// <summary>
/// An ability that keeps you alive in a fight: for <see cref="UndyingSeconds"/> you cannot drop below
/// <see cref="MinimumHealthPercent"/>, then you heal for <see cref="Heal"/>. Kindred's Lamb's Respite.
/// </summary>
public sealed record SurvivalAbility(string Name, double UndyingSeconds, double MinimumHealthPercent, double Heal, double Cooldown);

public sealed record CastHint(string Ability, double CastAtHealth, double KillingHealth, IReadOnlyList<CastAddition> Additions);

public interface ISupportedChampion
{
    string ChampionName { get; }

    IReadOnlyList<string> SkillOrder { get; }

    IChampionKit NewFight();

    IReadOnlyList<CastHint> Hints(FightSetup setup);

    /// <summary>The ability that stops you dying in a fight at these ranks, if the champion has one.</summary>
    SurvivalAbility? Survival(AbilityRanks ranks) => null;
}

public sealed class ChampionKits
{
    public const string FolderName = "Kits";

    private readonly Dictionary<string, ISupportedChampion> _byName;

    public ChampionKits(IEnumerable<ISupportedChampion> supported)
    {
        _byName = supported.ToDictionary(s => s.ChampionName, StringComparer.OrdinalIgnoreCase);
    }

    public static ChampionKits None { get; } = new([]);

    public static ChampionKits Load(string dataRoot)
    {
        var folder = Path.Combine(dataRoot, FolderName);

        return new ChampionKits(
        [
            new KindredChampion(KindredKitData.Load(Path.Combine(folder, KindredKitData.FileName))),
        ]);
    }

    public IReadOnlyCollection<string> Supported => _byName.Keys;

    public ISupportedChampion? For(Champion champion) => _byName.GetValueOrDefault(champion.Name);

    public IChampionKit? NewFight(Champion champion) => For(champion)?.NewFight();

    /// <summary>
    /// Ability ranks at a future <paramref name="level"/>: the observed ranks, plus the points still to come
    /// in the kit's skill order. Without observed ranks it is the skill order itself.
    /// </summary>
    public AbilityRanks RanksAt(Champion champion, int level, AbilityRanks? observed)
    {
        if (observed is null || observed == AbilityRanks.None || For(champion) is not { } supported)
        {
            return RanksFor(champion, level, null);
        }

        var ranks = new Dictionary<string, int> { ["Q"] = observed.Q, ["W"] = observed.W, ["E"] = observed.E, ["R"] = observed.R };
        var points = Math.Clamp(level, 0, supported.SkillOrder.Count) - ranks.Values.Sum();
        var taken = new Dictionary<string, int>();

        foreach (var ability in supported.SkillOrder)
        {
            if (points <= 0)
            {
                break;
            }

            taken[ability] = taken.GetValueOrDefault(ability) + 1;
            if (taken[ability] > ranks.GetValueOrDefault(ability))
            {
                ranks[ability] = taken[ability];
                points--;
            }
        }

        return new AbilityRanks(ranks["Q"], ranks["W"], ranks["E"], ranks["R"]);
    }

    public AbilityRanks RanksFor(Champion champion, int level, AbilityRanks? observed)
    {
        if (observed is not null && observed != AbilityRanks.None)
        {
            return observed;
        }

        if (For(champion) is not { } supported)
        {
            return AbilityRanks.None;
        }

        var taken = supported.SkillOrder.Take(Math.Clamp(level, 0, supported.SkillOrder.Count)).ToList();
        return new AbilityRanks(
            taken.Count(s => s == "Q"),
            taken.Count(s => s == "W"),
            taken.Count(s => s == "E"),
            taken.Count(s => s == "R"));
    }
}
