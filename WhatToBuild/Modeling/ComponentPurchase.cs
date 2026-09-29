using WhatToBuild.Data;

namespace WhatToBuild.Modeling;

public sealed record PurchasePlan(
    Item Target,
    IReadOnlyList<Item> Buy,
    int Cost,
    bool CompletesTarget,
    IReadOnlyList<Item> InventoryAfter);

public interface IPurchaseScorer
{
    double Score(IReadOnlyList<Item> inventory);

    void Prepare(IReadOnlyList<IReadOnlyList<Item>> inventories)
    {
    }
}

public static class ComponentPurchase
{
    /// <summary>Worth per 1000 gold put toward the target now rather than left unspent.</summary>
    public const double ProgressWeight = 0.03;

    /// <summary>Worth per 1000 gold of basic components: a tie-break between equal buys.</summary>
    public const double BasicComponentWeight = 0.005;

    private const int MaxCandidates = 14;

    public static PurchasePlan Plan(Item target, IEnumerable<Item> owned, double gold, ItemRepository items, IPurchaseScorer? scorer = null)
    {
        var ownedList = owned.ToList();
        var pool = ownedList.GroupBy(i => i.Id).ToDictionary(g => g.Key, g => g.Count());
        var root = Build(target, pool, items);

        if (root.Satisfied)
        {
            return new PurchasePlan(target, [], 0, true, ownedList);
        }

        if (root.PriceToBuy <= gold)
        {
            return new PurchasePlan(target, [target], root.PriceToBuy, true, InventoryAfter(ownedList, [root]));
        }

        var candidates = new List<Node>();
        Collect(root, candidates, includeSelf: false);
        candidates = candidates.OrderByDescending(c => c.PriceToBuy).Take(MaxCandidates).ToList();

        var options = new List<(List<Node> Chosen, int Cost, List<Item> Inventory)>();

        for (var mask = 1; mask < 1 << candidates.Count; mask++)
        {
            var chosen = new List<Node>();
            var cost = 0;
            var valid = true;

            for (var i = 0; i < candidates.Count && valid; i++)
            {
                if ((mask & (1 << i)) == 0)
                {
                    continue;
                }

                var node = candidates[i];
                valid = chosen.All(c => !c.Contains(node) && !node.Contains(c));
                chosen.Add(node);
                cost += node.PriceToBuy;
            }

            if (!valid || cost > gold)
            {
                continue;
            }

            var inventory = InventoryAfter(ownedList, chosen);
            if (!ItemRules.IsLegal(inventory) || ItemRules.Slots(inventory) > Math.Max(ItemRules.InventorySlots, ItemRules.Slots(ownedList)))
            {
                continue;
            }

            options.Add((chosen, cost, inventory));
        }

        options = Undominated(options);

        scorer?.Prepare([ownedList, .. options.Select(o => (IReadOnlyList<Item>)o.Inventory)]);

        var best = new List<Node>();
        var bestCost = 0;
        var bestInventory = ownedList;
        var bestValue = Value(scorer, ownedList, []);

        foreach (var (chosen, cost, inventory) in options)
        {
            var value = Value(scorer, inventory, chosen);
            if (value > bestValue)
            {
                best = chosen;
                bestCost = cost;
                bestValue = value;
                bestInventory = inventory;
            }
        }

        return new PurchasePlan(
            target,
            best.OrderByDescending(n => n.PriceToBuy).Select(n => n.Item).ToList(),
            bestCost,
            false,
            bestInventory);
    }

    /// <summary>Above this many affordable options the dominance check is skipped; the scorer alone decides.</summary>
    private const int MaxDominanceOptions = 2000;

    /// <summary>
    /// Leaves out every buy that another affordable one contains: two Long Swords when the whole
    /// Caulfield's fits in the gold and the slots, or one piece when a second one fits beside it.
    /// A finished piece has at least the stats of its parts, so the bigger buy is never worse,
    /// and whatever the scorer thinks of the difference, you never leave the shop with gold that
    /// could have gone into the item.
    /// </summary>
    private static List<(List<Node> Chosen, int Cost, List<Item> Inventory)> Undominated(List<(List<Node> Chosen, int Cost, List<Item> Inventory)> options)
    {
        if (options.Count > MaxDominanceOptions)
        {
            return options;
        }

        static bool Covers(List<Node> bigger, List<Node> smaller) =>
            smaller.All(s => bigger.Any(b => ReferenceEquals(b, s) || b.Contains(s)));

        return options
            .Where(o => !options.Any(other => other.Cost > o.Cost && Covers(other.Chosen, o.Chosen)))
            .ToList();
    }

    private static double Value(IPurchaseScorer? scorer, IReadOnlyList<Item> inventory, IList<Node> bought)
    {
        // Gold put into the target is never lost: it is part of the item's price, and gold left
        // in the pocket does nothing until the next back. So spending more of it counts, whatever
        // the pieces are (a Caulfield's over two Long Swords that leave 400 gold unspent), and a
        // basic component only breaks what is left of a tie.
        var spent = bought.Sum(n => n.PriceToBuy);
        var basicGold = bought.Where(n => n.Item.BuildPath.Count == 0).Sum(n => n.Item.Cost);
        // Without a scorer (an enemy's forecast), stats go with gold: spend as much of it as
        // the item allows.
        var score = scorer?.Score(inventory) ?? inventory.Sum(i => i.Cost);

        return score * (1 + ProgressWeight * spent / 1000 + BasicComponentWeight * basicGold / 1000);
    }

    private static List<Item> InventoryAfter(List<Item> owned, IEnumerable<Node> bought)
    {
        var inventory = owned.ToList();

        foreach (var node in bought)
        {
            foreach (var used in node.Consumed())
            {
                inventory.Remove(used);
            }

            inventory.Add(node.Item);
        }

        return inventory;
    }

    private static Node Build(Item item, Dictionary<Guid, int> pool, ItemRepository items)
    {
        if (pool.TryGetValue(item.Id, out var count) && count > 0)
        {
            pool[item.Id] = count - 1;
            return new Node(item, [], Satisfied: true);
        }

        var children = item.BuildPath
            .Select(items.ById)
            .OfType<Item>()
            .Select(child => Build(child, pool, items))
            .ToList();

        return new Node(item, children, Satisfied: false);
    }

    private static void Collect(Node node, List<Node> into, bool includeSelf)
    {
        if (node.Satisfied)
        {
            return;
        }

        if (includeSelf)
        {
            into.Add(node);
        }

        foreach (var child in node.Children)
        {
            Collect(child, into, includeSelf: true);
        }
    }

    private sealed record Node(Item Item, IReadOnlyList<Node> Children, bool Satisfied)
    {
        public int PriceToBuy => Item.Cost - Children.Where(c => c.Satisfied).Sum(c => c.Item.Cost)
                                 - Children.Where(c => !c.Satisfied).Sum(c => c.OwnedValue);

        private int OwnedValue => Satisfied ? Item.Cost : Children.Sum(c => c.OwnedValue);

        public bool Contains(Node other) =>
            Children.Any(c => ReferenceEquals(c, other) || c.Contains(other));

        public IEnumerable<Item> Consumed() =>
            Children.SelectMany(c => c.Satisfied ? [c.Item] : c.Consumed());
    }
}
