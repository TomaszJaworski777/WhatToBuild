namespace WhatToBuild.Dtos;

public sealed record GameStateDto(
    string Phase,
    string Patch,
    double GameTime,
    double CurrentGold,
    double GoldEarned,
    string? ActiveTeam,
    IReadOnlyList<TeamDto> Teams,
    IReadOnlyList<string> UnknownChampions,
    IReadOnlyList<int> UnknownItemIds)
{
    public IReadOnlyList<MatchupDto> Matchups { get; init; } = [];

    public const string InGame = "InGame";
    public const string NoGame = "NoGame";

    public static GameStateDto Waiting(string patch) =>
        new(NoGame, patch, 0, 0, 0, null, [], [], []);
}

public sealed record TeamDto(
    string Team,
    int Kills,
    int Deaths,
    int Assists,
    int ItemValue,
    ObjectivesDto Objectives,
    IReadOnlyList<PlayerDto> Players);

public sealed record ObjectivesDto(
    IReadOnlyList<DragonDto> Dragons,
    string? SoulType,
    string? SoulName,
    int Elders,
    int Barons,
    int Heralds,
    int Turrets,
    int Inhibitors);

public sealed record DragonDto(string Type, string Name, int Count);

public sealed record PlayerDto(
    string Champion,
    string ChampionIcon,
    int Level,
    bool IsActivePlayer,
    bool IsDead,
    string Position,
    int Kills,
    int Deaths,
    int Assists,
    int CreepScore,
    int ItemValue,
    IReadOnlyList<ItemDto> Items,
    StatsDto Stats,
    double? CurrentHealth,
    bool StatsAreExact,
    IReadOnlyList<StackDto> EstimatedStacks,
    double? GoldPerMinute = null);

public sealed record ItemDto(
    int RiotId,
    string Name,
    string Icon,
    int Count,
    int Cost,
    IReadOnlyList<string> Stats,
    IReadOnlyList<string> Effects,
    int Slot = -1);

public sealed record StatsDto(
    double Health,
    double AttackDamage,
    double AbilityPower,
    double Armor,
    double MagicResist,
    double AttackSpeed,
    double MoveSpeed,
    double Tenacity);

public sealed record StackDto(string Stat, double Stacks, double Max);
