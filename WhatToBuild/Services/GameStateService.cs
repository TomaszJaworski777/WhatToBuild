using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using WhatToBuild.Data;
using WhatToBuild.Dtos;
using WhatToBuild.Game;
using WhatToBuild.Hubs;
using WhatToBuild.Recommendations;

namespace WhatToBuild.Services;

public sealed class GameStateService : BackgroundService
{
    public const string GameStateMessage = "GameState";
    public const string RecommendationMessage = "Recommendation";

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private readonly GameTracker _tracker;
    private readonly NeutralRepository _neutrals;
    private readonly IRecommendationSource _recommendations;
    private readonly MatchupCalculator _matchups;
    private readonly string _patch;
    private readonly IHubContext<GameHub> _hub;
    private readonly ILogger<GameStateService> _logger;

    private string? _lastRecommendationJson;

    public GameStateService(
        GameTracker tracker,
        NeutralRepository neutrals,
        ItemRepository items,
        IRecommendationSource recommendations,
        MatchupCalculator matchups,
        IHubContext<GameHub> hub,
        ILogger<GameStateService> logger)
    {
        _tracker = tracker;
        _neutrals = neutrals;
        _recommendations = recommendations;
        _matchups = matchups;
        _patch = items.Patch;
        _hub = hub;
        _logger = logger;
        Latest = GameStateDto.Waiting(_patch);
    }

    public GameStateDto Latest { get; private set; }

    public RecommendationDto? LatestRecommendation { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);

        try
        {
            do
            {
                await PollOnceAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        try
        {
            var state = await _tracker.PollAsync(ct);

            Latest = state is null
                ? GameStateDto.Waiting(_patch)
                : GameStateMapper.ToDto(state, _neutrals, _patch) with { Matchups = _matchups.For(state) };

            await _hub.Clients.All.SendAsync(GameStateMessage, Latest, ct);

            LatestRecommendation = state is null ? null : _recommendations.For(state, _tracker.Stack);

            var json = JsonSerializer.Serialize(LatestRecommendation);
            if (json != _lastRecommendationJson)
            {
                _lastRecommendationJson = json;
                await _hub.Clients.All.SendAsync(RecommendationMessage, LatestRecommendation, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Polling the game failed; retrying.");
        }
    }
}
