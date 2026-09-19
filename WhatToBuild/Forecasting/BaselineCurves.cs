using System.Text.Json;
using WhatToBuild.Data;
using WhatToBuild.Planning;

namespace WhatToBuild.Forecasting;

public sealed class RoleCurve
{
    public List<double> Level { get; set; } = new();

    public List<double> GoldEarned { get; set; } = new();
}

/// <summary>
/// Average level and total gold earned by role over game time, from <c>GameData/Model/baseline.json</c>.
/// A player with an unknown role gets the average of all roles. Past the last point, gold keeps the last
/// slope and level stops at 18.
/// </summary>
public sealed class BaselineCurves
{
    public const string FileName = "baseline.json";

    private RoleCurve? _average;

    public string Source { get; set; } = "";

    public List<double> Minutes { get; set; } = new();

    public Dictionary<string, RoleCurve> Roles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static BaselineCurves Load(string dataRoot) =>
        JsonSerializer.Deserialize<BaselineCurves>(
            File.ReadAllText(Path.Combine(dataRoot, ModelSettings.FolderName, FileName)), GameDataJson.Options)
        ?? throw new InvalidDataException($"{FileName} is empty.");

    public bool Knows(string role) => Roles.ContainsKey(role);

    public double LevelAt(string role, double seconds) =>
        Math.Min(18, Interpolate(Curve(role).Level, seconds));

    public double GoldAt(string role, double seconds) => Interpolate(Curve(role).GoldEarned, seconds);

    /// <summary>Gold per second at <paramref name="seconds"/>.</summary>
    public double GoldRateAt(string role, double seconds)
    {
        const double half = 30;
        return (GoldAt(role, seconds + half) - GoldAt(role, Math.Max(0, seconds - half))) / (seconds + half - Math.Max(0, seconds - half));
    }

    private RoleCurve Curve(string role) => Roles.GetValueOrDefault(role) ?? Average();

    private RoleCurve Average()
    {
        return _average ??= new RoleCurve
        {
            Level = Minutes.Select((_, i) => Roles.Values.Average(r => r.Level[i])).ToList(),
            GoldEarned = Minutes.Select((_, i) => Roles.Values.Average(r => r.GoldEarned[i])).ToList(),
        };
    }

    private double Interpolate(IReadOnlyList<double> values, double seconds)
    {
        var minutes = Math.Max(0, seconds / 60);

        for (var i = 1; i < Minutes.Count; i++)
        {
            if (minutes <= Minutes[i])
            {
                var share = (minutes - Minutes[i - 1]) / (Minutes[i] - Minutes[i - 1]);
                return values[i - 1] + share * (values[i] - values[i - 1]);
            }
        }

        var n = Minutes.Count - 1;
        var slope = (values[n] - values[n - 1]) / (Minutes[n] - Minutes[n - 1]);
        return values[n] + slope * (minutes - Minutes[n]);
    }
}
