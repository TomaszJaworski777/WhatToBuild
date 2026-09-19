using WhatToBuild.Data;
using WhatToBuild.Modeling;

namespace WhatToBuild.Tests;

[TestClass]
public class AttackDpsTests
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

    private static ChampionState Target() => new(Garen, 11);

    [TestMethod]
    public void KrakenScalesWithAttackSpeed()
    {
        var slow = new ChampionState(Kindred, 11, [Kraken]);
        var fast = new ChampionState(Kindred, 11, [Kraken, Berserkers]);

        var slowOnHit = AttackDps.Against(slow, Target()).OnHit;
        var fastOnHit = AttackDps.Against(fast, Target()).OnHit;

        Assert.IsGreaterThan(slow.Stats.AttackSpeed, fast.Stats.AttackSpeed);
        Assert.AreEqual(fast.Stats.AttackSpeed / slow.Stats.AttackSpeed, fastOnHit / slowOnHit, 0.0001);
    }

    [TestMethod]
    public void KrakenProcsEveryThirdAttackAtTheRangedValue()
    {
        var kindred = new ChampionState(Kindred, 11, [Kraken]);
        var proc = DamageCalculator.Create(Target()).AdDamage(175 * 0.8).Attack().Run().HealthDamage;

        Assert.AreEqual(proc * kindred.Stats.AttackSpeed / 3, AttackDps.Against(kindred, Target()).OnHit, 0.001);
    }

    [TestMethod]
    public void SplashDamageDoesNotHitTheTarget()
    {
        var kindred = new ChampionState(Kindred, 11, [RavenousHydra]);

        Assert.AreEqual(0, AttackDps.Against(kindred, Target()).OnHit, 0.001);
    }

    [TestMethod]
    public void CritFromEffectsCounts()
    {
        var without = AttackDps.Against(new ChampionState(Kindred, 11), Target()).Attacks;
        var with = AttackDps.Against(new ChampionState(Kindred, 11, [YunTal]), Target()).Attacks;

        Assert.IsGreaterThan(without, with);
    }

    [TestMethod]
    public void KindredFightsWithQAttackSpeedScaledByMarks()
    {
        var calm = new ChampionState(Kindred, 11);
        var fighting = new ChampionState(Kindred, 11, combat: new CombatAssumption(10));

        var expectedBonus = Kindred.AttackSpeedRatio * (0.35 + 0.05 * 10);
        Assert.AreEqual(calm.Stats.AttackSpeed + expectedBonus, fighting.Stats.AttackSpeed, 0.0001);
    }
}
