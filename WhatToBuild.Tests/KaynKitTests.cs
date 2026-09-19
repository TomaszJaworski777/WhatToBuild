using WhatToBuild.Data;
using WhatToBuild.Game;
using WhatToBuild.Modeling;
using WhatToBuild.Modeling.Simulation;
using WhatToBuild.Planning;
using WhatToBuild.Recommendations;
using WhatToBuild.SupportedChampions;
using WhatToBuild.SupportedChampions.Kayn;

namespace WhatToBuild.Tests;

[TestClass]
public class KaynKitTests
{
    private static ChampionRepository _champions = null!;
    private static ItemRepository _items = null!;
    private static NeutralRepository _neutrals = null!;
    private static ChampionKits _kits = null!;
    private static ModelData _model = null!;
    private static KaynKitData _data = null!;

    [ClassInitialize]
    public static void Load(TestContext context)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "GameData");
        _champions = ChampionRepository.Load(root);
        _items = ItemRepository.Load(root);
        _neutrals = NeutralRepository.Load(root);
        _kits = ChampionKits.Load(root);
        _model = ModelData.Load(root);
        _data = KaynKitData.Load(Path.Combine(root, ChampionKits.FolderName, KaynKitData.FileName));
    }

    private static Champion Kayn => _champions.ByName("Kayn")!;

    private static ChampionState KaynWith(int level, params int[] items) => new(Kayn, level, items.Select(i => _items.ByRiotId(i)!));

    private static ChampionState Target() => new(_champions.ByName("Caitlyn")!, 13, [_items.ByRiotId(3031)!]);

    private static FightResult Fight(string form, ChampionState us, AbilityRanks ranks, double seconds = 10) =>
        FightSimulator.Run(new FightSetup(us, Target(), ranks, 0, seconds, 0.5, Sustained: true), _kits.NewFight(Kayn, form));

    [TestMethod]
    public void ReapingSlashHitsTwiceAtItsBaseDamage()
    {
        var us = KaynWith(9, 3071);
        var kit = new KaynKit(_data, KaynForm.Base);
        var fight = new Fight(new FightSetup(us, Target(), new AbilityRanks(3, 0, 0, 0)));

        Assert.AreEqual(135 + 0.85 * AttackerHits.BonusAttackDamage(us), kit.QHit(fight, 3), 1e-6);
        Assert.AreEqual(2, _data.Q.Hits);
    }

    [TestMethod]
    public void RhaastReapingSlashScalesWithMaxHealth()
    {
        var us = KaynWith(13, 3071, 3053);
        var target = Target();
        var fight = new Fight(new FightSetup(us, target, new AbilityRanks(5, 0, 0, 0)));
        var bonusAd = AttackerHits.BonusAttackDamage(us);

        var expected = 0.65 * us.Stats.AttackDamage + (0.06 + 0.035 * bonusAd / 100) * target.MaxHealth;
        Assert.AreEqual(expected, new KaynKit(_data, KaynForm.Darkin).QHit(fight, 5), 1e-6);
    }

    [TestMethod]
    public void UmbralTrespassDiffersByForm()
    {
        var us = KaynWith(13, 3071);
        var target = Target();
        var fight = new Fight(new FightSetup(us, target, new AbilityRanks(0, 0, 0, 2)));
        var bonusAd = AttackerHits.BonusAttackDamage(us);

        Assert.AreEqual(250 + 1.5 * bonusAd, new KaynKit(_data, KaynForm.ShadowAssassin).RDamage(fight), 1e-6);
        Assert.AreEqual((0.15 + 0.001 * bonusAd) * target.MaxHealth, new KaynKit(_data, KaynForm.Darkin).RDamage(fight), 1e-6);
    }

    [TestMethod]
    public void ShadowAssassinBurstsAtTheStartOfAFight()
    {
        var us = KaynWith(13, 3071, 3047);
        var ranks = new AbilityRanks(5, 3, 1, 2);
        var assassin = Fight(KaynForm.ShadowAssassin, us, ranks);
        var plain = Fight(KaynForm.Base, us, ranks);

        Assert.IsTrue(assassin.DamageBySource.ContainsKey(KaynKit.ShadowPassive));
        Assert.IsFalse(plain.DamageBySource.ContainsKey(KaynKit.ShadowPassive));
        Assert.IsGreaterThan(plain.EarlyDamage, assassin.EarlyDamage);
        Assert.AreEqual(0.2, new KaynKit(_data, KaynForm.ShadowAssassin).PassiveShare(1), 1e-9);
        Assert.AreEqual(0.4, new KaynKit(_data, KaynForm.ShadowAssassin).PassiveShare(18), 1e-9);
    }

    [TestMethod]
    public void RhaastHealsFromDamageAndUmbralTrespass()
    {
        var supported = _kits.For(Kayn)!;
        var us = KaynWith(13, 3071, 3053);

        Assert.IsGreaterThanOrEqualTo(0.25, supported.DamageHealShare(us, KaynForm.Darkin));
        Assert.AreEqual(0, supported.DamageHealShare(us, KaynForm.ShadowAssassin), 1e-9);
        Assert.IsGreaterThan(0, supported.Survival(new AbilityRanks(0, 0, 0, 1), us, KaynForm.Darkin, 2000)!.Heal);
        Assert.AreEqual(0, supported.Survival(new AbilityRanks(0, 0, 0, 1), us, KaynForm.ShadowAssassin, 2000)!.Heal, 1e-9);
    }

    [TestMethod]
    public void ShadowAssassinIsDetectedFromItsW()
    {
        var supported = _kits.For(Kayn)!;

        Assert.AreEqual(KaynForm.ShadowAssassin, supported.DetectForm(new Dictionary<string, string> { ["W"] = "KaynAssW" }));
        Assert.IsNull(supported.DetectForm(new Dictionary<string, string> { ["W"] = "KaynW" }));
    }

    [TestMethod]
    public void RhaastIsTheDefaultUnlessShadowAssassinIsClearlyBetter()
    {
        var replay = GameStateParser.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Replays", "demo", "000.json")), _champions, _items);
        var state = new GameState
        {
            GameTime = 22 * 60, CurrentGold = 400, Objectives = replay.Objectives,
            Players = replay.Players.Select(p => p.IsActivePlayer
                ? new PlayerState
                {
                    Champion = Kayn, Team = p.Team, Position = "JUNGLE", Level = 13, IsActivePlayer = true,
                    Items = new[] { 1101, 3071, 3047 }.Select((id, slot) => new OwnedItem(_items.ByRiotId(id)!, 1, slot)).ToList(),
                }
                : p).ToList(),
        };

        var form = new BuildRecommendations(_items, _neutrals, _kits, _model).Compute(state, new GameStack())!.Form!;
        var rhaast = form.Options.Single(o => o.Form == KaynForm.Darkin);
        var assassin = form.Options.Single(o => o.Form == KaynForm.ShadowAssassin);

        Assert.AreEqual(assassin.FightValue > rhaast.FightValue * (1 + _model.Settings.Forms.PreferDefaultMargin) ? KaynForm.ShadowAssassin : KaynForm.Darkin, form.Recommended);
        Assert.IsGreaterThan(0, rhaast.HealingPerSecond);
    }
}
