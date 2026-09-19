using WhatToBuild.Clients;
using WhatToBuild.Data;

namespace WhatToBuild.Game;

public sealed class GameTracker
{
    public const double NewGameThresholdSeconds = 30;

    private readonly ILiveClient _client;
    private readonly ChampionRepository _champions;
    private readonly ItemRepository _items;

    public GameTracker(ILiveClient client, ChampionRepository champions, ItemRepository items)
    {
        _client = client;
        _champions = champions;
        _items = items;
    }

    public GameStack Stack { get; private set; } = new();

    public async Task<GameState?> PollAsync(CancellationToken ct = default)
    {
        var json = await _client.GetAllGameDataAsync(ct);

        if (json is null)
        {
            return null;
        }

        var state = GameStateParser.Parse(json, _champions, _items);

        if (Stack.Latest is { } latest && state.GameTime + NewGameThresholdSeconds < latest.GameTime)
        {
            Stack = new GameStack();
        }

        Stack.Push(state);

        return state;
    }
}
