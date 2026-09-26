using WhatToBuild.Data;

namespace WhatToBuild.Modeling.Simulation;

public interface IChampionKit
{
    void Update(Fight fight);

    void OnAttack(Fight fight);

    double AttackUptime => 1;

    /// <summary>The kit keeps the next attack back even though the timer is ready (a burst waiting on its next spell).</summary>
    bool HoldsAttack(Fight fight) => false;

    void OnDamage(Fight fight, string source, double damage)
    {
    }
}

/// <summary>
/// One champion acting in a fight, one <see cref="Fight.Step"/> at a time: its attack timer,
/// the windup of the attack in flight, its item on-hits and its kit's turn. It owns no clock and
/// never advances time, so a driver decides how many run side by side: <see cref="FightSimulator"/>
/// steps one against a target that does not fight back, and two stepped on the same clock, each
/// aimed at the other, fight each other.
/// </summary>
public sealed class ChampionSimulation
{
    private readonly List<OnHit> _onHits;
    private double _attackProgress;
    private double? _landsAt;

    public ChampionSimulation(FightSetup setup, IChampionKit? kit = null)
    {
        Kit = kit;
        Fight = new Fight(setup) { Kit = kit };
        _onHits = OnHitEffects(setup.Attacker);
        _attackProgress = 1 - setup.AttackPhase;
    }

    public Fight Fight { get; }

    public IChampionKit? Kit { get; }

    public double Time
    {
        get => Fight.Time;
        set => Fight.Time = value;
    }

    /// <summary>The target died and a fresh one takes its place: whatever was winding up is lost.</summary>
    public void Respawn()
    {
        Fight.Respawn();
        _landsAt = null;
        Fight.WindupUntil = double.MinValue;
    }

    /// <summary>Everything this champion does at the current time. The driver moves the clock on after it.</summary>
    public void Tick()
    {
        var fight = Fight;

        if (_landsAt is { } at && fight.Time >= at)
        {
            _landsAt = null;
            Land();
        }

        // The attack timer runs through windups and spell animations alike; an attack
        // that comes off cooldown during a cast waits for the cast to end.
        _attackProgress += fight.AttackSpeed * Fight.Step * (Kit?.AttackUptime ?? 1);
        if (fight.TakeAttackReset())
        {
            _attackProgress = Math.Max(_attackProgress, 1);
        }

        if (fight.Time < fight.AttacksBlockedUntil)
        {
            _attackProgress = Math.Min(_attackProgress, 1);
        }

        fight.AttackReady = CanAttack();
        Kit?.Update(fight);

        // Only a true reset (Vi's E, Nasus's Q, Kindred's Q) brings the next attack forward.
        if (fight.TakeAttackReset())
        {
            _attackProgress = Math.Max(_attackProgress, 1);
        }

        if (CanAttack())
        {
            _attackProgress -= 1;
            var windup = fight.AttackWindup;
            fight.WindupUntil = fight.Time + windup;
            if (windup < Fight.Step)
            {
                Land();
            }
            else
            {
                _landsAt = fight.Time + windup;
            }
        }

        fight.AttackReady = false;
    }

    /// <summary>What this champion did to its target so far.</summary>
    public FightResult Result()
    {
        var fight = Fight;
        var target = fight.Target;

        if (fight.Setup.Sustained)
        {
            return new FightResult(
                fight.KilledAt,
                fight.Time,
                fight.Attacks,
                fight.DamageBySource.Values.Sum(),
                target.MaxHealth,
                new Dictionary<string, double>(fight.DamageBySource),
                fight.KillingBlow,
                fight.Removed,
                fight.Healed,
                fight.ShieldTotal,
                Sustained: true,
                Kills: fight.Kills + (fight.TargetDead ? 1 : 0),
                EarlyDamage: fight.EarlyDamage,
                SelfHealed: fight.SelfHealed);
        }

        return new FightResult(
            fight.KilledAt,
            fight.KilledAt ?? fight.Time,
            fight.Attacks,
            fight.DamageBySource.Values.Sum(),
            target.MaxHealth,
            new Dictionary<string, double>(fight.DamageBySource),
            fight.KillingBlow,
            fight.InitialPool - fight.Pool,
            fight.Healed,
            fight.ShieldTotal,
            SelfHealed: fight.SelfHealed);
    }

    private bool CanAttack() =>
        _landsAt is null && _attackProgress >= 1 && Fight.Time >= Fight.AttacksBlockedUntil && !Fight.TargetDead
        && Kit?.HoldsAttack(Fight) != true;

    private void Land()
    {
        if (Fight.TargetDead)
        {
            return;
        }

        Attack();
        Kit?.OnAttack(Fight);
    }

    private void Attack()
    {
        var fight = Fight;
        fight.Attacks++;
        var attacker = fight.Attacker;
        var critChance = AttackerHits.CritChance(attacker);
        var ad = attacker.Stats.AttackDamage;

        fight.Deal(FightSimulator.Attacks, fight.Physical(ad).Attack(), 1 - critChance);
        fight.Spellblade();

        if (critChance > 0)
        {
            fight.Deal(FightSimulator.Attacks, fight.Physical(ad * AttackerHits.CritDamage(attacker)).Crit(), critChance);
        }

        foreach (var onHit in _onHits)
        {
            onHit.Attacks++;

            var ready = onHit.Effect.EveryAttacks > 0
                ? onHit.Attacks % (int)onHit.Effect.EveryAttacks == 0
                : fight.Time >= onHit.ReadyAt;

            if (!ready || !fight.Target.Satisfies(onHit.Effect.When))
            {
                continue;
            }

            if (AttackerHits.ForEffect(onHit.Effect, attacker, fight.Target) is { } hit)
            {
                fight.Deal(onHit.Source, hit.Attack());
            }

            onHit.ReadyAt = fight.Time + onHit.Effect.Cooldown;
        }
    }

    private static List<OnHit> OnHitEffects(ChampionState attacker) =>
        attacker.Items
            .SelectMany(i => i.Effects.Select(e => (Item: i, Effect: e)))
            .Where(x => x.Effect.Trigger == EffectTrigger.OnAttack && !x.Effect.Splash)
            .Where(x => x.Effect.Kind is EffectKind.PhysicalDamage or EffectKind.MagicDamage
                                     or EffectKind.TrueDamage or EffectKind.AdaptiveDamage)
            .Select(x => new OnHit(x.Item.Name, x.Effect))
            .ToList();

    private sealed class OnHit(string source, Effect effect)
    {
        public string Source { get; } = source;

        public Effect Effect { get; } = effect;

        public int Attacks { get; set; }

        public double ReadyAt { get; set; }
    }
}
