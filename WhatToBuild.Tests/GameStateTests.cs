using System.Text.Json.Nodes;
using WhatToBuild.Clients;
using WhatToBuild.Data;
using WhatToBuild.Game;
using WhatToBuild.Modeling;

namespace WhatToBuild.Tests;

[TestClass]
public class GameStateTests
{
    private static ChampionRepository _champions = null!;
    private static ItemRepository _items = null!;

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static GameState MidGame() => GameStateParser.Parse(Fixture("midgame.json"), _champions, _items);

    private static Champion Champ(string internalName) => _champions.ByInternalName(internalName)!;

    [ClassInitialize]
    public static void Load(TestContext context)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "GameData");
        _champions = ChampionRepository.Load(root);
        _items = ItemRepository.Load(root);
    }

    [TestMethod]
    public void ParsesRiotsOwnSample()
    {
        var state = GameStateParser.Parse(Fixture("riot-sample.json"), _champions, _items);

        var annie = state.Players.Single();
        Assert.AreEqual("Annie", annie.Champion.Name);
        Assert.AreEqual(1, annie.Level);
        Assert.AreEqual(Team.Order, annie.Team);
        Assert.IsTrue(annie.IsActivePlayer);
        Assert.AreEqual(0, state.GameTime, 0.001);
        Assert.AreEqual(0, state.Objectives[Team.Order].TotalDragons);
    }

    [TestMethod]
    public void FindsEveryPlayerAndOurselves()
    {
        var state = MidGame();

        Assert.HasCount(5, state.Players);
        Assert.AreEqual("Kindred", state.ActivePlayer!.Champion.Name);
        Assert.AreEqual(Team.Order, state.ActiveTeam);
        Assert.HasCount(2, state.Allies.ToList());
        Assert.HasCount(3, state.Enemies.ToList());
    }

    [TestMethod]
    public void UsesTheInternalNameWhenItDiffers()
    {
        var wukong = MidGame().Players.Single(p => p.Position == "JUNGLE" && p.Team == Team.Chaos);

        Assert.AreEqual("MonkeyKing", wukong.Champion.InternalName);
    }

    [TestMethod]
    public void ReadsLevelsScoresAndDeaths()
    {
        var state = MidGame();
        var kindred = state.Find(Champ("Kindred"))!;
        var soraka = state.Find(Champ("Soraka"))!;

        Assert.AreEqual(11, kindred.Level);
        Assert.AreEqual(5, kindred.Kills);
        Assert.AreEqual(2, kindred.Deaths);
        Assert.AreEqual(4, kindred.Assists);
        Assert.AreEqual(120, kindred.CreepScore);
        Assert.IsTrue(soraka.IsDead);
    }

    [TestMethod]
    public void ResolvesItemsAndCounts()
    {
        var garen = MidGame().Find(Champ("Garen"))!;

        Assert.HasCount(3, garen.Items);
        Assert.AreEqual(2, garen.Items.Single(i => i.Item.RiotId == 2003).Count);
        Assert.AreEqual(2700 + 1200 + 2 * 50, garen.ItemValue);
    }

    [TestMethod]
    public void ReportsItemsItCannotResolve()
    {
        CollectionAssert.AreEqual(new[] { 999999 }, MidGame().UnknownItemIds.ToList());
    }

    [TestMethod]
    public void OurGoldIncludesWhatWeSpent()
    {
        var state = MidGame();

        Assert.AreEqual(850, state.CurrentGold, 0.001);
        Assert.AreEqual(850 + 3000 + 1100, state.GoldEarned, 0.001);
    }

    [TestMethod]
    public void OurStatsComeFromTheGame()
    {
        var stats = MidGame().ActivePlayerStats!;

        Assert.AreEqual(148, stats.AttackDamage, 0.001);
        Assert.AreEqual(1640, stats.Health, 0.001);
        Assert.AreEqual(71.5, stats.Armor, 0.001);
    }

    [TestMethod]
    public void CreditsObjectivesToTheRightTeam()
    {
        var state = MidGame();
        var order = state.Objectives[Team.Order];
        var chaos = state.Objectives[Team.Chaos];

        Assert.AreEqual(1, order.Dragons["Fire"]);
        Assert.AreEqual(1, order.Elders);
        Assert.AreEqual(1, order.Heralds);
        Assert.AreEqual(1, chaos.Dragons["Earth"]);
        Assert.AreEqual(1, chaos.Barons);
        Assert.AreEqual(1, chaos.Inhibitors);
    }

    [TestMethod]
    public void CreditsStructuresByOwnerNotKiller()
    {
        var state = MidGame();

        Assert.AreEqual(1, state.Objectives[Team.Order].Turrets);
        Assert.AreEqual(0, state.Objectives[Team.Chaos].Turrets);
    }

    [TestMethod]
    public void FourthDragonGrantsTheSoul()
    {
        var state = WithDragons(("Fire", "Enemy One"), ("Earth", "Enemy One"), ("Water", "Enemy One"), ("Water", "Enemy One"));

        Assert.AreEqual("Water", state.Objectives[Team.Chaos].SoulType);
        Assert.IsFalse(state.Objectives[Team.Order].HasSoul);
    }

    [TestMethod]
    public void ThreeDragonsAreNotASoul()
    {
        var state = WithDragons(("Fire", "Enemy One"), ("Earth", "Enemy One"), ("Water", "Enemy One"));

        Assert.IsFalse(state.Objectives[Team.Chaos].HasSoul);
    }

    [TestMethod]
    public void ElderDoesNotCountTowardsSoul()
    {
        var state = WithDragons(("Fire", "Enemy One"), ("Earth", "Enemy One"), ("Water", "Enemy One"), ("Elder", "Enemy One"));

        Assert.IsFalse(state.Objectives[Team.Chaos].HasSoul);
        Assert.AreEqual(1, state.Objectives[Team.Chaos].Elders);
    }

    [TestMethod]
    public void SoulFollowsKillOrderNotListOrder()
    {
        var state = WithDragons(
            ("Water", "Enemy One", 1500.0),
            ("Fire", "Enemy One", 300.0),
            ("Earth", "Enemy One", 600.0),
            ("Water", "Enemy One", 900.0));

        Assert.AreEqual("Water", state.Objectives[Team.Chaos].SoulType);
    }

    [TestMethod]
    public void EnemyOceanSoulShowsUpAsHealing()
    {
        var neutrals = Neutrals();
        var state = WithDragons(("Fire", "Enemy One"), ("Earth", "Enemy One"), ("Water", "Enemy One"), ("Water", "Enemy One"));

        var enemySoul = neutrals.SoulFor(state.Objectives[Team.Chaos].SoulType)!;

        Assert.AreEqual("Ocean Soul", enemySoul.Name);
        Assert.IsGreaterThan(0, enemySoul.Tag("healing"));
    }

    [TestMethod]
    public void TeamHealingCountsStacksAndSoul()
    {
        var neutrals = Neutrals();
        var state = WithDragons(("Fire", "Enemy One"), ("Earth", "Enemy One"), ("Water", "Enemy One"), ("Water", "Enemy One"));

        var ocean = neutrals.DragonFor("Water")!;
        var expected = ocean.StackTag("healing", 2) + ocean.Soul.Tag("healing");

        Assert.AreEqual(expected, state.TeamTag(Team.Chaos, "healing", neutrals), 0.0001);
        Assert.AreEqual(0, state.TeamTag(Team.Order, "healing", neutrals), 0.0001);
    }

    [TestMethod]
    public void DrakesChangeTheEnemysStats()
    {
        var neutrals = Neutrals();
        var state = WithDragons(("Earth", "Enemy One"), ("Earth", "Enemy One"));
        var garen = state.Find(Champ("Garen"))!;

        var plain = garen.ToEntity();
        var buffed = state.EntityFor(garen, neutrals);

        Assert.AreEqual(plain.Stats.Armor * 1.10, buffed.Stats.Armor, 0.001);

        var hitPlain = DamageCalculator.Create(plain).AdDamage(300).Attack().Run();
        var hitBuffed = DamageCalculator.Create(buffed).AdDamage(300).Attack().Run();

        Assert.IsLessThan(hitPlain.HealthDamage, hitBuffed.HealthDamage);
    }

    [TestMethod]
    public void AlliedDrakesDoNotBuffEnemies()
    {
        var neutrals = Neutrals();
        var state = WithDragons(("Earth", "Ally One"), ("Earth", "Ally One"));
        var garen = state.Find(Champ("Garen"))!;

        Assert.AreEqual(garen.ToEntity().Stats.Armor, state.EntityFor(garen, neutrals).Stats.Armor, 0.001);
    }

    private static NeutralRepository Neutrals() =>
        NeutralRepository.Load(Path.Combine(AppContext.BaseDirectory, "GameData"));

    private static GameState WithDragons(params (string Type, string Killer)[] kills) =>
        WithDragons(kills.Select((k, i) => (k.Type, k.Killer, (i + 1) * 300.0)).ToArray());

    private static GameState WithDragons(params (string Type, string Killer, double Time)[] kills)
    {
        var node = JsonNode.Parse(Fixture("midgame.json"))!;
        var events = new JsonArray();

        foreach (var (type, killer, time) in kills)
        {
            events.Add(new JsonObject
            {
                ["EventName"] = "DragonKill",
                ["EventTime"] = time,
                ["DragonType"] = type,
                ["KillerName"] = killer,
            });
        }

        node["events"]!["Events"] = events;

        return GameStateParser.Parse(node.ToJsonString(), _champions, _items);
    }

    [TestMethod]
    public void EnemyStacksFollowTheInitialRate()
    {
        var veigar = MidGame().Find(Champ("Veigar"))!;

        var stacks = veigar.EstimatedStacks.Single();
        Assert.AreSame(Stats.AbilityPower, stacks.Stat);
        Assert.AreEqual(6 * 20, stacks.Stacks, 0.001);
    }

    [TestMethod]
    [DataRow(500.0, 0.0, 3.0)]
    [DataRow(575.0, 4.0, 7.0)]
    [DataRow(600.0, 8.0, 11.0)]
    [DataRow(700.0, 24.0, 27.0)]
    public void KindredMarksAreReadFromAttackRange(double range, double low, double high)
    {
        var marks = WithOurRange(range).ActivePlayer!.EstimatedStacks.Single();

        Assert.IsTrue(marks.Observed);
        Assert.AreSame(Stats.AbilityDamage, marks.Stat);
        Assert.AreEqual(low, marks.Stacks, 0.001);
        Assert.AreEqual(high, marks.High!.Value, 0.001);
    }

    [TestMethod]
    public void MaxedMarksHaveNoUpperBound()
    {
        var marks = WithOurRange(750).ActivePlayer!.EstimatedStacks.Single();

        Assert.AreEqual(32, marks.Stacks, 0.001);
        Assert.IsNull(marks.High);
    }

    [TestMethod]
    public void OffStepRangeFallsBackToTheEstimate()
    {
        var state = WithOurRange(610);
        var marks = state.ActivePlayer!.EstimatedStacks.Single();

        Assert.IsFalse(marks.Observed);
        Assert.AreEqual(state.ActivePlayer.Champion.Stacking.Single().InitialStacksPerMinute * 20, marks.Stacks, 0.001);
    }

    [TestMethod]
    public void OnlyOurMarksAreRead()
    {
        var node = JsonNode.Parse(Fixture("midgame.json"))!;
        node["activePlayer"]!["championStats"]!["attackRange"] = 600.0;
        var players = node["allPlayers"]!.AsArray();
        players[0]!["team"] = "CHAOS";
        node["activePlayer"]!["riotId"] = "Ally Two#TAG";
        node["activePlayer"]!["riotIdGameName"] = "Ally Two";
        node["activePlayer"]!["summonerName"] = "Ally Two#TAG";

        var state = GameStateParser.Parse(node.ToJsonString(), _champions, _items);
        var kindred = state.Find(Champ("Kindred"))!;

        Assert.IsFalse(kindred.IsActivePlayer);
        Assert.IsFalse(kindred.EstimatedStacks.Single().Observed);
    }

    [TestMethod]
    public void OurStatsStayExact()
    {
        Assert.AreEqual(148, MidGame().ActivePlayerStats!.AttackDamage, 0.001);
    }

    private static GameState WithOurRange(double range)
    {
        var node = JsonNode.Parse(Fixture("midgame.json"))!;
        node["activePlayer"]!["championStats"]!["attackRange"] = range;
        return GameStateParser.Parse(node.ToJsonString(), _champions, _items);
    }

    [TestMethod]
    public void PlayersBecomeCalculatorEntities()
    {
        var garen = MidGame().Find(Champ("Garen"))!.ToEntity();

        Assert.AreEqual(12, garen.Level);
        Assert.IsTrue(garen.Mitigations.Any(m => m.Versus == DamageSource.Crit));
        Assert.IsTrue(garen.Mitigations.Any(m => m.Versus == DamageSource.Attacks));
    }

    [TestMethod]
    public void SurvivesMissingSections()
    {
        var state = GameStateParser.Parse("{}", _champions, _items);

        Assert.IsEmpty(state.Players);
        Assert.IsNull(state.ActivePlayer);
        Assert.AreEqual(0, state.GameTime, 0.001);
    }

    [TestMethod]
    public void TrackerFillsTheStackFromReplays()
    {
        var snapshots = new[] { 60.0, 120.0, 180.0 }.Select(t => Snapshot(t, gold: t * 5)).ToList();
        var tracker = new GameTracker(new ReplayClient(snapshots), _champions, _items);

        while (tracker.PollAsync().Result is not null)
        {
        }

        Assert.AreEqual(3, tracker.Stack.Count);
        Assert.AreEqual(180, tracker.Stack.Latest!.GameTime, 0.001);
    }

    private static string Snapshot(double gameTime, double gold)
    {
        var node = JsonNode.Parse(Fixture("midgame.json"))!;
        node["gameData"]!["gameTime"] = gameTime;
        node["activePlayer"]!["currentGold"] = gold;
        return node.ToJsonString();
    }
}
