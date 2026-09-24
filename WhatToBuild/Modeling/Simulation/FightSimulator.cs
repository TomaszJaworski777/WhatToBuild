using WhatToBuild.Data;

namespace WhatToBuild.Modeling.Simulation;

public interface IChampionKit
{
    void Update(Fight fight);

    void OnAttack(Fight fight);

    double AttackUptime => 1;

    void OnDamage(Fight fight, string source, double damage)
    {
    }
}

public static class FightSimulator
{
    public const string Attacks = "Attacks";

    public static FightResult Run(FightSetup setup, IChampionKit? kit = null)
    {
        var target = setup.Target;
        var healthBefore = target.CurrentHealth;
        target.CurrentHealth = target.MaxHealth;

        try
        {
            var fight = new Fight(setup) { Kit = kit };
            var onHits = OnHitEffects(setup.Attacker);
            var attackProgress = 1 - setup.AttackPhase;
            double? landsAt = null;

            while (fight.Time < setup.MaxSeconds && (setup.Sustained || !fight.TargetDead))
            {
                if (fight.TargetDead)
                {
                    // The next target is a new attack: whatever was winding up is lost.
                    fight.Respawn();
                    landsAt = null;
                    fight.WindupUntil = double.MinValue;
                }

                fight.Regenerate(Fight.Step);

                if (landsAt is { } at && fight.Time >= at)
                {
                    landsAt = null;
                    Land(fight, onHits, kit);
                }

                // The attack timer runs through windups and spell animations alike; an attack
                // that comes off cooldown during a cast waits for the cast to end.
                attackProgress += fight.AttackSpeed * Fight.Step * (kit?.AttackUptime ?? 1);
                if (fight.TakeAttackReset())
                {
                    attackProgress = Math.Max(attackProgress, 1);
                }

                if (fight.Time < fight.AttacksBlockedUntil)
                {
                    attackProgress = Math.Min(attackProgress, 1);
                }

                fight.AttackReady = CanAttack(fight, landsAt, attackProgress);
                kit?.Update(fight);

                // Only a true reset (Vi's E, Nasus's Q, Kindred's Q) brings the next attack forward.
                if (fight.TakeAttackReset())
                {
                    attackProgress = Math.Max(attackProgress, 1);
                }

                if (CanAttack(fight, landsAt, attackProgress))
                {
                    attackProgress -= 1;
                    var windup = fight.AttackWindup;
                    fight.WindupUntil = fight.Time + windup;
                    if (windup < Fight.Step)
                    {
                        Land(fight, onHits, kit);
                    }
                    else
                    {
                        landsAt = fight.Time + windup;
                    }
                }

                fight.AttackReady = false;
                fight.Time += Fight.Step;
            }

            if (setup.Sustained)
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
        finally
        {
            target.CurrentHealth = healthBefore;
        }
    }

    private static bool CanAttack(Fight fight, double? landsAt, double attackProgress) =>
        landsAt is null && attackProgress >= 1 && fight.Time >= fight.AttacksBlockedUntil && !fight.TargetDead;

    private static void Land(Fight fight, List<OnHit> onHits, IChampionKit? kit)
    {
        if (fight.TargetDead)
        {
            return;
        }

        Attack(fight, onHits);
        kit?.OnAttack(fight);
    }

    private static void Attack(Fight fight, List<OnHit> onHits)
    {
        fight.Attacks++;
        var attacker = fight.Attacker;
        var critChance = AttackerHits.CritChance(attacker);
        var ad = attacker.Stats.AttackDamage;

        fight.Deal(Attacks, fight.Physical(ad).Attack(), 1 - critChance);
        fight.Spellblade();

        if (critChance > 0)
        {
            fight.Deal(Attacks, fight.Physical(ad * AttackerHits.CritDamage(attacker)).Crit(), critChance);
        }

        foreach (var onHit in onHits)
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
