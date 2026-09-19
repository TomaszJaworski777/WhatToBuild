namespace WhatToBuild.Forecasting;

public sealed class TrendBlend
{
    public double PredictionOnlyUntilSeconds { get; init; } = 240;

    public double TrendOnlyFromSeconds { get; init; } = 1200;

    public double TrendWeight(double gameTime)
    {
        var span = TrendOnlyFromSeconds - PredictionOnlyUntilSeconds;

        if (span <= 0)
        {
            return gameTime >= TrendOnlyFromSeconds ? 1 : 0;
        }

        return Math.Clamp((gameTime - PredictionOnlyUntilSeconds) / span, 0, 1);
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
