using System.Text.Json;
using WhatToBuild.Data;
using WhatToBuild.Game;
using WhatToBuild.Modeling;
using WhatToBuild.Planning;

namespace WhatToBuild.Forecasting;

public sealed class BuildArchetype
{
    /// <summary>Champion tag weights: the archetype's fit is the dot product with the champion's tags.</summary>
    public Dictionary<string, double> Match { get; set; } = new();

    public Dictionary<string, double> Positions { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Riot item ids in the usual buy order.</summary>
    public List<int> Build { get; set; } = new();
}

/// <summary>
/// Common build orders by champion archetype, from <c>GameData/Model/archetypes.json</c>. They are only a
/// prior about what enemies will own later, never advice: the spec keeps common builds out of our own
/// recommendations.
/// </summary>
public sealed class BuildArchetypes
{
    public const string FileName = "archetypes.json";

    public Dictionary<string, BuildArchetype> Archetypes { get; set; } = new();

    public double OwnedItemWeight { get; set; } = 1.5;

    public double OwnedComponentWeight { get; set; } = 0.4;

    public static BuildArchetypes Load(string dataRoot) =>
        JsonSerializer.Deserialize<BuildArchetypes>(
            File.ReadAllText(Path.Combine(dataRoot, ModelSettings.FolderName, FileName)), GameDataJson.Options)
        ?? throw new InvalidDataException($"{FileName} is empty.");
}

public sealed record ProjectedPurchase(Item Item, double At);

public sealed record ProjectedBuild(
    string Archetype,
    IReadOnlyList<Item> Items,
    IReadOnlyList<ProjectedPurchase> Purchases);

/// <summary>
/// Projects what an enemy will own once they have spent a given amount of gold: first the completed
/// items their components are heading for, then the rest of their archetype's build, in order, until the
/// gold runs out. Leftover gold goes into components of the next item, since those stats are real too.
/// </summary>
public sealed class BuildProjector
{
    private readonly BuildArchetypes _archetypes;
    private readonly ItemRepository _items;
    private readonly Dictionary<string, List<Item>> _builds;
    private readonly HashSet<Guid> _components;

    public BuildProjector(BuildArchetypes archetypes, ItemRepository items)
    {
        _archetypes = archetypes;
        _items = items;
        _components = items.All.SelectMany(i => i.BuildPath).ToHashSet();
        _builds = archetypes.Archetypes.ToDictionary(
            a => a.Key,
            a => a.Value.Build.Select(items.ByRiotId).OfType<Item>().ToList());
    }

    public string ArchetypeFor(PlayerState player)
    {
        var owned = player.Items.Select(i => i.Item).ToList();

        return _archetypes.Archetypes
            .Select(a => (a.Key, Score: Fit(player, a.Value, owned, _builds[a.Key])))
            .MaxBy(a => a.Score)
            .Key ?? "";
    }

    public IReadOnlyList<Item> BuildOf(string archetype) => _builds.GetValueOrDefault(archetype) ?? [];

    /// <param name="player">The enemy, with the items they own now.</param>
    /// <param name="gold">Gold they will have spent beyond what they own now.</param>
    /// <param name="spentBy">Game time at which they will have spent a given amount of that gold.</param>
    public ProjectedBuild Project(PlayerState player, double gold, Func<double, double> spentBy)
    {
        var archetype = ArchetypeFor(player);
        var build = _builds.GetValueOrDefault(archetype) ?? [];
        var inventory = player.Items.Where(i => i.Slot != 6).SelectMany(i => Enumerable.Repeat(i.Item, i.Count)).ToList();
        var purchases = new List<ProjectedPurchase>();
        var spent = 0.0;

        foreach (var target in Queue(inventory, build))
        {
            if (gold - spent <= 0)
            {
                break;
            }

            if (ItemRules.Conflict(inventory.Where(i => !IsConsumedBy(i, target)), target) is not null)
            {
                continue;
            }

            MakeRoom(inventory);

            var plan = ComponentPurchase.Plan(target, inventory, gold - spent, _items);
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

        return new ProjectedBuild(archetype, inventory, purchases);
    }

    private IEnumerable<Item> Queue(List<Item> inventory, List<Item> build)
    {
        var queued = new HashSet<Guid>();

        foreach (var component in inventory.ToList().Where(i => IsComponent(i)))
        {
            var target = build.FirstOrDefault(b => !inventory.Contains(b) && Contains(b, component));
            if (target is not null && queued.Add(target.Id))
            {
                yield return target;
            }
        }

        foreach (var item in build.Where(b => !inventory.Any(i => i.Id == b.Id)))
        {
            if (queued.Add(item.Id))
            {
                yield return item;
            }
        }
    }

    private double Fit(PlayerState player, BuildArchetype archetype, List<Item> owned, List<Item> build)
    {
        var tags = archetype.Match.Sum(m => m.Value * player.Champion.Tag(m.Key));
        var position = archetype.Positions.GetValueOrDefault(player.Position);
        var items = owned.Count(o => build.Any(b => b.Id == o.Id)) * _archetypes.OwnedItemWeight;
        var components = owned.Count(o => IsComponent(o) && build.Any(b => Contains(b, o))) * _archetypes.OwnedComponentWeight;

        return tags + position + items + components;
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

    private bool IsComponent(Item item) => _components.Contains(item.Id);

    private bool IsConsumedBy(Item owned, Item target) => Contains(target, owned);

    private bool Contains(Item parent, Item component) =>
        parent.BuildPath.Any(id => id == component.Id || _items.ById(id) is { } child && Contains(child, component));
}
