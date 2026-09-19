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
    public void EveryDragonIsKnown()
    {
        CollectionAssert.AreEquivalent(
            new[] { "Water", "Earth", "Fire", "Air", "Hextech", "Chemtech" },
            _neutrals.Dragons.Keys.ToList());
    }

    [TestMethod]
    [DataRow("Fire", "attackDamage", 0.03, 0.0)]
    [DataRow("Fire", "abilityPower", 0.03, 0.0)]
    [DataRow("Earth", "armor", 0.05, 0.0)]
    [DataRow("Earth", "magicResist", 0.05, 0.0)]
    [DataRow("Hextech", "abilityHaste", 0.0, 5.0)]
    [DataRow("Hextech", "attackSpeedPercent", 0.0, 0.05)]
    [DataRow("Chemtech", "tenacityPercent", 0.0, 0.06)]
    [DataRow("Chemtech", "healAndShieldPowerPercent", 0.0, 0.06)]
    public void DrakeStacksMatchTheWiki(string type, string stat, double percent, double flat)
    {
        var modifier = _neutrals.DragonFor(type)!.PerStack.Single(m => m.Stat.Name == stat);

        Assert.AreEqual(percent, modifier.Percent, 0.0001);
        Assert.AreEqual(flat, modifier.Flat, 0.0001);
    }

    [TestMethod]
    public void StacksScaleLinearly()
    {
        var mountain = _neutrals.DragonFor("Earth")!;

        var threeStacks = mountain.BuffsFor(3).Single(m => m.Stat == Stats.Armor);

        Assert.AreEqual(0.15, threeStacks.Percent, 0.0001);
        Assert.IsEmpty(mountain.BuffsFor(0).ToList());
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
    public void ChemtechSoulIsNotHealing()
    {
        var chemtech = _neutrals.DragonFor("Chemtech")!;

        Assert.AreEqual(0, chemtech.Soul.Tag("healing"), 0.0001);
        Assert.AreEqual(0, chemtech.Soul.Tag("shielding"), 0.0001);
        Assert.IsGreaterThan(0, chemtech.StackTag("healing", 1));
    }

    [TestMethod]
    public void DragonTagsUseTheChampionVocabulary()
    {
        var vocabulary = new HashSet<string>
        {
            "tank", "adDamageDealer", "apDamageDealer", "burst", "trueDamage",
            "healing", "shielding", "crowdControl", "ranged", "melee",
        };

        foreach (var (type, dragon) in _neutrals.Dragons)
        {
            foreach (var tag in dragon.Soul.Tags.Keys.Concat(dragon.StackTags.Keys))
            {
                Assert.Contains(tag, vocabulary, $"{type} has unknown tag '{tag}'.");
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
