using WhatToBuild.Clients;
using WhatToBuild.Data;

namespace WhatToBuild.Game;

public sealed class GameTracker
{
    private readonly ILiveClient _client;
    private readonly ChampionRepository _champions;
    private readonly ItemRepository _items;

    public GameTracker(ILiveClient client, ChampionRepository champions, ItemRepository items)
    {
        _client = client;
        _champions = champions;
        _items = items;
    }

    public GameStack Stack { get; } = new();

    public async Task<GameState?> PollAsync(CancellationToken ct = default)
    {
        var json = await _client.GetAllGameDataAsync(ct);

        if (json is null)
        {
            return null;
        }

        var state = GameStateParser.Parse(json, _champions, _items);
        Stack.Push(state);

        return state;
    }
}
