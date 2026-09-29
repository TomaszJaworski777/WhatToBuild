using System.Text.Json;
using WhatToBuild.Data;

namespace WhatToBuild.SupportedChampions.Talon;

public sealed class TalonKitData
{
    public const string FileName = "talon.json";

    public string Champion { get; set; } = "";

    public QData Q { get; set; } = new();

    public WData W { get; set; } = new();

    public RData R { get; set; } = new();

    public PassiveData Passive { get; set; } = new();

    public List<string> SkillOrder { get; set; } = new();

    public static TalonKitData Load(string path) =>
        JsonSerializer.Deserialize<TalonKitData>(File.ReadAllText(path), GameDataJson.Options)
        ?? throw new InvalidDataException($"{path} is empty.");

    public static double AtRank(List<double> values, int rank) =>
        values.Count == 0 ? 0 : values[Math.Clamp(rank, 0, values.Count - 1)];

    /// <summary>Noxian Diplomacy: a leap to the target, or a critical strike when already in melee range.</summary>
    public sealed class QData
    {
        public List<double> Cooldown { get; set; } = new();
        public List<double> Damage { get; set; } = new();

        /// <summary>Whether the melee stab applies on-hit effects, as an attack would.</summary>
        public bool AppliesOnHit { get; set; }
        public double BonusAdRatio { get; set; }

        /// <summary>In melee range the stab critically strikes for this, plus any crit damage beyond the base.</summary>
        public double MeleeCritMultiplier { get; set; }

        /// <summary>How close counts as melee range for the critical strike.</summary>
        public double MeleeRange { get; set; }

        /// <summary>How far he leaps to the target.</summary>
        public double Range { get; set; }

        public double CastTime { get; set; }
    }

    /// <summary>Rake: blades out, then back after a delay for more damage.</summary>
    public sealed class WData
    {
        public List<double> Cooldown { get; set; } = new();
        public List<double> Damage { get; set; } = new();
        public double BonusAdRatio { get; set; }
        public List<double> ReturnDamage { get; set; } = new();
        public double ReturnBonusAdRatio { get; set; }

        /// <summary>Seconds from the throw to the returning blades passing the target.</summary>
        public double ReturnSeconds { get; set; }

        public double Range { get; set; }
        public double CastTime { get; set; }
    }

    /// <summary>Shadow Assault: a ring of blades out, invisibility, and the blades back.</summary>
    public sealed class RData
    {
        public List<double> Cooldown { get; set; } = new();
        public List<double> Damage { get; set; } = new();
        public double BonusAdRatio { get; set; }

        /// <summary>How long he stays invisible; the blades return when it ends, or at his target when he attacks or casts Q.</summary>
        public double Duration { get; set; }

        /// <summary>How far the ring of blades reaches.</summary>
        public double Range { get; set; }

        public double CastTime { get; set; }
    }

    /// <summary>Blade's End: abilities stack on the target, and an attack at full stacks makes it bleed.</summary>
    public sealed class PassiveData
    {
        public int Stacks { get; set; }
        public double StackDuration { get; set; }

        /// <summary>The bleed's base damage at level 1 and at 18, straight in between.</summary>
        public double BleedLevel1 { get; set; }
        public double BleedLevel18 { get; set; }
        public double BleedBonusAdRatio { get; set; }
    }
}
