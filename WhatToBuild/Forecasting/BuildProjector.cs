using WhatToBuild.Data;
using WhatToBuild.Game;
using WhatToBuild.Modeling;

namespace WhatToBuild.Forecasting;

public sealed record ProjectedPurchase(Item Item, double At);

public sealed record ProjectedBuild(
    string Archetype,
    IReadOnlyList<Item> Items,
    IReadOnlyList<ProjectedPurchase> Purchases);

public sealed class BuildProjector
{
    public const string StandardBuild = "standard build";

    private readonly ItemRepository _items;
    private readonly MetaBuilds _meta;
    private readonly double _goldGrace;

    /// <param name="goldGrace">
    /// How short of an item an enemy can be and still be forecast to have it: the forecast is
    /// not that exact, and a finished item is closer to the truth than a pile of components.
    /// </param>
    public BuildProjector(ItemRepository items, MetaBuilds meta, double goldGrace = 0)
    {
        _items = items;
        _meta = meta;
        _goldGrace = goldGrace;
    }

    public IReadOnlyList<Item> BuildOf(Champion champion) => _meta.For(champion);

    public ProjectedBuild Project(PlayerState player, double gold, Func<double, double> spentBy)
    {
        var inventory = player.Items.Where(i => i.Slot != 6).SelectMany(i => Enumerable.Repeat(i.Item, i.Count)).ToList();
        var queue = _meta.For(player.Champion).Where(b => inventory.All(i => i.Id != b.Id)).ToList();
        var purchases = new List<ProjectedPurchase>();
        var spent = 0.0;

        foreach (var target in queue)
        {
            if (gold - spent <= 0)
            {
                break;
            }

            if (ItemRules.Conflict(inventory.Where(i => !Contains(target, i)), target) is not null)
            {
                continue;
            }

            MakeRoom(inventory);

            var plan = ComponentPurchase.Plan(target, inventory, gold - spent + _goldGrace, _items);
            if (!plan.CompletesTarget)
            {
                plan = ComponentPurchase.Plan(target, inventory, gold - spent, _items);
            }
            if (plan.Buy.Count == 0 || !ItemRules.IsLegal(plan.InventoryAfter) || ItemRules.Slots(plan.InventoryAfter) > ItemRules.InventorySlots)
            {
                break;
            }

            inventory = plan.InventoryAfter.ToList();
            spent += plan.Cost;

            if (!plan.CompletesTarget)
            {
                break;
            }

            purchases.Add(new ProjectedPurchase(target, spentBy(spent)));
        }

        return new ProjectedBuild(StandardBuild, inventory, purchases);
    }

    private static void MakeRoom(List<Item> inventory)
    {
        while (ItemRules.Slots(inventory) >= ItemRules.InventorySlots)
        {
            var filler = inventory.FirstOrDefault(ItemRules.IsFiller);
            if (filler is null)
            {
                return;
            }

            inventory.Remove(filler);
        }
    }

    private bool Contains(Item parent, Item component) =>
        parent.BuildPath.Any(id => id == component.Id || _items.ById(id) is { } child && Contains(child, component));
}
