using WhatToBuild.Data;

namespace WhatToBuild.Modeling;

public sealed record ItemConflict(Item Candidate, Item Owned, string? Group)
{
    public string Reason => Group is null
        ? $"you can only own one {Candidate.Name}"
        : $"only one {GroupLabel(Group)} item, and you have {Owned.Name}";

    private static string GroupLabel(string group) => ItemRules.GroupLabels.GetValueOrDefault(group, group);
}

public static class ItemRules
{
    public const int InventorySlots = 6;

    public static readonly IReadOnlyDictionary<string, string> GroupLabels = new Dictionary<string, string>
    {
        ["LastWhisper"] = "Last Whisper",
        ["LifelineItems"] = "Lifeline",
        ["VoidPen"] = "magic penetration",
        ["ImmolateItems"] = "Immolate",
        ["GoldItems"] = "support",
        ["HuntersTalismanGroup"] = "jungle pet",
        ["DoransItems"] = "starter",
        ["StopwatchGroup"] = "Stopwatch",
        ["TearItems"] = "Tear",
        ["EternityItems"] = "Eternity",
        ["GuardianItems"] = "Guardian",
        ["BootsWithoutActives"] = "Boots",
        ["TheBlackSpear"] = "Black Spear",
    };

    public static ItemConflict? Conflict(IEnumerable<Item> inventory, Item candidate)
    {
        foreach (var owned in inventory)
        {
            if (owned.Id == candidate.Id && candidate.Unique)
            {
                return new ItemConflict(candidate, owned, null);
            }

            var shared = candidate.Groups.FirstOrDefault(owned.Groups.Contains);
            if (shared is not null)
            {
                return new ItemConflict(candidate, owned, shared);
            }
        }

        return null;
    }

    public static bool IsLegal(IEnumerable<Item> inventory)
    {
        var checkedSoFar = new List<Item>();

        foreach (var item in inventory)
        {
            if (Conflict(checkedSoFar, item) is not null)
            {
                return false;
            }

            checkedSoFar.Add(item);
        }

        return true;
    }

    /// <summary>
    /// Inventory slots the items take. The jungle pet takes none: it leaves the inventory once it
    /// has grown, so a build never has to make room for it.
    /// </summary>
    public static int Slots(IEnumerable<Item> inventory)
    {
        var list = inventory.Where(i => !IsJunglePet(i)).ToList();
        return list.Count(i => !IsStackable(i)) + list.Where(IsStackable).Select(i => i.Id).Distinct().Count();
    }

    public static bool IsJunglePet(Item item) => item.Groups.Contains("HuntersTalismanGroup");

    public static bool IsStackable(Item item) => item.Groups.Contains("Potion") || item.Cost < 100;

    public static bool IsFiller(Item item) =>
        IsStackable(item) || item.Groups.Contains("DoransItems") && !item.Groups.Contains("HuntersTalismanGroup");
}
