using WhatToBuild.Data;
using WhatToBuild.Forecasting;
using WhatToBuild.Game;
using WhatToBuild.Modeling.Simulation;
using WhatToBuild.Planning;
using WhatToBuild.SupportedChampions;
using WhatToBuild.SupportedChampions.Kayn;

namespace WhatToBuild.Tests;

[TestClass]
[DoNotParallelize]
public class SliderPlanTests
{
    private const int Pet = 1101, Gustwalker = 1102;

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

    private static GameState Game(string champion, double time, double gold, int level = 9, int pet = Pet,
        double? marks = null, IReadOnlyDictionary<string, string>? abilities = null, int kills = 4)
    {
        var spots = new[] { "TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY" };
        var me = _champions.ByName(champion)!;

        return new GameState
        {
            GameTime = time,
            CurrentGold = gold,
            Objectives = new Dictionary<Team, ObjectiveCounts> { [Team.Order] = new(), [Team.Chaos] = new() },
            ActivePlayerAbilityIds = abilities ?? new Dictionary<string, string>(),
            Players =
            [
                new PlayerState
                {
                    Champion = me, Team = Team.Order, Position = "JUNGLE", Level = level,
                    CreepScore = (int)(time / 60 * 5.5), Kills = kills, Assists = 3, IsActivePlayer = true,
                    Items = [new OwnedItem(Item(pet), 1, 0)],
                    EstimatedStacks = marks is { } m ? [new StackEstimate(me.Stacking[0].Stat, m, 0, true)] : [],
                },
                .. new[] { "Garen", "Wukong", "Veigar", "Caitlyn", "Soraka" }.Select((name, i) => new PlayerState
                {
                    Champion = _champions.ByName(name)!, Team = Team.Chaos, Position = spots[i], Level = level,
                    Items = [new OwnedItem(Item(1038), 1, 0)],
                }),
            ],
        };
    }

    private static BuildContext Context(GameState state)
    {
        var stack = new GameStack();
        stack.Push(state);
        return new BuildContext(state, stack, _items, _neutrals, _kits, _model);
    }

    private static List<Item> Plan(GameState state, IReadOnlyDictionary<string, ModelSettings.ObjectiveWeights>? chosen = null)
    {
        var stage = _model.Settings.Planner.Ladder(state.GameTime)[^1];
        var core = new ModelSettings.PlanStage
        {
            Name = stage.Name, BudgetMilliseconds = 60_000, ScreenCount = stage.ScreenCount, BeamWidth = stage.BeamWidth,
            Branching = stage.Branching, Depth = 3, Mode = stage.Mode, HorizonGold = stage.HorizonGold,
        };
        var stack = new GameStack();
        stack.Push(state);
        var context = new BuildContext(state, stack, _items, _neutrals, _kits, _model)
        {
            ChosenWeights = chosen ?? new Dictionary<string, ModelSettings.ObjectiveWeights>(),
        };
        return new BuildPlanner(new BuildEvaluator(context)).Plan(null, core).Steps.Select(s => s.Item).ToList();
    }

    private static bool Defensive(Item item) =>
        item.Stats.Health > 0 || item.Stats.Armor > 0 || item.Stats.MagicResist > 0 || item.Stats.LifeStealPercent > 0
        || item.Stats.OmnivampPercent > 0
        || item.Effects.Any(e => e.Kind is EffectKind.Heal or EffectKind.Shield or EffectKind.Revive or EffectKind.DamageReduction);

    [TestMethod]
    public void ChosenWeightsReplaceOnlyTheConstantChampionWeights()
    {
        var kayn = _champions.ByName("Kayn")!;
        var state = Game("Kayn", 12 * 60, 0);
        var chosen = new Dictionary<string, ModelSettings.ObjectiveWeights>
        {
            ["Kayn/Darkin"] = new() { Damage = 0.4, Burst = 2, Uptime = 0, Survival = 0, Clear = 3, Movement = 3 },
        };
        var stack = new GameStack();
        stack.Push(state);
        var context = new BuildContext(state, stack, _items, _neutrals, _kits, _model) { ChosenWeights = chosen };
        var model = _model.Settings.Objectives.For(kayn, KaynForm.Darkin);

        var rhaast = context.ObjectiveFor(KaynForm.Darkin);
        Assert.AreEqual(2, rhaast.Burst, 1e-9);
        Assert.AreEqual(0.4, rhaast.Damage, 1e-9);
        Assert.AreEqual(model.Clear, rhaast.Clear, 1e-9, "Clear changes with time, so it stays the model's.");
        Assert.AreEqual(model.Movement, rhaast.Movement, 1e-9, "Movement changes with time, so it stays the model's.");

        var assassin = context.ObjectiveFor(KaynForm.ShadowAssassin);
        Assert.AreEqual(_model.Settings.Objectives.For(kayn, KaynForm.ShadowAssassin).Burst, assassin.Burst, 1e-9,
            "Weights are per entry: changing Rhaast leaves Shadow Assassin alone.");
    }

    [TestMethod]
    public void MoreSurvivalWeightBuysSurvivalOnKindred()
    {
        var state = Game("Kindred", 12 * 60, 800, pet: Gustwalker, marks: 4);
        var tanky = new Dictionary<string, ModelSettings.ObjectiveWeights>
        {
            ["Kindred"] = new() { Damage = 1, Burst = 0, Uptime = 1, Survival = 2 },
        };

        var standard = Plan(state);
        var survival = Plan(state, tanky);

        Assert.IsFalse(standard.Any(Defensive), string.Join(" > ", standard.Select(i => i.Name)));
        Assert.IsTrue(survival.Count(Defensive) >= 2, string.Join(" > ", survival.Select(i => i.Name)));
    }

    [TestMethod]
    public void ChosenWeightsArePerEntryAndReplan()
    {
        var preferences = new PlanPreferences();
        var before = preferences.Key;

        preferences.SetWeights("Kindred", new ModelSettings.ObjectiveWeights { Damage = 1, Burst = 9, Uptime = 0, Survival = 0.5 });

        Assert.AreNotEqual(before, preferences.Key, "New weights are a reason to re-plan.");
        Assert.AreEqual(ModelSettings.ObjectiveWeights.MaxWeight, preferences.Weights["kindred"].Burst, 1e-9, "Clamped to the page's range.");
        Assert.IsFalse(preferences.Weights.ContainsKey("Kayn"));

        preferences.SetWeights("Kindred", null);
        Assert.AreEqual(before, preferences.Key, "Back to the model's own.");
    }

    [TestMethod]
    public void TheCoreDoesNotDependOnWhatYouCanAffordRightNow()
    {
        var broke = Plan(Game("Kindred", 8 * 60, 0, pet: Gustwalker, marks: 2));
        var flush = Plan(Game("Kindred", 8 * 60, 1250, pet: Gustwalker, marks: 2));

        static string Set(IEnumerable<Item> items) => string.Join(", ", items.Select(i => i.Name).Order());

        Assert.AreEqual(4, broke.Count, "Three items and shoes.");
        Assert.AreEqual(Set(broke), Set(flush), "Gold in the pocket may change the order, never which items make the core.");
    }

    [TestMethod]
    public void ThePlanIsTheSliderSizePlusShoes()
    {
        var plan = Plan(Game("Kayn", 5 * 60, 600));

        Assert.AreEqual(3, plan.Count(i => !i.Groups.Contains("Boots")));
        Assert.AreEqual(1, plan.Count(i => i.Groups.Contains("Boots")));
    }

    [TestMethod]
    public void WithTheCoreItemsBuiltTheShoesAreWhatIsLeft()
    {
        var state = Game("Kayn", 22 * 60, 400, level: 14);
        var me = state.ActivePlayer!;
        state = new GameState
        {
            GameTime = state.GameTime,
            CurrentGold = state.CurrentGold,
            Objectives = state.Objectives,
            Players =
            [
                new PlayerState
                {
                    Champion = me.Champion, Team = me.Team, Position = me.Position, Level = me.Level, IsActivePlayer = true,
                    Items = [new OwnedItem(Item(Pet), 1, 0), new OwnedItem(Item(6698), 1, 1), new OwnedItem(Item(3142), 1, 2), new OwnedItem(Item(6694), 1, 3)],
                },
                .. state.Enemies,
            ],
        };
        var stack = new GameStack();
        stack.Push(state);

        var source = new Recommendations.BuildRecommendations(_items, _neutrals, _kits, _model);
        source.Compute(state, stack);
        while (source.Refine())
        {
        }

        var path = source.Current(state)!.BuildPath.Where(s => s.Status != Dtos.BuildStepDto.Owned).ToList();
        Assert.HasCount(1, path, string.Join(" > ", path.Select(s => s.Item.Name)));
        Assert.IsTrue(Item(path[0].Item.RiotId).Groups.Contains("Boots"));
    }

    [TestMethod]
    public void PaceFollowsTheGoldTrendWithoutAFloor()
    {
        // 25 minutes in with almost nothing earned: the old floor held this at half of normal.
        var state = Game("Kayn", 25 * 60, 700, level: 11);
        var pace = new GameForecaster(state, new GameStack(), _model).Outlook(state.ActivePlayer!).Pace;

        Assert.IsLessThan(0.5, pace);
        Assert.IsGreaterThan(0.0, pace);
    }

    [TestMethod]
    public void AnItemIsBoughtWhenItsGoldIsInNotOnARecallGrid()
    {
        var early = Context(Game("Kayn", 7.5 * 60, 0));
        var later = Context(Game("Kayn", 8 * 60, 0));
        var opening = Context(Game("Kayn", 60, 0));

        // Affordable at 11:40: bought at 11:40, whether you ask at 7:30 or 8:00.
        Assert.AreEqual(700, early.NextRecall(700, early.Now), 1e-6);
        Assert.AreEqual(700, later.NextRecall(700, later.Now), 1e-6);
        Assert.AreEqual(_model.Settings.Planner.FirstRecallSeconds, opening.NextRecall(120, opening.Now), 1e-6, "Never before the first recall.");
    }

    [TestMethod]
    public void BootsAreNotFirstJustBecauseTheyAreAffordable()
    {
        // Gold for boots in hand, not for the first item: boots only go first if owning them
        // early is worth more than the delay they put on the first item.
        var plan = Plan(Game("Kayn", 8 * 60, 1100, level: 7));

        Assert.IsFalse(plan[0].Groups.Contains("Boots"), string.Join(" > ", plan.Select(i => i.Name)));
    }

    [TestMethod]
    public void MarksKeepMostlyTheStandardPace()
    {
        var kindred = _champions.ByName("Kindred")!;
        var stacking = kindred.Stacking[0];

        // One mark in fifteen minutes is far behind the standard 0.35 a minute, yet the forecast
        // keeps most of the standard pace.
        var behind = Game("Kindred", 15 * 60, 500, pet: Gustwalker, marks: 1);
        var forecaster = new GameForecaster(behind, new GameStack(), _model);
        var gained = forecaster.StacksAt(behind.ActivePlayer!, stacking, 25 * 60) - 1;

        Assert.IsGreaterThan(0.7 * stacking.InitialStacksPerMinute * 10, gained);
        Assert.IsLessThan(stacking.InitialStacksPerMinute * 10, gained);

        // Before 8:00 your own pace does not count at all.
        var early = Game("Kindred", 7 * 60, 500, pet: Gustwalker, marks: 5);
        var fromEarly = new GameForecaster(early, new GameStack(), _model).StacksAt(early.ActivePlayer!, stacking, 17 * 60) - 5;
        Assert.AreEqual(stacking.InitialStacksPerMinute * 10, fromEarly, 1e-6);
    }

    [TestMethod]
    public void MarksAreForecastNoHigherThanTenButNeverBelowWhatYouHave()
    {
        var stacking = _champions.ByName("Kindred")!.Stacking[0];
        var usual = Game("Kindred", 30 * 60, 500, pet: Gustwalker, marks: 9);
        var ahead = Game("Kindred", 30 * 60, 500, pet: Gustwalker, marks: 14);

        Assert.AreEqual(10, new GameForecaster(usual, new GameStack(), _model).StacksAt(usual.ActivePlayer!, stacking, 60 * 60), 1e-6);
        Assert.AreEqual(14, new GameForecaster(ahead, new GameStack(), _model).StacksAt(ahead.ActivePlayer!, stacking, 60 * 60), 1e-6);
    }

    [TestMethod]
    public void KaynNamesUmbralTrespassAsHisSurvivalAbility()
    {
        var kayn = _kits.For(_champions.ByName("Kayn")!)!;

        Assert.AreEqual("Umbral Trespass", kayn.Survival(new AbilityRanks(5, 3, 1, 1))?.Name);
        Assert.IsNull(kayn.Survival(new AbilityRanks(3, 1, 1, 0)));
    }

    [TestMethod]
    public void AKaynWithoutShadowAssassinByFifteenMinutesIsRhaast()
    {
        var plain = new Dictionary<string, string> { ["W"] = "KaynW" };
        var assassin = new Dictionary<string, string> { ["W"] = "KaynAssW" };

        Assert.IsNull(Context(Game("Kayn", 12 * 60, 0, abilities: plain)).DetectedForm, "Could still be untransformed.");
        Assert.AreEqual(KaynForm.Darkin, Context(Game("Kayn", 15 * 60, 0, abilities: plain)).DetectedForm);
        Assert.AreEqual(KaynForm.ShadowAssassin, Context(Game("Kayn", 15 * 60, 0, abilities: assassin)).DetectedForm);
        Assert.IsNull(Context(Game("Kindred", 20 * 60, 0, pet: Gustwalker)).DetectedForm, "Kindred has no forms.");
    }
}
