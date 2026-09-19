using System.Diagnostics;
using WhatToBuild.Data;
using WhatToBuild.Game;
using WhatToBuild.Modeling;
using WhatToBuild.Recommendations;
using WhatToBuild.SupportedChampions;
using WhatToBuild.SupportedChampions.Kindred;

namespace WhatToBuild.Tests;

[TestClass]
public class SampleRecommendationsTests
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

    private static GameState MidGame() =>
        GameStateParser.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "midgame.json")), _champions, _items);

    [TestMethod]
    public void IsFastEnoughForEverySecond()
    {
        var source = new SampleRecommendations(_items, _neutrals, _kits);
        var state = MidGame();
        var stack = new GameStack();
        stack.Push(state);

        source.For(state, stack);
        var watch = Stopwatch.StartNew();
        var recommendation = source.For(state, stack);
        watch.Stop();

        Assert.IsNotNull(recommendation);
        Assert.IsLessThan(500, watch.ElapsedMilliseconds, $"Took {watch.ElapsedMilliseconds} ms.");
        Console.WriteLine($"Recommendation took {watch.ElapsedMilliseconds} ms.");
    }

    [TestMethod]
    public void TheBuildPathFollowsTheGameRules()
    {
        var recommendation = new SampleRecommendations(_items, _neutrals, _kits).For(MidGame(), new GameStack())!;
        var path = recommendation.BuildPath.Select(s => _items.ByRiotId(s.Item.RiotId)!).ToList();

        Assert.IsTrue(ItemRules.IsLegal(path));
    }
}
