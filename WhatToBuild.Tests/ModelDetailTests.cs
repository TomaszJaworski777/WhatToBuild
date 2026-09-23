using WhatToBuild.Data;
using WhatToBuild.Forecasting;
using WhatToBuild.Game;
using WhatToBuild.Modeling;
using WhatToBuild.Modeling.Simulation;
using WhatToBuild.Planning;
using WhatToBuild.Recommendations;
using WhatToBuild.SupportedChampions;

namespace WhatToBuild.Tests;

[TestClass]
public class ModelDetailTests
{
    private const int Hexoptics = 2523, Zhonyas = 3157, Pet = 1101, Boots = 3006;

    private static ChampionRepository _champions = null!;
    private static ItemRepository _items = null!;
    private static NeutralRepository _neutrals = null!;
    private static ChampionKits _kits = null!;
    private static ModelData _model = null!;

    [ClassInitialize]
    public static void Load(TestContext context)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "GameData");
        _champions = ChampionRepository.Load(root);
        _items = ItemRepository.Load(root);
        _neutrals = NeutralRepository.Load(root);
        _kits = ChampionKits.Load(root);
        _model = ModelData.Load(root);
    }

    private static Item Item(int riotId) => _items.ByRiotId(riotId)!;

    private static GameState Game(string mine, double time = 10 * 60, IEnumerable<int>? enemyItems = null)
    {
        var spots = new[] { "TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY" };
        var enemies = new[] { "Garen", "Wukong", "Veigar", "Caitlyn", "Soraka" };
        var theirs = (enemyItems ?? [1038]).Select((id, slot) => new OwnedItem(Item(id), 1, slot)).ToList();

        return new GameState
        {
            GameTime = time,
            CurrentGold = 1200,
            Objectives = new Dictionary<Team, ObjectiveCounts> { [Team.Order] = new(), [Team.Chaos] = new() },
            Players =
            [
                new PlayerState
                {
                    Champion = _champions.ByName(mine)!, Team = Team.Order, Position = "JUNGLE", Level = 9,
                    CreepScore = 90, Kills = 2, Assists = 1, IsActivePlayer = true,
                    Items = [new OwnedItem(Item(Pet), 1, 0)],
                },
                .. enemies.Select((name, i) => new PlayerState
                {
                    Champion = _champions.ByName(name)!, Team = Team.Chaos, Position = spots[i], Level = 9, Items = theirs,
                }),
            ],
        };
    }

    private static BuildEvaluator Evaluator(GameState state)
    {
        var stack = new GameStack();
        stack.Push(state);
        return new BuildEvaluator(new BuildContext(state, stack, _items, _neutrals, _kits, _model));
    }

    [TestMethod]
    public void HexopticsAmpliesDamageOnlyForRangedChampions()
    {
        double Amp(string champion)
        {
            var us = new ChampionState(_champions.ByName(champion)!, 11, [Item(Hexoptics)]);
            var them = new ChampionState(_champions.ByName("Garen")!, 11);
            var bare = new ChampionState(_champions.ByName(champion)!, 11);

            // The same raw hit either way: Hexoptics carries no penetration, so any difference
            // in what lands is the amplifier and nothing else.
            var with = AttackerHits.Physical(us, them, 1000).Attack().Run().HealthDamage;
            var without = AttackerHits.Physical(bare, them, 1000).Attack().Run().HealthDamage;
            return with / without - 1;
        }

        Assert.AreEqual(0.1, Amp("Kindred"), 0.001, "A ranged owner gets the 10%.");
        Assert.AreEqual(0, Amp("Kayn"), 0.001, "A melee owner gets none of it.");
    }

    [TestMethod]
    public void EnemiesKeepWhatTheyAlreadyBoughtWhenForecastForward()
    {
        var state = Game("Kindred", enemyItems: [Zhonyas]);
        var evaluator = Evaluator(state);

        foreach (var enemy in evaluator.BattlefieldAt(25 * 60).Enemies)
        {
            var items = enemy.Forecast.Items.Select(i => i.Name).ToList();
            Assert.Contains(Item(Zhonyas).Name, items, $"{enemy.Champion.Name} dropped what they already own: {string.Join(", ", items)}");
            Assert.IsGreaterThan(1, items.Count, "The standard build is filled in on top of it.");
        }
    }

    [TestMethod]
    public void EnemyArmourTheyAlreadyOwnReachesTheFight()
    {
        var plain = Evaluator(Game("Kindred", enemyItems: [1038]));
        var armoured = Evaluator(Game("Kindred", enemyItems: [Zhonyas]));

        double Armour(BuildEvaluator evaluator) =>
            evaluator.BattlefieldAt(25 * 60).Enemies.First(e => e.Champion.Name == "Veigar").Entity.Stats.Armor;

        Assert.IsGreaterThan(Armour(plain), Armour(armoured), "Zhonya's on the board is armour in the simulation.");
    }

    [TestMethod]
    public void TheGoldTrendIsTheBaselineEarlyAndTheirOwnRateLater()
    {
        double Confidence(double minutes) =>
            new GameTrends(Game("Kindred", minutes * 60), new GameStack(), _model).Confidence;

        Assert.AreEqual(0, Confidence(8), 1e-9, "Up to 8 minutes it is the average game.");
        Assert.AreEqual(0, Confidence(4), 1e-9);
        Assert.AreEqual(1, Confidence(20), 1e-9, "From 20 minutes it is entirely this game.");
        Assert.AreEqual(1, Confidence(30), 1e-9);
        Assert.AreEqual(0.5, Confidence(14), 0.02, "Half way between, it is half and half.");
    }

    [TestMethod]
    public void KaynPicksAFormOnceAndKeepsIt()
    {
        var source = new BuildRecommendations(_items, _neutrals, _kits, _model, new PlanPreferences { CoreItems = 3 });
        var state = Game("Kayn");

        var first = source.Compute(state, new GameStack())!.Form!;
        Assert.IsTrue(first.Locked, "The form is settled, not re-argued every tick.");
        Assert.IsNotNull(first.LockedBecause);

        // Buying items, taking kills and time passing must not swap the form under the build.
        var later = source.Compute(Game("Kayn", 18 * 60, [3157, 3047]), new GameStack())!.Form!;

        Assert.AreEqual(first.Recommended, later.Recommended);
        Assert.AreEqual(first.RecommendedLabel, later.RecommendedLabel);
    }
}
