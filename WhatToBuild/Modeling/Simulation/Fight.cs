using WhatToBuild.Data;

namespace WhatToBuild.Modeling.Simulation;

public sealed record AbilityRanks(int Q, int W, int E, int R)
{
    public static readonly AbilityRanks None = new(0, 0, 0, 0);
}

/// <summary>
/// What keeps the target alive besides its health: healing per second (before Grievous Wounds) and
/// shields that absorb damage once per fight. The external values are what allies already apply to
/// the target, as expected values, so 40% Grievous Wounds on an ally who is there half the time is 0.2.
/// </summary>
public sealed record TargetSustain(
    double HealPerSecond,
    IReadOnlyList<Shield> Shields,
    double ExternalGrievousWounds = 0,
    double ExternalShieldReduction = 0,
    double ExternalArmorShred = 0,
    double ExternalMagicResistShred = 0)
{
    public static readonly TargetSustain None = new(0, []);

    public double ShieldTotal => Shields.Sum(s => s.Amount);
}

public sealed record FightSetup(
    ChampionState Attacker,
    Entity Target,
    AbilityRanks Ranks,
    double Stacks = 0,
    double MaxSeconds = 30,
    double AttackPhase = 0,
    TargetSustain? Sustain = null);

public sealed class Fight
{
    public const double Step = 0.02;

    private readonly Dictionary<string, double> _damage = new();
    private readonly List<(double Bonus, double Until)> _attackSpeedBuffs = new();
    private readonly List<ShieldPool> _shields = new();
    private double _previousTime;
    private double _currentTime;
    private double _poolAtBatchStart;
    private double _batchDamage;

    public Fight(FightSetup setup)
    {
        Setup = setup;

        var sustain = setup.Sustain ?? TargetSustain.None;
        var attackerEffects = setup.Attacker.Items.SelectMany(i => i.Effects).ToList();

        ShieldReduction = StackMultiplicatively(
            attackerEffects.Where(e => e.Kind == EffectKind.ShieldReduction).Select(e => e.Amount)
                .Append(sustain.ExternalShieldReduction));

        GrievousWounds = attackerEffects
            .Where(e => e.Kind == EffectKind.GrievousWounds)
            .Select(e => e.Amount)
            .Append(sustain.ExternalGrievousWounds)
            .Max();

        HealPerSecond = sustain.HealPerSecond * (1 - GrievousWounds);
        ExternalArmorShred = sustain.ExternalArmorShred;
        ExternalMagicResistShred = sustain.ExternalMagicResistShred;

        ExecuteHealth = setup.Target is ChampionState
            ? attackerEffects.Where(e => e.Kind == EffectKind.Execute).Select(e => e.Amount).DefaultIfEmpty(0).Max() * setup.Target.MaxHealth
            : 0;

        foreach (var shield in sustain.Shields.Where(s => s.Amount > 0))
        {
            _shields.Add(new ShieldPool(shield.Amount * (1 - ShieldReduction), shield.Versus));
        }

        ShieldTotal = _shields.Sum(s => s.Amount);
        _poolAtBatchStart = Pool;
        InitialPool = Pool;
    }

    public FightSetup Setup { get; }

    public ChampionState Attacker => Setup.Attacker;

    public Entity Target => Setup.Target;

    public AbilityRanks Ranks => Setup.Ranks;

    public double Stacks => Setup.Stacks;

    public double Time { get; set; }

    public int Attacks { get; set; }

    public bool TargetDead { get; private set; }

    public double? KilledAt { get; private set; }

    public string? KillingBlow { get; private set; }

    /// <summary>Fraction of the target's shields removed before they absorb anything (Serpent's Fang).</summary>
    public double ShieldReduction { get; }

    /// <summary>Fraction of the target's healing denied. Grievous Wounds does not stack, so this is the strongest source.</summary>
    public double GrievousWounds { get; }

    /// <summary>Healing the target gets per second, after Grievous Wounds.</summary>
    public double HealPerSecond { get; }

    /// <summary>Armor the target loses to allies' shred (Black Cleaver on a teammate), as an expected fraction.</summary>
    public double ExternalArmorShred { get; }

    public double ExternalMagicResistShred { get; }

    /// <summary>Health at or below which the target dies outright (The Collector).</summary>
    public double ExecuteHealth { get; }

    /// <summary>Total shield the target starts with, after shield reduction.</summary>
    public double ShieldTotal { get; }

    public double Healed { get; private set; }

    public double ShieldLeft => _shields.Sum(s => s.Amount);

    /// <summary>What still has to be dealt to kill the target: health above the execute line plus shields.</summary>
    public double Pool => Math.Max(0, Target.CurrentHealth - ExecuteHealth) + ShieldLeft;

    public double InitialPool { get; }

    public IReadOnlyDictionary<string, double> DamageBySource => _damage;

    public double DamageDealt => _damage.Values.Sum();

    public double AttackSpeedFromBuffs =>
        _attackSpeedBuffs.Where(b => b.Until > Time).Sum(b => b.Bonus);

    public double AttackSpeed =>
        Math.Min(StatCalculator.AttackSpeedCap,
            Attacker.Stats.AttackSpeed + Attacker.Champion.AttackSpeedRatio * AttackSpeedFromBuffs);

    public double BonusAttackSpeed =>
        Attacker.Champion.AttackSpeedRatio > 0
            ? (AttackSpeed - Attacker.Champion.Base.AttackSpeed) / Attacker.Champion.AttackSpeedRatio
            : 0;

    public void AddAttackSpeed(double bonus, double duration)
    {
        _attackSpeedBuffs.Add((bonus, Time + duration));
    }

    public double Cooldown(double seconds) => seconds * 100 / (100 + Attacker.Stats.AbilityHaste);

    public DamageCalculator Physical(double amount) => AttackerHits.Physical(Attacker, Target, amount);

    public DamageCalculator Magic(double amount) => AttackerHits.Magic(Attacker, Target, amount);

    /// <summary>Heals the target for one step, from the first hit on.</summary>
    public void Regenerate(double seconds)
    {
        if (TargetDead || HealPerSecond <= 0 || _damage.Count == 0)
        {
            return;
        }

        var before = Target.CurrentHealth;
        Target.CurrentHealth += HealPerSecond * seconds;
        Healed += Target.CurrentHealth - before;
    }

    public void Deal(string source, DamageCalculator hit, double multiplier = 1)
    {
        if (TargetDead)
        {
            return;
        }

        if (ExternalArmorShred > 0)
        {
            hit.ArmorShred(ExternalArmorShred);
        }

        if (ExternalMagicResistShred > 0)
        {
            hit.MagicResistShred(ExternalMagicResistShred);
        }

        var result = hit.Run();
        var damage = result.HealthDamage * multiplier;

        if (Time > _currentTime)
        {
            _previousTime = _currentTime;
            _currentTime = Time;
            _poolAtBatchStart = Pool;
            _batchDamage = 0;
        }

        var toHealth = damage;
        foreach (var shield in _shields)
        {
            if (toHealth <= 0)
            {
                break;
            }

            if (shield.Amount > 0 && result.Hit.Matches(shield.Versus))
            {
                var absorbed = Math.Min(shield.Amount, toHealth);
                shield.Amount -= absorbed;
                toHealth -= absorbed;
            }
        }

        Target.CurrentHealth -= toHealth;
        _damage[source] = _damage.GetValueOrDefault(source) + damage;
        _batchDamage += damage;

        if (Target.CurrentHealth <= ExecuteHealth)
        {
            TargetDead = true;
            var share = _batchDamage > 0 ? _poolAtBatchStart / _batchDamage : 1;
            KilledAt = _previousTime + Math.Clamp(share, 0, 1) * (_currentTime - _previousTime);
            KillingBlow = source;
        }
    }

    private static double StackMultiplicatively(IEnumerable<double> sources) =>
        1 - sources.Aggregate(1.0, (remaining, source) => remaining * (1 - Math.Clamp(source, 0, 1)));

    private sealed class ShieldPool(double amount, DamageSource versus)
    {
        public double Amount { get; set; } = amount;

        public DamageSource Versus { get; } = versus;
    }
}

public sealed record FightResult(
    double? TimeToKill,
    double Seconds,
    int Attacks,
    double Damage,
    double TargetHealth,
    IReadOnlyDictionary<string, double> DamageBySource,
    string? KillingBlow = null,
    double? Progress = null,
    double Healed = 0,
    double Shielded = 0)
{
    public const double MinimumKillTime = 0.1;

    /// <summary>
    /// Target health removed per second. With a kill it is the target's health over the time to kill,
    /// so shields and healing slow it down. Without one it is net progress: damage minus what was
    /// healed back, so a target that out-heals you scores close to zero rather than your raw damage.
    /// </summary>
    public double EffectiveDps => TimeToKill is { } kill
        ? TargetHealth / Math.Max(MinimumKillTime, kill)
        : Seconds > 0 ? Math.Max(0, Progress ?? Damage) / Seconds : 0;

    public static FightResult Average(IReadOnlyList<FightResult> results)
    {
        var killed = results.All(r => r.TimeToKill is not null);

        return new FightResult(
            killed ? results.Average(r => r.TimeToKill!.Value) : null,
            results.Average(r => r.Seconds),
            (int)Math.Round(results.Average(r => r.Attacks)),
            results.Average(r => r.Damage),
            results[0].TargetHealth,
            results
                .SelectMany(r => r.DamageBySource)
                .GroupBy(d => d.Key)
                .ToDictionary(g => g.Key, g => g.Sum(d => d.Value) / results.Count),
            Progress: results.All(r => r.Progress is not null) ? results.Average(r => r.Progress!.Value) : null,
            Healed: results.Average(r => r.Healed),
            Shielded: results.Average(r => r.Shielded));
    }
}
