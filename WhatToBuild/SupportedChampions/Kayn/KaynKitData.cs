using System.Text.Json;
using WhatToBuild.Data;

namespace WhatToBuild.SupportedChampions.Kayn;

public sealed class KaynKitData
{
    public const string FileName = "kayn.json";

    public string Champion { get; set; } = "";

    public QData Q { get; set; } = new();

    public WData W { get; set; } = new();

    public RData R { get; set; } = new();

    public PassiveData Passive { get; set; } = new();

    public List<string> SkillOrder { get; set; } = new();

    public double AttackUptime { get; set; } = 1;

    public static KaynKitData Load(string path) =>
        JsonSerializer.Deserialize<KaynKitData>(File.ReadAllText(path), GameDataJson.Options)
        ?? throw new InvalidDataException($"{path} is empty.");

    public static double AtRank(List<double> values, int rank) =>
        values.Count == 0 ? 0 : values[Math.Clamp(rank, 0, values.Count - 1)];

    public sealed class QData
    {
        /// <summary>Reach: the dash, then the spin around where he lands.</summary>
        public double Range { get; set; } = 500;

        public double Dash { get; set; } = 350;

        public List<double> Cooldown { get; set; } = new();
        public List<double> Damage { get; set; } = new();
        public double BonusAdRatio { get; set; }
        public int Hits { get; set; } = 2;
        public double CastTime { get; set; }
        public double DarkinTotalAdRatio { get; set; }
        public double DarkinMaxHealth { get; set; }
        public double DarkinMaxHealthPer100BonusAd { get; set; }
        public double MonsterBonus { get; set; }
        public List<double> MonsterCap { get; set; } = new();
    }

    public sealed class WData
    {
        public double Range { get; set; } = 700;

        /// <summary>Rhaast's Blade's Reach knocks up what it hits.</summary>
        public double DarkinKnockup { get; set; } = 1;

        public List<double> Cooldown { get; set; } = new();
        public List<double> Damage { get; set; } = new();
        public List<double> AssassinDamage { get; set; } = new();
        public double BonusAdRatio { get; set; }
        public double CastTime { get; set; }
        public double AssassinCastTime { get; set; }
    }

    public sealed class RData
    {
        /// <summary>How far away he can infest a marked target; he bursts out next to it.</summary>
        public double Range { get; set; } = 550;

        public List<double> Cooldown { get; set; } = new();
        public List<double> Damage { get; set; } = new();
        public double BonusAdRatio { get; set; }
        public double DarkinMaxHealth { get; set; }
        public double DarkinMaxHealthPerBonusAd { get; set; }
        public double DarkinHeal { get; set; }
        public double MinimumInfest { get; set; }
        public double InfestDuration { get; set; }
        public double CastTime { get; set; }
    }

    public sealed class PassiveData
    {
        public double DarkinHealing { get; set; }
        public double DarkinHealingPerBonusHealth { get; set; }
        public double AssassinMin { get; set; }
        public double AssassinMax { get; set; }
        public double AssassinDuration { get; set; }
        public double AssassinCooldown { get; set; }
    }
}
