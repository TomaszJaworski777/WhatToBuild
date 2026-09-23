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
    private readonly TrendBlend _stackBlend;
    private readonly Dictionary<PlayerState, PlayerOutlook> _outlooks = new();
    private readonly Dictionary<PlayerState, double[]> _earnedTables = new();
    private readonly object _lock = new();

    public GameForecaster(GameState state, GameStack stack, ModelData model)
        : this(state, stack, model, new GameTrends(state, stack, model))
    {
    }

    public GameForecaster(GameState state, GameStack stack, ModelData model, GameTrends trends)
    {
        _state = state;
        _stack = stack;
        _baseline = model.Baseline;
        _income = model.Settings.Income;
        _stackBlend = new TrendBlend
        {
            PredictionOnlyUntilSeconds = model.Settings.Stacks.TrendFromSeconds,
            TrendOnlyFromSeconds = model.Settings.Stacks.TrendFullSeconds,
            MaxTrendWeight = model.Settings.Stacks.MaxTrendWeight,
        };
        Trends = trends;
    }

    public GameTrends Trends { get; }

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

        // Mostly the standard pace: your own pace so far only leans on it, and only once the game
        // is long enough for it to mean something. Gold pace is not applied on top, since the
        // measured pace already carries it.
        var measured = Now > 60 ? now / (Now / 60) : stacking.InitialStacksPerMinute;
        var rate = _stackBlend.Blend(stacking.InitialStacksPerMinute, measured, Now);
        var stacks = now + rate * Math.Max(0, time - Now) / 60;

        // The cap stops the forecast, not what you already have.
        return stacking.Max > 0 ? Math.Min(Math.Max(stacking.Max, now), stacks) : stacks;
    }

    public static double GoldOf(GameState state, PlayerState player) =>
        player.IsActivePlayer ? state.GoldEarned : player.ItemValue;

    private PlayerOutlook BuildOutlook(PlayerState player)
    {
        var trend = Trends.For(player);

        return new PlayerOutlook(trend.Player, trend.Role, trend.Earned, trend.GoldPerMinute, trend.Pace, trend.EarnedIsExact);
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

    private string RoleOf(PlayerState player) => Trends.RoleOf(player);
}
