using WhatToBuild.Data;

namespace WhatToBuild.Game;

public sealed record TrendPoint(double GameTime, double Value);

public sealed class GameStack
{
    private readonly List<GameState> _states = new();

    public IReadOnlyList<GameState> States => _states;

    public GameState? Latest => _states.Count > 0 ? _states[^1] : null;

    public int Count => _states.Count;

    public GameStack Copy()
    {
        var copy = new GameStack();
        copy._states.AddRange(_states);
        return copy;
    }

    public bool Push(GameState state)
    {
        if (Latest is { } last && state.GameTime <= last.GameTime)
        {
            return false;
        }

        _states.Add(state);
        return true;
    }

    public IReadOnlyList<TrendPoint> Series(Func<GameState, double?> selector, double windowSeconds = double.PositiveInfinity)
    {
        if (Latest is not { } latest)
        {
            return [];
        }

        var from = latest.GameTime - windowSeconds;

        return _states
            .Where(s => s.GameTime >= from)
            .Select(s => (s.GameTime, Value: selector(s)))
            .Where(p => p.Value.HasValue)
            .Select(p => new TrendPoint(p.GameTime, p.Value!.Value))
            .ToList();
    }

    public double? RatePerMinute(Func<GameState, double?> selector, double windowSeconds = double.PositiveInfinity)
    {
        var points = Series(selector, windowSeconds);

        if (points.Count < 2)
        {
            return null;
        }

        var meanTime = points.Average(p => p.GameTime);
        var meanValue = points.Average(p => p.Value);
        var spread = points.Sum(p => (p.GameTime - meanTime) * (p.GameTime - meanTime));

        if (spread <= 0)
        {
            return null;
        }

        var slope = points.Sum(p => (p.GameTime - meanTime) * (p.Value - meanValue)) / spread;

        return slope * 60;
    }

    public double? GoldEarnedPerMinute(double windowSeconds = double.PositiveInfinity) =>
        RatePerMinute(s => s.GoldEarned, windowSeconds);

    public double? LevelPerMinute(Champion champion, double windowSeconds = double.PositiveInfinity) =>
        RatePerMinute(s => s.Find(champion)?.Level, windowSeconds);

    public double? ItemValuePerMinute(Champion champion, double windowSeconds = double.PositiveInfinity) =>
        RatePerMinute(s => s.Find(champion)?.ItemValue, windowSeconds);

    public double? CreepScorePerMinute(Champion champion, double windowSeconds = double.PositiveInfinity) =>
        RatePerMinute(s => s.Find(champion)?.CreepScore, windowSeconds);
}
