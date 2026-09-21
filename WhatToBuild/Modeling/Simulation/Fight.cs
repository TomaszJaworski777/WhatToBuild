using WhatToBuild.Data;

namespace WhatToBuild.Modeling.Simulation;

public sealed record AbilityRanks(int Q, int W, int E, int R)
{
    public static readonly AbilityRanks None = new(0, 0, 0, 0);
}

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
    TargetSustain? Sustain = null,
    bool Sustained = false);

public sealed class Fight
{
    public const double Step = 0.02;

    private readonly Dictionary<string, double> _damage = new();
    private readonly List<(double Bonus, double Until)> _attackSpeedBuffs = new();
    private readonly List<ShieldPool> _shields = new();
    private readonly List<Shield> _sustainShields;
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
            attackerEffects.Where(e => e.Kind == EffectKind.ShieldReduction)
                .Select(e => e.Amount * (setup.Attacker.Champion.IsRanged ? e.RangedMultiplier : 1))
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

        _sustainShields = sustain.Shields.Where(s => s.Amount > 0).ToList();
        FillShields();

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

    public double ShieldReduction { get; }

    public double GrievousWounds { get; }

    public double HealPerSecond { get; }

    public double ExternalArmorShred { get; }

    public double ExternalMagicResistShred { get; }

    public double ExecuteHealth { get; }

    public double ShieldTotal { get; }

    public double Healed { get; private set; }

    public double ShieldLeft => _shields.Sum(s => s.Amount);

    public double Pool => Math.Max(0, Target.CurrentHealth - ExecuteHealth) + ShieldLeft;

    public double InitialPool { get; }

    public int Kills { get; private set; }

    public double Removed => (Kills + (InitialPool > 0 ? (InitialPool - Pool) / InitialPool : 0)) * Target.MaxHealth;

    public void Respawn()
    {
        Kills++;
        TargetDead = false;
        Target.CurrentHealth = Target.MaxHealth;
        FillShields();
        _poolAtBatchStart = Pool;
        _batchDamage = 0;
        _currentTime = Time;
        _previousTime = Time;
    }

    private void FillShields()
    {
        _shields.Clear();
        foreach (var shield in _sustainShields)
        {
            _shields.Add(new ShieldPool(shield.Amount * (1 - ShieldReduction), shield.Versus));
        }
    }

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

    public IChampionKit? Kit { get; init; }

    public double AttacksBlockedUntil { get; set; }

    public const double BurstSeconds = 3;

    public double EarlyDamage { get; private set; }

    private bool _notifying;

    public const double SpellbladeWindow = 10;

    private readonly Dictionary<Effect, double> _castReadyAt = new();
    private double _spellbladeReadyAt;
    private double _spellbladeArmedUntil = double.MinValue;

    public void Cast()
    {
        foreach (var (item, effect) in Attacker.Items.SelectMany(i => i.Effects.Select(e => (Item: i, Effect: e))).Where(x => IsCastDamage(x.Effect)))
        {
            if (item.Groups.Contains("Spellblade"))
            {
                if (Time >= _spellbladeReadyAt)
                {
                    _spellbladeArmedUntil = Time + SpellbladeWindow;
                }

                continue;
            }

            if (Time < _castReadyAt.GetValueOrDefault(effect) || !Target.Satisfies(effect.When))
            {
                continue;
            }

            if (AttackerHits.ForEffect(effect, Attacker, Target) is { } hit)
            {
                Deal(item.Name, hit.Ability());
            }

            _castReadyAt[effect] = Time + effect.Cooldown;
        }
    }

    public void Spellblade()
    {
        if (Time > _spellbladeArmedUntil)
        {
            return;
        }

        _spellbladeArmedUntil = double.MinValue;
        foreach (var (item, effect) in Attacker.Items
                     .Where(i => i.Groups.Contains("Spellblade"))
                     .SelectMany(i => i.Effects.Select(e => (Item: i, Effect: e)))
                     .Where(x => IsCastDamage(x.Effect)))
        {
            if (AttackerHits.ForEffect(effect, Attacker, Target) is { } hit)
            {
                Deal(item.Name, hit.Attack());
            }

            _spellbladeReadyAt = Math.Max(_spellbladeReadyAt, Time + effect.Cooldown);
        }
    }

    private static bool IsCastDamage(Effect effect) =>
        effect.Trigger == EffectTrigger.OnAbility && IsDamage(effect);

    private static bool IsUltimateDamage(Effect effect) =>
        effect.Trigger == EffectTrigger.OnUltimate && IsDamage(effect);

    private static bool IsDamage(Effect effect) =>
        effect.Kind is EffectKind.PhysicalDamage or EffectKind.MagicDamage or EffectKind.TrueDamage or EffectKind.AdaptiveDamage;

    /// <summary>Kits call this when their ultimate hits enemies, for items gated on it.</summary>
    public void Ultimate()
    {
        foreach (var (item, effect) in Attacker.Items
                     .SelectMany(i => i.Effects.Select(e => (Item: i, Effect: e)))
                     .Where(x => IsUltimateDamage(x.Effect)))
        {
            if (Time < _castReadyAt.GetValueOrDefault(effect) || !Target.Satisfies(effect.When))
            {
                continue;
            }

            if (AttackerHits.ForEffect(effect, Attacker, Target) is { } hit)
            {
                Deal(item.Name, hit.Ability());
            }

            _castReadyAt[effect] = Time + effect.Cooldown;
        }
    }

    public void AddAttackSpeed(double bonus, double duration)
    {
        _attackSpeedBuffs.Add((bonus, Time + duration));
    }

    public double Cooldown(double seconds) => seconds * 100 / (100 + Attacker.Stats.AbilityHaste);

    public DamageCalculator Physical(double amount) => AttackerHits.Physical(Attacker, Target, amount);

    public DamageCalculator Magic(double amount) => AttackerHits.Magic(Attacker, Target, amount);

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

        hit.AttacksLanded(Attacks);

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
        if (Time <= BurstSeconds)
        {
            EarlyDamage += damage;
        }

        if (Kit is not null && !_notifying)
        {
            _notifying = true;
            Kit.OnDamage(this, source, damage);
            _notifying = false;
        }

        if (Target.CurrentHealth <= ExecuteHealth)
        {
            TargetDead = true;
            var share = _batchDamage > 0 ? _poolAtBatchStart / _batchDamage : 1;
            KilledAt ??= _previousTime + Math.Clamp(share, 0, 1) * (_currentTime - _previousTime);
            KillingBlow ??= source;
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
    double Shielded = 0,
    bool Sustained = false,
    double Kills = 0,
    double EarlyDamage = 0)
{
    public const double MinimumKillTime = 0.1;

    public double EffectiveDps => Sustained
        ? Seconds > 0 ? Math.Max(0, Progress ?? Damage) / Seconds : 0
        : TimeToKill is { } kill
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
            Shielded: results.Average(r => r.Shielded),
            Sustained: results[0].Sustained,
            Kills: results.Average(r => r.Kills),
            EarlyDamage: results.Average(r => r.EarlyDamage));
    }
}
