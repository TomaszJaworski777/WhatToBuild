using System.Text.Json;
using System.Text.Json.Serialization;

namespace WhatToBuild.Data;

[JsonConverter(typeof(StatConverter))]
public abstract class Stat
{
    public abstract string Name { get; }

    public virtual bool IsFraction => false;

    public virtual bool TargetsEnemy => false;

    public override string ToString() => Name;
}

public sealed class AttackDamageStat : Stat
{
    public override string Name => "attackDamage";
}

public sealed class AbilityPowerStat : Stat
{
    public override string Name => "abilityPower";
}

public sealed class AbilityDamageStat : Stat
{
    public override string Name => "abilityDamage";
}

public sealed class HealthStat : Stat
{
    public override string Name => "health";
}

public sealed class ArmorStat : Stat
{
    public override string Name => "armor";
}

public sealed class MagicResistStat : Stat
{
    public override string Name => "magicResist";
}

public sealed class AttackSpeedStat : Stat
{
    public override string Name => "attackSpeed";
}

public sealed class AttackSpeedPercentStat : Stat
{
    public override string Name => "attackSpeedPercent";

    public override bool IsFraction => true;
}

public sealed class CritChanceStat : Stat
{
    public override string Name => "critChance";

    public override bool IsFraction => true;
}

public sealed class OmnivampPercentStat : Stat
{
    public override string Name => "omnivampPercent";

    public override bool IsFraction => true;
}

public sealed class HealAndShieldPowerPercentStat : Stat
{
    public override string Name => "healAndShieldPowerPercent";

    public override bool IsFraction => true;
}

public sealed class ArmorPenetrationPercentStat : Stat
{
    public override string Name => "armorPenetrationPercent";

    public override bool IsFraction => true;
}

public sealed class DamageAmpStat : Stat
{
    public override string Name => "damageAmp";

    public override bool IsFraction => true;
}

public sealed class AbilityPowerAmpStat : Stat
{
    public override string Name => "abilityPowerAmp";

    public override bool IsFraction => true;
}

public sealed class EnemyAttackSpeedPercentStat : Stat
{
    public override string Name => "enemyAttackSpeedPercent";

    public override bool IsFraction => true;

    public override bool TargetsEnemy => true;
}

public sealed class EnemyMagicDamageAmpStat : Stat
{
    public override string Name => "enemyMagicDamageAmp";

    public override bool IsFraction => true;

    public override bool TargetsEnemy => true;
}

public sealed class AttackRangeStat : Stat
{
    public override string Name => "attackRange";
}

public sealed class AdaptiveForceStat : Stat
{
    public override string Name => "adaptiveForce";
}

public sealed class AbilityHasteStat : Stat
{
    public override string Name => "abilityHaste";
}

public sealed class MoveSpeedPercentStat : Stat
{
    public override string Name => "moveSpeedPercent";

    public override bool IsFraction => true;
}

public sealed class TenacityPercentStat : Stat
{
    public override string Name => "tenacityPercent";

    public override bool IsFraction => true;
}

public sealed class ManaStat : Stat
{
    public override string Name => "mana";
}

public sealed class GoldStat : Stat
{
    public override string Name => "gold";
}

public static class Stats
{
    public static readonly Stat AttackDamage = new AttackDamageStat();
    public static readonly Stat AbilityPower = new AbilityPowerStat();
    public static readonly Stat AbilityDamage = new AbilityDamageStat();
    public static readonly Stat AdaptiveForce = new AdaptiveForceStat();
    public static readonly Stat AttackRange = new AttackRangeStat();
    public static readonly Stat AbilityHaste = new AbilityHasteStat();
    public static readonly Stat MoveSpeedPercent = new MoveSpeedPercentStat();
    public static readonly Stat TenacityPercent = new TenacityPercentStat();
    public static readonly Stat Mana = new ManaStat();
    public static readonly Stat Gold = new GoldStat();
    public static readonly Stat Health = new HealthStat();
    public static readonly Stat Armor = new ArmorStat();
    public static readonly Stat MagicResist = new MagicResistStat();
    public static readonly Stat AttackSpeed = new AttackSpeedStat();
    public static readonly Stat AttackSpeedPercent = new AttackSpeedPercentStat();
    public static readonly Stat CritChance = new CritChanceStat();
    public static readonly Stat OmnivampPercent = new OmnivampPercentStat();
    public static readonly Stat HealAndShieldPowerPercent = new HealAndShieldPowerPercentStat();
    public static readonly Stat ArmorPenetrationPercent = new ArmorPenetrationPercentStat();
    public static readonly Stat DamageAmp = new DamageAmpStat();
    public static readonly Stat AbilityPowerAmp = new AbilityPowerAmpStat();
    public static readonly Stat EnemyAttackSpeedPercent = new EnemyAttackSpeedPercentStat();
    public static readonly Stat EnemyMagicDamageAmp = new EnemyMagicDamageAmpStat();

    public static IReadOnlyList<Stat> All { get; } = new[]
    {
        AttackDamage, AbilityPower, AbilityDamage, AdaptiveForce, Health, Armor, MagicResist,
        AbilityHaste, MoveSpeedPercent, TenacityPercent, AttackRange, Mana, Gold,
        AttackSpeed, AttackSpeedPercent, CritChance, OmnivampPercent,
        HealAndShieldPowerPercent, ArmorPenetrationPercent,
        DamageAmp, AbilityPowerAmp, EnemyAttackSpeedPercent, EnemyMagicDamageAmp,
    };

    private static readonly Dictionary<string, Stat> ByName =
        All.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);

    public static Stat? Find(string name) => ByName.GetValueOrDefault(name);
}

public class StatConverter : JsonConverter<Stat>
{
    public override Stat Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var name = reader.GetString() ?? throw new JsonException("Stat name was null.");

        return Stats.Find(name) ?? throw new JsonException($"Unknown stat '{name}'.");
    }

    public override void Write(Utf8JsonWriter writer, Stat value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Name);
    }
}

