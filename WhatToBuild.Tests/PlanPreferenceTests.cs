using WhatToBuild.Data;
using WhatToBuild.Dtos;
using WhatToBuild.Game;
using WhatToBuild.Planning;
using WhatToBuild.Recommendations;
using WhatToBuild.SupportedChampions;

namespace WhatToBuild.Tests;

[TestClass]
[DoNotParallelize]
public class PlanPreferenceTests
{
    private static ChampionRepository _champions = null!;
    private static ItemRepository _items = null!;
    private static NeutralRepository _neutrals = null!;
    private static ChampionKits _kits = null!;

    [ClassInitialize]
    public static void Load(TestContext context)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "GameData");
        _champions = ChampionRepository.Load(root);
        _items = ItemRepository.Load(root);
        _neutrals = NeutralRepository.Load(root);
        _kits = ChampionKits.Load(root);
    }

    private static GameState Game(params int[] mine)
    {
        var spots = new[] { "TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY" };
        var owned = new List<int> { 1101 };
        owned.AddRange(mine);

        return new GameState
        {
            GameTime = 8 * 60,
            CurrentGold = 1200,
            Objectives = new Dictionary<Team, ObjectiveCounts> { [Team.Order] = new(), [Team.Chaos] = new() },
            Players =
            [
                new PlayerState
                {
                    Champion = _champions.ByName("Kindred")!, Team = Team.Order, Position = "JUNGLE", Level = 8,
                    CreepScore = 70, Kills = 2, Assists = 1, IsActivePlayer = true,
                    Items = owned.Select((id, slot) => new OwnedItem(_items.ByRiotId(id)!, 1, slot)).ToList(),
                },
                .. new[] { "Garen", "Wukong", "Veigar", "Caitlyn", "Soraka" }.Select((name, i) => new PlayerState
                {
                    Champion = _champions.ByName(name)!, Team = Team.Chaos, Position = spots[i], Level = 8,
                    Items = [new OwnedItem(_items.ByRiotId(1038)!, 1, 0)],
                }),
            ],
        };
    }

    private static ModelData FastModel()
    {
        var model = ModelData.Load(Path.Combine(AppContext.BaseDirectory, "GameData"));
        foreach (var stage in model.Settings.Planner.Stages.Concat(model.Settings.Planner.OpeningStages))
        {
            stage.BudgetMilliseconds = 2000;
        }

        return model;
    }

    private static IReadOnlyList<BuildStepDto> Planned(RecommendationDto recommendation) =>
        recommendation.BuildPath.Where(s => s.Status != BuildStepDto.Owned).ToList();

    private static int Items(RecommendationDto recommendation) =>
        Planned(recommendation).Count(s => !_items.ByRiotId(s.Item.RiotId)!.Groups.Contains("Boots"));

    [TestMethod]
    public void TheSliderIsACoreSize()
    {
        var preferences = new PlanPreferences();

        Assert.AreEqual(3, preferences.CoreItems, "Three items plus shoes by default.");
        Assert.AreEqual("3 items + shoes", preferences.Label);

        preferences.CoreItems = 1;
        Assert.AreEqual("1 item + shoes", preferences.Label);

        preferences.CoreItems = 9;
        Assert.AreEqual(PlanPreferences.MaxItems, preferences.CoreItems, "Five items is the most a build can hold beside shoes.");

        preferences.CoreItems = 0;
        Assert.AreEqual(PlanPreferences.MinItems, preferences.CoreItems);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(3)]
    [DataRow(5)]
    public void ThePlanAimsAtTheCoreYouAskedFor(int core)
    {
        var preferences = new PlanPreferences { CoreItems = core };
        var source = new BuildRecommendations(_items, _neutrals, _kits, FastModel(), preferences);
        var state = Game();

        var recommendation = source.Compute(state, new GameStack())!;
        while (source.Refine())
        {
        }

        recommendation = source.Current(state)!;
        var planned = Planned(recommendation);

        var names = string.Join(" > ", planned.Select(s => s.Sells is null ? s.Item.Name : $"{s.Item.Name} (sells {s.Sells})"));
        // Six slots hold the pet, the shoes and four items, so a fifth is a swap, not an addition.
        Assert.AreEqual(Math.Min(core, 4), Math.Min(Items(recommendation), 4), names);
        Assert.IsLessThanOrEqualTo(core, Items(recommendation), names);
        Assert.IsTrue(planned.All(s => s.Sells is null) || core > 4, names);
        Assert.IsTrue(planned.Any(s => _items.ByRiotId(s.Item.RiotId)!.Groups.Contains("Boots")), "Shoes come on top of the core.");
        Assert.AreEqual(core, recommendation.Model!.CoreItems);
        Assert.AreEqual(core <= 4 ? core : PlanPreferences.MaxItems, preferences.CoreItems);
        Assert.AreEqual(preferences.Label, recommendation.Model.CoreLabel);
    }

    [TestMethod]
    [DataRow(new[] { 1001, 1036, 2003 }, DisplayName = "Boots, a Long Sword and a potion")]
    [DataRow(new[] { 1036, 1036, 2003 }, DisplayName = "No boots yet, a full hand of components")]
    [DataRow(new[] { 1001 }, DisplayName = "Just boots")]
    public void AFourItemCoreStillGetsItsShoes(int[] mine)
    {
        var source = new BuildRecommendations(_items, _neutrals, _kits, FastModel(), new PlanPreferences { CoreItems = 4 });
        var state = Game(mine);

        source.Compute(state, new GameStack());
        while (source.Refine())
        {
        }

        var planned = Planned(source.Current(state)!);
        var names = string.Join(" > ", planned.Select(s => s.Item.Name));
        Assert.IsTrue(planned.Any(s => _items.ByRiotId(s.Item.RiotId) is { } item && item.Groups.Contains("Boots") && item.BuildPath.Count > 0), $"No shoes in {names}.");
        Assert.AreEqual(4, Items(source.Current(state)!), names);
    }

    [TestMethod]
    public void APurchaseShowsAtOnceAndIsNeverPlannedAgain()
    {
        var source = new BuildRecommendations(_items, _neutrals, _kits, FastModel(), new PlanPreferences { CoreItems = 3 });
        var state = Game();
        source.Compute(state, new GameStack());
        while (source.Refine())
        {
        }

        var first = Planned(source.Display(state)!).First(s => !_items.ByRiotId(s.Item.RiotId)!.Groups.Contains("Boots"));
        var bought = Game(first.Item.RiotId);

        // One pass on the new inventory, no deeper search yet: the page already knows.
        var shown = source.Compute(bought, new GameStack());
        shown = source.Display(bought)!;
        Assert.IsTrue(shown.BuildPath.Any(s => s.Status == BuildStepDto.Owned && s.Item.RiotId == first.Item.RiotId), $"{first.Item.Name} is not shown as built.");
        Assert.IsFalse(Planned(shown).Any(s => s.Item.RiotId == first.Item.RiotId), $"{first.Item.Name} is still planned.");

        while (source.Refine())
        {
        }

        Assert.IsFalse(Planned(source.Display(bought)!).Any(s => s.Item.RiotId == first.Item.RiotId), $"{first.Item.Name} came back in the finished plan.");
    }

    [TestMethod]
    public void OnceTheCoreStandsItBuysTheBestItemOfTheMoment()
    {
        // Berserker's, Hubris and Hexoptics: a three item core with shoes, already built.
        var state = Game(3006, 6697, 2523, 6696);
        var source = new BuildRecommendations(_items, _neutrals, _kits, FastModel(), new PlanPreferences { CoreItems = 3 });

        var recommendation = source.Compute(state, new GameStack())!;
        while (source.Refine())
        {
        }

        recommendation = source.Current(state)!;

        Assert.AreEqual(3, recommendation.Model!.BuiltItems, "Shoes and the pet do not count toward the core.");
        Assert.HasCount(1, Planned(recommendation), "Nothing left to save for: one item at a time, the best one now.");
    }

    [TestMethod]
    public void TheCoreIsOrderedByWhatEachItemIsWorthWhenItLands()
    {
        var model = FastModel();
        var source = new BuildRecommendations(_items, _neutrals, _kits, model, new PlanPreferences { CoreItems = 3 });
        var state = Game();

        source.Compute(state, new GameStack());
        while (source.Refine())
        {
        }

        var chosen = Planned(source.Current(state)!).Select(s => _items.ByRiotId(s.Item.RiotId)!).ToList();
        Assert.IsGreaterThan(1, chosen.Count);

        var stack = new GameStack();
        stack.Push(state);
        var planner = new BuildPlanner(new BuildEvaluator(new BuildContext(state, stack, _items, _neutrals, _kits, model)));
        var stage = model.Settings.Planner.Ladder(state.GameTime)[^1];
        var value = planner.Replay(chosen, stage).Value;

        foreach (var order in Orders(chosen))
        {
            var other = planner.Replay(order, stage);
            if (other.Steps.Count == order.Count)
            {
                Assert.IsGreaterThanOrEqualTo(other.Value, value,
                    $"{string.Join(" > ", order.Select(i => i.Name))} beats the planned {string.Join(" > ", chosen.Select(i => i.Name))}.");
            }
        }
    }

    private static IEnumerable<List<Item>> Orders(List<Item> items)
    {
        if (items.Count <= 1)
        {
            yield return items;
            yield break;
        }

        for (var i = 0; i < items.Count; i++)
        {
            foreach (var tail in Orders(items.Where((_, index) => index != i).ToList()))
            {
                yield return [items[i], .. tail];
            }
        }
    }

    [TestMethod]
    public void TheBurstIsAveragedOverTheEnemiesYouCount()
    {
        var state = Game();
        var model = FastModel();
        Evaluation Burst(params string[] excluded)
        {
            var context = new BuildContext(state, new GameStack(), _items, _neutrals, _kits, model) { BurstExcluded = excluded.ToHashSet() };
            return new BuildEvaluator(context).Evaluate(context.Owned, state.GameTime, null, EvaluationMode.Full);
        }

        var all = Burst();
        var share = all.Targets.ToDictionary(t => t.Enemy.Champion.Name, t => BuildEvaluator.BurstShare(t.Fight));
        var threat = all.Targets.ToDictionary(t => t.Enemy.Champion.Name, t => t.Enemy.Threat);

        Assert.AreEqual(all.Targets.Sum(t => t.Enemy.Threat * share[t.Enemy.Champion.Name]), all.Burst, 1e-9, "Every enemy counts by default.");

        var noTank = Burst("Garen");
        var rest = threat.Keys.Where(n => n != "Garen").ToList();
        Assert.AreEqual(rest.Sum(n => threat[n] * share[n]) / rest.Sum(n => threat[n]), noTank.Burst, 1e-9, "Garen's weight spreads over the rest.");
        Assert.AreEqual(share["Caitlyn"], Burst("Garen", "Wukong", "Veigar", "Soraka").Burst, 1e-9, "One enemy left: its share alone.");
        Assert.AreEqual(all.Burst, Burst("Garen", "Wukong", "Veigar", "Caitlyn", "Soraka").Burst, 1e-9, "Leaving every enemy out counts them all.");
    }

    [TestMethod]
    public void BurstChoicesChangeThePlanKeyAndEnemiesResetWithTheGame()
    {
        var preferences = new PlanPreferences();
        var key = preferences.Key;

        preferences.SetBurstCombo("Nasus", ["Q", "AA"]);
        CollectionAssert.AreEqual(new[] { "Q", "AA" }, preferences.BurstCombo("nasus")!.ToList());
        Assert.AreNotEqual(key, preferences.Key, "A new combo re-plans.");

        preferences.SetBurstCombo("Nasus", []);
        Assert.IsNull(preferences.BurstCombo("Nasus"), "An empty combo goes back to the kit's own.");
        Assert.AreEqual(key, preferences.Key);

        preferences.StartGame("a");
        preferences.BurstExcluded = new HashSet<string> { "Garen" };
        Assert.AreNotEqual(key, preferences.Key, "Leaving an enemy out re-plans.");
        preferences.StartGame("a");
        Assert.Contains("garen", preferences.BurstExcluded);
        preferences.StartGame("b");
        Assert.IsEmpty(preferences.BurstExcluded, "A new game counts every enemy again.");
    }

    [TestMethod]
    public void TheRecommendationCarriesTheBurst()
    {
        var preferences = new PlanPreferences();
        preferences.SetBurstCombo("Kindred", ["W", "AA", "AA"]);
        preferences.BurstExcluded = new HashSet<string> { "Garen" };
        var source = new BuildRecommendations(_items, _neutrals, _kits, FastModel(), preferences);

        var burst = source.Compute(Game(), new GameStack())!.Burst!;

        Assert.IsTrue(burst.Custom);
        CollectionAssert.AreEqual(new[] { "W", "AA", "AA" }, burst.Steps.ToList());
        Assert.IsNotEmpty(burst.Default);
        Assert.Contains("AA", burst.Choices);
        Assert.HasCount(5, burst.Targets);
        Assert.IsFalse(burst.Targets.Single(t => t.Champion == "Garen").Counted);
        Assert.IsTrue(burst.Targets.Where(t => t.Champion != "Garen").All(t => t.Counted));
    }

    [TestMethod]
    public void MovingTheSliderRepicksTheBuild()
    {
        var preferences = new PlanPreferences { CoreItems = 1 };
        var source = new BuildRecommendations(_items, _neutrals, _kits, FastModel(), preferences);
        var state = Game();

        int Deepen()
        {
            source.Compute(state, new GameStack());
            while (source.Refine())
            {
            }

            return Items(source.Current(state)!);
        }

        var one = Deepen();
        var oneAgain = Deepen();

        preferences.CoreItems = 4;
        var four = Deepen();

        Assert.AreEqual(1, one);
        Assert.AreEqual(1, oneAgain, "The same setting keeps the same build.");
        Assert.AreEqual(4, four, "Moving the slider re-plans against the new core.");
    }
}
