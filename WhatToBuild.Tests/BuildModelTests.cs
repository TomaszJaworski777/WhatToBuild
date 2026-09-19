using System.Diagnostics;
using WhatToBuild.Data;
using WhatToBuild.Dtos;
using WhatToBuild.Forecasting;
using WhatToBuild.Game;
using WhatToBuild.Modeling;
using WhatToBuild.Modeling.Simulation;
using WhatToBuild.Planning;
using WhatToBuild.Recommendations;
using WhatToBuild.SupportedChampions;
using WhatToBuild.SupportedChampions.Kindred;

namespace WhatToBuild.Tests;

[TestClass]
public class BuildModelTests
{
    private const double Now = 22 * 60;

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

    private static PlayerState Player(string champion, Team team, string position, int level, params int[] items) => new()
    {
        Champion = _champions.ByName(champion)!,
        Team = team,
        Position = position,
        Level = level,
        Items = items.Select((id, slot) => new OwnedItem(Item(id), 1, slot)).ToList(),
    };

    private static PlayerState Kindred(params int[] items) =>
        new()
        {
            Champion = _champions.ByName("Kindred")!,
            Team = Team.Order,
            Position = "JUNGLE",
            Level = 13,
            Items = (items.Length > 0 ? items : [6672, 3031, 3006, 1101]).Select((id, slot) => new OwnedItem(Item(id), 1, slot)).ToList(),
            IsActivePlayer = true,
        };

    private static GameState Game(IEnumerable<PlayerState> enemies, IEnumerable<PlayerState>? allies = null, PlayerState? me = null, double time = Now, double gold = 500) => new()
    {
        GameTime = time,
        CurrentGold = gold,
        Players = [me ?? Kindred(), .. allies ?? [], .. enemies],
        Objectives = new Dictionary<Team, ObjectiveCounts> { [Team.Order] = new(), [Team.Chaos] = new() },
    };

    private static PlayerState[] Healers() =>
    [
        Player("Aatrox", Team.Chaos, "TOP", 14, 3071, 3047, 3053),
        Player("Warwick", Team.Chaos, "JUNGLE", 13, 3153, 3047, 3065),
        Player("Vladimir", Team.Chaos, "MIDDLE", 14, 6653, 3020, 3089),
        Player("Draven", Team.Chaos, "BOTTOM", 13, 6672, 3006, 3072),
        Player("Soraka", Team.Chaos, "UTILITY", 11, 6617, 3158, 3107),
    ];

    private static PlayerState[] NoSustain() =>
    [
        Player("Ornn", Team.Chaos, "TOP", 14, 3068, 3047, 3143),
        Player("Rammus", Team.Chaos, "JUNGLE", 13, 3075, 3047, 3143),
        Player("Zed", Team.Chaos, "MIDDLE", 14, 6698, 3158, 3142),
        Player("Caitlyn", Team.Chaos, "BOTTOM", 13, 6672, 3006, 3031),
        Player("Leona", Team.Chaos, "UTILITY", 11, 3190, 3047, 3109),
    ];

    private static PlayerState[] Shielders() =>
    [
        Player("Shen", Team.Chaos, "TOP", 14, 3068, 3047, 3143),
        Player("Nautilus", Team.Chaos, "JUNGLE", 13, 3075, 3047, 3143),
        Player("Karma", Team.Chaos, "MIDDLE", 14, 6655, 3020, 4645),
        Player("Caitlyn", Team.Chaos, "BOTTOM", 13, 6673, 3006, 3031),
        Player("Lulu", Team.Chaos, "UTILITY", 11, 6617, 3158, 3107),
    ];

    private static PlayerState[] Squishies() =>
    [
        Player("Zed", Team.Chaos, "TOP", 14, 6698, 3158, 3142),
        Player("Talon", Team.Chaos, "JUNGLE", 13, 6698, 3158, 3142),
        Player("Xerath", Team.Chaos, "MIDDLE", 14, 6655, 3020, 4645),
        Player("Caitlyn", Team.Chaos, "BOTTOM", 13, 6672, 3006, 3031),
        Player("Draven", Team.Chaos, "UTILITY", 11, 6672, 3006, 3031),
    ];

    private static PlayerState[] Tanks() =>
    [
        Player("Ornn", Team.Chaos, "TOP", 14, 3068, 3047, 3143, 3083),
        Player("Rammus", Team.Chaos, "JUNGLE", 13, 3075, 3047, 3143, 3065),
        Player("Malphite", Team.Chaos, "MIDDLE", 14, 3068, 3111, 3065, 3083),
        Player("Maokai", Team.Chaos, "BOTTOM", 13, 3084, 3047, 3075, 3065),
        Player("Leona", Team.Chaos, "UTILITY", 11, 3190, 3047, 3109, 3083),
    ];

    private static BuildEvaluator Evaluator(GameState state, ModelData? model = null)
    {
        var stack = new GameStack();
        stack.Push(state);
        return new BuildEvaluator(new BuildContext(state, stack, _items, _neutrals, _kits, model ?? _model));
    }

    private static ModelData DefaultWeights()
    {
        var model = ModelData.Load(Path.Combine(AppContext.BaseDirectory, "GameData"));
        model.Settings.Objectives.Champions.Clear();
        return model;
    }

    private static Item StatsOnly(Item item) => new()
    {
        Id = Guid.NewGuid(),
        RiotId = -item.RiotId,
        Name = item.Name + " (stats only)",
        Cost = item.Cost,
        Unique = item.Unique,
        Groups = item.Groups,
        BuildPath = item.BuildPath,
        Stats = item.Stats,
    };

    private static double EffectValue(GameState state, int riotId, EvaluationMode mode = EvaluationMode.Screen)
    {
        var evaluator = Evaluator(state);
        var owned = evaluator.Context.Owned;
        var item = Item(riotId);
        var time = Now + 300;

        var with = evaluator.Evaluate([.. owned, item], time, null, mode).Score;
        var without = evaluator.Evaluate([.. owned, StatsOnly(item)], time, null, mode).Score;
        return Math.Exp(with - without) - 1;
    }

    private static ChampionState KindredEntity(params int[] items) => new(_champions.ByName("Kindred")!, 13, items.Append(3006).Append(3031).Select(Item));

    private static AbilityRanks Ranks => _kits.RanksFor(_champions.ByName("Kindred")!, 13, null);

    private static ChampionState Garen() => new(_champions.ByName("Garen")!, 13, [Item(3068), Item(3047)]);

    private static FightResult Fight(ChampionState us, TargetSustain? sustain, double maxSeconds = 30) =>
        FightSimulator.Run(new FightSetup(us, Garen(), Ranks, 4, maxSeconds, 0.5, sustain), _kits.NewFight(us.Champion));

    [TestMethod]
    public void ShieldsAbsorbOnceAcrossManyHits()
    {
        var plain = Fight(KindredEntity(6672), null);
        var shielded = Fight(KindredEntity(6672), new TargetSustain(0, [new Shield(600)]));

        Assert.AreEqual(600, shielded.Shielded, 0.001);
        Assert.AreEqual(plain.Damage + 600, shielded.Damage, 250, "The shield soaks 600 once, not a slice of every hit.");
        Assert.IsGreaterThan(plain.TimeToKill!.Value, shielded.TimeToKill!.Value);
    }

    [TestMethod]
    public void SerpentsFangHalvesShieldsInFights()
    {
        var result = Fight(KindredEntity(6672, 6695), new TargetSustain(0, [new Shield(600)]));

        Assert.AreEqual(300, result.Shielded, 0.001);
    }

    [TestMethod]
    public void HealingSlowsTheKillAndGrievousWoundsCutsIt()
    {
        var sustain = new TargetSustain(120, []);
        var plain = Fight(KindredEntity(6672), null);
        var healed = Fight(KindredEntity(6672), sustain);
        var reminder = Fight(KindredEntity(6672, 3033), sustain);
        var statsOnly = new ChampionState(_champions.ByName("Kindred")!, 13, [Item(6672), Item(3006), Item(3031), StatsOnly(Item(3033))]);
        var reminderStatsOnly = FightSimulator.Run(new FightSetup(statsOnly, Garen(), Ranks, 4, 30, 0.5, sustain), _kits.NewFight(statsOnly.Champion));

        Assert.IsGreaterThan(plain.TimeToKill!.Value, healed.TimeToKill!.Value);
        Assert.IsNotNull(reminder.TimeToKill);
        Assert.AreEqual(reminderStatsOnly.Healed / reminderStatsOnly.Seconds * 0.6, reminder.Healed / reminder.Seconds, 6, "40% Grievous Wounds leaves 60% of the healing.");
        Assert.IsGreaterThan(reminder.TimeToKill!.Value, reminderStatsOnly.TimeToKill!.Value);
    }

    [TestMethod]
    public void GrievousWoundsDoesNotStackWithAnAllysCoverage()
    {
        var withAlly = new Fight(new FightSetup(KindredEntity(3033), Garen(), AbilityRanks.None, Sustain: new TargetSustain(100, [], ExternalGrievousWounds: 0.2)));
        var alone = new Fight(new FightSetup(KindredEntity(6672), Garen(), AbilityRanks.None, Sustain: new TargetSustain(100, [], ExternalGrievousWounds: 0.2)));

        Assert.AreEqual(0.4, withAlly.GrievousWounds, 1e-9);
        Assert.AreEqual(0.2, alone.GrievousWounds, 1e-9);
    }

    [TestMethod]
    public void TheCollectorExecutesChampions()
    {
        var fight = new Fight(new FightSetup(KindredEntity(6676), Garen(), AbilityRanks.None));

        Assert.AreEqual(0.05 * Garen().MaxHealth, fight.ExecuteHealth, 1e-6);
    }

    [TestMethod]
    public void ATargetThatOutHealsYouScoresNearZero()
    {
        var result = FightSimulator.Run(new FightSetup(new ChampionState(_champions.ByName("Kindred")!, 1), Garen(), AbilityRanks.None, 0, 10, 0.5, new TargetSustain(5000, [])));

        Assert.IsNull(result.TimeToKill);
        Assert.IsLessThan(result.Damage / result.Seconds * 0.2, result.EffectiveDps);
    }

    [TestMethod]
    public void EnemyGoldIsTheirItemValueAndGrowsWithTheirOwnRate()
    {
        var rich = Player("Caitlyn", Team.Chaos, "BOTTOM", 13, 6672, 3006, 3031, 3036);
        var poor = Player("Draven", Team.Chaos, "BOTTOM", 13, 3006);
        var state = Game([rich, poor]);
        var forecaster = new GameForecaster(state, new GameStack(), _model);

        Assert.AreEqual(rich.ItemValue, forecaster.EarnedAt(rich, Now), 1e-6);
        Assert.IsGreaterThan(forecaster.EarnedAt(poor, Now + 300) - poor.ItemValue, forecaster.EarnedAt(rich, Now + 300) - rich.ItemValue);
        Assert.IsGreaterThan(forecaster.EarnedAt(rich, Now + 300), forecaster.EarnedAt(rich, Now + 600));
    }

    [TestMethod]
    public void EnemyRatesArePulledTowardTheLobbyTrend()
    {
        var poor = Player("Draven", Team.Chaos, "BOTTOM", 13, 3006);
        var lobby = Enumerable.Range(0, 4).Select(_ => Player("Caitlyn", Team.Chaos, "BOTTOM", 13, 6672, 3006, 3031, 3036)).ToList();
        var alone = new GameForecaster(Game([poor]), new GameStack(), _model).Outlook(poor).Pace;
        var withLobby = new GameForecaster(Game([poor, .. lobby]), new GameStack(), _model).Outlook(poor).Pace;

        Assert.IsGreaterThan(alone, withLobby);
    }

    [TestMethod]
    public void LevelsNeverDropAndStopAtEighteen()
    {
        var state = Game(NoSustain());
        var forecaster = new GameForecaster(state, new GameStack(), _model);

        foreach (var player in state.Players)
        {
            Assert.IsGreaterThanOrEqualTo(player.Level, forecaster.LevelAt(player, Now + 60));
            Assert.IsLessThanOrEqualTo(18, forecaster.LevelAt(player, Now + 3600));
        }
    }

    [TestMethod]
    public void EnemiesFinishTheItemTheirComponentsAreFor()
    {
        var garen = Player("Garen", Team.Chaos, "TOP", 13, 3047, 3044);
        var projector = new BuildProjector(_items, _model.Meta);
        var build = projector.Project(garen, 3000, _ => Now);

        Assert.IsTrue(build.Purchases.Any(p => p.Item.BuildPath.Contains(Item(3044).Id)), $"Phage should turn into its item, got {string.Join(", ", build.Purchases.Select(p => p.Item.Name))}");
        Assert.IsTrue(ItemRules.IsLegal(build.Items));
        Assert.IsLessThanOrEqualTo(ItemRules.InventorySlots, ItemRules.Slots(build.Items));
    }

    [TestMethod]
    public void ForecastStacksGrowWithTime()
    {
        var ornn = Player("Ornn", Team.Chaos, "TOP", 14, 3084, 3047);
        var state = Game([ornn]);
        state.ItemFirstSeen = new Dictionary<(string Champion, int Item), double> { [("Ornn", 3084)] = Now - 300 };
        var world = new WorldForecast(new GameForecaster(state, new GameStack(), _model), new BuildProjector(_items, _model.Meta), _neutrals, 15);

        var soon = world.EnemiesAt(Now).Single().ItemStacks[Item(3084).Id];
        var later = world.EnemiesAt(Now + 600).Single().ItemStacks[Item(3084).Id];

        Assert.IsGreaterThan(soon, later);
        Assert.IsGreaterThan(world.EnemiesAt(Now).Single().Entity.MaxHealth, world.EnemiesAt(Now + 600).Single().Entity.MaxHealth);
    }

    [TestMethod]
    public void AntiHealIsWorthMoreAgainstHealers()
    {
        var healers = EffectValue(Game(Healers()), 3033);
        var none = EffectValue(Game(NoSustain()), 3033);

        Assert.IsGreaterThan(0.02, healers, $"Grievous Wounds adds {healers:P1} against healers.");
        Assert.IsGreaterThan(none + 0.02, healers);
    }

    [TestMethod]
    public void AnAllysAntiHealLowersYours()
    {
        var alone = EffectValue(Game(Healers()), 3033);
        var covered = EffectValue(Game(Healers(), [Player("Jinx", Team.Order, "BOTTOM", 13, 3033, 3006, 3031)]), 3033);

        Assert.IsGreaterThan(covered, alone);
    }

    [TestMethod]
    public void ShieldReductionIsWorthMoreAgainstShields()
    {
        var shielders = EffectValue(Game(Shielders()), 6695);
        var none = EffectValue(Game(Squishies()), 6695);

        Assert.IsGreaterThan(0.01, shielders, $"Shield reduction adds {shielders:P1} against shielders.");
        Assert.IsGreaterThan(none, shielders);
    }

    [TestMethod]
    public void PercentHealthDamageIsWorthBuyingAgainstTanks()
    {
        var tanks = EffectValue(Game(Tanks()), 3153, EvaluationMode.Full);
        var fresh = EffectValue(Game([Player("Zed", Team.Chaos, "TOP", 7), Player("Xerath", Team.Chaos, "MIDDLE", 7)]), 3153, EvaluationMode.Full);

        Assert.IsGreaterThan(0.05, tanks, $"Blade of the Ruined King's passive adds {tanks:P1} against tanks.");
        Assert.IsGreaterThan(fresh, tanks, $"Against tanks {tanks:P1}, against low-health squishies {fresh:P1}.");
    }

    [TestMethod]
    public void FrontlineAlliesDrawDamageAwayFromYou()
    {
        var alone = Evaluator(Game(Squishies())).BattlefieldAt(Now);
        var guarded = Evaluator(Game(Squishies(),
        [
            Player("Ornn", Team.Order, "TOP", 14, 3068),
            Player("Leona", Team.Order, "UTILITY", 11, 3190),
        ])).BattlefieldAt(Now);

        Assert.IsGreaterThan(guarded.Focus.Values.Sum(), alone.Focus.Values.Sum());
    }

    [TestMethod]
    public void SurvivalItemsAddTimeAlive()
    {
        var evaluator = Evaluator(Game(Squishies()));
        var owned = evaluator.Context.Owned;

        var before = evaluator.Evaluate(owned, Now + 300);
        var angel = evaluator.Evaluate([.. owned, Item(3026)], Now + 300);

        Assert.IsGreaterThan(before.TimeAlive, angel.TimeAlive);
        Assert.IsGreaterThan(0, before.IncomingDps);
    }

    [TestMethod]
    public void ClearMattersEarlyAndFadesLater()
    {
        var jungle = _model.Settings.Jungle;

        Assert.IsGreaterThan(jungle.ClearWeightAt(1800), jungle.ClearWeightAt(300));
        Assert.AreEqual(jungle.ClearWeightEarly, jungle.ClearWeightAt(0), 1e-9);
        Assert.AreEqual(jungle.ClearWeightLate, jungle.ClearWeightAt(3600), 1e-9);
    }

    [TestMethod]
    public void DamageItemsSpeedUpTheClear()
    {
        var me = new PlayerState
        {
            Champion = _champions.ByName("Kindred")!,
            Team = Team.Order,
            Position = "JUNGLE",
            Level = 5,
            Items = [new OwnedItem(Item(1101), 1, 0)],
            IsActivePlayer = true,
        };
        var evaluator = Evaluator(Game(NoSustain(), me: me, time: 300), DefaultWeights());

        var owned = evaluator.Context.Owned;
        var before = evaluator.Evaluate(owned, 300);
        var kraken = evaluator.Evaluate([.. owned, Item(6672)], 300);

        Assert.IsNotNull(before.Clear);
        Assert.IsGreaterThan(kraken.Clear!.KillSeconds, before.Clear.KillSeconds);
        Assert.IsGreaterThan(0.5, before.ClearWeight);
    }

    [TestMethod]
    public void ClearStopsMatteringOnceYouHaveItems()
    {
        var early = Evaluator(Game(NoSustain(), me: Kindred(1101), time: 300), DefaultWeights()).Context;
        var built = Evaluator(Game(NoSustain(), me: Kindred(1101, 6672, 3031, 3036), time: 300), DefaultWeights()).Context;

        Assert.IsGreaterThan(0.5, early.ClearWeightAt(300));
        Assert.AreEqual(0, built.ClearWeightAt(300), 1e-9, "Three completed items: you are fighting, not farming.");
        Assert.AreEqual(0, early.ClearWeightAt(20 * 60), 1e-9, "Past the fade, clear speed no longer counts.");
    }

    [TestMethod]
    public void MoveSpeedFollowsTheWikiFormula()
    {
        Assert.AreEqual(350, KindredEntity().Stats.MoveSpeed - 45 + 25, 1e-9, "Base 325 plus Boots' 25 (the helper adds Berserker's 45).");
        Assert.AreEqual(325 + 45, KindredEntity().Stats.MoveSpeed, 1e-9);
        Assert.AreEqual((325 + 45) * 1.04, KindredEntity(6672).Stats.MoveSpeed, 1e-9, "Flat first, then percent (Kraken 4%).");
        Assert.AreEqual(480, StatCalculator.MoveSpeed(500, 0), 1e-9, "Above 490: raw × 0.5 + 230.");
        Assert.AreEqual(415 + 0.8 * 35, StatCalculator.MoveSpeed(450, 0), 1e-9, "415–490 counts at 80%.");
    }

    private static GameState EarlyBack(double minutes, double gold) =>
        Game(NoSustain().Select(p => Player(p.Champion.Name, p.Team, p.Position, 4, 1055)).ToArray(),
            me: new PlayerState
            {
                Champion = _champions.ByName("Kindred")!, Team = Team.Order, Position = "JUNGLE", Level = 4,
                Items = [new OwnedItem(Item(1101), 1, 0)], IsActivePlayer = true,
            },
            time: minutes * 60, gold: gold);

    [TestMethod]
    public void BootsShortenTheWalkBetweenCamps()
    {
        var evaluator = Evaluator(EarlyBack(3.5, 500), DefaultWeights());
        var owned = evaluator.Context.Owned;

        var barefoot = evaluator.Evaluate(owned, 210);
        var booted = evaluator.Evaluate([.. owned, Item(1001)], 210);

        Assert.AreEqual(barefoot.Clear!.WalkSeconds * 325 / 350, booted.Clear!.WalkSeconds, 1e-6);
        Assert.IsGreaterThan(barefoot.Tempo, booted.Tempo);
        Assert.IsGreaterThan(booted.IncomingDps, barefoot.IncomingDps, "Faster than the enemy team: easier to dodge and kite.");
    }

    [TestMethod]
    public void OneBootsPointIsWorthAboutTwelveGoldOfStats()
    {
        var model = DefaultWeights();
        var evaluator = Evaluator(Game(NoSustain()), model);
        var owned = evaluator.Context.Owned;
        var time = Now;

        var baseline = evaluator.Evaluate(owned, time, null, EvaluationMode.Full);
        var perGold = new[] { 1036, 1042, 1029, 1028, 1033 }
            .Average(id => (evaluator.Evaluate([.. owned, Item(id)], time, null, EvaluationMode.Full).Score - baseline.Score) / Item(id).Cost);
        var boots = evaluator.Evaluate([.. owned, Item(1001)], time, null, EvaluationMode.Full);
        var tempoOnly = model.Settings.Movement.TempoWeightAt(time) * Math.Log(boots.Tempo / baseline.Tempo);

        Assert.AreEqual(300, tempoOnly / perGold, 150, $"Tempo prices Boots at {tempoOnly / perGold:0} gold.");
    }

    [TestMethod]
    public void OnASmallBackSpendTowardThePlan()
    {
        var recommendation = new BuildRecommendations(_items, _neutrals, _kits, _model).Compute(EarlyBack(3.5, 500), new GameStack())!;
        var planned = recommendation.BuildPath.Where(s => s.Status != BuildStepDto.Owned).Select(s => _items.ByRiotId(s.Item.RiotId)!).ToList();

        Assert.IsNotEmpty(recommendation.BuyNow.Items);
        Assert.IsLessThanOrEqualTo(500, recommendation.BuyNow.Cost);
        foreach (var bought in recommendation.BuyNow.Items.Select(i => _items.ByRiotId(i.RiotId)!))
        {
            Assert.IsTrue(planned.Any(p => p.Id == bought.Id || Builds(p, bought)), $"{bought.Name} is not part of the plan.");
        }
    }

    private static bool Builds(Item parent, Item component) =>
        parent.BuildPath.Any(id => id == component.Id || _items.ById(id) is { } child && Builds(child, component));

    [TestMethod]
    public void EveryChampionHasAStandardBuild()
    {
        foreach (var champion in _champions.All)
        {
            var build = _model.Meta.For(champion);
            Assert.IsGreaterThanOrEqualTo(6, build.Count, $"{champion.Name} needs a full metaBuild.");
            Assert.IsTrue(ItemRules.IsLegal(build), $"{champion.Name}'s metaBuild breaks the item rules.");
        }
    }

    [TestMethod]
    public void EnemiesKeepTheirItemsAndFollowTheirStandardBuild()
    {
        var garen = Player("Garen", Team.Chaos, "TOP", 13, 3047);
        var build = new BuildProjector(_items, _model.Meta).Project(garen, 3300, _ => Now);
        var standard = _model.Meta.For(garen.Champion);

        Assert.Contains(Item(3047), build.Items, "What they own stays.");
        Assert.AreEqual(standard.First(i => i.RiotId != 3047).Id, build.Purchases.First().Item.Id, "Next is the first standard item they lack.");
    }

    [TestMethod]
    public void TheOpeningPlansAWholeBuild()
    {
        var opening = _model.Settings.Planner.Opening!;
        var quick = new ModelSettings.PlanStage
        {
            Name = opening.Name, BudgetMilliseconds = 3000, ScreenCount = 12, BeamWidth = 4, Branching = 8, Depth = 6, Mode = opening.Mode, HorizonGold = opening.HorizonGold,
        };

        var plan = new BuildPlanner(Evaluator(EarlyBack(0.5, 0))).Plan(null, quick);

        Assert.IsGreaterThanOrEqualTo(4, plan.Steps.Count(s => s.Item.Cost >= 2000), string.Join(" > ", plan.Steps.Select(s => s.Item.Name)));
        Assert.IsTrue(plan.Steps.All(s => s.At >= _model.Settings.Planner.FirstRecallSeconds), "Nothing is bought before the first recall.");
    }

    [TestMethod]
    public void UpgradedBootsAreNeverSuggested()
    {
        var pool = new BuildPlanner(Evaluator(Game(NoSustain()))).CandidatePool();

        Assert.IsTrue(pool.Any(i => i.RiotId == 1001), "Plain Boots are a candidate.");
        Assert.IsTrue(pool.Any(i => i.RiotId == 3006), "Tier 2 boots are candidates.");
        Assert.IsFalse(pool.Any(i => i.RiotId is 3172 or 3170 or 3171), "Tier 3 boots come from Feats of Strength, not the shop.");
    }

    private static KindredKitData KitData() =>
        KindredKitData.Load(Path.Combine(AppContext.BaseDirectory, "GameData", ChampionKits.FolderName, KindredKitData.FileName));

    private static NeutralState Monster(string internalName) =>
        new(_neutrals.ByInternalName(internalName)!, _neutrals.Scaling, 900);

    [TestMethod]
    public void WolfBitesMonstersHarder()
    {
        var withBonus = KitData();
        var withoutBonus = KitData();
        withoutBonus.W.MonsterBonusDamage = 0;

        FightResult Run(KindredKitData data, Entity target) =>
            FightSimulator.Run(new FightSetup(KindredEntity(), target, new AbilityRanks(0, 1, 0, 0), 0, 3, 0.5), new KindredKit(data));

        var red = Run(withBonus, Monster("SRU_Red")).DamageBySource[KindredKit.WolfsFrenzy];
        var redPlain = Run(withoutBonus, Monster("SRU_Red")).DamageBySource[KindredKit.WolfsFrenzy];
        var garen = Run(withBonus, Garen()).DamageBySource[KindredKit.WolfsFrenzy];
        var garenPlain = Run(withoutBonus, Garen()).DamageBySource[KindredKit.WolfsFrenzy];

        Assert.AreEqual(1.5, red / redPlain, 0.02, "Wolf deals 50% more to jungle monsters (MonsterBonusDmg).");
        Assert.AreEqual(garenPlain, garen, 1e-9, "Champions are not monsters.");
    }

    [TestMethod]
    public void PounceMissingHealthIsCappedOnMonsters()
    {
        var baron = Monster("SRU_Baron");
        baron.AtHealthPercent(0.2);
        var garen = Garen();
        garen.AtHealthPercent(0.2);

        var kit = new KindredKit(KitData());
        var ranks = new AbilityRanks(0, 0, 1, 0);
        var onBaron = kit.PounceDamage(new Fight(new FightSetup(new ChampionState(_champions.ByName("Kindred")!, 13), baron, ranks)));
        var onGaren = kit.PounceDamage(new Fight(new FightSetup(new ChampionState(_champions.ByName("Kindred")!, 13), garen, ranks)));

        var mitigation = 100 / (100 + baron.Stats.Armor);
        Assert.IsLessThanOrEqualTo((80 + 200) * mitigation + 1, onBaron, "At most 200 from missing health against monsters (MonsterCap).");
        Assert.IsGreaterThan(0.8 * garen.MaxHealth * 0.05 * 100 / (100 + garen.Stats.Armor), onGaren, "No cap against champions.");
    }

    [TestMethod]
    public void LambsRespiteKeepsKindredAliveLonger()
    {
        var evaluator = Evaluator(Game(Squishies()));
        var evaluation = evaluator.Evaluate(evaluator.Context.Owned, Now);

        Assert.IsNotNull(evaluator.BattlefieldAt(Now).Survival);
        Assert.IsGreaterThan(evaluation.TimeAliveWithoutAbility + 4 * 120.0 / 140 - 0.01, evaluation.TimeAlive);
    }

    [TestMethod]
    public void AGlassCannonDiesFastWithoutHerUltimate()
    {
        var evaluator = Evaluator(Game(NoSustain()));
        var glass = new[] { 6672, 3031, 3006, 1101, 3036, 6676 }.Select(Item).ToList();
        var evaluation = evaluator.Evaluate(glass, 35 * 60, null, EvaluationMode.Full);

        Assert.IsLessThan(4, evaluation.TimeAliveWithoutAbility, $"Lasts {evaluation.TimeAliveWithoutAbility:0.0}s under focus.");
    }

    [TestMethod]
    public void TagEstimatesAreInReachOfTheSimulatedKit()
    {
        var kindred = _champions.ByName("Kindred")!;
        var build = new[] { 6672, 3006, 3031 }.Select(Item).ToList();
        var us = new ChampionState(kindred, 13, build);
        var dummy = new ChampionState(kindred, 13, build);
        var simulated = FightResult.Average(_model.Settings.Fight.AttackPhases
            .Select(phase => FightSimulator.Run(new FightSetup(us, dummy, _kits.RanksFor(kindred, 13, null), 3, 30, phase), _kits.NewFight(kindred)))
            .ToList()).EffectiveDps;

        var enemy = new PlayerState { Champion = kindred, Team = Team.Chaos, Position = "JUNGLE", Level = 13, Items = build.Select((b, s) => new OwnedItem(b, 1, s)).ToList() };
        var world = new WorldForecast(new GameForecaster(Game([enemy]), new GameStack(), _model), new BuildProjector(_items, _model.Meta), _neutrals, 15);
        var profile = new CombatProfiler(_model.Settings).Profile(world.EnemiesAt(Now).Single());
        var estimated = profile.Streams.Sum(s =>
        {
            var hit = s.Type == DamageType.Physical ? AttackerHits.Physical(profile.Entity, dummy, 1000) : AttackerHits.Magic(profile.Entity, dummy, 1000);
            return hit.Run().HealthDamage / 1000 * s.RawPerSecond;
        });

        Assert.IsGreaterThan(0.5 * simulated, estimated, $"Tags estimate {estimated:0} DPS, the kit deals {simulated:0}.");
        Assert.IsLessThan(1.2 * simulated, estimated);
    }

    [TestMethod]
    public void RangedChampionsKiteMeleeAttacks()
    {
        var state = Game(NoSustain());
        var noKiting = ModelData.Load(Path.Combine(AppContext.BaseDirectory, "GameData"));
        noKiting.Settings.Focus.KiteReduction = 0;

        var kiting = Evaluator(state);
        var standing = new BuildEvaluator(new BuildContext(state, new GameStack(), _items, _neutrals, _kits, noKiting));

        var withKiting = kiting.Evaluate(kiting.Context.Owned, Now).IncomingDps;
        var without = standing.Evaluate(standing.Context.Owned, Now).IncomingDps;

        Assert.IsGreaterThan(withKiting, without, "Standing still in melee range takes more damage.");
    }

    [TestMethod]
    public void HubrisIsUpMoreOftenTheMoreTakedownsYouGet()
    {
        var evaluator = Evaluator(Game(NoSustain()));

        Assert.AreEqual(0, evaluator.TakedownBuffUptime(0, 90), 1e-9);
        Assert.IsGreaterThan(1 - Math.Exp(-1.5), evaluator.TakedownBuffUptime(1, 90), "A takedown inside the fight also turns it on, on top of the 90 second carry-over.");
        Assert.IsGreaterThan(evaluator.TakedownBuffUptime(0.4, 90), evaluator.TakedownBuffUptime(1, 90));
        Assert.IsLessThan(1, evaluator.TakedownBuffUptime(3, 90));
    }

    [TestMethod]
    public void HubrisGrowsWithYourTakedowns()
    {
        var quiet = Evaluator(Game(NoSustain(), me: Kindred(6697, 3006, 1101)));
        var fed = Evaluator(Game(NoSustain(), me: new PlayerState
        {
            Champion = _champions.ByName("Kindred")!, Team = Team.Order, Position = "JUNGLE", Level = 13, IsActivePlayer = true, Kills = 12, Assists = 10,
            Items = new[] { 6697, 3006, 1101 }.Select((id, slot) => new OwnedItem(Item(id), 1, slot)).ToList(),
        }));

        Assert.IsGreaterThan(quiet.Us(quiet.Context.Owned, Now).Stats.AttackDamage, fed.Us(fed.Context.Owned, Now).Stats.AttackDamage);
    }

    [TestMethod]
    public void AlliesShredMakesYouKillFaster()
    {
        var plain = Fight(KindredEntity(6672), null);
        var shredded = Fight(KindredEntity(6672), new TargetSustain(0, [], ExternalArmorShred: 0.3));

        Assert.IsGreaterThan(shredded.TimeToKill!.Value, plain.TimeToKill!.Value);
    }

    [TestMethod]
    public void AnAllysBlackCleaverReachesYourTargets()
    {
        var evaluator = Evaluator(Game(Tanks(), [Player("Darius", Team.Order, "TOP", 14, 3071, 3047)]));
        var sustain = evaluator.BattlefieldAt(Now).Sustain.Values.First();

        Assert.AreEqual(0.3 * _model.Settings.Allies.ShredCoverage, sustain.ExternalArmorShred, 1e-9);
    }

    private static GameState Replay() =>
        GameStateParser.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Replays", "demo", "000.json")), _champions, _items);

    [TestMethod]
    public void PlansAreLegalAndFitTheInventory()
    {
        var evaluator = Evaluator(Replay());
        var plan = new BuildPlanner(evaluator).Plan();

        Assert.IsNotEmpty(plan.Steps);
        foreach (var step in plan.Steps)
        {
            Assert.IsTrue(ItemRules.IsLegal(step.After), string.Join(", ", step.After.Select(i => i.Name)));
            Assert.IsLessThanOrEqualTo(ItemRules.InventorySlots, ItemRules.Slots(step.After));
            Assert.IsFalse(step.Sold?.Groups.Contains("Boots") ?? false, "Boots are never sold: movement speed is not priced.");
            Assert.IsFalse(step.Sold?.Groups.Contains("HuntersTalismanGroup") ?? false);
        }

        Assert.IsTrue(plan.Steps.Zip(plan.Steps.Skip(1)).All(p => p.First.At <= p.Second.At));
    }

    [TestMethod]
    public void FinishedItemsAreOnlySoldForAClearGain()
    {
        var evaluator = Evaluator(Replay());
        var plan = new BuildPlanner(evaluator).Plan(null, _model.Settings.Planner.Stages[^1]);

        foreach (var step in plan.Steps.Where(s => s.Sold is { Cost: >= 2000 }))
        {
            var kept = evaluator.Evaluate(step.Before, step.At, step.StacksBefore, EvaluationMode.Full).Score;
            var swapped = evaluator.Evaluate(step.After, step.At, step.StacksAfter, EvaluationMode.Full).Score;
            Assert.IsGreaterThanOrEqualTo(_model.Settings.Planner.ReplaceFinishedMargin - 0.02, swapped - kept, $"Selling {step.Sold!.Name} for {step.Item.Name}.");
        }

        Assert.IsFalse(plan.Steps.Any(s => s.Item.RiotId == 6676 && s.Sold is not null),
            "The Collector is not worth selling a finished item for late: its gold needs time to pay back.");
    }

    [TestMethod]
    public void GoldPerKillFollowsYourKillRate()
    {
        var quiet = Kindred();
        var fed = new PlayerState
        {
            Champion = quiet.Champion, Team = quiet.Team, Position = quiet.Position, Level = quiet.Level,
            Items = quiet.Items, IsActivePlayer = true, Kills = 11,
        };
        var collector = Item(6676);

        var quietIncome = new BuildPlanner(Evaluator(Game(NoSustain(), me: quiet))).GoldIncome([collector]);
        var fedIncome = new BuildPlanner(Evaluator(Game(NoSustain(), me: fed))).GoldIncome([collector]);

        Assert.AreEqual(0, quietIncome, 1e-9, "No kills yet, no Collector gold.");
        Assert.AreEqual(25 * 11 / 22.0 / 60, fedIncome, 1e-9, "25 gold per kill at 11 kills in 22 minutes.");
    }

    [TestMethod]
    public void TheQuickStageAnswersWithinASecondOnOneThread()
    {
        var warmup = new BuildRecommendations(_items, _neutrals, _kits, _model);
        warmup.Compute(Game(NoSustain()), new GameStack());

        var source = new BuildRecommendations(_items, _neutrals, _kits, _model);
        var state = Replay();
        var stack = new GameStack();
        stack.Push(state);

        var watch = Stopwatch.StartNew();
        var recommendation = source.Compute(state, stack);
        watch.Stop();

        Assert.IsNotNull(recommendation);
        Assert.IsLessThan(1000, watch.ElapsedMilliseconds, $"Took {watch.ElapsedMilliseconds} ms.");
        Assert.AreEqual("quick", recommendation.Model!.Stage);
        Assert.IsTrue(recommendation.Model.Refining);
    }

    [TestMethod]
    public void TheDetailedStageRefinesThePlan()
    {
        var source = new BuildRecommendations(_items, _neutrals, _kits, _model);
        var state = Replay();
        source.Compute(state, new GameStack());

        Assert.IsTrue(source.Refine());
        var refined = source.Current(state)!;

        Assert.AreEqual("detailed", refined.Model!.Stage);
        Assert.IsFalse(refined.Model.Refining);
        Assert.IsFalse(source.Refine(), "There is no stage after the last one.");
    }

    [TestMethod]
    public void ASameStateReplanKeepsTheTarget()
    {
        var source = new BuildRecommendations(_items, _neutrals, _kits, _model);
        var state = Replay();
        source.Compute(state, new GameStack());
        source.Refine();
        var detailed = source.Current(state)!;

        Assert.IsTrue(source.Refresh(state, new GameStack()));
        var again = source.Current(state)!;

        Assert.AreEqual(detailed.BuyNow.Target, again.BuyNow.Target);
    }

    [TestMethod]
    public void ACancelledStageKeepsThePlanInHand()
    {
        var source = new BuildRecommendations(_items, _neutrals, _kits, _model);
        var state = Replay();
        var quick = source.Compute(state, new GameStack())!;

        Assert.IsFalse(source.Refine(new StopAtOnce()));
        Assert.AreEqual("quick", source.Current(state)!.Model!.Stage);
        Assert.AreEqual(quick.BuyNow.Target, source.Current(state)!.BuyNow.Target);
    }

    private sealed class StopAtOnce : IPlanControl
    {
        public bool ShouldStop() => true;

        public void Tick()
        {
        }
    }

    [TestMethod]
    public void TheBackgroundWorkerPublishesWithoutBlockingThePoll()
    {
        var source = new BuildRecommendations(_items, _neutrals, _kits, _model);
        using var published = new ManualResetEventSlim();
        source.Updated += r =>
        {
            if (r is not null)
            {
                published.Set();
            }
        };

        var state = Replay();
        var watch = Stopwatch.StartNew();
        var first = source.For(state, new GameStack());
        watch.Stop();

        Assert.IsNull(first, "Nothing is ready on the very first poll.");
        Assert.IsLessThan(50, watch.ElapsedMilliseconds, "The poll must not wait for the planner.");
        Assert.IsTrue(published.Wait(TimeSpan.FromSeconds(10)));
        Assert.IsNotNull(source.For(state, new GameStack()));
    }
}
