namespace WhatToBuild.Forecasting;

public sealed class TrendBlend
{
    public double PredictionOnlyUntilSeconds { get; init; } = 240;

    public double TrendOnlyFromSeconds { get; init; } = 1200;

    /// <summary>The most the trend ever counts for, reached at <see cref="TrendOnlyFromSeconds"/>.</summary>
    public double MaxTrendWeight { get; init; } = 1;

    public double TrendWeight(double gameTime)
    {
        var span = TrendOnlyFromSeconds - PredictionOnlyUntilSeconds;

        if (span <= 0)
        {
            return gameTime >= TrendOnlyFromSeconds ? MaxTrendWeight : 0;
        }

        return MaxTrendWeight * Math.Clamp((gameTime - PredictionOnlyUntilSeconds) / span, 0, 1);
    }

    public double Blend(double prediction, double? trend, double gameTime)
    {
        if (trend is not { } observed)
        {
            return prediction;
        }

        var weight = TrendWeight(gameTime);

        return prediction * (1 - weight) + observed * weight;
    }
}
