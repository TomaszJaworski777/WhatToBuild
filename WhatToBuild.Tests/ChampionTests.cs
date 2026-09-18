using WhatToBuild.Data;

namespace WhatToBuild.Tests;

[TestClass]
public class ChampionTests
{
    private const int KindredId = 203;
    private const int SorakaId = 16;
    private const int GarenId = 86;

    private static readonly HashSet<string> KnownTags = new()
    {
        "tank", "adDamageDealer", "apDamageDealer", "burst", "trueDamage",
        "healing", "shielding", "crowdControl", "ranged", "melee",
    };

    private static readonly HashSet<string> Untagged = new() { "Yunara", "Locke", "Zaahen" };

    private static ChampionRepository _champions = null!;

    [ClassInitialize]
    public static void Load(TestContext context)
    {
        _champions = ChampionRepository.Load(Path.Combine(AppContext.BaseDirectory, "GameData"));
    }

    [TestMethod]
    public void LoadsEveryChampion()
    {
        Assert.AreEqual(173, _champions.Count);
        Assert.AreEqual(_champions.Count, _champions.All.Select(c => c.Id).Distinct().Count());
        Assert.AreEqual(_champions.Count, _champions.All.Select(c => c.RiotId).Distinct().Count());
    }

    [TestMethod]
    public void ReadsBaseStatsAndGrowth()
    {
        var kindred = _champions.ByRiotId(KindredId)!;

        Assert.AreEqual("Kindred", kindred.Name);
        Assert.AreEqual(595, kindred.Base.Health, 0.001);
        Assert.AreEqual(104, kindred.PerLevel.Health, 0.001);
        Assert.AreEqual(29, kindred.Base.Armor, 0.001);
        Assert.AreEqual(4.7, kindred.PerLevel.Armor, 0.001);
        Assert.AreEqual(500, kindred.Base.AttackRange, 0.001);
        Assert.AreEqual(3.25, kindred.PerLevel.AttackDamage, 0.001);
    }

    [TestMethod]
    public void AttackDamageGrowthIsPresent()
    {
        var missing = _champions.All
            .Where(c => c.PerLevel.AttackDamage <= 0)
            .Select(c => c.Name)
            .ToList();

        CollectionAssert.AreEquivalent(new[] { "Senna" }, missing);
    }

    [TestMethod]
    public void AttackSpeedRatioIsPresent()
    {
        var missing = _champions.All
            .Where(c => c.AttackSpeedRatio <= 0)
            .Select(c => c.Name)
            .ToList();

        CollectionAssert.AreEquivalent(new[] { "Jhin" }, missing);
    }

    [TestMethod]
    public void AttackSpeedGrowthIsAFraction()
    {
        foreach (var champion in _champions.All)
        {
            Assert.IsLessThan(1, champion.PerLevel.AttackSpeed,
                $"{champion.Name} attack speed growth looks like a percent, not a fraction.");
        }

        Assert.AreEqual(0.035, _champions.ByRiotId(KindredId)!.PerLevel.AttackSpeed, 0.0001);
    }

    [TestMethod]
    public void LooksUpByWhatEachApiReports()
    {
        Assert.AreEqual("Kindred", _champions.ByRiotId(KindredId)!.Name);
        Assert.AreEqual("Kindred", _champions.ByInternalName("Kindred")!.Name);

        Assert.AreEqual("Wukong", _champions.ByInternalName("MonkeyKing")!.Name);
    }

    [TestMethod]
    public void EveryChampionIsRangedOrMelee()
    {
        foreach (var champion in _champions.All)
        {
            var ranged = champion.Tag("ranged");
            var melee = champion.Tag("melee");

            Assert.AreNotEqual(ranged > 0, melee > 0, $"{champion.Name} is neither or both.");
        }

        Assert.IsTrue(_champions.ByRiotId(KindredId)!.IsRanged);
        Assert.IsFalse(_champions.ByRiotId(GarenId)!.IsRanged);
    }

    [TestMethod]
    public void TagsUseTheDocumentedVocabulary()
    {
        foreach (var champion in _champions.All)
        {
            foreach (var (tag, weight) in champion.Tags)
            {
                Assert.Contains(tag, KnownTags, $"{champion.Name} has unknown tag '{tag}'.");
                Assert.IsGreaterThan(0, weight, $"{champion.Name} has a zero-weight '{tag}'; omit it instead.");
                Assert.IsLessThanOrEqualTo(1, weight, $"{champion.Name} has '{tag}' above 1.");
            }
        }
    }

    [TestMethod]
    public void HealingAndShieldingAreWeighted()
    {
        var soraka = _champions.ByRiotId(SorakaId)!;
        var garen = _champions.ByRiotId(GarenId)!;

        Assert.IsGreaterThan(garen.Tag("healing"), soraka.Tag("healing"));
        Assert.IsGreaterThan(0.8, soraka.Tag("healing"));

        Assert.IsGreaterThan(5, _champions.All.Count(c => c.Tag("healing") >= 0.7));
        Assert.IsGreaterThan(20, _champions.All.Count(c => c.Tag("shielding") >= 0.4));
    }

    [TestMethod]
    public void StackingDeclaresWhichStatGrows()
    {
        var veigar = _champions.All.Single(c => c.Name == "Veigar");
        Assert.AreSame(Stats.AbilityPower, veigar.Stacking.Single().Stat);

        var nasus = _champions.All.Single(c => c.Name == "Nasus");
        Assert.AreSame(Stats.AbilityDamage, nasus.Stacking.Single().Stat);

        Assert.HasCount(13, _champions.All.Where(c => c.Stacking.Count > 0).ToList());

        var garen = _champions.ByRiotId(GarenId)!;
        Assert.HasCount(2, garen.Stacking);
        Assert.AreEqual(30, garen.Stacking.First().Max, 0.001);

        Assert.AreSame(Stats.AbilityDamage, _champions.ByRiotId(KindredId)!.Stacking.Single().Stat);
    }

    [TestMethod]
    public void StackedStatsAreKnownStats()
    {
        foreach (var champion in _champions.All)
        {
            foreach (var stack in champion.Stacking)
            {
                Assert.Contains(stack.Stat, Stats.All, $"{champion.Name} stacks an unregistered stat.");
                Assert.IsGreaterThan(0, stack.InitialStacksPerMinute,
                    $"{champion.Name} has no initial stacking rate.");
            }
        }
    }

    [TestMethod]
    public void OnlyTheKnownGapsAreUntagged()
    {
        var missing = _champions.All
            .Where(c => c.Tag("adDamageDealer") == 0 && c.Tag("apDamageDealer") == 0 && c.Tag("tank") == 0)
            .Select(c => c.Name)
            .ToHashSet();

        CollectionAssert.AreEquivalent(Untagged.ToList(), missing.ToList());
    }
}
