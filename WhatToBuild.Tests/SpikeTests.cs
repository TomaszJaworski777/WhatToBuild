using WhatToBuild.Data;
using WhatToBuild.Game;
using WhatToBuild.Planning;
using WhatToBuild.SupportedChampions;

namespace WhatToBuild.Tests;

[TestClass]
public class SpikeTests
{
    private const int Pet = 1101, Boots = 3006, LongSword = 1036, Hubris = 6697, Hexoptics = 2523, Umbral = 3179, Axiom = 6696;

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

    private static ModelData Model() => ModelData.Load(Path.Combine(AppContext.BaseDirectory, "GameData"));

    private static Item Item(int riotId) => _items.ByRiotId(riotId)!;

    private static GameState FreshGame()
    {
        var spots = new[] { "TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY" };
        var enemies = new[] { "Garen", "Wukong", "Veigar", "Caitlyn", "Soraka" };

        return new GameState
        {
            GameTime = 15,
            CurrentGold = 500,
            Objectives = new Dictionary<Team, ObjectiveCounts> { [Team.Order] = new(), [Team.Chaos] = new() },
            Players =
            [
                new PlayerState
                {
                    Champion = _champions.ByName("Kindred")!, Team = Team.Order, Position = "JUNGLE", Level = 1,
                    IsActivePlayer = true, Items = [new OwnedItem(Item(Pet), 1, 0)],
                },
                .. enemies.Select((name, i) => new PlayerState
                {
                    Champion = _champions.ByName(name)!, Team = Team.Chaos, Position = spots[i], Level = 1, Items = [],
                }),
            ],
        };
    }

    private static BuildEvaluator Evaluator(ModelData model) =>
        new(new BuildContext(FreshGame(), new GameStack(), _items, _neutrals, _kits, model));

    [TestMethod]
    public void SpikesAreTheMinutesEnemiesFinishItems()
    {
        var spikes = Evaluator(Model()).SpikesBetween(15, 20 * 60);

        Assert.IsNotEmpty(spikes);
        CollectionAssert.AreEqual(spikes.Select(s => s.Time).ToList(), spikes.Select(s => s.Time).Order().ToList(), "Spikes come in order.");
        Assert.IsTrue(spikes.All(s => s.Time > 15 && s.Time <= 20 * 60));
        Assert.IsTrue(spikes.All(s => s.Enemy.Forecast.Player.Team == Team.Chaos));
        Assert.IsGreaterThan(1, spikes.Select(s => s.Enemy.Champion.Name).Distinct().Count(), "More than one enemy spikes.");
    }

    [TestMethod]
    public void AFinishedItemBeatsBootsAndComponentsAtTheirSpike()
    {
        var evaluator = Evaluator(Model());
        var spike = evaluator.SpikesBetween(15, 20 * 60).First();

        double Edge(params int[] holding)
        {
            var inventory = holding.Select(Item).ToList();
            return evaluator.DuelAt(evaluator.Evaluate(inventory, spike.Time, null, EvaluationMode.Full), spike.Enemy).Edge;
        }

        var savingUp = Edge(Pet);
        var bootsAndParts = Edge(Pet, Boots, LongSword);
        var finished = Edge(Pet, Hubris);

        // With almost nothing built neither side gets the kill inside the fight, so these two can
        // legitimately tie: what matters is that the finished item breaks that deadlock.
        Assert.IsGreaterThanOrEqualTo(savingUp, bootsAndParts, "Boots and a component are never worse than nothing.");
        Assert.IsGreaterThan(bootsAndParts, finished, $"A finished item is stronger at the spike: {finished:0.00} against {bootsAndParts:0.00}.");
        Assert.IsLessThan(1, bootsAndParts, "Boots and components lose that duel, which is what the spike costs price.");
    }

    [TestMethod]
    public void ArrivingLateToASpikeCostsThePlan()
    {
        double Value(double spikeWeight, int[] order)
        {
            var model = Model();
            model.Settings.Planner.SpikeWeight = spikeWeight;
            var stage = model.Settings.Planner.Ladder(0)[^1];
            return new BuildPlanner(Evaluator(model)).Replay(order.Select(Item).ToList(), stage).Value;
        }

        int[] bootsFirst = [Boots, Hubris, Hexoptics, Umbral, Axiom];
        int[] itemFirst = [Hubris, Hexoptics, Axiom, Umbral, Boots];

        var ignored = Value(0, itemFirst) - Value(0, bootsFirst);
        var priced = Value(1, itemFirst) - Value(1, bootsFirst);

        // Whether boots or the item goes first at level 1 is the order's call on what each is worth
        // early; what the spikes have to do is pull toward holding the item when enemies spike.
        Assert.IsGreaterThan(ignored, priced, $"Weighing spikes favours the item first: {ignored:0.00} ignoring them, {priced:0.00} counting them.");
    }
}
