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
    bool Sustained = false,
    double LifeSteal = 0,
    double Omnivamp = 0,
    double StartDistance = 0,
    double AttackUptime = 1,
    double DashContactSeconds = 0,
    IReadOnlyList<string>? BurstCombo = null);

public sealed class Fight
{
    public const double Step = 0.02;

    private readonly Dictionary<string, double> _damage = new();
    private readonly Dictionary<DamageType, double> _damageByType = new();
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

        LifeSteal = setup.Attacker.Items.Sum(i => i.Stats.LifeStealPercent) + setup.LifeSteal;
        Omnivamp = setup.Attacker.Items.Sum(i => i.Stats.OmnivampPercent)
                   + attackerEffects.Where(e => StatCalculator.IsPermanentStatBuff(e) && e.Stat == Stats.OmnivampPercent).Sum(e => e.Amount)
                   + setup.Omnivamp;

        _sustainShields = sustain.Shields.Where(s => s.Amount > 0).ToList();
        FillShields();

        Distance = setup.StartDistance;

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

    public double LifeSteal { get; }

    public double Omnivamp { get; }

    /// <summary>
    /// What the attacker's life steal and omnivamp healed them for, hit by hit: life steal off
    /// attacks and on-hits, omnivamp off everything, both only on damage that landed.
    /// </summary>
    public double SelfHealed { get; private set; }

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

    /// <summary>How far the attacker is from the target: it walks in from the setup's start distance.</summary>
    public double Distance { get; set; }

    public double AttackRange => Attacker.Stats.AttackRange;

    public bool InAttackRange => InRange(null);

    /// <summary>Within <paramref name="range"/> of the target; no range means attack range.</summary>
    public bool InRange(double? range) => Distance <= (range ?? AttackRange) + 1e-9;

    /// <summary>
    /// A dash toward the target, as far as <paramref name="distance"/>, stopping at attack range.
    /// One that <paramref name="engages"/> lands on the target, re-engaging it wherever it had
    /// walked to: for <see cref="FightSetup.DashContactSeconds"/> no attack time goes to chasing it.
    /// </summary>
    public void Dash(double distance, bool engages = true)
    {
        Close(distance);
        if (engages)
        {
            PinTarget(Setup.DashContactSeconds);
        }
    }

    /// <summary>A step's walk toward the target, stopping at attack range.</summary>
    public void Walk(double seconds) => Close(Attacker.Stats.MoveSpeed * seconds);

    private void Close(double distance)
    {
        if (Distance > AttackRange)
        {
            Distance = Math.Max(AttackRange, Distance - distance);
        }
    }

    private double _targetPinnedUntil = double.MinValue;

    /// <summary>The attacker stays on the target for a while (Nasus's Wither, a knock-up, a dash onto it): no attack time goes to chasing it.</summary>
    public void PinTarget(double seconds) => _targetPinnedUntil = Math.Max(_targetPinnedUntil, Time + seconds);

    public bool TargetPinned => Time < _targetPinnedUntil;

    private double _targetDisabledUntil = double.MinValue;

    /// <summary>
    /// Hard crowd control on the target (a stun, knock-up or suppression): for its duration, cut
    /// by a champion's tenacity, the target can do nothing at all, not move, not attack, not cast.
    /// Overlapping locks count once.
    /// </summary>
    public void Disable(double seconds)
    {
        var tenacity = Target is ChampionState ? Math.Clamp(Target.Stats.Tenacity, 0, 1) : 0;
        var until = Time + seconds * (1 - tenacity);
        if (until <= _targetDisabledUntil)
        {
            return;
        }

        DisabledSeconds += until - Math.Max(Time, _targetDisabledUntil);
        _targetDisabledUntil = until;
        PinTarget(until - Time);
    }

    /// <summary>How long the target has been locked down so far.</summary>
    public double DisabledSeconds { get; private set; }

    /// <summary>The matchup's share of attack time, or all of it while the target cannot move.</summary>
    public double MatchupUptime => TargetPinned ? 1 : Setup.AttackUptime;

    public IReadOnlyDictionary<string, double> DamageBySource => _damage;

    public IReadOnlyDictionary<DamageType, double> DamageByType => _damageByType;

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

    /// <summary>An attack is off cooldown and nothing stops it: it goes out before any spell.</summary>
    public bool AttackReady { get; internal set; }

    /// <summary>The attack being wound up lands then; nothing is cast until it does.</summary>
    public double WindupUntil { get; internal set; } = double.MinValue;

    /// <summary>The attack's windup at the current attack speed.</summary>
    public double AttackWindup => Attacker.Champion.AttackWindup / Math.Max(0.01, AttackSpeed);

    /// <summary>
    /// A spell fits now: between attacks, never over an attack's windup or another spell's
    /// animation, and never ahead of an attack that is ready. So spells weave into the gaps
    /// the attack timer leaves, and none of them resets it unless it says so.
    /// </summary>
    public bool CanCast => CanCastInstant && !AttackReady;

    /// <summary>
    /// A spell with no animation fits now, even ahead of a ready attack, since it costs the attack
    /// no time (Nasus's Q, Vi's E: they empower the attack that is coming anyway). Still never
    /// over a windup or another spell's animation.
    /// </summary>
    public bool CanCastInstant => !TargetDead && Time >= WindupUntil && Time >= AttacksBlockedUntil;

    /// <summary>A spell's animation: no attacks and no other spells until it ends. The attack timer keeps running.</summary>
    public void Casting(double seconds) =>
        AttacksBlockedUntil = Math.Max(AttacksBlockedUntil, Time + seconds);

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

    private bool _attackReset;
    private double _armorShred;
    private double _armorShredUntil = double.MinValue;

    /// <summary>An ability that resets the basic attack timer: the next attack comes as soon as nothing blocks it.</summary>
    public void ResetAttack() => _attackReset = true;

    /// <summary>True once per <see cref="ResetAttack"/>; the simulator reads it after the kit's turn.</summary>
    public bool TakeAttackReset()
    {
        var reset = _attackReset;
        _attackReset = false;
        return reset;
    }

    /// <summary>A kit's own armor shred for a while (Vi's Denting Blows); a new one replaces the old.</summary>
    public void ShredArmor(double percent, double duration)
    {
        _armorShred = percent;
        _armorShredUntil = Time + duration;
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

        if (Time < _armorShredUntil && _armorShred > 0)
        {
            hit.ArmorShred(_armorShred);
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

        var vamp = Omnivamp + (result.Hit.IsAttack ? LifeSteal : 0);
        if (vamp > 0)
        {
            SelfHealed += vamp * Math.Min(damage, Math.Max(0, Target.CurrentHealth) + ShieldLeft);
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
        _damageByType[result.Hit.Type] = _damageByType.GetValueOrDefault(result.Hit.Type) + damage;
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
    double EarlyDamage = 0,
    double SelfHealed = 0,
    IReadOnlyDictionary<DamageType, double>? DamageByType = null,
    double Disabled = 0)
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
            EarlyDamage: results.Average(r => r.EarlyDamage),
            SelfHealed: results.Average(r => r.SelfHealed),
            DamageByType: results
                .SelectMany(r => r.DamageByType ?? new Dictionary<DamageType, double>())
                .GroupBy(d => d.Key)
                .ToDictionary(g => g.Key, g => g.Sum(d => d.Value) / results.Count),
            Disabled: results.Average(r => r.Disabled));
    }
}
