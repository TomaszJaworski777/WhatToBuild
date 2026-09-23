using WhatToBuild.Data;
using WhatToBuild.Game;
using WhatToBuild.Modeling;
using WhatToBuild.Modeling.Simulation;
using WhatToBuild.SupportedChampions;
using WhatToBuild.SupportedChampions.Kindred;

namespace WhatToBuild.Tests;

[TestClass]
public class KindredKitTests
{
    private static ChampionRepository _champions = null!;
    private static ItemRepository _items = null!;
    private static ChampionKits _kits = null!;
    private static NeutralRepository _neutrals = null!;

    private static Champion Kindred => _champions.ByName("Kindred")!;
    private static Champion Garen => _champions.ByName("Garen")!;

    [ClassInitialize]
    public static void Load(TestContext context)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "GameData");
        _champions = ChampionRepository.Load(root);
        _items = ItemRepository.Load(root);
        _kits = ChampionKits.Load(root);
        _neutrals = NeutralRepository.Load(root);
    }

    private static FightResult Fight(AbilityRanks ranks, double marks = 0, double maxSeconds = 30) =>
        FightSimulator.Run(
            new FightSetup(new ChampionState(Kindred, 11), new ChampionState(Garen, 11), ranks, marks, maxSeconds),
            _kits.NewFight(Kindred));

    [TestMethod]
    public void KindredAndKaynAreSupported()
    {
        Assert.IsNotNull(_kits.For(Kindred));
        Assert.IsNull(_kits.For(Garen));
        CollectionAssert.AreEquivalent(new[] { "Kindred", "Kayn", "Vi" }, _kits.Supported.ToList());
    }

    [TestMethod]
    public void KitNumbersAreIndexedByRank()
    {
        var data = KindredKitData.Load(Path.Combine(AppContext.BaseDirectory, "GameData", ChampionKits.FolderName, KindredKitData.FileName));

        Assert.AreEqual(40, KindredKitData.AtRank(data.Q.Damage, 1), 0.001);
        Assert.AreEqual(14, KindredKitData.AtRank(data.E.Cooldown, 1), 0.001);
        Assert.AreEqual(8, KindredKitData.AtRank(data.E.Cooldown, 5), 0.001);
        Assert.AreEqual(18, KindredKitData.AtRank(data.W.Cooldown, 1), 0.001);
    }

    [TestMethod]
    public void WithoutRanksOnlyAttacksDealDamage()
    {
        var result = Fight(AbilityRanks.None);

        CollectionAssert.AreEquivalent(new[] { FightSimulator.Attacks }, result.DamageBySource.Keys.ToList());
    }

    [TestMethod]
    public void EveryAbilityContributes()
    {
        var result = Fight(new AbilityRanks(3, 2, 1, 1));

        Assert.IsGreaterThan(0, result.DamageBySource[KindredKit.DanceOfArrows]);
        Assert.IsGreaterThan(0, result.DamageBySource[KindredKit.WolfsFrenzy]);
        Assert.IsGreaterThan(0, result.DamageBySource[KindredKit.MountingDread]);
    }

    [TestMethod]
    public void AbilitiesAddDamage()
    {
        Assert.IsGreaterThan(Fight(AbilityRanks.None).EffectiveDps, Fight(new AbilityRanks(3, 2, 1, 1)).EffectiveDps);
    }

    [TestMethod]
    public void QAttackSpeedMeansMoreAttacks()
    {
        var withoutQ = Fight(AbilityRanks.None, maxSeconds: 4);
        var withQ = Fight(new AbilityRanks(1, 0, 0, 0), maxSeconds: 4);

        Assert.IsGreaterThan(withoutQ.Attacks, withQ.Attacks);
    }

    private static Item Item(int riotId) => _items.ByRiotId(riotId)!;

    private static ChampionState Geared() => new(Kindred, 13, [Item(6672), Item(3006), Item(3031)]);

    private static KindredKitData Data() =>
        KindredKitData.Load(Path.Combine(AppContext.BaseDirectory, "GameData", ChampionKits.FolderName, KindredKitData.FileName));

    private static FightResult EFight(ChampionState kindred, Entity target, AbilityRanks ranks, bool holdE = true, double marks = 0, double maxSeconds = 30) =>
        FightResult.Average(FightScorer.AttackPhases
            .Select(phase => FightSimulator.Run(new FightSetup(kindred, target, ranks, marks, maxSeconds, phase), new KindredKit(Data(), holdE)))
            .ToList());

    private static double EDamage(ChampionState kindred, Entity target, bool holdE = true, double maxSeconds = 30) =>
        EFight(kindred, target, new AbilityRanks(3, 2, 5, 1), holdE, maxSeconds: maxSeconds)
            .DamageBySource.GetValueOrDefault(KindredKit.MountingDread);

    private static NeutralState RedBuff(double gameTime) =>
        new(_neutrals.ByInternalName("SRU_Red")!, _neutrals.Scaling, gameTime);

    private static double CastHealth(ChampionState kindred, Entity target, int eRank, double marks = 0)
    {
        var fight = new Fight(new FightSetup(kindred, target, new AbilityRanks(1, 1, eRank, 0), marks));
        return new KindredKit(Data()).OptimalCastHealth(fight, eRank);
    }

    [TestMethod]
    public void EWaitsUntilTheTargetIsLow()
    {
        Assert.AreEqual(0, EDamage(Geared(), new ChampionState(Garen, 13), maxSeconds: 1), 0.001);
    }

    [TestMethod]
    public void EStillLandsBeforeTheKill()
    {
        Assert.IsGreaterThan(0, EDamage(Geared(), new ChampionState(Garen, 13)));
        Assert.IsGreaterThan(0, EDamage(Geared(), new ChampionState(_champions.ByName("Soraka")!, 11)));
    }

    [TestMethod]
    public void FirstClearRedIsFinishedByThePounce()
    {
        var red = RedBuff(90);
        var result = FightSimulator.Run(new FightSetup(new ChampionState(Kindred, 3), red, new AbilityRanks(1, 1, 1, 0), 0, 90), new KindredKit(Data()));
        var castAt = CastHealth(new ChampionState(Kindred, 3), red, 1);

        Assert.AreEqual(KindredKit.MountingDread, result.KillingBlow);
    }

    [TestMethod]
    public void StrongerEIsCastEarlier()
    {
        var garen = new ChampionState(Garen, 13);

        var early = CastHealth(new ChampionState(Kindred, 13), garen, 1);
        var late = CastHealth(Geared(), garen, 5, marks: 10);

        Assert.IsGreaterThan(early, late);
    }

    [TestMethod]
    public void ArmorPushesTheCastPointDown()
    {
        var garen = new ChampionState(Garen, 13);
        var armored = new ChampionState(Garen, 13, [Item(3075), Item(3143)]);

        Assert.IsLessThan(CastHealth(Geared(), garen, 5), CastHealth(Geared(), armored, 5));
    }

    [TestMethod]
    [DataRow("Garen", 13, true)]
    [DataRow("Soraka", 11, true)]
    [DataRow("Garen", 11, false)]
    public void HoldingEIsNeverWorseThanOpeningWithIt(string enemy, int level, bool geared)
    {
        var kindred = geared ? Geared() : new ChampionState(Kindred, 11);
        var target = new ChampionState(_champions.ByName(enemy)!, level);

        var held = EFight(kindred, target, new AbilityRanks(3, 2, 5, 1));
        var opened = EFight(kindred, target, new AbilityRanks(3, 2, 5, 1), holdE: false);

        Assert.IsLessThanOrEqualTo(opened.Attacks, held.Attacks);
        Assert.IsGreaterThanOrEqualTo(opened.DamageBySource.GetValueOrDefault(KindredKit.MountingDread), held.DamageBySource.GetValueOrDefault(KindredKit.MountingDread));
    }

    [TestMethod]
    public void OneKrakenProcLandsBeforeThePounce()
    {
        var garen = new ChampionState(Garen, 13);
        var ranks = new AbilityRanks(3, 2, 5, 1);
        var kraken = _items.ByRiotId(6672)!;

        var without = _kits.For(Kindred)!.Hints(new FightSetup(new ChampionState(Kindred, 13), garen, ranks)).Single();
        var with = _kits.For(Kindred)!.Hints(new FightSetup(new ChampionState(Kindred, 13, [kraken]), garen, ranks)).Single();

        var krakenOwner = new ChampionState(Kindred, 13, [kraken]);
        var proc = AttackerHits.ForEffect(kraken.Effects.Single(), krakenOwner, garen)!.Attack().Run().HealthDamage;
        var attacks = 3 * (AttackerHits.Physical(krakenOwner, garen, krakenOwner.Stats.AttackDamage).Attack().Run().HealthDamage
                           - AttackerHits.Physical(new ChampionState(Kindred, 13), garen, new ChampionState(Kindred, 13).Stats.AttackDamage).Attack().Run().HealthDamage);
        var killingShift = with.KillingHealth - without.KillingHealth;

        Assert.AreEqual(proc + attacks + killingShift, with.CastAtHealth - without.CastAtHealth, 0.5);
    }

    [TestMethod]
    public void MarksMakeTheWolfHitHarder()
    {
        var ranks = new AbilityRanks(0, 1, 0, 0);

        Assert.IsGreaterThan(
            Fight(ranks, 0, maxSeconds: 3).DamageBySource[KindredKit.WolfsFrenzy],
            Fight(ranks, 10, maxSeconds: 3).DamageBySource[KindredKit.WolfsFrenzy]);
    }

    [TestMethod]
    [DataRow(1, 1, 0, 0, 0)]
    [DataRow(6, 3, 1, 1, 1)]
    [DataRow(18, 5, 5, 5, 3)]
    public void RanksFollowTheSkillOrderWhenTheGameDoesNotSay(int level, int q, int w, int e, int r)
    {
        Assert.AreEqual(new AbilityRanks(q, w, e, r), _kits.RanksFor(Kindred, level, null));
    }

    [TestMethod]
    public void RanksFromTheGameWin()
    {
        var observed = new AbilityRanks(1, 5, 3, 2);

        Assert.AreEqual(observed, _kits.RanksFor(Kindred, 11, observed));
    }

    [TestMethod]
    public void OurRanksAreReadFromTheGame()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "riot-sample.json"));
        var state = GameStateParser.Parse(json, _champions, _items);

        Assert.AreEqual(AbilityRanks.None, state.ActivePlayerRanks);
    }
}
