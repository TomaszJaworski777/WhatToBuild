using WhatToBuild.Data;
using WhatToBuild.Modeling;
using WhatToBuild.Modeling.Simulation;

namespace WhatToBuild.Planning;

public sealed record CampClear(string Camp, double Seconds);

public sealed record ClearResult(double KillSeconds, double WalkSeconds, IReadOnlyList<CampClear> Camps)
{
    public double TotalSeconds => KillSeconds + WalkSeconds;
}

public sealed class ClearSimulator
{
    private readonly NeutralRepository _neutrals;
    private readonly ModelSettings _settings;

    public ClearSimulator(NeutralRepository neutrals, ModelSettings settings)
    {
        _neutrals = neutrals;
        _settings = settings;
    }

    public ClearResult Clear(
        ChampionState us,
        AbilityRanks ranks,
        double marks,
        double gameTime,
        Func<IChampionKit?> newKit,
        IReadOnlyList<double> phases)
    {
        var camps = new List<CampClear>();
        var cleaveEffects = us.Items
            .SelectMany(i => i.Effects.Select(e => (Item: i, Effect: e)))
            .Where(x => x.Effect.Trigger == EffectTrigger.OnAttack && (x.Effect.Splash || x.Effect.Area))
            .ToList();

        foreach (var camp in _settings.Jungle.Camps)
        {
            var units = _neutrals.InCamp(camp)
                .Where(n => n.Base.Health > 1)
                .SelectMany(n => Enumerable.Repeat(n, Math.Max(1, n.CountPerCamp)))
                .OrderByDescending(n => n.Base.Health)
                .ToList();

            if (units.Count == 0)
            {
                continue;
            }

            var fights = units
                .Distinct()
                .ToDictionary(n => n, n => Kill(us, n, ranks, marks, gameTime, newKit, phases, cleaveEffects));

            var chipped = new double[units.Count];
            var seconds = 0.0;

            for (var i = 0; i < units.Count; i++)
            {
                var (killSeconds, _) = fights[units[i]];
                var health = Math.Max(1, new NeutralState(units[i], _neutrals.Scaling, gameTime).MaxHealth);
                var spent = killSeconds * Math.Max(0, 1 - chipped[i] / health);
                seconds += spent;

                for (var j = i + 1; j < units.Count; j++)
                {
                    chipped[j] += fights[units[j]].CleavePerSecond * spent;
                }
            }

            camps.Add(new CampClear(camp, seconds));
        }

        var walk = _settings.Jungle.WalkSeconds * us.Champion.Base.MoveSpeed / Math.Max(1, us.Stats.MoveSpeed);
        return new ClearResult(camps.Sum(c => c.Seconds), walk, camps);
    }

    private (double KillSeconds, double CleavePerSecond) Kill(
        ChampionState us,
        Neutral neutral,
        AbilityRanks ranks,
        double marks,
        double gameTime,
        Func<IChampionKit?> newKit,
        IReadOnlyList<double> phases,
        List<(Item Item, Effect Effect)> cleaveEffects)
    {
        var unit = new NeutralState(neutral, _neutrals.Scaling, gameTime);
        var result = FightResult.Average(phases
            .Select(phase => FightSimulator.Run(new FightSetup(us, unit, ranks, marks, _settings.Fight.MaxFightSeconds, phase), newKit()))
            .ToList());

        var killSeconds = result.TimeToKill ?? unit.MaxHealth / Math.Max(1, result.EffectiveDps);
        var attacksPerSecond = result.Seconds > 0 ? result.Attacks / result.Seconds : 0;

        var cleave = 0.0;
        foreach (var (item, effect) in cleaveEffects)
        {
            if (!unit.Satisfies(effect.When))
            {
                continue;
            }

            if (effect.Area)
            {
                cleave += result.DamageBySource.GetValueOrDefault(item.Name) / Math.Max(0.1, result.Seconds);
            }
            else if (AttackerHits.ForEffect(effect, us, unit) is { } hit)
            {
                var perAttack = hit.Run().HealthDamage;
                var rate = effect.Cooldown > 0 ? Math.Min(attacksPerSecond, 1 / effect.Cooldown) : attacksPerSecond;
                cleave += perAttack * (effect.EveryAttacks > 0 ? rate / effect.EveryAttacks : rate);
            }
        }

        return (killSeconds, cleave);
    }
}
