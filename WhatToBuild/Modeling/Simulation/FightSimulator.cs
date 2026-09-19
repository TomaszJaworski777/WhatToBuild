using WhatToBuild.Data;

namespace WhatToBuild.Modeling.Simulation;

public interface IChampionKit
{
    void Update(Fight fight);

    void OnAttack(Fight fight);
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
            var fight = new Fight(setup);
            var onHits = OnHitEffects(setup.Attacker);
            var attackProgress = 1 - setup.AttackPhase;

            while (fight.Time < setup.MaxSeconds && !fight.TargetDead)
            {
                kit?.Update(fight);

                attackProgress += fight.AttackSpeed * Fight.Step;
                if (attackProgress >= 1 && !fight.TargetDead)
                {
                    attackProgress -= 1;
                    Attack(fight, onHits);
                    kit?.OnAttack(fight);
                }

                fight.Time += Fight.Step;
            }

            return new FightResult(
                fight.KilledAt,
                fight.KilledAt ?? fight.Time,
                fight.Attacks,
                fight.DamageBySource.Values.Sum(),
                target.MaxHealth,
                new Dictionary<string, double>(fight.DamageBySource),
                fight.KillingBlow);
        }
        finally
        {
            target.CurrentHealth = healthBefore;
        }
    }

    private static void Attack(Fight fight, List<OnHit> onHits)
    {
        fight.Attacks++;
        var attacker = fight.Attacker;
        var critChance = AttackerHits.CritChance(attacker);
        var ad = attacker.Stats.AttackDamage;

        fight.Deal(Attacks, fight.Physical(ad).Attack(), 1 - critChance);

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
