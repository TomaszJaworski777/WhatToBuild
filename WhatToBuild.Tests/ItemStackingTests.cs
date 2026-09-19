using System.Text.Json.Nodes;
using WhatToBuild.Clients;
using WhatToBuild.Data;
using WhatToBuild.Dtos;
using WhatToBuild.Game;
using WhatToBuild.Modeling;
using WhatToBuild.Modeling.Simulation;

namespace WhatToBuild.Tests;

[TestClass]
public class ItemStackingTests
{
    private static ChampionRepository _champions = null!;
    private static ItemRepository _items = null!;
    private static NeutralRepository _neutrals = null!;

    private static Champion Kindred => _champions.ByName("Kindred")!;
    private static Champion Darius => _champions.ByName("Darius")!;
    private static Item RodOfAges => _items.ByRiotId(6657)!;
    private static Item YunTal => _items.ByRiotId(3032)!;
    private static Item Heartsteel => _items.ByRiotId(3084)!;

    [ClassInitialize]
    public static void Load(TestContext context)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "GameData");
        _champions = ChampionRepository.Load(root);
        _items = ItemRepository.Load(root);
        _neutrals = NeutralRepository.Load(root);
    }

    private static Dictionary<Guid, double> Stacks(Item item, double stacks) => new() { [item.Id] = stacks };

    [TestMethod]
    public void RodOfAgesStacksOncePerMinuteOwned()
    {
        Assert.AreEqual(5, RodOfAges.Stacking!.StacksAfter(5, ranged: false), 0.001);
        Assert.AreEqual(10, RodOfAges.Stacking!.StacksAfter(25, ranged: false), 0.001);
        Assert.AreEqual(0, RodOfAges.Stacking!.StacksAfter(null, ranged: false), 0.001);

        var plain = new ChampionState(Kindred, 11, [RodOfAges]);
        var stacked = new ChampionState(Kindred, 11, [RodOfAges], itemStacks: Stacks(RodOfAges, 5));

        Assert.AreEqual(50, stacked.Stats.Health - plain.Stats.Health, 0.001);
        Assert.AreEqual(15, stacked.Stats.AbilityPower - plain.Stats.AbilityPower, 0.001);
        Assert.AreEqual(150, stacked.Stats.Mana - plain.Stats.Mana, 0.001);
    }

    [TestMethod]
    public void EveryStackingItemHasARate()
    {
        foreach (var item in _items.All.Where(i => i.Stacking is not null))
        {
            Assert.IsGreaterThan(0, item.Stacking!.StacksPerMinute, item.Name);
            Assert.IsFalse(string.IsNullOrEmpty(item.Stacking.RateSource), item.Name);
        }
    }

    [TestMethod]
    public void RangedChampionsStackSlowerToTheSameCap()
    {
        var yunTal = YunTal.Stacking!;

        Assert.AreEqual(2 * yunTal.StacksAfter(2, ranged: true), yunTal.StacksAfter(2, ranged: false), 0.001);
        Assert.AreEqual(yunTal.StacksAfter(60, ranged: false), yunTal.StacksAfter(60, ranged: true), 0.001);

        var capped = new ChampionState(Kindred, 11, [YunTal], itemStacks: Stacks(YunTal, yunTal.StacksAfter(60, ranged: true)));
        Assert.AreEqual(0.25, AttackerHits.CritChance(capped), 0.0001);
    }

    [TestMethod]
    public void HeartsteelStacksGrowMaxHealth()
    {
        var plain = new ChampionState(Darius, 11, [Heartsteel]);
        var stacked = new ChampionState(Darius, 11, [Heartsteel], itemStacks: Stacks(Heartsteel, 10));

        Assert.AreEqual(10 * (7 + 0.006 * plain.Stats.Health), stacked.Stats.Health - plain.Stats.Health, 0.001);
        Assert.AreEqual(stacked.Stats.Health - plain.Stats.Health, stacked.BonusHealth - plain.BonusHealth, 0.001);
    }

    [TestMethod]
    public void TheTrackerRemembersWhenItemsWereBought()
    {
        var fixture = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "midgame.json"));

        string At(double time, bool withRod)
        {
            var node = JsonNode.Parse(fixture)!;
            node["gameData"]!["gameTime"] = time;
            var items = node["allPlayers"]![0]!["items"]!.AsArray();
            if (withRod)
            {
                items.Add(new JsonObject { ["itemID"] = 6657, ["count"] = 1, ["slot"] = 5 });
            }

            return node.ToJsonString();
        }

        var tracker = new GameTracker(new ReplayClient([At(100, false), At(200, true), At(500, true)]), _champions, _items);

        tracker.PollAsync().Wait();
        tracker.PollAsync().Wait();
        var state = tracker.PollAsync().Result!;
        var owner = state.Players[0];

        Assert.AreEqual(5, state.MinutesOwned(owner, RodOfAges)!.Value, 0.001);
        Assert.AreEqual(5, state.ItemStacksFor(owner)[RodOfAges.Id], 0.001);
    }

    [TestMethod]
    public void TooltipShowsTheStacks()
    {
        var owner = new ChampionState(Kindred, 11, [RodOfAges], itemStacks: Stacks(RodOfAges, 4));
        var line = ItemDescriber.StackLine(RodOfAges, owner)!;

        Assert.AreEqual("Stacks: +10 Health, +30 Mana, +3 Ability Power per minute owned, ~1 per minute, max 10, ~4 now", line);
        StringAssert.Contains(ItemDescriber.StackLine(YunTal, null)!, "50% when ranged");
    }
}
