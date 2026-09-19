using WhatToBuild.Data;
using WhatToBuild.Modeling;

namespace WhatToBuild.Tests;

[TestClass]
public class StatCalculatorTests
{
    private static ChampionRepository _champions = null!;
    private static ItemRepository _items = null!;
    private static NeutralRepository _neutrals = null!;

    private static Champion Kindred => _champions.ByRiotId(203)!;

    [ClassInitialize]
    public static void Load(TestContext context)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "GameData");
        _champions = ChampionRepository.Load(root);
        _items = ItemRepository.Load(root);
        _neutrals = NeutralRepository.Load(root);
    }

    [TestMethod]
    [DataRow(1, 0.0)]
    [DataRow(2, 0.72)]
    [DataRow(18, 17.0)]
    [DataRow(25, 17.0)]
    [DataRow(0, 0.0)]
    public void GrowthFollowsTheGameCurve(int level, double expected)
    {
        Assert.AreEqual(expected, StatCalculator.GrowthMultiplier(level), 0.0001);
    }

    [TestMethod]
    public void LevelOneIsBaseStats()
    {
        var stats = StatCalculator.ForChampion(Kindred, 1, []);

        Assert.AreEqual(595, stats.Health, 0.001);
        Assert.AreEqual(29, stats.Armor, 0.001);
        Assert.AreEqual(65, stats.AttackDamage, 0.001);
        Assert.AreEqual(0.625, stats.AttackSpeed, 0.0001);
    }

    [TestMethod]
    public void GrowthIsNotLinear()
    {
        var stats = StatCalculator.ForChampion(Kindred, 2, []);

        Assert.AreEqual(29 + 4.7 * 0.72, stats.Armor, 0.001);
    }

    [TestMethod]
    public void LevelEighteenAddsSeventeenFullSteps()
    {
        var stats = StatCalculator.ForChampion(Kindred, 18, []);

        Assert.AreEqual(595 + 104 * 17, stats.Health, 0.001);
        Assert.AreEqual(29 + 4.7 * 17, stats.Armor, 0.001);
        Assert.AreEqual(65 + 3.25 * 17, stats.AttackDamage, 0.001);
    }

    [TestMethod]
    public void BonusAttackSpeedScalesOffTheRatio()
    {
        var stats = StatCalculator.ForChampion(Kindred, 18, []);

        Assert.AreEqual(0.625 + 0.625 * 0.035 * 17, stats.AttackSpeed, 0.0001);
    }

    [TestMethod]
    public void AttackSpeedIsCapped()
    {
        var berserkers = _items.ByRiotId(3006)!;
        var items = Enumerable.Repeat(berserkers, 20);

        Assert.AreEqual(StatCalculator.AttackSpeedCap, StatCalculator.ForChampion(Kindred, 18, items).AttackSpeed, 0.0001);
    }

    [TestMethod]
    public void ItemStatsAreAdded()
    {
        var infinityEdge = _items.ByRiotId(3031)!;

        Assert.AreEqual(65 + 75, StatCalculator.ForChampion(Kindred, 1, [infinityEdge]).AttackDamage, 0.001);
    }

    [TestMethod]
    public void PermanentStatBuffsAreApplied()
    {
        var elixirOfIron = _items.ByRiotId(2138)!;

        Assert.AreEqual(595 + 300, StatCalculator.ForChampion(Kindred, 1, [elixirOfIron]).Health, 0.001);
    }

    [TestMethod]
    public void ConditionalStatBuffsAreNot()
    {
        var bloodmail = _items.ByRiotId(2501)!;
        var expected = 65 + bloodmail.Stats.AttackDamage;

        Assert.AreEqual(expected, StatCalculator.ForChampion(Kindred, 1, [bloodmail]).AttackDamage, 0.001);
    }

    [TestMethod]
    public void MountainStacksMultiplyTotalResists()
    {
        var mountain = _neutrals.DragonFor("Earth")!.BuffsFor(2);
        var plain = StatCalculator.ForChampion(Kindred, 18, []);

        var buffed = StatCalculator.ForChampion(Kindred, 18, [], mountain);

        Assert.AreEqual(plain.Armor * 1.10, buffed.Armor, 0.001);
        Assert.AreEqual(plain.MagicResist * 1.10, buffed.MagicResist, 0.001);
    }

    [TestMethod]
    public void InfernalStacksIncludeItemStats()
    {
        var infernal = _neutrals.DragonFor("Fire")!.BuffsFor(3);
        var infinityEdge = _items.ByRiotId(3031)!;

        var buffed = StatCalculator.ForChampion(Kindred, 1, [infinityEdge], infernal);

        Assert.AreEqual((65 + 75) * 1.09, buffed.AttackDamage, 0.001);
    }

    [TestMethod]
    public void HextechAttackSpeedScalesOffTheRatio()
    {
        var hextech = _neutrals.DragonFor("Hextech")!.BuffsFor(2);

        var buffed = StatCalculator.ForChampion(Kindred, 1, [], hextech);

        Assert.AreEqual(0.625 + 0.625 * 0.10, buffed.AttackSpeed, 0.0001);
    }

    [TestMethod]
    public void NeutralsScaleWithTime()
    {
        var murkwolf = _neutrals.ByInternalName("SRU_Murkwolf")!;

        var early = StatCalculator.ForNeutral(murkwolf, _neutrals.Scaling, 0);
        var late = StatCalculator.ForNeutral(murkwolf, _neutrals.Scaling, 1800);

        Assert.AreEqual(1600, early.Health, 0.001);
        Assert.IsGreaterThan(early.Health, late.Health);
        Assert.AreEqual(early.Armor, late.Armor, 0.001);
    }
}
