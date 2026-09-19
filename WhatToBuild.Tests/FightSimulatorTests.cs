using WhatToBuild.Data;
using WhatToBuild.Modeling;
using WhatToBuild.Modeling.Simulation;

namespace WhatToBuild.Tests;

[TestClass]
public class FightSimulatorTests
{
    private static ItemRepository _items = null!;
    private static ChampionRepository _champions = null!;

    private static Champion Kindred => _champions.ByName("Kindred")!;
    private static Champion Garen => _champions.ByName("Garen")!;
    private static Item Kraken => _items.ByRiotId(6672)!;
    private static Item Berserkers => _items.ByRiotId(3006)!;
    private static Item RavenousHydra => _items.ByRiotId(3074)!;
    private static Item YunTal => _items.ByRiotId(3032)!;

    [ClassInitialize]
    public static void Load(TestContext context)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "GameData");
        _items = ItemRepository.Load(root);
        _champions = ChampionRepository.Load(root);
    }

    private static FightResult Fight(params Item[] items) =>
        FightSimulator.Run(new FightSetup(new ChampionState(Kindred, 11, items), new ChampionState(Garen, 11), AbilityRanks.None));

    [TestMethod]
    public void FightsUntilTheTargetDies()
    {
        var result = Fight(Kraken);

        Assert.IsNotNull(result.TimeToKill);
        Assert.AreEqual(new ChampionState(Garen, 11).MaxHealth, result.Damage, 150);
    }

    [TestMethod]
    public void KrakenProcsOnEveryThirdAttackAtTheRangedValue()
    {
        var result = Fight(Kraken);
        var proc = DamageCalculator.Create(new ChampionState(Garen, 11)).AdDamage(175 * 0.8).Attack().Run().HealthDamage;

        Assert.AreEqual(result.Attacks / 3 * proc, result.DamageBySource[Kraken.Name], 0.001);
    }

    [TestMethod]
    public void AttackSpeedKillsFaster()
    {
        Assert.IsLessThan(Fight(Kraken).TimeToKill!.Value, Fight(Kraken, Berserkers).TimeToKill!.Value);
    }

    [TestMethod]
    public void SplashDamageDoesNotHitTheTarget()
    {
        Assert.IsFalse(Fight(RavenousHydra).DamageBySource.ContainsKey(RavenousHydra.Name));
    }

    [TestMethod]
    public void CritFromEffectsCounts()
    {
        Assert.IsGreaterThan(Fight().EffectiveDps, Fight(YunTal).EffectiveDps);
    }

    [TestMethod]
    public void TheTargetIsLeftAsItWas()
    {
        var garen = new ChampionState(Garen, 11);
        garen.CurrentHealth = 500;

        FightSimulator.Run(new FightSetup(new ChampionState(Kindred, 11), garen, AbilityRanks.None));

        Assert.AreEqual(500, garen.CurrentHealth, 0.001);
    }

    [TestMethod]
    public void StopsAtTheTimeLimit()
    {
        var tank = new ChampionState(Garen, 18, [_items.ByRiotId(3083)!, _items.ByRiotId(3143)!]);
        var result = FightSimulator.Run(new FightSetup(new ChampionState(Kindred, 1), tank, AbilityRanks.None, MaxSeconds: 2));

        Assert.IsNull(result.TimeToKill);
        Assert.AreEqual(2, result.Seconds, Modeling.Simulation.Fight.Step * 1.5);
        Assert.AreEqual(result.Damage / result.Seconds, result.EffectiveDps, 0.001);
    }
}
