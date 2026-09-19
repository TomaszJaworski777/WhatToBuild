using WhatToBuild.Data;
using WhatToBuild.Modeling;

namespace WhatToBuild.Tests;

[TestClass]
public class DamageCalculatorTests
{
    private static ChampionRepository _champions = null!;
    private static ItemRepository _items = null!;
    private static NeutralRepository _neutrals = null!;

    private static Champion Garen => _champions.ByRiotId(86)!;
    private static Item Randuins => _items.ByRiotId(3143)!;
    private static Item Steelcaps => _items.ByRiotId(3047)!;
    private static Item Warmogs => _items.ByRiotId(3083)!;
    private static Item SerpentsFang => _items.ByRiotId(6695)!;
    private static Item LordDominiks => _items.ByRiotId(3036)!;
    private static Item BlackCleaver => _items.ByRiotId(3071)!;

    [ClassInitialize]
    public static void Load(TestContext context)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "GameData");
        _champions = ChampionRepository.Load(root);
        _items = ItemRepository.Load(root);
        _neutrals = NeutralRepository.Load(root);
    }

    private static ChampionState GarenWith(params Item[] items) => new(Garen, 18, items);

    [TestMethod]
    public void DefenderKnowsItsOwnArmor()
    {
        var garen = GarenWith(Randuins);

        Assert.AreEqual(38 + 4.2 * 17 + 75, garen.Stats.Armor, 0.001);
        Assert.AreEqual(Randuins.Stats.Health, garen.BonusHealth, 0.001);
    }

    [TestMethod]
    public void ReadsLikeTheGame()
    {
        var garen = GarenWith(Randuins);

        var result = DamageCalculator.Create(garen).AdDamage(300).Crit().Run();

        var expected = 300 * (100 / (100 + garen.Stats.Armor)) * 0.7;
        Assert.AreEqual(expected, result.HealthDamage, 0.001);
    }

    [TestMethod]
    public void RanduinsOnlyAppliesToCrits()
    {
        var garen = GarenWith(Randuins);

        var crit = DamageCalculator.Create(garen).AdDamage(300).Crit().Run();
        var normal = DamageCalculator.Create(garen).AdDamage(300).Attack().Run();

        Assert.AreEqual(0.7, crit.HealthDamage / normal.HealthDamage, 0.0001);
    }

    [TestMethod]
    public void SteelcapsIgnoresAbilities()
    {
        var garen = GarenWith(Steelcaps);

        var attack = DamageCalculator.Create(garen).AdDamage(300).Attack().Run();
        var ability = DamageCalculator.Create(garen).AdDamage(300).Ability().Run();

        Assert.AreEqual(0.9, attack.HealthDamage / ability.HealthDamage, 0.0001);
    }

    [TestMethod]
    public void LordDominiksReadsBonusHealthFromTheDefender()
    {
        var tank = DamageCalculator.Create(GarenWith(Warmogs)).AdDamage(100).AttackerItems([LordDominiks]).Run();
        var squishy = DamageCalculator.Create(GarenWith()).AdDamage(100).AttackerItems([LordDominiks]).Run();

        Assert.AreEqual(1.15, tank.HealthDamage / squishy.HealthDamage, 0.0001);
    }

    [TestMethod]
    public void SerpentsFangCutsTheDefendersShields()
    {
        var withFang = DamageCalculator.Create(GarenWith().WithShield(400)).TrueDamage(600).AttackerItems([SerpentsFang]).Run();
        var without = DamageCalculator.Create(GarenWith().WithShield(400)).TrueDamage(600).Run();

        Assert.AreEqual(200, withFang.ShieldAbsorbed, 0.001);
        Assert.AreEqual(400, without.ShieldAbsorbed, 0.001);
    }

    [TestMethod]
    public void BlackCleaverShredsTheDefendersArmor()
    {
        var garen = GarenWith();

        var result = DamageCalculator.Create(garen).AdDamage(100).AttackerItems([BlackCleaver]).Run();

        Assert.AreEqual(garen.Stats.Armor * 0.7, result.EffectiveResist, 0.001);
    }

    [TestMethod]
    public void AttackerItemsContributeEffectsNotStats()
    {
        var garen = GarenWith();

        var itemsOnly = DamageCalculator.Create(garen).AdDamage(100).AttackerItems([SerpentsFang, LordDominiks]).Run();

        Assert.AreEqual(garen.Stats.Armor, itemsOnly.EffectiveResist, 0.001);

        var withStats = DamageCalculator.Create(garen).AdDamage(100)
            .Lethality(SerpentsFang.Stats.ArmorPenetrationFlat)
            .AttackerItems([SerpentsFang])
            .Run();

        Assert.AreEqual(garen.Stats.Armor - SerpentsFang.Stats.ArmorPenetrationFlat, withStats.EffectiveResist, 0.001);
    }

    [TestMethod]
    public void AttackerItemsCanBePassedTogether()
    {
        var garen = GarenWith(Warmogs).WithShield(400);

        var result = DamageCalculator.Create(garen).TrueDamage(600)
            .AttackerItems([SerpentsFang, LordDominiks, BlackCleaver])
            .Run();

        Assert.AreEqual(600 * 1.15 - 200, result.HealthDamage, 0.001);
        Assert.AreEqual(200, result.ShieldAbsorbed, 0.001);
    }

    [TestMethod]
    public void PenetrationWorksOnBothSides()
    {
        var garen = GarenWith();
        var armor = garen.Stats.Armor;
        var magicResist = garen.Stats.MagicResist;

        var physical = DamageCalculator.Create(garen).AdDamage(100).ArmorPenetration(0.35).Lethality(18).Run();
        var magic = DamageCalculator.Create(garen).ApDamage(100).MagicPenetration(0.4).FlatMagicPenetration(15).Run();

        Assert.AreEqual(armor * 0.65 - 18, physical.EffectiveResist, 0.001);
        Assert.AreEqual(magicResist * 0.6 - 15, magic.EffectiveResist, 0.001);
    }

    [TestMethod]
    public void ReductionWorksOnBothSides()
    {
        var garen = GarenWith();
        var armor = garen.Stats.Armor;
        var magicResist = garen.Stats.MagicResist;

        var physical = DamageCalculator.Create(garen).AdDamage(100).ArmorReduction(20).ArmorShred(0.3).Run();
        var magic = DamageCalculator.Create(garen).ApDamage(100).MagicResistReduction(20).MagicResistShred(0.3).Run();

        Assert.AreEqual((armor - 20) * 0.7, physical.EffectiveResist, 0.001);
        Assert.AreEqual((magicResist - 20) * 0.7, magic.EffectiveResist, 0.001);
    }

    [TestMethod]
    public void PenetrationDoesNotCrossOver()
    {
        var garen = GarenWith();

        var physical = DamageCalculator.Create(garen).AdDamage(100).MagicPenetration(0.4).FlatMagicPenetration(15).Run();
        var magic = DamageCalculator.Create(garen).ApDamage(100).ArmorPenetration(0.35).Lethality(18).Run();

        Assert.AreEqual(garen.Stats.Armor, physical.EffectiveResist, 0.001);
        Assert.AreEqual(garen.Stats.MagicResist, magic.EffectiveResist, 0.001);
    }

    [TestMethod]
    public void MagicDamageUsesTheDefendersMagicResist()
    {
        var garen = GarenWith();

        var result = DamageCalculator.Create(garen).ApDamage(100).Run();

        Assert.AreEqual(garen.Stats.MagicResist, result.EffectiveResist, 0.001);
    }

    [TestMethod]
    public void NeutralsAreDefendersToo()
    {
        var murkwolf = new NeutralState(_neutrals.ByInternalName("SRU_Murkwolf")!, _neutrals.Scaling);

        var result = DamageCalculator.Create(murkwolf).AdDamage(100).Attack().Run();

        Assert.AreEqual(42, result.EffectiveResist, 0.001);
        Assert.AreEqual(100.0 * 100 / 142, result.HealthDamage, 0.001);
    }

    [TestMethod]
    public void HealthPercentConditionsReadTheDefender()
    {
        var garen = GarenWith();

        Assert.AreEqual(1.0, garen.HealthPercent, 0.0001);

        garen.AtHealthPercent(0.3);

        Assert.AreEqual(0.3, garen.HealthPercent, 0.0001);
        Assert.AreEqual(garen.MaxHealth * 0.3, garen.CurrentHealth, 0.001);
    }

    [TestMethod]
    public void RunningTwiceGivesTheSameResult()
    {
        var calculation = DamageCalculator.Create(GarenWith(Randuins)).AdDamage(300).Crit().AttackerItems([BlackCleaver]);

        Assert.AreEqual(calculation.Run().HealthDamage, calculation.Run().HealthDamage, 0.0001);
    }

    [TestMethod]
    public void PipelineCanBeChanged()
    {
        var result = DamageCalculator.Create(GarenWith().WithShield(1000)).TrueDamage(100).Without<ShieldStep>().Run();

        Assert.AreEqual(100, result.HealthDamage, 0.001);
    }
}
