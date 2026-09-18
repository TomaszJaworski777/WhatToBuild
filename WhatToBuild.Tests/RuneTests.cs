using WhatToBuild.Data;

namespace WhatToBuild.Tests;

[TestClass]
public class RuneTests
{
    private const int Electrocute = 8112;
    private const int LethalTempo = 8008;
    private const int Grasp = 8437;
    private const int AdaptiveForceShard = 5008;
    private const int HealthScalingShard = 5001;

    private static RuneRepository _runes = null!;

    [ClassInitialize]
    public static void Load(TestContext context)
    {
        _runes = RuneRepository.Load(Path.Combine(AppContext.BaseDirectory, "GameData"));
    }

    [TestMethod]
    public void LoadsEveryRuneAndShard()
    {
        Assert.AreEqual(72, _runes.Count);
        Assert.AreEqual(_runes.Count, _runes.All.Select(r => r.Id).Distinct().Count());
        Assert.AreEqual(_runes.Count, _runes.All.Select(r => r.RiotId).Distinct().Count());
    }

    [TestMethod]
    public void TreesHaveTheirKeystones()
    {
        foreach (var tree in new[] { RuneTree.Precision, RuneTree.Domination, RuneTree.Sorcery, RuneTree.Resolve, RuneTree.Inspiration })
        {
            Assert.IsNotEmpty(_runes.InTree(tree).ToList(), $"{tree} has no runes.");
            Assert.IsTrue(_runes.InTree(tree).Any(r => r.IsKeystone), $"{tree} has no keystone.");
        }

        Assert.HasCount(17, _runes.Keystones.ToList());
        Assert.HasCount(10, _runes.Shards.ToList());
    }

    [TestMethod]
    public void ShardsAreExactStatBuffs()
    {
        var adaptive = _runes.ByRiotId(AdaptiveForceShard)!.Effects.Single();

        Assert.AreEqual(EffectTrigger.Always, adaptive.Trigger);
        Assert.AreEqual(EffectKind.StatBuff, adaptive.Kind);
        Assert.AreSame(Stats.AdaptiveForce, adaptive.Stat);
        Assert.AreEqual(9, adaptive.Amount, 0.001);

        foreach (var shard in _runes.Shards)
        {
            Assert.IsNotEmpty(shard.Effects, $"{shard.Name} has no effect.");
            Assert.IsTrue(shard.Effects.All(e => e.Kind == EffectKind.StatBuff),
                $"{shard.Name} should be nothing but stats.");
        }
    }

    [TestMethod]
    public void LevelScalingUsesPerLevel()
    {
        var scaling = _runes.ByRiotId(HealthScalingShard)!.Effects.Single();

        Assert.AreEqual(10, scaling.Amount, 0.001);
        Assert.AreEqual(180, scaling.Amount + scaling.PerLevel * 17, 0.5);
    }

    [TestMethod]
    public void KeystonesCarryTheirNumbers()
    {
        var electrocute = _runes.ByRiotId(Electrocute)!.Effects.Single();

        Assert.AreEqual(EffectKind.AdaptiveDamage, electrocute.Kind);
        Assert.AreEqual(70, electrocute.Amount, 0.001);
        Assert.AreEqual(240, electrocute.Amount + electrocute.PerLevel * 17, 0.5);
        Assert.AreEqual(20, electrocute.Cooldown, 0.001);

        var tempo = _runes.ByRiotId(LethalTempo)!.Effects.Single();
        Assert.AreEqual(EffectKind.StatBuff, tempo.Kind);
        Assert.AreSame(Stats.AttackSpeedPercent, tempo.Stat);

        var grasp = _runes.ByRiotId(Grasp)!.Effects;
        Assert.HasCount(2, grasp);
        Assert.IsTrue(grasp.Any(e => e.Kind == EffectKind.MagicDamage && e.PerMaxHealth > 0));
        Assert.IsTrue(grasp.Any(e => e.Kind == EffectKind.Heal && e.PerMaxHealth > 0));
    }

    [TestMethod]
    public void EveryKeystoneIsModelledOrDeliberatelyEmpty()
    {
        var unmodelled = _runes.Keystones
            .Where(r => r.Effects.Count == 0)
            .Select(r => r.Name)
            .ToHashSet();

        CollectionAssert.AreEquivalent(
            new[] { "Glacial Augment", "Unsealed Spellbook", "Stormraider's Surge" },
            unmodelled.ToList());
    }

    [TestMethod]
    public void EveryEffectCarriesAMagnitude()
    {
        foreach (var rune in _runes.All)
        {
            foreach (var e in rune.Effects)
            {
                var hasMagnitude = e.Amount != 0 || e.PerLevel != 0
                                   || e.PerBaseAd != 0 || e.PerTotalAd != 0 || e.PerAp != 0
                                   || e.PerMaxHealth != 0;

                Assert.IsTrue(hasMagnitude, $"{rune.Name} has a {e.Kind} effect with no magnitude.");
            }
        }
    }

    [TestMethod]
    public void EnemyShieldingFromRunesIsDiscoverable()
    {
        Assert.IsTrue(_runes.All.Any(r => r.Effects.Any(e => e.Kind == EffectKind.Shield)));
        Assert.IsTrue(_runes.All.Any(r => r.Effects.Any(e => e.Kind == EffectKind.Heal)));
    }
}
