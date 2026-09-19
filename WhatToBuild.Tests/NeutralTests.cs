using WhatToBuild.Data;

namespace WhatToBuild.Tests;

[TestClass]
public class NeutralTests
{
    private static readonly HashSet<string> KnownIncomplete = new()
    {
        "SRU_BlueMini", "SRU_BlueMini2", "SRU_RedMini", "SRU_MurkwolfMini",
    };

    private static NeutralRepository _neutrals = null!;

    [ClassInitialize]
    public static void Load(TestContext context)
    {
        _neutrals = NeutralRepository.Load(Path.Combine(AppContext.BaseDirectory, "GameData"));
    }

    [TestMethod]
    public void LoadsEveryUnit()
    {
        Assert.AreEqual(28, _neutrals.Count);
        Assert.IsNotEmpty(_neutrals.InCamp("Wolves").ToList());
        Assert.IsNotEmpty(_neutrals.InCamp("Dragon").ToList());
    }

    [TestMethod]
    public void ReadsCampStats()
    {
        var murkwolf = _neutrals.ByInternalName("SRU_Murkwolf")!;

        Assert.AreEqual(NeutralKind.JungleCamp, murkwolf.Kind);
        Assert.AreEqual("Wolves", murkwolf.Camp);
        Assert.AreEqual(1600, murkwolf.Base.Health, 0.001);
        Assert.AreEqual(42, murkwolf.Base.Armor, 0.001);
        Assert.AreEqual(55, murkwolf.GoldOnDeath, 0.001);
        Assert.AreEqual(50, murkwolf.ExpOnDeath, 0.001);
        Assert.AreEqual(1.6, murkwolf.AttackInterval, 0.01);
    }

    [TestMethod]
    public void CampsKnowHowManyUnitsSpawn()
    {
        var wolves = _neutrals.InCamp("Wolves").ToList();

        Assert.HasCount(2, wolves);
        Assert.AreEqual(3, wolves.Sum(w => w.CountPerCamp));

        var raptors = _neutrals.InCamp("Raptors").ToList();
        Assert.AreEqual(6, raptors.Sum(r => r.CountPerCamp));
    }

    [TestMethod]
    public void MinionsAndEpicsAreSeparated()
    {
        Assert.AreEqual(NeutralKind.Minion, _neutrals.ByInternalName("SRU_ChaosMinionMelee")!.Kind);
        Assert.AreEqual(NeutralKind.Epic, _neutrals.ByInternalName("SRU_Baron")!.Kind);

        var baron = _neutrals.ByInternalName("SRU_Baron")!;
        Assert.IsGreaterThan(10000, baron.Base.Health);
    }

    [TestMethod]
    public void OnlyTheKnownUnitsHaveIncompleteStats()
    {
        var incomplete = _neutrals.All
            .Where(n => n.StatsIncomplete)
            .Select(n => n.InternalName)
            .ToHashSet();

        CollectionAssert.AreEquivalent(KnownIncomplete.ToList(), incomplete.ToList());
    }

    [TestMethod]
    public void ArmorAndMagicResistAreSeparate()
    {
        var baron = _neutrals.ByInternalName("SRU_Baron")!;
        Assert.AreEqual(34, baron.Base.Armor, 0.001);
        Assert.AreEqual(32, baron.Base.MagicResist, 0.001);

        foreach (var n in _neutrals.All.Where(n => n.Kind != NeutralKind.Minion && !n.StatsIncomplete))
        {
            Assert.IsGreaterThan(0, n.Base.MagicResist, $"{n.Name} has no magic resist.");
        }
    }

    [TestMethod]
    public void EveryUnitHasAnAttackInterval()
    {
        foreach (var n in _neutrals.All)
        {
            Assert.IsGreaterThan(0, n.AttackInterval, $"{n.Name} has no attack interval.");
        }
    }

    [TestMethod]
    public void CompleteUnitsHaveUsableStats()
    {
        foreach (var n in _neutrals.All.Where(n => !n.StatsIncomplete))
        {
            Assert.IsGreaterThan(1, n.Base.Health, $"{n.Name} has no health.");
            Assert.IsGreaterThan(0, n.Base.AttackDamage, $"{n.Name} has no attack damage.");
        }
    }

    [TestMethod]
    public void EverySoulIsKnown()
    {
        CollectionAssert.AreEquivalent(
            new[] { "Water", "Earth", "Fire", "Air", "Hextech", "Chemtech" },
            _neutrals.Souls.Keys.ToList());
    }

    [TestMethod]
    public void SoulsSayWhatKindOfPowerTheyGive()
    {
        Assert.IsGreaterThan(0, _neutrals.SoulFor("Water")!.Tag("healing"));
        Assert.IsGreaterThan(0, _neutrals.SoulFor("Earth")!.Tag("shielding"));
        Assert.IsGreaterThan(0, _neutrals.SoulFor("Fire")!.Tag("burst"));
        Assert.IsEmpty(_neutrals.SoulFor("Air")!.Tags);
        Assert.IsNull(_neutrals.SoulFor(null));
    }

    [TestMethod]
    public void SoulTagsUseTheChampionVocabulary()
    {
        var vocabulary = new HashSet<string>
        {
            "tank", "adDamageDealer", "apDamageDealer", "burst", "trueDamage",
            "healing", "shielding", "crowdControl", "ranged", "melee",
        };

        foreach (var (type, soul) in _neutrals.Souls)
        {
            foreach (var tag in soul.Tags.Keys)
            {
                Assert.Contains(tag, vocabulary, $"{type} soul has unknown tag '{tag}'.");
            }
        }
    }

    [TestMethod]
    public void ScalingStartsFlatThenGrowsToACap()
    {
        var scaling = _neutrals.Scaling;

        Assert.AreEqual(600, scaling.StartTimeSeconds, 0.001);
        Assert.AreEqual(1, scaling.MultiplierAt(0), 0.0001);
        Assert.AreEqual(1, scaling.MultiplierAt(600), 0.0001);

        Assert.IsGreaterThan(1, scaling.MultiplierAt(900));
        Assert.IsGreaterThan(scaling.MultiplierAt(900), scaling.MultiplierAt(1800));
        Assert.IsLessThanOrEqualTo(1 + scaling.PercentCap, scaling.MultiplierAt(6000));
    }
}
