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

    private readonly Dictionary<(string Champion, int Item), double> _firstSeen = new();

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
            _firstSeen.Clear();
        }

        TrackPurchases(state);
        Stack.Push(state);

        return state;
    }

    private void TrackPurchases(GameState state)
    {
        var owned = state.Players
            .SelectMany(p => p.Items.Select(i => (p.Champion.Name, i.Item.RiotId)))
            .ToHashSet();

        foreach (var key in _firstSeen.Keys.Where(k => !owned.Contains(k)).ToList())
        {
            _firstSeen.Remove(key);
        }

        foreach (var key in owned)
        {
            _firstSeen.TryAdd(key, state.GameTime);
        }

        state.ItemFirstSeen = new Dictionary<(string Champion, int Item), double>(_firstSeen);
    }
}
