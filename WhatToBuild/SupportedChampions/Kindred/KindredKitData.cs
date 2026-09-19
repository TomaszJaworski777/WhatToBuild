using System.Text.Json;
using WhatToBuild.Data;

namespace WhatToBuild.SupportedChampions.Kindred;

public sealed class KindredKitData
{
    public const string FileName = "kindred.json";

    public string Champion { get; set; } = "";

    public QData Q { get; set; } = new();

    public WData W { get; set; } = new();

    public EData E { get; set; } = new();

    public RData R { get; set; } = new();

    public List<string> SkillOrder { get; set; } = new();

    public static KindredKitData Load(string path) =>
        JsonSerializer.Deserialize<KindredKitData>(File.ReadAllText(path), GameDataJson.Options)
        ?? throw new InvalidDataException($"{path} is empty.");

    public static double AtRank(List<double> values, int rank) =>
        values.Count == 0 ? 0 : values[Math.Clamp(rank, 0, values.Count - 1)];

    public sealed class QData
    {
        public List<double> Damage { get; set; } = new();
        public double BonusAdRatio { get; set; }
        public double Cooldown { get; set; }
        public List<double> CooldownInW { get; set; } = new();
        public double AttackSpeed { get; set; }
        public double AttackSpeedPerMark { get; set; }
        public double AttackSpeedDuration { get; set; }
    }

    public sealed class WData
    {
        public List<double> Cooldown { get; set; } = new();
        public double ZoneDuration { get; set; }
        public List<double> Damage { get; set; } = new();
        public double BonusAdRatio { get; set; }
        public double ApRatio { get; set; }
        public double CurrentHealth { get; set; }
        public double CurrentHealthPerMark { get; set; }
        public double WolfAttackSpeed { get; set; }
        public double WolfAttackSpeedRatio { get; set; }
        public double WolfAttackSpeedPerLevel { get; set; }
        public double WolfShareOfBonusAttackSpeed { get; set; }
        public double MonsterBonusDamage { get; set; }
    }

    public sealed class EData
    {
        public List<double> Cooldown { get; set; } = new();
        public int AttacksAfterCast { get; set; }
        public double Window { get; set; }
        public List<double> Damage { get; set; } = new();
        public double BonusAdRatio { get; set; }
        public double MissingHealth { get; set; }
        public double MissingHealthPerMark { get; set; }
        public double CritChanceRatio { get; set; }
        public double MonsterCap { get; set; } = double.PositiveInfinity;
    }

    public sealed class RData
    {
        public double Duration { get; set; }
        public double MinimumHealthPercent { get; set; }
        public List<double> Heal { get; set; } = new();
        public List<double> Cooldown { get; set; } = new();
    }
}
