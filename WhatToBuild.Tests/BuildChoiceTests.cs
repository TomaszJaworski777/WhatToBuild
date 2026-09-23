using WhatToBuild.Data;
using WhatToBuild.Game;
using WhatToBuild.Planning;
using WhatToBuild.SupportedChampions;
using WhatToBuild.SupportedChampions.Kayn;

namespace WhatToBuild.Tests;

[TestClass]
[DoNotParallelize]
public class BuildChoiceTests
{
    private const int Pet = 1101, Hubris = 6697, Profane = 6698, Umbral = 3179, DeathsDance = 6333,
        Sterak = 3053, Maw = 3156, Shieldbow = 6673, Collector = 6676, Hexoptics = 2523, Ie = 3031;

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

    private static GameState Game(string champion, double time = 10 * 60)
    {
        var spots = new[] { "TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY" };

        return new GameState
        {
            GameTime = time,
            CurrentGold = 1200,
            Objectives = new Dictionary<Team, ObjectiveCounts> { [Team.Order] = new(), [Team.Chaos] = new() },
            Players =
            [
                new PlayerState
                {
                    Champion = _champions.ByName(champion)!, Team = Team.Order, Position = "JUNGLE", Level = 9,
                    CreepScore = 95, Kills = 4, Assists = 3, IsActivePlayer = true,
                    Items = [new OwnedItem(Item(Pet), 1, 0)],
                },
                .. new[] { "Garen", "Wukong", "Veigar", "Caitlyn", "Soraka" }.Select((name, i) => new PlayerState
                {
                    Champion = _champions.ByName(name)!, Team = Team.Chaos, Position = spots[i], Level = 9,
                    Items = [new OwnedItem(Item(1038), 1, 0)],
                }),
            ],
        };
    }

    private static BuildContext Context(GameState state, string? form = null)
    {
        var stack = new GameStack();
        stack.Push(state);
        return new BuildContext(state, stack, _items, _neutrals, _kits, _model) { Form = form };
    }

    [TestMethod]
    public void HubrisIsWorthMoreTheEarlierItIsBought()
    {
        var state = Game("Kayn");
        var context = Context(state);
        var planner = new BuildPlanner(new BuildEvaluator(context));
        var stage = _model.Settings.Planner.Ladder(state.GameTime)[^1];
        var horizon = context.Now + _model.Settings.Planner.CompareSeconds;

        double Value(params int[] order) =>
            planner.Replay(order.Select(Item).ToList(), stage, horizon).Value;

        double Stacks(params int[] order)
        {
            var plan = planner.Replay(order.Select(Item).ToList(), stage, horizon);
            return plan.Steps[^1].StacksAfter.GetValueOrDefault(Item(Hubris).Id);
        }

        var first = Value(Hubris, Profane, Umbral);
        var middle = Value(Profane, Hubris, Umbral);
        var last = Value(Profane, Umbral, Hubris);

        Assert.IsGreaterThan(middle, first, $"Bought first {first:0.0}, second {middle:0.0}.");
        Assert.IsGreaterThan(last, first, $"Bought first {first:0.0}, last {last:0.0}.");
        // Second against last also weighs Umbral Glaive against Hubris for that slot, which is
        // about the items, not the stacking, so it is not asserted here.
        Assert.IsGreaterThan(Stacks(Profane, Umbral, Hubris), Stacks(Hubris, Profane, Umbral), "Because it has been stacking for longer.");
    }

    [TestMethod]
    public void ShadowAssassinBuysDamageAndRhaastBuysSomeSurvival()
    {
        var survival = new[] { DeathsDance, Sterak, Maw, Shieldbow }.Select(Item).ToList();
        var state = Game("Kayn");

        IReadOnlyList<Item> Build(string form)
        {
            var planner = new BuildPlanner(new BuildEvaluator(Context(state, form)));
            return planner.Plan(null, _model.Settings.Planner.Ladder(state.GameTime)[^1]).Steps.Select(s => s.Item).ToList();
        }

        double Value(string form, int riotId)
        {
            var evaluator = new BuildEvaluator(Context(state, form));
            var owned = evaluator.Context.Owned;
            var at = 20 * 60.0;
            var bare = evaluator.Evaluate(owned, at, null, EvaluationMode.Full, form);
            var with = evaluator.Evaluate([.. owned, Item(riotId)], at, null, EvaluationMode.Full, form);
            return (with.Score - bare.Score) / Item(riotId).Cost * 1000;
        }

        var assassin = Build(KaynForm.ShadowAssassin);

        Assert.IsFalse(assassin.Any(i => survival.Contains(i)),
            $"Shadow Assassin is pure damage: {string.Join(" > ", assassin.Select(i => i.Name))}");

        // The same item, judged by each form: staying alive is worth something to Rhaast only.
        Assert.IsGreaterThan(Value(KaynForm.ShadowAssassin, DeathsDance), Value(KaynForm.Darkin, DeathsDance));
    }

    [TestMethod]
    public void TheCoreIsTheStrongestSetNotJustTheHandiestOne()
    {
        var state = Game("Kindred");
        var context = Context(state);
        var evaluator = new BuildEvaluator(context);
        var stage = _model.Settings.Planner.Ladder(state.GameTime)[^1];
        var chosen = new BuildPlanner(evaluator).Plan(null, stage).Steps.Select(s => s.Item).ToList();
        var at = 26 * 60.0;

        double Strength(IEnumerable<Item> set) =>
            evaluator.Evaluate([.. context.Owned, .. set], at, null, EvaluationMode.Full).Score;

        var mine = Strength(chosen);
        var others = new[]
        {
            new[] { Collector, Hexoptics, Ie },
            new[] { Hubris, Profane, Umbral },
            new[] { Collector, Ie, DeathsDance },
        };

        foreach (var other in others)
        {
            var set = other.Select(Item).Take(chosen.Count).ToList();
            Assert.IsGreaterThanOrEqualTo(Strength(set), mine,
                $"{string.Join(" + ", set.Select(i => i.Name))} is a stronger build than {string.Join(" + ", chosen.Select(i => i.Name))}.");
        }
    }
}
