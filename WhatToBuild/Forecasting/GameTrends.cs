using System.Globalization;
using System.Text;
using WhatToBuild.Game;
using WhatToBuild.Planning;

namespace WhatToBuild.Forecasting;

public sealed record PlayerTrend(
    PlayerState Player,
    string Role,
    double Earned,
    bool EarnedIsExact,
    double? AverageRate,
    double? RecentRate,
    double BaselineRate,
    double ObservedPace,
    double Pace,
    double GoldPerMinute,
    double Confidence,
    int Samples,
    double TakedownsPerMinute);

public sealed class GameTrends
{
    private readonly GameState _state;
    private readonly GameStack _stack;
    private readonly BaselineCurves _baseline;
    private readonly ModelSettings.IncomeSettings _income;
    private readonly Dictionary<PlayerState, PlayerTrend> _trends = new();
    private readonly object _lock = new();

    private double? _lobbyPace;
    private string? _signature;

    public GameTrends(GameState state, GameStack stack, ModelData model)
    {
        _state = state;
        _stack = stack;
        _baseline = model.Baseline;
        _income = model.Settings.Income;
    }

    public double Now => _state.GameTime;

    public int Samples => _stack.Series(s => s.GameTime, _income.PaceWindowSeconds).Count;

    public double Confidence => Math.Clamp(
        (Now - _income.BaselineOnlyUntilSeconds) / Math.Max(1, _income.ObservedOnlyFromSeconds - _income.BaselineOnlyUntilSeconds), 0, 1);

    public bool Established => Confidence >= 1;

    public PlayerTrend For(PlayerState player)
    {
        lock (_lock)
        {
            if (!_trends.TryGetValue(player, out var trend))
            {
                trend = Build(player);
                _trends[player] = trend;
            }

            return trend;
        }
    }

    public IReadOnlyList<PlayerTrend> All => _state.Players.Select(For).ToList();

    public double LobbyPace
    {
        get
        {
            lock (_lock)
            {
                _lobbyPace ??= _state.Players.Where(p => !p.IsActivePlayer).Select(ObservedPace).DefaultIfEmpty(1).Average();
                return _lobbyPace.Value;
            }
        }
    }

    public string Signature => _signature ??= BuildSignature();

    public double BaselineRateFor(PlayerState player) =>
        (_baseline.GoldAt(RoleOf(player), Now) - _income.StartingGold) / Math.Max(1, Now);

    public string RoleOf(PlayerState player)
    {
        var position = player.Position.ToUpperInvariant();
        return _baseline.Knows(position) ? position : "";
    }

    public static double GoldOf(GameState state, PlayerState player) =>
        player.IsActivePlayer ? state.GoldEarned : player.ItemValue;

    private PlayerTrend Build(PlayerState player)
    {
        var role = RoleOf(player);
        var earned = GoldOf(_state, player);
        var baselineRate = BaselineRateFor(player);
        var average = Now > 60 ? Math.Max(0, earned - _income.StartingGold) / Now : (double?)null;
        var recent = RecentRate(player);
        var observedPace = ObservedPace(player);

        var pace = player.IsActivePlayer
            ? observedPace
            : (1 - _income.TrendWeight) * observedPace + _income.TrendWeight * LobbyPace;

        var minutes = Math.Max(1, Now / 60);

        return new PlayerTrend(
            player,
            role,
            earned,
            player.IsActivePlayer,
            average,
            recent,
            baselineRate,
            observedPace,
            pace,
            baselineRate * pace * 60,
            Confidence,
            Samples,
            (player.Kills + player.Assists) / minutes);
    }

    private double ObservedPace(PlayerState player)
    {
        var earned = GoldOf(_state, player);
        var baselineRate = BaselineRateFor(player);

        var average = Now > 60 ? Math.Max(0, earned - _income.StartingGold) / Now : (double?)null;
        var recent = RecentRate(player);
        var observed = (average, recent) switch
        {
            ({ } a, { } r) => (1 - _income.RecentWeight) * a + _income.RecentWeight * r,
            ({ } a, null) => a,
            _ => (double?)null,
        };

        var observedPace = observed is { } rate && baselineRate > 0 ? rate / baselineRate : 1;

        return 1 + (observedPace - 1) * Confidence;
    }

    private double? RecentRate(PlayerState player)
    {
        var points = _stack.Series(s => s.Find(player.Champion) is { } p ? GoldOf(s, p) : null, _income.PaceWindowSeconds);

        if (points.Count < 2 || points[^1].GameTime - points[0].GameTime < _income.MinRecentSpanSeconds)
        {
            return null;
        }

        var meanTime = points.Average(p => p.GameTime);
        var meanValue = points.Average(p => p.Value);
        var spread = points.Sum(p => (p.GameTime - meanTime) * (p.GameTime - meanTime));

        return spread > 0 ? Math.Max(0, points.Sum(p => (p.GameTime - meanTime) * (p.Value - meanValue)) / spread) : null;
    }

    private string BuildSignature()
    {
        var key = new StringBuilder();

        foreach (var player in _state.Players.OrderBy(p => p.Champion.Name, StringComparer.Ordinal))
        {
            var trend = For(player);
            key.Append(player.Champion.Name).Append(':').Append(player.Level).Append(':');

            foreach (var item in player.Items.OrderBy(i => i.Item.RiotId))
            {
                key.Append(item.Item.RiotId).Append('x').Append(item.Count).Append(',');
            }

            key.Append(':').Append(Bucket(player.Kills + player.Assists, _income.TakedownBucket))
                .Append(':').Append(Bucket(trend.Pace, _income.PaceBucket).ToString("0.0", CultureInfo.InvariantCulture))
                .Append('|');
        }

        foreach (var (team, objectives) in _state.Objectives)
        {
            key.Append(team).Append(objectives.TotalDragons).Append(objectives.Elders).Append(objectives.Barons).Append(objectives.SoulType);
        }

        return key.ToString();
    }

    private static double Bucket(double value, double size) =>
        size <= 0 ? value : Math.Round(value / size) * size;
}
