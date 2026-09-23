using System.Text.Json;
using WhatToBuild.Data;

namespace WhatToBuild.SupportedChampions.Nasus;

public sealed class NasusKitData
{
    public const string FileName = "nasus.json";

    public string Champion { get; set; } = "";

    public QData Q { get; set; } = new();

    public WData W { get; set; } = new();

    public EData E { get; set; } = new();

    public RData R { get; set; } = new();

    public PassiveData Passive { get; set; } = new();

    public List<string> SkillOrder { get; set; } = new();

    public static NasusKitData Load(string path) =>
        JsonSerializer.Deserialize<NasusKitData>(File.ReadAllText(path), GameDataJson.Options)
        ?? throw new InvalidDataException($"{path} is empty.");

    public static double AtRank(List<double> values, int rank) =>
        values.Count == 0 ? 0 : values[Math.Clamp(rank, 0, values.Count - 1)];

    public sealed class QData
    {
        public List<double> Cooldown { get; set; } = new();
        public List<double> Damage { get; set; } = new();
    }

    public sealed class WData
    {
        public List<double> Cooldown { get; set; } = new();
        public double Duration { get; set; }
        public double SlowBase { get; set; }
        public List<double> SlowMax { get; set; } = new();
        public double AttackSpeedSlowRatio { get; set; }
    }

    public sealed class EData
    {
        public double Cooldown { get; set; }
        public List<double> Damage { get; set; } = new();
        public double ApRatio { get; set; }
        public List<double> TickDamage { get; set; } = new();
        public double TickApRatio { get; set; }
        public double Duration { get; set; }
        public List<double> ArmorShred { get; set; } = new();
        public double CastTime { get; set; }
    }

    public sealed class RData
    {
        public List<double> Cooldown { get; set; } = new();
        public double Duration { get; set; }
        public double CastTime { get; set; }
        public List<double> BonusHealth { get; set; } = new();
        public List<double> Resists { get; set; } = new();
        public List<double> MaxHealthPerSecond { get; set; } = new();
        public double MaxHealthPerSecondPerAp { get; set; }
        public double TickSeconds { get; set; }
        public double MonsterCap { get; set; }
        public double QCooldownReduction { get; set; }
    }

    public sealed class PassiveData
    {
        public double Lifesteal { get; set; }
        public List<List<double>> LifestealSteps { get; set; } = new();
    }
}
