using WhatToBuild.Data;
using WhatToBuild.Dtos;
using WhatToBuild.Game;
using WhatToBuild.Recommendations;
using WhatToBuild.SupportedChampions;

namespace WhatToBuild.Tests;

[TestClass]
public class MatchupCalculatorTests
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

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static GameState MidGame() => GameStateParser.Parse(Fixture("midgame.json"), _champions, _items);

    [TestMethod]
    public void EveryEnemyGetsAMatchup()
    {
        var state = MidGame();
        var matchups = new MatchupCalculator(_neutrals, _kits).For(state);

        CollectionAssert.AreEquivalent(
            state.Enemies.Select(e => e.Champion.Name).ToList(),
            matchups.Select(m => m.Champion).ToList());
    }

    [TestMethod]
    public void KindredGetsAnETimingForEveryEnemy()
    {
        var state = MidGame();
        var healthByName = state.Enemies.ToDictionary(e => e.Champion.Name, e => state.EntityFor(e, _neutrals).MaxHealth);

        foreach (var matchup in new MatchupCalculator(_neutrals, _kits).For(state))
        {
            var e = matchup.Casts.Single();
            Assert.AreEqual("E", e.Ability);
            Assert.IsGreaterThan(e.KillingHealth, e.CastAtHealth);
            Assert.IsLessThan(healthByName[matchup.Champion], e.CastAtHealth);
        }
    }

    [TestMethod]
    public void NoGameNoMatchups()
    {
        var empty = new GameState
        {
            GameTime = 0,
            CurrentGold = 0,
            Players = [],
            Objectives = new Dictionary<Team, ObjectiveCounts> { [Team.Order] = new(), [Team.Chaos] = new() },
        };

        Assert.IsEmpty(new MatchupCalculator(_neutrals, _kits).For(empty));
    }

    [TestMethod]
    public void TheStateMessageCarriesMatchups()
    {
        var state = MidGame();
        var dto = GameStateMapper.ToDto(state, _neutrals, "16.18.1") with { Matchups = new MatchupCalculator(_neutrals, _kits).For(state) };

        Assert.IsNotEmpty(dto.Matchups);
    }

    [TestMethod]
    public void OurCurrentHealthComesFromTheGameWhenItIsThere()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(Fixture("midgame.json"))!;
        node["activePlayer"]!["championStats"]!["currentHealth"] = 448.5;

        var dto = GameStateMapper.ToDto(GameStateParser.Parse(node.ToJsonString(), _champions, _items), _neutrals, "16.18.1");
        var players = dto.Teams.SelectMany(t => t.Players).ToList();

        Assert.AreEqual(448.5, players.Single(p => p.IsActivePlayer).CurrentHealth!.Value, 0.001);
        Assert.IsTrue(players.Where(p => !p.IsActivePlayer).All(p => p.CurrentHealth is null));
    }

    [TestMethod]
    public void MissingCurrentHealthIsUnknownNotZero()
    {
        var dto = GameStateMapper.ToDto(MidGame(), _neutrals, "16.18.1");

        Assert.IsNull(dto.Teams.SelectMany(t => t.Players).Single(p => p.IsActivePlayer).CurrentHealth);
    }
}
