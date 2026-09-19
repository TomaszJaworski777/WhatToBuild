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
    IReadOnlyList<string> Assumptions);

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
    IReadOnlyList<string> Why)
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
    string Note);

public sealed record SkippedItemDto(ItemDto Item, string Reason);

public sealed record TeamNeedDto(
    string Need,
    string Detail,
    string? CoveredBy,
    double? CoveredAtSeconds,
    IReadOnlyList<ItemDto> Options);
