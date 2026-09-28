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

    /// <summary>
    /// Of the attacks he could throw while Siphoning Strike is on cooldown, the share he does:
    /// he mostly walks with the target and saves his attacks for the next Q.
    /// </summary>
    public double AttacksBetweenQs { get; set; } = 0.2;

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

        /// <summary>The share of the target's movement and attack speed Wither takes while it lasts: all but nothing of either.</summary>
        public double Removes { get; set; } = 1;

        public double Range { get; set; } = 700;
        public double CastTime { get; set; } = 0.25;
    }

    public sealed class EData
    {
        public double Range { get; set; } = 650;

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
