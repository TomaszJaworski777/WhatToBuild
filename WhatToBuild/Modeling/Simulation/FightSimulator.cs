namespace WhatToBuild.Modeling.Simulation;

/// <summary>
/// Drives one <see cref="ChampionSimulation"/> against a target that does not fight back: owns
/// the clock, the target's regeneration and, in a sustained fight, its respawns.
/// </summary>
public static class FightSimulator
{
    public const string Attacks = "Attacks";

    /// <summary>
    /// The fight, played with the kit's combo. Its <see cref="FightResult.EarlyDamage"/> is the
    /// burst: what the kit's burst sequence deals when played once from a fresh start, or, for a
    /// kit with no script, what lands in the first <see cref="Fight.BurstSeconds"/>.
    /// </summary>
    public static FightResult Run(FightSetup setup, IChampionKit? kit = null)
    {
        var result = Play(setup, kit, stop: _ => false);

        return kit is ScriptedKit { Playstyle.Burst.Count: > 0 } scripted
            ? result with { EarlyDamage = Burst(setup, scripted).Damage }
            : result;
    }

    /// <summary>The kit's burst sequence, played once from a fresh start against a fresh target.</summary>
    public static FightResult Burst(FightSetup setup, ScriptedKit kit)
    {
        var burst = kit.ForBurst();
        return Play(setup with { Sustained = false }, burst, stop: _ => burst.BurstDone);
    }

    private static FightResult Play(FightSetup setup, IChampionKit? kit, Func<Fight, bool> stop)
    {
        var target = setup.Target;
        var healthBefore = target.CurrentHealth;
        target.CurrentHealth = target.MaxHealth;

        try
        {
            var champion = new ChampionSimulation(setup, kit);
            var fight = champion.Fight;

            while (fight.Time < setup.MaxSeconds && (setup.Sustained || !fight.TargetDead) && !stop(fight))
            {
                if (fight.TargetDead)
                {
                    champion.Respawn();
                }

                fight.Regenerate(Fight.Step);
                champion.Tick();
                champion.Time += Fight.Step;
            }

            return champion.Result();
        }
        finally
        {
            target.CurrentHealth = healthBefore;
        }
    }
}
