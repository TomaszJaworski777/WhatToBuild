using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using WhatToBuild.Data;
using WhatToBuild.Forecasting;
using WhatToBuild.Planning;
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
    private readonly ModelData _model;
    private readonly string _patch;
    private readonly IHubContext<GameHub> _hub;
    private readonly ILogger<GameStateService> _logger;

    private readonly object _sendLock = new();

    private string? _lastRecommendationJson;

    public GameStateService(
        GameTracker tracker,
        NeutralRepository neutrals,
        ItemRepository items,
        IRecommendationSource recommendations,
        MatchupCalculator matchups,
        ModelData model,
        IHubContext<GameHub> hub,
        ILogger<GameStateService> logger)
    {
        _tracker = tracker;
        _neutrals = neutrals;
        _recommendations = recommendations;
        _matchups = matchups;
        _model = model;
        _patch = items.Patch;
        _hub = hub;
        _logger = logger;
        Latest = GameStateDto.Waiting(_patch);
        _recommendations.Updated += recommendation => _ = PushRecommendationAsync(recommendation, CancellationToken.None);
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
                : WithGoldRates(GameStateMapper.ToDto(state, _neutrals, _patch) with { Matchups = _matchups.For(state) }, state);

            await _hub.Clients.All.SendAsync(GameStateMessage, Latest, ct);

            await PushRecommendationAsync(state is null ? null : _recommendations.For(state, _tracker.Stack), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Polling the game failed; retrying.");
        }
    }

    private GameStateDto WithGoldRates(GameStateDto dto, GameState state)
    {
        var forecaster = new GameForecaster(state, _tracker.Stack, _model);
        var rates = state.Players.ToDictionary(p => p.Champion.Name, p => forecaster.Outlook(p).GoldPerMinute);

        return dto with
        {
            Teams = dto.Teams
                .Select(t => t with { Players = t.Players.Select(p => p with { GoldPerMinute = rates.GetValueOrDefault(p.Champion) }).ToList() })
                .ToList(),
        };
    }

    private async Task PushRecommendationAsync(RecommendationDto? recommendation, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(recommendation);

        lock (_sendLock)
        {
            if (json == _lastRecommendationJson)
            {
                return;
            }

            _lastRecommendationJson = json;
            LatestRecommendation = recommendation;
        }

        try
        {
            await _hub.Clients.All.SendAsync(RecommendationMessage, recommendation, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Sending the recommendation failed.");
        }
    }
}
