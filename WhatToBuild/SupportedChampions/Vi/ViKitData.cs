using System.Text.Json;
using WhatToBuild.Data;

namespace WhatToBuild.SupportedChampions.Vi;

public sealed class ViKitData
{
    public const string FileName = "vi.json";

    public string Champion { get; set; } = "";

    public QData Q { get; set; } = new();

    public WData W { get; set; } = new();

    public EData E { get; set; } = new();

    public RData R { get; set; } = new();

    public PassiveData Passive { get; set; } = new();

    public List<string> SkillOrder { get; set; } = new();

    public static ViKitData Load(string path) =>
        JsonSerializer.Deserialize<ViKitData>(File.ReadAllText(path), GameDataJson.Options)
        ?? throw new InvalidDataException($"{path} is empty.");

    public static double AtRank(List<double> values, int rank) =>
        values.Count == 0 ? 0 : values[Math.Clamp(rank, 0, values.Count - 1)];

    public sealed class QData
    {
        /// <summary>How far the fully charged dash goes.</summary>
        public double Range { get; set; } = 725;

        /// <summary>How long the target is knocked back when she crashes into it.</summary>
        public double Knockup { get; set; } = 0.25;

        public List<double> Cooldown { get; set; } = new();
        public List<double> Damage { get; set; } = new();
        public double BonusAdRatio { get; set; }
        public double MaxDamageMultiplier { get; set; } = 1;
        public double ChargeSeconds { get; set; }

        /// <summary>The dash after the charge is let go (ViQMissile's mCastTime): no attacks until she lands.</summary>
        public double ReleaseSeconds { get; set; }
    }

    public sealed class WData
    {
        public List<double> MaxHealth { get; set; } = new();
        public double MaxHealthPerBonusAd { get; set; }
        public List<double> AttackSpeed { get; set; } = new();
        public double BuffDuration { get; set; }
        public double ArmorShred { get; set; }
        public int HitsToProc { get; set; } = 3;
        public double MonsterCap { get; set; }
    }

    public sealed class EData
    {
        public List<double> Damage { get; set; } = new();

        /// <summary>The empowered attack can critically strike: its extra damage crits with it.</summary>
        public bool CanCrit { get; set; }

        /// <summary>It is an attack, so on-hit effects already apply once; this is for any second application.</summary>
        public bool AppliesOnHit { get; set; }
        public double TotalAdRatio { get; set; }
        public double ApRatio { get; set; }
        public int Charges { get; set; } = 1;
        public List<double> Recharge { get; set; } = new();
        public double StaticCooldown { get; set; }
    }

    public sealed class RData
    {
        /// <summary>How far away she can lock on; she lands on the target.</summary>
        public double Range { get; set; } = 800;

        /// <summary>How long the target is knocked up once she lands: it can do nothing meanwhile.</summary>
        public double Knockup { get; set; } = 1.3;

        public List<double> Cooldown { get; set; } = new();
        public List<double> Damage { get; set; } = new();
        public double BonusAdRatio { get; set; }
        public double CastTime { get; set; }
        public double TravelSeconds { get; set; }
    }

    public sealed class PassiveData
    {
        public double ShieldMaxHealth { get; set; }
        public double CooldownLevel1 { get; set; }
        public double CooldownPerLevel { get; set; }
    }
}
