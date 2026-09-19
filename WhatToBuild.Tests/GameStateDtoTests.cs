using System.Text.Json.Nodes;
using WhatToBuild.Clients;
using WhatToBuild.Data;
using WhatToBuild.Dtos;
using WhatToBuild.Game;

namespace WhatToBuild.Tests;

[TestClass]
public class GameStateDtoTests
{
    private const string Patch = "16.18.1";

    private static ChampionRepository _champions = null!;
    private static ItemRepository _items = null!;
    private static NeutralRepository _neutrals = null!;

    [ClassInitialize]
    public static void Load(TestContext context)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "GameData");
        _champions = ChampionRepository.Load(root);
        _items = ItemRepository.Load(root);
        _neutrals = NeutralRepository.Load(root);
    }

    private static string Fixture() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "midgame.json"));

    private static GameStateDto Dto() =>
        GameStateMapper.ToDto(GameStateParser.Parse(Fixture(), _champions, _items), _neutrals, Patch);

    [TestMethod]
    public void SplitsPlayersIntoTeams()
    {
        var dto = Dto();

        Assert.AreEqual(GameStateDto.InGame, dto.Phase);
        Assert.AreEqual("Order", dto.ActiveTeam);
        Assert.HasCount(2, dto.Teams.Single(t => t.Team == "Order").Players);
        Assert.HasCount(3, dto.Teams.Single(t => t.Team == "Chaos").Players);
    }

    [TestMethod]
    public void TeamScoreIsTheSumOfItsPlayers()
    {
        var order = Dto().Teams.Single(t => t.Team == "Order");

        Assert.AreEqual(order.Players.Sum(p => p.Kills), order.Kills);
        Assert.AreEqual(order.Players.Sum(p => p.ItemValue), order.ItemValue);
    }

    [TestMethod]
    public void PlayersAreInRoleOrder()
    {
        var chaos = Dto().Teams.Single(t => t.Team == "Chaos").Players;

        CollectionAssert.AreEqual(
            new[] { "TOP", "JUNGLE", "MIDDLE" },
            chaos.Select(p => p.Position).ToList());
    }

    [TestMethod]
    public void OnlyOurStatsAreExact()
    {
        var players = Dto().Teams.SelectMany(t => t.Players).ToList();
        var us = players.Single(p => p.IsActivePlayer);

        Assert.IsTrue(us.StatsAreExact);
        Assert.AreEqual(148, us.Stats.AttackDamage, 0.001);
        Assert.IsTrue(players.Where(p => !p.IsActivePlayer).All(p => !p.StatsAreExact));
    }

    [TestMethod]
    public void IconsPointAtDataDragon()
    {
        var garen = Dto().Teams.SelectMany(t => t.Players).Single(p => p.Champion == "Garen");

        Assert.AreEqual("https://ddragon.leagueoflegends.com/cdn/16.18.1/img/champion/Garen.png", garen.ChampionIcon);
        Assert.IsTrue(garen.Items.All(i => i.Icon.StartsWith("https://ddragon.leagueoflegends.com/cdn/16.18.1/img/item/")));
    }

    [TestMethod]
    public void DragonsCarryTheirNames()
    {
        var chaos = Dto().Teams.Single(t => t.Team == "Chaos").Objectives;

        var earth = chaos.Dragons.Single();
        Assert.AreEqual("Earth", earth.Type);
        Assert.AreEqual("Mountain Drake", earth.Name);
        Assert.AreEqual(1, chaos.Barons);
    }

    [TestMethod]
    public void SoulHasADisplayName()
    {
        var node = JsonNode.Parse(Fixture())!;
        var events = new JsonArray();
        foreach (var type in new[] { "Fire", "Earth", "Water", "Water" })
        {
            events.Add(new JsonObject { ["EventName"] = "DragonKill", ["EventTime"] = events.Count * 300.0 + 300, ["DragonType"] = type, ["KillerName"] = "Enemy One" });
        }
        node["events"]!["Events"] = events;

        var dto = GameStateMapper.ToDto(GameStateParser.Parse(node.ToJsonString(), _champions, _items), _neutrals, Patch);
        var chaos = dto.Teams.Single(t => t.Team == "Chaos").Objectives;

        Assert.AreEqual("Water", chaos.SoulType);
        Assert.AreEqual("Ocean Soul", chaos.SoulName);
    }

    [TestMethod]
    public void EstimatedStacksAreExposed()
    {
        var veigar = Dto().Teams.SelectMany(t => t.Players).Single(p => p.Champion == "Veigar");

        Assert.AreEqual("abilityPower", veigar.EstimatedStacks.Single().Stat);
    }

    [TestMethod]
    public void WaitingStateHasNoTeams()
    {
        var waiting = GameStateDto.Waiting(Patch);

        Assert.AreEqual(GameStateDto.NoGame, waiting.Phase);
        Assert.IsEmpty(waiting.Teams);
    }

    [TestMethod]
    public void TrackerStartsANewStackForANewGame()
    {
        var snapshots = new[] { 1200.0, 1210.0, 30.0, 40.0 }.Select(Snapshot).ToList();
        var tracker = new GameTracker(new ReplayClient(snapshots), _champions, _items);

        for (var i = 0; i < snapshots.Count; i++)
        {
            tracker.PollAsync().Wait();
        }

        Assert.AreEqual(2, tracker.Stack.Count);
        Assert.AreEqual(40, tracker.Stack.Latest!.GameTime, 0.001);
    }

    [TestMethod]
    public void LoopingReplayNeverRunsOut()
    {
        var client = new ReplayClient([Snapshot(10), Snapshot(20)], loop: true);

        for (var i = 0; i < 5; i++)
        {
            Assert.IsNotNull(client.GetAllGameDataAsync().Result);
        }

        Assert.IsFalse(client.IsFinished);
    }

    private static string Snapshot(double gameTime)
    {
        var node = JsonNode.Parse(Fixture())!;
        node["gameData"]!["gameTime"] = gameTime;
        return node.ToJsonString();
    }
}
