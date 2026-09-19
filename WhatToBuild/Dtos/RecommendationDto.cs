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
    IReadOnlyList<MatchupDto>? Matchups = null);

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
    bool Refining);

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
