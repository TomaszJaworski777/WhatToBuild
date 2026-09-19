using WhatToBuild.Data;
using WhatToBuild.Game;

namespace WhatToBuild.Forecasting;

public interface IPaceSource
{
    double? PaceFactor(GameStack stack, PlayerState player);
}

public sealed class LobbyRelativePace : IPaceSource
{
    public double MinFactor { get; init; } = 0.25;

    public double MaxFactor { get; init; } = 3;

    public double? PaceFactor(GameStack stack, PlayerState player)
    {
        if (stack.Latest is not { } latest)
        {
            return null;
        }

        var own = stack.ItemValuePerMinute(player.Champion);

        var lobby = latest.Players
            .Select(p => stack.ItemValuePerMinute(p.Champion))
            .Where(r => r is > 0)
            .Select(r => r!.Value)
            .ToList();

        if (own is not { } ownRate || lobby.Count == 0)
        {
            return null;
        }

        return Math.Clamp(ownRate / lobby.Average(), MinFactor, MaxFactor);
    }
}

public sealed class StackForecaster
{
    private readonly GameStack _stack;
    private readonly TrendBlend _blend;
    private readonly IPaceSource _pace;

    public StackForecaster(GameStack stack, TrendBlend? blend = null, IPaceSource? pace = null)
    {
        _stack = stack;
        _blend = blend ?? new TrendBlend();
        _pace = pace ?? new LobbyRelativePace();
    }

    private double Now => _stack.Latest?.GameTime ?? 0;

    public double StacksPerMinute(PlayerState player, ChampionStacking stacking)
    {
        var prediction = stacking.InitialStacksPerMinute;
        var trend = _pace.PaceFactor(_stack, player) * prediction;

        return _blend.Blend(prediction, trend, Now);
    }

    public double StacksNow(PlayerState player, ChampionStacking stacking) =>
        StacksPerMinute(player, stacking) * Now / 60;

    public double StacksGainedBy(PlayerState player, ChampionStacking stacking, double targetTime) =>
        StacksPerMinute(player, stacking) * Math.Max(0, targetTime - Now) / 60;

    public double StacksAt(PlayerState player, ChampionStacking stacking, double targetTime) =>
        StacksNow(player, stacking) + StacksGainedBy(player, stacking, targetTime);
}
