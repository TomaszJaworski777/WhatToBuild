using WhatToBuild.Data;

namespace WhatToBuild.Tests;

[TestClass]
public class ItemTests
{
    private const int BfSword = 1038;
    private const int InfinityEdge = 3031;
    private const int KrakenSlayer = 6672;
    private const int TheCollector = 6676;
    private const int SerpentsFang = 6695;
    private const int MortalReminder = 3033;
    private const int GuardianAngel = 3026;
    private const int DeathsDance = 6333;
    private const int Shieldbow = 6673;
    private const int BlackCleaver = 3071;
    private const int AbyssalMask = 8020;
    private const int RanduinsOmen = 3143;
    private const int PlatedSteelcaps = 3047;
    private const int MawOfMalmortius = 3156;
    private const int LordDominiks = 3036;

    private static ItemRepository _items = null!;

    [ClassInitialize]
    public static void Load(TestContext context)
    {
        _items = ItemRepository.Load(Path.Combine(AppContext.BaseDirectory, "GameData"));
    }

    [TestMethod]
    public void LoadsTheWholeRiftPool()
    {
        Assert.AreEqual(219, _items.Count);
        Assert.AreEqual("16.18.1", _items.Patch);
    }

    [TestMethod]
    public void ExcludesAlternateModeVariants()
    {
        Assert.IsFalse(_items.All.Any(i => i.RiotId >= 100000));
    }

    [TestMethod]
    public void ReadsStatsDataDragonOmits()
    {
        Assert.AreEqual(0.30, _items.ByRiotId(InfinityEdge)!.Stats.CritDamage, 0.001);

        Assert.AreEqual(10, _items.ByRiotId(TheCollector)!.Stats.ArmorPenetrationFlat, 0.001);
    }

    [TestMethod]
    public void ShipsComponentsAsItemsInTheirOwnRight()
    {
        var bfSword = _items.ByRiotId(BfSword)!;

        Assert.AreEqual(1300, bfSword.Cost);
        Assert.AreEqual(40, bfSword.Stats.AttackDamage, 0.001);
        Assert.AreEqual(0, bfSword.Stats.AttackSpeedPercent, 0.001);
        Assert.IsEmpty(bfSword.BuildPath);
        Assert.IsTrue(_items.All.Any(i => i.BuildPath.Contains(bfSword.Id)));
    }

    [TestMethod]
    public void EveryBuildPathResolves()
    {
        foreach (var item in _items.All)
        {
            foreach (var componentId in item.BuildPath)
            {
                Assert.IsNotNull(_items.ById(componentId), $"{item.Name} has an unknown component.");
            }
        }
    }

    [TestMethod]
    public void IdsAreUnique()
    {
        Assert.AreEqual(_items.Count, _items.All.Select(i => i.Id).Distinct().Count());
        Assert.AreEqual(_items.Count, _items.All.Select(i => i.RiotId).Distinct().Count());
        Assert.IsFalse(_items.All.Any(i => i.Id == Guid.Empty));
    }

    [TestMethod]
    public void PureStatItemsHaveNoEffects()
    {
        Assert.IsEmpty(_items.ByRiotId(InfinityEdge)!.Effects);
        Assert.IsEmpty(_items.ByRiotId(BfSword)!.Effects);
    }

    [TestMethod]
    public void EffectsAreMagnitudeAndCooldown()
    {
        var kraken = _items.ByRiotId(KrakenSlayer)!.Effects.Single();

        Assert.AreEqual(EffectTrigger.OnAttack, kraken.Trigger);
        Assert.AreEqual(EffectKind.PhysicalDamage, kraken.Kind);
        Assert.AreEqual(175, kraken.Amount, 0.001);
        Assert.AreEqual(3, kraken.EveryAttacks, 0.001);
        Assert.AreEqual(0.8, kraken.RangedMultiplier, 0.001);
    }

    [TestMethod]
    public void KeepsTheDecisionChangingEffects()
    {
        Assert.AreEqual(EffectKind.Revive, _items.ByRiotId(GuardianAngel)!.Effects.Single().Kind);
        Assert.AreEqual(EffectKind.Execute, _items.ByRiotId(TheCollector)!.Effects.Single().Kind);
        Assert.AreEqual(EffectKind.ShieldReduction, _items.ByRiotId(SerpentsFang)!.Effects.Single().Kind);
        Assert.AreEqual(EffectKind.GrievousWounds, _items.ByRiotId(MortalReminder)!.Effects.Single().Kind);
        Assert.AreEqual(EffectKind.ArmorShred, _items.ByRiotId(BlackCleaver)!.Effects.Single().Kind);

        var dd = _items.ByRiotId(DeathsDance)!.Effects;
        Assert.HasCount(2, dd);
        Assert.IsTrue(dd.Any(e => e.Kind == EffectKind.DamageReduction));
        Assert.IsTrue(dd.Any(e => e.Kind == EffectKind.Heal && e.Trigger == EffectTrigger.OnTakedown));

        var shieldbow = _items.ByRiotId(Shieldbow)!.Effects.Single();
        Assert.AreEqual(EffectTrigger.WhenLow, shieldbow.Trigger);
        Assert.AreEqual(90, shieldbow.Cooldown, 0.001);
    }

    [TestMethod]
    public void EveryEffectCarriesAMagnitude()
    {
        foreach (var item in _items.All)
        {
            foreach (var e in item.Effects)
            {
                var hasMagnitude = e.Amount != 0
                                   || e.PerBaseAd != 0 || e.PerTotalAd != 0 || e.PerAp != 0
                                   || e.PerMaxHealth != 0
                                   || e.PerTargetMaxHealth != 0 || e.PerTargetCurrentHealth != 0;

                Assert.IsTrue(hasMagnitude, $"{item.Name} has a {e.Kind} effect with no magnitude.");
            }
        }
    }

    [TestMethod]
    public void StatBuffsNameARealStat()
    {
        foreach (var item in _items.All)
        {
            foreach (var e in item.Effects.Where(e => e.Kind == EffectKind.StatBuff))
            {
                Assert.IsNotNull(e.Stat, $"{item.Name} has a StatBuff with no stat named.");
                Assert.Contains(e.Stat, Stats.All, $"{item.Name} buffs an unregistered stat.");
            }
        }
    }

    [TestMethod]
    public void EnemyFacingStatsAreMarked()
    {
        var abyssal = _items.ByRiotId(AbyssalMask)!.Effects.Single();

        Assert.IsTrue(abyssal.Stat!.TargetsEnemy);
        Assert.IsTrue(abyssal.Stat.IsFraction);
        Assert.IsFalse(Stats.AttackDamage.TargetsEnemy);
    }

    [TestMethod]
    public void ConditionalMitigationSaysWhatItAppliesTo()
    {
        Assert.AreEqual(DamageSource.Crit, _items.ByRiotId(RanduinsOmen)!.Effects.Single().Versus);
        Assert.AreEqual(DamageSource.Attacks, _items.ByRiotId(PlatedSteelcaps)!.Effects.Single().Versus);

        Assert.AreEqual(DamageSource.Magic, _items.ByRiotId(MawOfMalmortius)!.Effects.Single().Versus);
        Assert.AreEqual(DamageSource.All, _items.ByRiotId(Shieldbow)!.Effects.Single().Versus);
    }

    [TestMethod]
    public void ConditionalEffectsCarryTheirCondition()
    {
        var ldr = _items.ByRiotId(LordDominiks)!.Effects.MaxBy(e => e.Amount)!;

        var condition = ldr.When.Single();
        Assert.AreEqual(ConditionSubject.Target, condition.Subject);
        Assert.AreEqual(ConditionProperty.BonusHealth, condition.Property);
        Assert.AreEqual(ConditionOp.AtLeast, condition.Op);

        Assert.IsTrue(condition.IsMet(2000), "Should count against a stacked-health target.");
        Assert.IsFalse(condition.IsMet(300), "Should not count against a squishy.");
    }

    [TestMethod]
    public void ConditionsAreSaneThresholds()
    {
        foreach (var item in _items.All)
        {
            foreach (var condition in item.Effects.SelectMany(e => e.When))
            {
                Assert.IsGreaterThan(0, condition.Value, $"{item.Name} has a zero-threshold condition.");

                if (condition.Property == ConditionProperty.HealthPercent)
                {
                    Assert.IsLessThanOrEqualTo(1, condition.Value,
                        $"{item.Name} uses a health percent above 1; these are fractions.");
                }
            }
        }
    }

    [TestMethod]
    public void EnemyHealingAndShieldingIsDiscoverableFromEffects()
    {
        var healers = _items.All.Where(i => i.Effects.Any(e => e.Kind == EffectKind.Heal)).ToList();
        var shielders = _items.All.Where(i => i.Effects.Any(e => e.Kind == EffectKind.Shield)).ToList();

        Assert.IsGreaterThan(5, healers.Count);
        Assert.IsGreaterThan(5, shielders.Count);
        Assert.IsTrue(shielders.Any(i => i.RiotId == Shieldbow));
    }
}
