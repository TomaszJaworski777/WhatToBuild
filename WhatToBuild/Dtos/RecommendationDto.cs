namespace WhatToBuild.Dtos;

public sealed record RecommendationDto(
    bool IsSample,
    double Gold,
    double GoldEarned,
    double? GoldPerMinute,
    PurchaseDto BuyNow,
    IReadOnlyList<BuildStepDto> BuildPath,
    IReadOnlyList<string> TeamNeeds,
    IReadOnlyList<string> Assumptions);

public sealed record PurchaseDto(
    string Target,
    IReadOnlyList<ItemDto> Items,
    int Cost,
    double GoldLeft,
    bool CompletesTarget,
    string Summary);

public sealed record BuildStepDto(
    ItemDto Item,
    string Status,
    double? EtaSeconds,
    double? EtaSpreadSeconds,
    IReadOnlyList<string> Why)
{
    public const string Owned = "Owned";
    public const string Next = "Next";
    public const string Planned = "Planned";
}
