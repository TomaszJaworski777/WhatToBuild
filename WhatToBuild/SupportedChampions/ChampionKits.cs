using WhatToBuild.Data;
using WhatToBuild.Modeling;
using WhatToBuild.Modeling.Simulation;
using WhatToBuild.SupportedChampions.Kayn;
using WhatToBuild.SupportedChampions.Kindred;
using WhatToBuild.SupportedChampions.Nasus;
using WhatToBuild.SupportedChampions.Vi;

namespace WhatToBuild.SupportedChampions;

public sealed record CastAddition(string When, double Damage);

public sealed record SurvivalAbility(string Name, double UndyingSeconds, double MinimumHealthPercent, double Heal, double Cooldown);

public sealed record CastHint(string Ability, double CastAtHealth, double KillingHealth, IReadOnlyList<CastAddition> Additions);

/// <summary>Stats an ability gives for fights it is up in (Nasus's R), and its cooldown.</summary>
public sealed record FightStats(StatSheet Stats, double Cooldown);

public interface ISupportedChampion
{
    string ChampionName { get; }

    IReadOnlyList<string> SkillOrder { get; }

    IChampionKit NewFight();

    IChampionKit NewFight(string? form) => NewFight();

    IReadOnlyList<string> Forms => [];

    string? DefaultForm => null;

    string? BaseForm => null;

    string FormLabel(string form) => form;

    string? DetectForm(IReadOnlyDictionary<string, string> abilityIds) => null;

    IReadOnlyList<CastHint> Hints(FightSetup setup);

    SurvivalAbility? Survival(AbilityRanks ranks) => null;

    SurvivalAbility? Survival(AbilityRanks ranks, ChampionState us, string? form, double enemyHealth) => Survival(ranks);

    /// <summary>Life steal from the kit (Nasus's Soul Eater): heals off basic attacks and on-hits only.</summary>
    double LifeSteal(ChampionState us, string? form) => 0;

    /// <summary>Omnivamp from the kit (Rhaast's passive): heals off all damage dealt.</summary>
    double Omnivamp(ChampionState us, string? form) => 0;

    /// <summary>Stats a cooldown gives in fights; counted for as many teamfights as its cooldown allows.</summary>
    FightStats? Stats(AbilityRanks ranks) => null;

    /// <summary>
    /// The share of one enemy's attack damage an ability takes away over a fight of this length
    /// (Nasus's Wither on whoever hits you hardest).
    /// </summary>
    double AttackCut(AbilityRanks ranks, ChampionState us, double fightSeconds) => 0;
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
            new KaynChampion(KaynKitData.Load(Path.Combine(folder, KaynKitData.FileName))),
            new ViChampion(ViKitData.Load(Path.Combine(folder, ViKitData.FileName))),
            new NasusChampion(NasusKitData.Load(Path.Combine(folder, NasusKitData.FileName))),
        ]);
    }

    public IReadOnlyCollection<string> Supported => _byName.Keys;

    public ISupportedChampion? For(Champion champion) => _byName.GetValueOrDefault(champion.Name);

    public IChampionKit? NewFight(Champion champion, string? form = null) => For(champion)?.NewFight(form);

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
