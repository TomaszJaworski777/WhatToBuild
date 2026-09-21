using WhatToBuild.Data;
using WhatToBuild.Dtos;
using WhatToBuild.Forecasting;
using WhatToBuild.Game;
using WhatToBuild.Planning;
using WhatToBuild.Recommendations;
using WhatToBuild.SupportedChampions;

namespace WhatToBuild.Tests;

[TestClass]
[DoNotParallelize]
public class TrendPlanningTests
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

    private static ModelData Model(double budget = 400)
    {
        var model = ModelData.Load(Path.Combine(AppContext.BaseDirectory, "GameData"));

        foreach (var stage in model.Settings.Planner.Stages.Concat(model.Settings.Planner.OpeningStages))
        {
            stage.BudgetMilliseconds = budget;
            stage.ScreenCount = Math.Min(stage.ScreenCount, 8);
            stage.Branching = Math.Min(stage.Branching, 5);
            stage.BeamWidth = Math.Min(stage.BeamWidth, 3);
        }

        return model;
    }

    private static Item Item(int riotId) => _items.ByRiotId(riotId)!;

    private static PlayerState Enemy(string name, string position, int level, params int[] items) => new()
    {
        Champion = _champions.ByName(name)!,
        Team = Team.Chaos,
        Position = position,
        Level = level,
        Items = items.Select((id, slot) => new OwnedItem(Item(id), 1, slot)).ToList(),
    };

    private static GameState State(double time, double gold, int creepScore = 60, IEnumerable<int>? mine = null, int enemyItem = 3047) => new()
    {
        GameTime = time,
        CurrentGold = gold,
        Objectives = new Dictionary<Team, ObjectiveCounts> { [Team.Order] = new(), [Team.Chaos] = new() },
        Players =
        [
            new PlayerState
            {
                Champion = _champions.ByName("Kindred")!, Team = Team.Order, Position = "JUNGLE", Level = 9,
                CreepScore = creepScore, Kills = 2, Assists = 1, IsActivePlayer = true,
                Items = (mine ?? [1101]).Select((id, slot) => new OwnedItem(Item(id), 1, slot)).ToList(),
            },
            Enemy("Garen", "TOP", 9, enemyItem),
            Enemy("Wukong", "JUNGLE", 9, 1036),
            Enemy("Veigar", "MIDDLE", 9, 1052),
            Enemy("Caitlyn", "BOTTOM", 9, 1038),
            Enemy("Soraka", "UTILITY", 8, 1004),
        ],
    };

    private static string Signature(GameState state) =>
        new GameTrends(state, new GameStack(), Model()).Signature;

    private static IReadOnlyList<string> Build(RecommendationDto recommendation) =>
        recommendation.BuildPath.Where(s => s.Status != BuildStepDto.Owned).Select(s => s.Item.Name).ToList();

    [TestMethod]
    public void GoldAndFarmTicksDoNotMoveTheTrendSignature()
    {
        Assert.AreEqual(Signature(State(600, 500)), Signature(State(601, 780, 63)));
    }

    [TestMethod]
    public void AnItemOnAnyPlayerMovesTheTrendSignature()
    {
        Assert.AreNotEqual(Signature(State(600, 500)), Signature(State(600, 500, enemyItem: 3068)));
        Assert.AreNotEqual(Signature(State(600, 500)), Signature(State(600, 500, mine: [1101, 1038])));
    }

    [TestMethod]
    public void NothingIsShownUntilTheBuildIsThoughtThrough()
    {
        var source = new BuildRecommendations(_items, _neutrals, _kits, Model());
        var state = State(600, 500);

        source.Compute(state, new GameStack());
        var first = source.Display(state)!;
        Assert.IsTrue(first.Calculating, "A build half way up the ladder is not shown.");
        Assert.IsEmpty(first.BuildPath);

        while (source.Refine())
        {
        }

        var settled = source.Display(state)!;
        Assert.IsFalse(settled.Calculating);
        Assert.IsNotEmpty(settled.BuildPath);

        // An enemy item sends it back to the drawing board, but the build in hand stays up.
        var newState = State(620, 300, enemyItem: 3068);
        source.Compute(newState, new GameStack());
        var changed = source.Display(newState)!;
        Assert.IsFalse(changed.Calculating, "The settled build carries over while the next one is worked out.");
        CollectionAssert.AreEqual(Build(settled).ToList(), Build(changed).ToList());
    }

    [TestMethod]
    public void AQuietTickKeepsTheWholeBuildAndItsDepth()
    {
        var source = new BuildRecommendations(_items, _neutrals, _kits, Model());
        source.Compute(State(600, 500), new GameStack());
        while (source.Refine())
        {
        }

        var deep = source.Current(State(600, 500))!;
        var afterTick = source.Compute(State(601, 780, 63), new GameStack())!;

        Assert.IsNotEmpty(Build(deep));
        CollectionAssert.AreEqual(Build(deep).ToList(), Build(afterTick).ToList(), "A quiet tick re-times the build, it does not re-pick it.");
        Assert.AreEqual(deep.Model!.Stage, afterTick.Model!.Stage, "The depth reached is kept.");
        Assert.IsFalse(afterTick.Model.Refining);
    }

    [TestMethod]
    public void AMaterialChangeKeepsTheBuildItAlreadyFound()
    {
        var source = new BuildRecommendations(_items, _neutrals, _kits, Model());
        source.Compute(State(600, 500), new GameStack());
        while (source.Refine())
        {
        }

        var deep = Build(source.Current(State(600, 500))!);
        var afterBuy = source.Compute(State(620, 200, enemyItem: 3068), new GameStack())!;

        Assert.AreEqual("quick", afterBuy.Model!.Stage, "A real change re-decides the next item straight away.");
        Assert.IsGreaterThanOrEqualTo(deep.Count, Build(afterBuy).Count, "The build survives the change instead of collapsing to one item.");
        Assert.IsTrue(afterBuy.Model.Refining, "The deeper stages run again in the background.");
    }

    [TestMethod]
    public void AcrossAReplayTheBuildHoldsStill()
    {
        var folder = System.IO.Path.Combine(AppContext.BaseDirectory, "Replays", "demo");
        var snapshots = Directory.GetFiles(folder, "*.json").OrderBy(f => f)
            .Select(f => GameStateParser.Parse(File.ReadAllText(f), _champions, _items))
            .ToList();

        var source = new BuildRecommendations(_items, _neutrals, _kits, Model());
        var stack = new GameStack();
        var firsts = new List<string>();
        var lengths = new List<int>();

        foreach (var snapshot in snapshots)
        {
            stack.Push(snapshot);
            var recommendation = source.Compute(snapshot, stack)!;
            firsts.Add(Build(recommendation).FirstOrDefault() ?? "");
            lengths.Add(Build(recommendation).Count);

            // What the worker thread does whenever no new state is waiting.
            source.Refine();
        }

        var changes = firsts.Zip(firsts.Skip(1)).Count(pair => pair.First != pair.Second);

        Assert.IsLessThanOrEqualTo(1, changes, $"The next item was re-picked {changes} times: {string.Join(" > ", firsts.Distinct())}");
        Assert.IsNotEmpty(Build(source.Current(snapshots[^1])!), "There is still something to buy at the end.");
        Assert.IsGreaterThanOrEqualTo(lengths.Max() - 1, lengths.Skip(3).Min(), "The build never collapses between ticks.");
    }

    [TestMethod]
    public void TheOpeningKeepsThinkingInsteadOfAnsweringOnce()
    {
        var model = Model();
        var source = new BuildRecommendations(_items, _neutrals, _kits, model);
        source.Compute(State(30, 500), new GameStack());
        var opening = source.Display(State(30, 500))!;

        Assert.IsTrue(opening.Calculating, "The opening is still being worked out.");

        var rungs = 0;
        while (source.Refine())
        {
            rungs++;
        }

        Assert.AreEqual(model.Settings.Planner.OpeningStages.Count - 1, rungs);
        Assert.AreEqual(model.Settings.Planner.OpeningStages[^1].Name, source.Current(State(30, 500))!.Model!.Stage);
    }
}
