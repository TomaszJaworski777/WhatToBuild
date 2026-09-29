namespace WhatToBuild.Dtos;

public sealed record RecommendationDto(
    bool IsSample,
    double Gold,
    double GoldEarned,
    double? GoldPerMinute,
    PurchaseDto BuyNow,
    IReadOnlyList<BuildStepDto> BuildPath,
    IReadOnlyList<SkippedItemDto> Skipped,
    IReadOnlyList<TeamNeedDto> TeamNeeds,
    ModelDto? Model = null,
    IReadOnlyList<string>? Assumptions = null,
    IReadOnlyList<MatchupDto>? Matchups = null,
    FormAdviceDto? Form = null,
    bool Calculating = false,
    WeightsDto? Weights = null,
    BurstDto? Burst = null);

/// <summary>
/// The burst the plan scores: <c>Steps</c>, the sequence in play (yours when <c>Custom</c>, else
/// the kit's <c>Default</c>), built from <c>Choices</c>; none for a champion with no script.
/// <c>Share</c> is the share of the counted enemies' health it takes with the finished build.
/// </summary>
public sealed record BurstDto(
    string Champion,
    IReadOnlyList<string> Steps,
    IReadOnlyList<string> Default,
    IReadOnlyList<string> Choices,
    bool Custom,
    double Share,
    IReadOnlyList<BurstTargetDto> Targets);

/// <summary>An enemy for the burst: whether it counts, and the share of its health the burst takes.</summary>
public sealed record BurstTargetDto(string Champion, string Icon, bool Counted, double Threat, double? Share);

/// <summary>
/// The objective weights the plan scores builds with, for the entry in model.json it reads
/// (<c>Kindred</c>, <c>Kayn/Darkin</c>): the model's own values and the ones in use now.
/// </summary>
public sealed record WeightsDto(
    string Key,
    string Label,
    IReadOnlyList<WeightDto> Weights,
    double Max,
    bool Custom,
    double MeasuredAt,
    double TeamfightSeconds);

/// <summary>A weight, and <c>Measure</c>: what it measures for the build the plan finishes.</summary>
public sealed record WeightDto(string Name, string Label, string Meaning, double Default, double Current, double Measure);

public sealed record FormAdviceDto(
    string? Detected,
    string Recommended,
    string RecommendedLabel,
    IReadOnlyList<FormOptionDto> Options,
    string ChargeHint,
    IReadOnlyList<string> Why,
    bool Locked = false,
    string? LockedBecause = null);

public sealed record FormOptionDto(
    string Form,
    string Label,
    double FightValue,
    double Dps,
    double TimeAlive,
    double HealingPerSecond,
    string? BurstTarget,
    double? BurstKillSeconds,
    double Burst,
    double? BurstOnTarget);

public sealed record ModelDto(
    double At,
    double DamageWeight,
    double SurvivalWeight,
    double ClearWeight,
    double Dps,
    double TimeAlive,
    double TimeAliveWithoutAbility,
    string? SurvivalAbility,
    double TeamfightSeconds,
    double IncomingDps,
    double IncomingBurst,
    double HealthPool,
    double PhysicalShare,
    double MagicShare,
    double TrueShare,
    double? ClearSeconds,
    IReadOnlyList<EnemyForecastDto> Enemies,
    double PlanMilliseconds,
    int Evaluations,
    int Candidates,
    bool TimedOut,
    string Stage,
    bool Refining,
    int StageIndex = 0,
    int StageCount = 1,
    double StableSeconds = 0,
    int TrendSamples = 0,
    double TrendConfidence = 0,
    int CoreItems = 3,
    string CoreLabel = "",
    int BuiltItems = 0);

public sealed record EnemyForecastDto(
    string Champion,
    string Icon,
    string Archetype,
    int LevelNow,
    int Level,
    IReadOnlyList<ItemDto> NewItems,
    double Threat,
    double Focus,
    double Health,
    double Armor,
    double MagicResist,
    double HealPerSecond,
    double Shield,
    double DpsOnYou,
    double? TimeToKill,
    IReadOnlyList<string> Notes);

public sealed record CastHintDto(
    string Ability,
    double CastAtHealth,
    double KillingHealth,
    IReadOnlyList<string> Additions);

public sealed record MatchupDto(
    string Champion,
    double? TimeToKill,
    double Dps,
    IReadOnlyList<CastHintDto> Casts);

public sealed record PurchaseDto(
    string Target,
    IReadOnlyList<ItemDto> Items,
    int Cost,
    double GoldLeft,
    bool CompletesTarget,
    string Summary,
    IReadOnlyList<string> Why);

public sealed record BuildStepDto(
    ItemDto Item,
    string Status,
    double? EtaSeconds,
    double? EtaSpreadSeconds,
    int GoldNeeded,
    ImpactDto? Impact,
    IReadOnlyList<string> Why,
    string? Sells = null)
{
    public const string Owned = "Owned";
    public const string Next = "Next";
    public const string Planned = "Planned";
}

public sealed record ImpactDto(
    double DpsBefore,
    double DpsAfter,
    IReadOnlyList<EnemyImpactDto> PerEnemy);

public sealed record EnemyImpactDto(
    string Champion,
    string Icon,
    double DpsBefore,
    double DpsAfter,
    double? TtkBefore,
    double? TtkAfter,
    string Note);

public sealed record SkippedItemDto(ItemDto Item, string Reason);

public sealed record TeamNeedDto(
    string Need,
    string Detail,
    string? CoveredBy,
    double? CoveredAtSeconds,
    IReadOnlyList<ItemDto> Options);
