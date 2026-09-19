using WhatToBuild.Data;
using WhatToBuild.Modeling;

namespace WhatToBuild.Tests;

[TestClass]
public class ItemRulesTests
{
    private static ItemRepository _items = null!;

    private static Item Item(int riotId) => _items.ByRiotId(riotId)!;

    private static Item Ldr => Item(3036);
    private static Item MortalReminder => Item(3033);
    private static Item LastWhisper => Item(3035);
    private static Item Steraks => Item(3053);
    private static Item Maw => Item(3156);
    private static Item Berserkers => Item(3006);
    private static Item Boots => Item(1001);
    private static Item Kraken => Item(6672);
    private static Item LongSword => Item(1036);
    private static Item LichBane => Item(3100);
    private static Item TrinityForce => Item(3078);
    private static Item Sheen => Item(3057);

    [ClassInitialize]
    public static void Load(TestContext context)
    {
        _items = ItemRepository.Load(Path.Combine(AppContext.BaseDirectory, "GameData"));
    }

    [TestMethod]
    public void OnlyOneLastWhisperItem()
    {
        var conflict = ItemRules.Conflict([Ldr], MortalReminder);

        Assert.IsNotNull(conflict);
        Assert.AreEqual("LastWhisper", conflict.Group);
        Assert.AreSame(Ldr, conflict.Owned);
    }

    [TestMethod]
    public void OnlyOneLifelineItem()
    {
        Assert.AreEqual("LifelineItems", ItemRules.Conflict([Steraks], Maw)?.Group);
    }

    [TestMethod]
    public void OnlyOnePairOfBoots()
    {
        Assert.AreEqual("Boots", ItemRules.Conflict([Berserkers], Boots)?.Group);
    }

    [TestMethod]
    public void UniqueItemsCannotBeDoubled()
    {
        var conflict = ItemRules.Conflict([Kraken], Kraken);

        Assert.IsNotNull(conflict);
        Assert.IsNull(conflict.Group);
    }

    [TestMethod]
    public void ComponentsCanBeDoubled()
    {
        Assert.IsTrue(ItemRules.IsLegal([LongSword, LongSword, LongSword]));
    }

    [TestMethod]
    public void BuildingFromAComponentOfTheSameGroupIsLegal()
    {
        var plan = ComponentPurchase.Plan(Ldr, [LastWhisper], double.MaxValue, _items);

        Assert.IsTrue(ItemRules.IsLegal(plan.InventoryAfter));
        CollectionAssert.DoesNotContain(plan.InventoryAfter.ToList(), LastWhisper);
        CollectionAssert.Contains(plan.InventoryAfter.ToList(), Ldr);
    }

    [TestMethod]
    public void ComponentsThatBreakARuleAreNotBought()
    {
        var plan = ComponentPurchase.Plan(TrinityForce, [LichBane], Sheen.Cost, _items);

        CollectionAssert.DoesNotContain(plan.Buy.ToList(), Sheen);
        Assert.IsTrue(ItemRules.IsLegal(plan.InventoryAfter));
    }

    [TestMethod]
    public void EveryExpensiveItemHasALimit()
    {
        foreach (var item in _items.All.Where(i => i.Cost >= 2000))
        {
            Assert.IsTrue(item.Unique || item.Groups.Count > 0, $"{item.Name} has no purchase limit.");
        }
    }

    [TestMethod]
    public void ScorerDecidesWhichComponentsToBuy()
    {
        var gold = 1000;
        var byCost = ComponentPurchase.Plan(Ldr, [], gold, _items);
        var byCrit = ComponentPurchase.Plan(Ldr, [], gold, _items, new CritScorer());

        Assert.IsGreaterThan(0, byCrit.InventoryAfter.Sum(i => i.Stats.CritChance));
        Assert.IsGreaterThanOrEqualTo(byCrit.Cost, byCost.Cost);
    }

    [TestMethod]
    public void EqualDamagePrefersBigBasicComponents()
    {
        var plan = ComponentPurchase.Plan(Ldr, [], 1300, _items, new FlatScorer());
        var basicGold = plan.Buy.Where(i => i.BuildPath.Count == 0).Sum(i => i.Cost);

        Assert.IsTrue(plan.Buy.All(i => i.BuildPath.Count == 0));
        Assert.IsGreaterThan(0, basicGold);
    }

    [TestMethod]
    public void DamageOutweighsTheBasicBonus()
    {
        var plan = ComponentPurchase.Plan(Ldr, [], 1000, _items, new CritScorer());

        Assert.IsGreaterThan(0, plan.InventoryAfter.Sum(i => i.Stats.CritChance));
    }

    private sealed class CritScorer : IPurchaseScorer
    {
        public double Score(IReadOnlyList<Item> inventory) => inventory.Sum(i => i.Stats.CritChance);
    }

    private sealed class FlatScorer : IPurchaseScorer
    {
        public double Score(IReadOnlyList<Item> inventory) => 1;
    }
}
