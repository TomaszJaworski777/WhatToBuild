using WhatToBuild.Data;
using WhatToBuild.Forecasting;
using WhatToBuild.Game;

namespace WhatToBuild.Tests;

[TestClass]
public class StackForecasterTests
{
    private static ChampionRepository _champions = null!;
    private static Item _longSword = null!;

    private static Champion Nasus => _champions.ByInternalName("Nasus")!;

    [ClassInitialize]
    public static void Load(TestContext context)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "GameData");
        _champions = ChampionRepository.Load(root);
        _longSword = ItemRepository.Load(root).ByRiotId(1036)!;
    }

    private static GameStack Game(int minutes, int nasusPace)
    {
        var others = new[] { "Garen", "Ahri", "Jinx" }.Select(n => _champions.ByInternalName(n)!).ToList();
        var stack = new GameStack();

        for (var m = 1; m <= minutes; m++)
        {
            var players = new List<PlayerState>
            {
                new() { Champion = Nasus, Team = Team.Chaos, Level = 1, Items = [new OwnedItem(_longSword, nasusPace * m)] },
            };

            players.AddRange(others.Select(c => new PlayerState
            {
                Champion = c,
                Team = Team.Order,
                Level = 1,
                Items = [new OwnedItem(_longSword, m)],
            }));

            stack.Push(new GameState
            {
                GameTime = m * 60,
                CurrentGold = 0,
                Players = players,
                Objectives = new Dictionary<Team, ObjectiveCounts> { [Team.Order] = new(), [Team.Chaos] = new() },
            });
        }

        return stack;
    }

    private static (StackForecaster Forecaster, PlayerState Nasus, ChampionStacking Stacking) Setup(int minutes, int nasusPace)
    {
        var stack = Game(minutes, nasusPace);
        var nasus = stack.Latest!.Find(Nasus)!;
        return (new StackForecaster(stack), nasus, Nasus.Stacking.Single());
    }

    [TestMethod]
    [DataRow(0, 0.0)]
    [DataRow(240, 0.0)]
    [DataRow(720, 0.5)]
    [DataRow(1200, 1.0)]
    [DataRow(3000, 1.0)]
    public void TrendTakesOverBetweenFourAndTwentyMinutes(double gameTime, double expected)
    {
        Assert.AreEqual(expected, new TrendBlend().TrendWeight(gameTime), 0.0001);
    }

    [TestMethod]
    public void BlendFallsBackToThePredictionWithoutATrend()
    {
        Assert.AreEqual(14, new TrendBlend().Blend(14, null, 3000), 0.0001);
    }

    [TestMethod]
    public void BlendMovesFromPredictionToTrend()
    {
        var blend = new TrendBlend();

        Assert.AreEqual(10, blend.Blend(10, 30, 120), 0.0001);
        Assert.AreEqual(20, blend.Blend(10, 30, 720), 0.0001);
        Assert.AreEqual(30, blend.Blend(10, 30, 1500), 0.0001);
    }

    [TestMethod]
    public void AveragePaceKeepsThePrediction()
    {
        var (forecaster, nasus, stacking) = Setup(minutes: 25, nasusPace: 1);

        Assert.AreEqual(stacking.InitialStacksPerMinute, forecaster.StacksPerMinute(nasus, stacking), 0.001);
    }

    [TestMethod]
    public void FedStackerStacksFasterOnceTheTrendTakesOver()
    {
        var (forecaster, nasus, stacking) = Setup(minutes: 25, nasusPace: 2);

        var lobbyAverage = (2 + 1 + 1 + 1) / 4.0;
        var pace = 2 / lobbyAverage;

        Assert.AreEqual(stacking.InitialStacksPerMinute * pace, forecaster.StacksPerMinute(nasus, stacking), 0.001);
    }

    [TestMethod]
    public void EarlyGameIsPurePrediction()
    {
        var (forecaster, nasus, stacking) = Setup(minutes: 3, nasusPace: 2);

        Assert.AreEqual(stacking.InitialStacksPerMinute, forecaster.StacksPerMinute(nasus, stacking), 0.001);
    }

    [TestMethod]
    public void MidGameIsHalfway()
    {
        var (forecaster, nasus, stacking) = Setup(minutes: 12, nasusPace: 2);

        var pace = 2 / ((2 + 1 + 1 + 1) / 4.0);
        var expected = stacking.InitialStacksPerMinute * (0.5 + 0.5 * pace);

        Assert.AreEqual(expected, forecaster.StacksPerMinute(nasus, stacking), 0.001);
    }

    [TestMethod]
    public void ProjectsStacksForward()
    {
        var (forecaster, nasus, stacking) = Setup(minutes: 25, nasusPace: 1);
        var rate = forecaster.StacksPerMinute(nasus, stacking);

        Assert.AreEqual(rate * 25, forecaster.StacksNow(nasus, stacking), 0.001);
        Assert.AreEqual(rate * 5, forecaster.StacksGainedBy(nasus, stacking, 30 * 60), 0.001);
        Assert.AreEqual(rate * 30, forecaster.StacksAt(nasus, stacking, 30 * 60), 0.001);
    }

    [TestMethod]
    public void PastTargetsGainNothing()
    {
        var (forecaster, nasus, stacking) = Setup(minutes: 25, nasusPace: 1);

        Assert.AreEqual(0, forecaster.StacksGainedBy(nasus, stacking, 10 * 60), 0.001);
    }

    [TestMethod]
    public void WithoutHistoryItIsPurePrediction()
    {
        var (_, nasus, stacking) = Setup(minutes: 25, nasusPace: 2);
        var single = new GameStack();
        single.Push(new GameState
        {
            GameTime = 1500,
            CurrentGold = 0,
            Players = [nasus],
            Objectives = new Dictionary<Team, ObjectiveCounts> { [Team.Order] = new(), [Team.Chaos] = new() },
        });

        var forecaster = new StackForecaster(single);

        Assert.AreEqual(stacking.InitialStacksPerMinute, forecaster.StacksPerMinute(nasus, stacking), 0.001);
    }

    [TestMethod]
    public void PaceSourceCanBeReplaced()
    {
        var stack = Game(25, 1);
        var nasus = stack.Latest!.Find(Nasus)!;
        var stacking = Nasus.Stacking.Single();

        var forecaster = new StackForecaster(stack, pace: new FixedPace(3));

        Assert.AreEqual(stacking.InitialStacksPerMinute * 3, forecaster.StacksPerMinute(nasus, stacking), 0.001);
    }

    private sealed class FixedPace(double factor) : IPaceSource
    {
        public double? PaceFactor(GameStack stack, PlayerState player) => factor;
    }
}
