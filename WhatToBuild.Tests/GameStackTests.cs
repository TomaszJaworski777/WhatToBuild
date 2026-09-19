using WhatToBuild.Data;
using WhatToBuild.Game;

namespace WhatToBuild.Tests;

[TestClass]
public class GameStackTests
{
    private static Champion _veigar = null!;

    [ClassInitialize]
    public static void Load(TestContext context)
    {
        var champions = ChampionRepository.Load(Path.Combine(AppContext.BaseDirectory, "GameData"));
        _veigar = champions.ByInternalName("Veigar")!;
    }

    private static GameState State(double gameTime, double gold = 0, int level = 1, int creepScore = 0) => new()
    {
        GameTime = gameTime,
        CurrentGold = gold,
        Players =
        [
            new PlayerState { Champion = _veigar, Team = Team.Chaos, Level = level, CreepScore = creepScore },
        ],
        Objectives = new Dictionary<Team, ObjectiveCounts> { [Team.Order] = new(), [Team.Chaos] = new() },
    };

    [TestMethod]
    public void KeepsStatesInOrder()
    {
        var stack = new GameStack();

        Assert.IsTrue(stack.Push(State(60)));
        Assert.IsTrue(stack.Push(State(120)));

        Assert.AreEqual(2, stack.Count);
        Assert.AreEqual(120, stack.Latest!.GameTime, 0.001);
    }

    [TestMethod]
    public void IgnoresAPausedGame()
    {
        var stack = new GameStack();
        stack.Push(State(60));

        Assert.IsFalse(stack.Push(State(60)));
        Assert.IsFalse(stack.Push(State(30)));
        Assert.AreEqual(1, stack.Count);
    }

    [TestMethod]
    public void MeasuresARatePerMinute()
    {
        var stack = new GameStack();

        foreach (var minute in Enumerable.Range(1, 10))
        {
            stack.Push(State(minute * 60, gold: minute * 400));
        }

        Assert.AreEqual(400, stack.GoldEarnedPerMinute()!.Value, 0.001);
    }

    [TestMethod]
    public void RateIsRobustToNoise()
    {
        var stack = new GameStack();
        var noise = new[] { 30, -30, 30, -30, 30, -30, 30, -30 };

        for (var i = 0; i < noise.Length; i++)
        {
            stack.Push(State((i + 1) * 60, gold: (i + 1) * 400 + noise[i]));
        }

        Assert.AreEqual(400, stack.GoldEarnedPerMinute()!.Value, 15);
    }

    [TestMethod]
    public void WindowLooksAtRecentStatesOnly()
    {
        var stack = new GameStack();

        foreach (var minute in Enumerable.Range(1, 10))
        {
            var gold = minute <= 5 ? minute * 200 : 1000 + (minute - 5) * 600;
            stack.Push(State(minute * 60, gold: gold));
        }

        Assert.AreEqual(600, stack.GoldEarnedPerMinute(windowSeconds: 240)!.Value, 0.001);
        Assert.IsLessThan(600, stack.GoldEarnedPerMinute()!.Value);
    }

    [TestMethod]
    public void TracksEachPlayer()
    {
        var stack = new GameStack();

        foreach (var minute in Enumerable.Range(1, 6))
        {
            stack.Push(State(minute * 60, level: minute, creepScore: minute * 7));
        }

        Assert.AreEqual(1, stack.LevelPerMinute(_veigar)!.Value, 0.001);
        Assert.AreEqual(7, stack.CreepScorePerMinute(_veigar)!.Value, 0.001);
    }

    [TestMethod]
    public void NeedsAtLeastTwoStates()
    {
        var stack = new GameStack();

        Assert.IsNull(stack.GoldEarnedPerMinute());

        stack.Push(State(60, gold: 500));

        Assert.IsNull(stack.GoldEarnedPerMinute());
    }

    [TestMethod]
    public void SeriesExposesTheRawTrend()
    {
        var stack = new GameStack();
        stack.Push(State(60, gold: 100));
        stack.Push(State(120, gold: 300));

        var series = stack.Series(s => s.CurrentGold);

        Assert.HasCount(2, series);
        Assert.AreEqual(300, series[1].Value, 0.001);
    }
}
