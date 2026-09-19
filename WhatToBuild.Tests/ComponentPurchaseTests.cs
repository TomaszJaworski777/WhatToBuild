using WhatToBuild.Data;
using WhatToBuild.Modeling;

namespace WhatToBuild.Tests;

[TestClass]
public class ComponentPurchaseTests
{
    private static ItemRepository _items = null!;

    private static Item Ldr => _items.ByRiotId(3036)!;
    private static Item LastWhisper => _items.ByRiotId(3035)!;
    private static Item Noonquiver => _items.ByRiotId(6670)!;

    [ClassInitialize]
    public static void Load(TestContext context)
    {
        _items = ItemRepository.Load(Path.Combine(AppContext.BaseDirectory, "GameData"));
    }

    [TestMethod]
    public void BuysTheWholeItemWhenAffordable()
    {
        var plan = ComponentPurchase.Plan(Ldr, [], 3300, _items);

        Assert.IsTrue(plan.CompletesTarget);
        Assert.AreSame(Ldr, plan.Buy.Single());
        Assert.AreEqual(3300, plan.Cost);
    }

    [TestMethod]
    public void OwnedComponentsLowerThePrice()
    {
        var plan = ComponentPurchase.Plan(Ldr, [Noonquiver], 2000, _items);

        Assert.IsTrue(plan.CompletesTarget);
        Assert.AreEqual(3300 - 1300, plan.Cost);
    }

    [TestMethod]
    public void FillsTheBudgetWithComponents()
    {
        var plan = ComponentPurchase.Plan(Ldr, [], 2750, _items);

        Assert.IsFalse(plan.CompletesTarget);
        CollectionAssert.AreEquivalent(new[] { LastWhisper, Noonquiver }, plan.Buy.ToList());
        Assert.AreEqual(2750, plan.Cost);
    }

    [TestMethod]
    public void NeverSpendsMoreThanItHas()
    {
        foreach (var gold in new[] { 0, 250, 400, 844, 1000, 1500, 2000, 3000 })
        {
            var plan = ComponentPurchase.Plan(Ldr, [], gold, _items);

            Assert.IsLessThanOrEqualTo(gold, plan.Cost, $"Overspent with {gold} gold.");
            Assert.AreEqual(plan.Buy.Sum(i => i.Cost), plan.Cost, $"Cost mismatch with {gold} gold.");
        }
    }

    [TestMethod]
    public void NeverBuysAComponentAndItsParent()
    {
        var plan = ComponentPurchase.Plan(Ldr, [], 3000, _items);

        foreach (var item in plan.Buy)
        {
            foreach (var other in plan.Buy.Where(o => o != item))
            {
                Assert.DoesNotContain(other.Id, item.BuildPath, $"{item.Name} and its component {other.Name} both bought.");
            }
        }
    }

    [TestMethod]
    public void NoGoldBuysNothing()
    {
        var plan = ComponentPurchase.Plan(Ldr, [], 0, _items);

        Assert.IsEmpty(plan.Buy);
        Assert.IsFalse(plan.CompletesTarget);
    }

    [TestMethod]
    public void AlreadyOwnedNeedsNothing()
    {
        var plan = ComponentPurchase.Plan(Ldr, [Ldr], 0, _items);

        Assert.IsTrue(plan.CompletesTarget);
        Assert.IsEmpty(plan.Buy);
    }
}
