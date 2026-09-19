using System.Text.Json;

namespace WhatToBuild.Data;

public enum NeutralKind
{
    JungleCamp,
    Epic,
    Minion,
}

public class NeutralScaling
{
    public double StartTimeSeconds { get; set; }

    public double IncrementSeconds { get; set; }

    public double PercentPerIncrement { get; set; }

    public double PercentCap { get; set; }

    public List<ScalingRateChange> RateChanges { get; set; } = new();

    public double MultiplierAt(double seconds)
    {
        if (seconds <= StartTimeSeconds || IncrementSeconds <= 0)
        {
            return 1;
        }

        var gained = 0.0;
        var rate = PercentPerIncrement;
        var changes = RateChanges.OrderBy(c => c.AtSeconds).ToList();

        for (var t = StartTimeSeconds + IncrementSeconds; t <= seconds; t += IncrementSeconds)
        {
            foreach (var change in changes.Where(c => c.AtSeconds <= t))
            {
                rate = change.PercentPerIncrement;
            }

            gained += rate;

            if (gained >= PercentCap)
            {
                return 1 + PercentCap;
            }
        }

        return 1 + gained;
    }
}

public class ScalingRateChange
{
    public double AtSeconds { get; set; }

    public double PercentPerIncrement { get; set; }
}

public class Neutral
{
    public Guid Id { get; set; }

    public string InternalName { get; set; } = "";

    public string Name { get; set; } = "";

    public NeutralKind Kind { get; set; }

    public string Camp { get; set; } = "";

    public int CountPerCamp { get; set; }

    public bool StatsIncomplete { get; set; }

    public StatSheet Base { get; set; } = new();

    public double AttackInterval => Base.AttackSpeed > 0 ? 1 / Base.AttackSpeed : 0;

    public double GoldOnDeath { get; set; }

    public double ExpOnDeath { get; set; }

    public override string ToString() => Name;
}

public class DragonSoul
{
    public string Name { get; set; } = "";

    public Dictionary<string, double> Tags { get; set; } = new();

    public double Tag(string name) => Tags.GetValueOrDefault(name);
}

public class NeutralRepository
{
    public const string FolderName = "Neutrals";
    public const string ScalingFileName = "_scaling.json";
    public const string SoulsFileName = "_souls.json";

    private readonly Dictionary<string, Neutral> _byInternalName;
    private readonly Dictionary<string, DragonSoul> _souls;

    public NeutralRepository(IEnumerable<Neutral> neutrals, NeutralScaling scaling, IDictionary<string, DragonSoul>? souls = null)
    {
        Scaling = scaling;
        _byInternalName = neutrals.ToDictionary(n => n.InternalName, StringComparer.OrdinalIgnoreCase);
        _souls = new Dictionary<string, DragonSoul>(souls ?? new Dictionary<string, DragonSoul>(), StringComparer.OrdinalIgnoreCase);
    }

    public NeutralScaling Scaling { get; }

    public IReadOnlyDictionary<string, DragonSoul> Souls => _souls;

    public DragonSoul? SoulFor(string? dragonType) =>
        dragonType is null ? null : _souls.GetValueOrDefault(dragonType);

    public IReadOnlyCollection<Neutral> All => _byInternalName.Values;

    public int Count => _byInternalName.Count;

    public Neutral? ByInternalName(string internalName) => _byInternalName.GetValueOrDefault(internalName);

    public IEnumerable<Neutral> InCamp(string camp) =>
        All.Where(n => string.Equals(n.Camp, camp, StringComparison.OrdinalIgnoreCase));

    public IEnumerable<string> Camps => All.Select(n => n.Camp).Distinct();

    public static NeutralRepository Load(string dataRoot)
    {
        var folder = Path.Combine(dataRoot, FolderName);

        var scaling = JsonSerializer.Deserialize<NeutralScaling>(
                          File.ReadAllText(Path.Combine(folder, ScalingFileName)), GameDataJson.Options)
                      ?? throw new InvalidDataException($"{ScalingFileName} is not valid.");

        var soulsPath = Path.Combine(folder, SoulsFileName);
        var souls = File.Exists(soulsPath)
            ? JsonSerializer.Deserialize<Dictionary<string, DragonSoul>>(File.ReadAllText(soulsPath), GameDataJson.Options)
            : null;

        var neutrals = Directory
            .EnumerateFiles(folder, "*.json")
            .Where(f => !Path.GetFileName(f).StartsWith('_'))
            .Select(Read)
            .ToList();

        return new NeutralRepository(neutrals, scaling, souls);
    }

    private static Neutral Read(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<Neutral>(File.ReadAllText(path), GameDataJson.Options)
                   ?? throw new InvalidDataException("empty file");
        }
        catch (Exception ex)
        {
            throw new InvalidDataException($"{Path.GetFileName(path)} is not a valid neutral: {ex.Message}", ex);
        }
    }
}

