using WhatToBuild.Data;
using WhatToBuild.Game;
using WhatToBuild.Planning;

namespace WhatToBuild.Forecasting;

public sealed record PlayerOutlook(
    PlayerState Player,
    string Role,
    double EarnedNow,
    double GoldPerMinute,
    double Pace,
    bool EarnedIsExact);

public sealed class GameForecaster
{
    private const double TableStep = 15;
    private const double TableSeconds = 45 * 60;

    private readonly GameState _state;
    private readonly GameStack _stack;
    private readonly BaselineCurves _baseline;
    private readonly ModelSettings.IncomeSettings _income;
    private readonly TrendBlend _blend = new();
    private readonly Dictionary<PlayerState, PlayerOutlook> _outlooks = new();
    private readonly Dictionary<PlayerState, double[]> _earnedTables = new();
    private readonly object _lock = new();
    private double? _lobbyPace;

    public GameForecaster(GameState state, GameStack stack, ModelData model)
    {
        _state = state;
        _stack = stack;
        _baseline = model.Baseline;
        _income = model.Settings.Income;
    }

    public double Now => _state.GameTime;

    public GameState State => _state;

    public PlayerOutlook Outlook(PlayerState player)
    {
        lock (_lock)
        {
            if (!_outlooks.TryGetValue(player, out var outlook))
            {
                outlook = BuildOutlook(player);
                _outlooks[player] = outlook;
            }

            return outlook;
        }
    }

    public double EarnedAt(PlayerState player, double time)
    {
        var table = Table(player);
        var offset = Math.Max(0, time - Now) / TableStep;
        var i = (int)Math.Floor(offset);

        if (i >= table.Length - 1)
        {
            var last = table.Length - 1;
            var slope = table[last] - table[last - 1];
            return table[last] + slope * (offset - last);
        }

        return table[i] + (offset - i) * (table[i + 1] - table[i]);
    }

    public double TimeToEarn(PlayerState player, double earned)
    {
        var table = Table(player);

        if (earned <= table[0])
        {
            return Now;
        }

        for (var i = 1; i < table.Length; i++)
        {
            if (earned <= table[i])
            {
                var share = (earned - table[i - 1]) / Math.Max(1e-9, table[i] - table[i - 1]);
                return Now + (i - 1 + share) * TableStep;
            }
        }

        var last = table.Length - 1;
        var slope = Math.Max(1e-6, table[last] - table[last - 1]);
        return Now + (last + (earned - table[last]) / slope) * TableStep;
    }

    public int LevelAt(PlayerState player, double time)
    {
        var role = Outlook(player).Role;
        var ahead = player.Level + 0.5 - _baseline.LevelAt(role, Now);
        var fade = Math.Exp(-Math.Max(0, time - Now) / _income.LevelReversionSeconds);
        var level = _baseline.LevelAt(role, time) + ahead * fade;

        return (int)Math.Clamp(Math.Floor(level), player.Level, 18);
    }

    public double Spread(double time) => 10 + Math.Max(0, time - Now) * _income.RateUncertainty;

    public double StacksAt(PlayerState player, ChampionStacking stacking, double time)
    {
        var observed = player.EstimatedStacks.FirstOrDefault(s => s.Stat == stacking.Stat);
        var now = observed?.Stacks ?? stacking.InitialStacksPerMinute * Now / 60;

        var measured = Now > 60 ? now / (Now / 60) : stacking.InitialStacksPerMinute;
        var rate = _blend.Blend(stacking.InitialStacksPerMinute, measured, Now);
        var stacks = now + rate * Outlook(player).Pace * Math.Max(0, time - Now) / 60;

        return stacking.Max > 0 ? Math.Min(stacking.Max, stacks) : stacks;
    }

    public static double GoldOf(GameState state, PlayerState player) =>
        player.IsActivePlayer ? state.GoldEarned : player.ItemValue;

    private PlayerOutlook BuildOutlook(PlayerState player)
    {
        var role = RoleOf(player);
        var own = OwnPace(player);

        var pace = player.IsActivePlayer
            ? own
            : (1 - _income.TrendWeight) * own + _income.TrendWeight * LobbyPace();

        pace = Math.Clamp(pace, _income.MinPace, _income.MaxPace);
        var baselineRate = (_baseline.GoldAt(role, Now) - _income.StartingGold) / Math.Max(1, Now);

        return new PlayerOutlook(player, role, GoldOf(_state, player), baselineRate * pace * 60, pace, player.IsActivePlayer);
    }

    private double OwnPace(PlayerState player)
    {
        var role = RoleOf(player);
        var earned = GoldOf(_state, player);
        var baselineRate = (_baseline.GoldAt(role, Now) - _income.StartingGold) / Math.Max(1, Now);

        var average = Now > 60 ? Math.Max(0, earned - _income.StartingGold) / Now : (double?)null;
        var recent = RecentRate(player);
        var observed = (average, recent) switch
        {
            ({ } a, { } r) => (1 - _income.RecentWeight) * a + _income.RecentWeight * r,
            ({ } a, null) => a,
            _ => (double?)null,
        };

        var trust = Math.Clamp(
            (Now - _income.BaselineOnlyUntilSeconds) / Math.Max(1, _income.ObservedOnlyFromSeconds - _income.BaselineOnlyUntilSeconds), 0, 1);
        var observedPace = observed is { } rate && baselineRate > 0 ? rate / baselineRate : 1;

        return Math.Clamp(1 + (observedPace - 1) * trust, _income.MinPace, _income.MaxPace);
    }

    private double LobbyPace()
    {
        _lobbyPace ??= _state.Players.Where(p => !p.IsActivePlayer).Select(OwnPace).DefaultIfEmpty(1).Average();
        return _lobbyPace.Value;
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

    private double[] Table(PlayerState player)
    {
        lock (_lock)
        {
            if (_earnedTables.TryGetValue(player, out var table))
            {
                return table;
            }

            var outlook = Outlook(player);
            var steps = (int)(TableSeconds / TableStep);
            table = new double[steps + 1];
            table[0] = outlook.EarnedNow;

            for (var i = 1; i <= steps; i++)
            {
                var t = Now + (i - 0.5) * TableStep;
                table[i] = table[i - 1] + _baseline.GoldRateAt(outlook.Role, t) * outlook.Pace * TableStep;
            }

            _earnedTables[player] = table;
            return table;
        }
    }

    private string RoleOf(PlayerState player)
    {
        var position = player.Position.ToUpperInvariant();
        return _baseline.Knows(position) ? position : "";
    }
}
