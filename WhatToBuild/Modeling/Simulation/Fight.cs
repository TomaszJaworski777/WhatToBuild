namespace WhatToBuild.Modeling.Simulation;

public sealed record AbilityRanks(int Q, int W, int E, int R)
{
    public static readonly AbilityRanks None = new(0, 0, 0, 0);
}

public sealed record FightSetup(
    ChampionState Attacker,
    Entity Target,
    AbilityRanks Ranks,
    double Stacks = 0,
    double MaxSeconds = 30,
    double AttackPhase = 0);

public sealed class Fight
{
    public const double Step = 0.02;

    private readonly Dictionary<string, double> _damage = new();
    private readonly List<(double Bonus, double Until)> _attackSpeedBuffs = new();
    private double _previousTime;
    private double _previousDamage;
    private double _currentTime;
    private double _currentDamage;

    public Fight(FightSetup setup)
    {
        Setup = setup;
    }

    public FightSetup Setup { get; }

    public ChampionState Attacker => Setup.Attacker;

    public Entity Target => Setup.Target;

    public AbilityRanks Ranks => Setup.Ranks;

    public double Stacks => Setup.Stacks;

    public double Time { get; set; }

    public int Attacks { get; set; }

    public bool TargetDead => Target.CurrentHealth <= 0;

    public double? KilledAt { get; private set; }

    public string? KillingBlow { get; private set; }

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

    public void Deal(string source, DamageCalculator hit, double multiplier = 1)
    {
        if (TargetDead)
        {
            return;
        }

        var damage = hit.Run().HealthDamage * multiplier;
        Target.CurrentHealth -= damage;
        _damage[source] = _damage.GetValueOrDefault(source) + damage;

        if (Time > _currentTime)
        {
            _previousTime = _currentTime;
            _previousDamage = _currentDamage;
            _currentTime = Time;
        }

        _currentDamage += damage;

        if (TargetDead)
        {
            var needed = Target.MaxHealth - _previousDamage;
            var share = _currentDamage > _previousDamage ? needed / (_currentDamage - _previousDamage) : 1;
            KilledAt = _previousTime + Math.Clamp(share, 0, 1) * (_currentTime - _previousTime);
            KillingBlow = source;
        }
    }
}

public sealed record FightResult(
    double? TimeToKill,
    double Seconds,
    int Attacks,
    double Damage,
    double TargetHealth,
    IReadOnlyDictionary<string, double> DamageBySource,
    string? KillingBlow = null)
{
    public const double MinimumKillTime = 0.1;

    public double EffectiveDps => TimeToKill is { } kill
        ? TargetHealth / Math.Max(MinimumKillTime, kill)
        : Seconds > 0 ? Damage / Seconds : 0;

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
                .ToDictionary(g => g.Key, g => g.Sum(d => d.Value) / results.Count));
    }
}
