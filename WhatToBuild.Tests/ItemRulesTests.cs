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
    public void HammerMoneyBuysTheHammerNotTwoLongSwords()
    {
        // Serylda's with 1100 gold and two free slots, every buy judged equal: the hammer (1050)
        // puts the most gold in, where a Pickaxe (875) or two Long Swords (700) leave it idle.
        var serylda = Item(6694);
        var tome = Item(1052);
        var plan = ComponentPurchase.Plan(serylda, [tome, tome, tome, tome], 1100, _items, new FlatScorer());

        CollectionAssert.AreEqual(new[] { "Caulfield's Warhammer" }, plan.Buy.Select(i => i.Name).ToList());
        Assert.AreEqual(1050, plan.Cost);

        // With damage counting, it still spends the gold: three Long Swords, not two.
        var byDamage = ComponentPurchase.Plan(serylda, [], 1100, _items, new AttackDamageScorer());
        Assert.IsGreaterThanOrEqualTo(1050, byDamage.Cost, string.Join(" + ", byDamage.Buy.Select(i => i.Name)));
    }

    [TestMethod]
    [DataRow(6694, DisplayName = "Serylda's Grudge")]
    [DataRow(6697, DisplayName = "Hubris")]
    public void EnoughForTheWholeHammerBuysTheHammer(int target)
    {
        // Two free slots and 1450 gold: the whole Caulfield's (two Long Swords and a Glowing
        // Mote) fits in one slot and is affordable, so it is bought, not just its two swords.
        var tome = Item(1052);
        var plan = ComponentPurchase.Plan(Item(target), [tome, tome, tome, tome], 1450, _items, new MostlyDamageScorer());
        var bought = string.Join(" + ", plan.Buy.Select(i => i.Name));

        Assert.Contains("Caulfield's Warhammer", plan.Buy.Select(i => i.Name).ToList(), bought);
        Assert.IsLessThan(400, 1450 - plan.Cost, $"{bought} leaves {1450 - plan.Cost} gold unspent.");
    }

    [TestMethod]
    public void TheWholeHammerBeatsItsSwordsWhateverTheScorerThinks()
    {
        // Your case: the hammer affordable, only its two Long Swords recommended. Even a scorer
        // that likes the swords a little better (haste counted slightly against) gets the hammer:
        // it holds both swords, so buying only them just leaves its gold in the pocket.
        var caulfield = Item(3133);
        var tome = Item(1052);
        var plan = ComponentPurchase.Plan(Item(6697), [tome, tome, tome, tome, tome], 1100, _items, new HasteAverseScorer());

        CollectionAssert.AreEqual(new[] { caulfield.Name }, plan.Buy.Select(i => i.Name).ToList(), string.Join(" + ", plan.Buy.Select(i => i.Name)));
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

    private sealed class AttackDamageScorer : IPurchaseScorer
    {
        public double Score(IReadOnlyList<Item> inventory) => 100 + inventory.Sum(i => i.Stats.AttackDamage);
    }

    /// <summary>Damage counts; ability haste barely does (a champion that hardly uses it).</summary>
    private sealed class MostlyDamageScorer : IPurchaseScorer
    {
        public double Score(IReadOnlyList<Item> inventory) =>
            1000 + inventory.Sum(i => 10 * i.Stats.AttackDamage + 0.5 * i.Stats.AbilityHaste);
    }

    /// <summary>Damage counts and haste is held very slightly against an item: the swords look best.</summary>
    private sealed class HasteAverseScorer : IPurchaseScorer
    {
        public double Score(IReadOnlyList<Item> inventory) =>
            1000 + inventory.Sum(i => 10 * i.Stats.AttackDamage - 0.1 * i.Stats.AbilityHaste);
    }

    private sealed class FlatScorer : IPurchaseScorer
    {
        public double Score(IReadOnlyList<Item> inventory) => 1;
    }
}
