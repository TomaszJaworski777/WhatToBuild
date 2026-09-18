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
    private const int RanduinsOmen = 3143;
    private const int PlatedSteelcaps = 3047;
    private const int MawOfMalmortius = 3156;
    private const int LordDominiks = 3036;

    /// <summary>Stat names that are not <see cref="ItemStats"/> fields, per the README.</summary>
    private static readonly HashSet<string> SyntheticStats = new()
    {
        "damageAmp", "abilityPowerAmp", "enemyAttackSpeedPercent", "enemyMagicDamageAmp",
    };

    private static ItemRepository _items = null!;

    [ClassInitialize]
    public static void Load(TestContext context)
    {
        // Throws with the offending file name if any hand-written file is malformed,
        // including an unknown trigger or kind.
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

        // Lethality lives in a differently named field and was silently dropped once.
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
        // Infinity Edge is entirely stats. An effect here would mean we invented one.
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
        Assert.AreEqual(2, kraken.Cooldown, 0.001);
    }

    [TestMethod]
    public void KeepsTheDecisionChangingEffects()
    {
        Assert.AreEqual(EffectKind.Revive, _items.ByRiotId(GuardianAngel)!.Effects.Single().Kind);
        Assert.AreEqual(EffectKind.Execute, _items.ByRiotId(TheCollector)!.Effects.Single().Kind);
        Assert.AreEqual(EffectKind.ShieldReduction, _items.ByRiotId(SerpentsFang)!.Effects.Single().Kind);
        Assert.AreEqual(EffectKind.GrievousWounds, _items.ByRiotId(MortalReminder)!.Effects.Single().Kind);
        Assert.AreEqual(EffectKind.ArmorShred, _items.ByRiotId(BlackCleaver)!.Effects.Single().Kind);

        // Death's Dance is mitigation plus a takedown heal.
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
        // An effect with no amount and no scaling contributes nothing and is a
        // half-finished entry.
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
        var statFields = typeof(ItemStats).GetProperties().Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var item in _items.All)
        {
            foreach (var e in item.Effects.Where(e => e.Kind == EffectKind.StatBuff))
            {
                Assert.IsNotNull(e.Stat, $"{item.Name} has a StatBuff with no stat named.");
                Assert.IsTrue(
                    statFields.Contains(e.Stat) || SyntheticStats.Contains(e.Stat),
                    $"{item.Name} buffs unknown stat '{e.Stat}'.");
            }
        }
    }

    [TestMethod]
    public void ConditionalMitigationSaysWhatItAppliesTo()
    {
        // Randuin's 30% is crit-only and Steelcaps' 10% is attacks-only. Treating
        // either as blanket mitigation overrates it against comps it is not bought
        // for - and Kindred is a crit champion on the receiving end of both.
        Assert.AreEqual(DamageSource.Crit, _items.ByRiotId(RanduinsOmen)!.Effects.Single().Versus);
        Assert.AreEqual(DamageSource.Attacks, _items.ByRiotId(PlatedSteelcaps)!.Effects.Single().Versus);

        // Lifeline shields differ the same way: Maw only absorbs magic, Shieldbow all.
        Assert.AreEqual(DamageSource.Magic, _items.ByRiotId(MawOfMalmortius)!.Effects.Single().Versus);
        Assert.AreEqual(DamageSource.All, _items.ByRiotId(Shieldbow)!.Effects.Single().Versus);
    }

    [TestMethod]
    public void ConditionalEffectsCarryTheirCondition()
    {
        // Giant Slayer is worthless against a squishy. Without the condition the
        // planner would price it as a flat 15% amp and buy it into any comp.
        var ldr = _items.ByRiotId(LordDominiks)!.Effects.Single();

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
        // TeamNeeds decides anti-heal and Serpent's Fang partly from enemy items, so
        // healing and shielding have to be findable without reading item names.
        var healers = _items.All.Where(i => i.Effects.Any(e => e.Kind == EffectKind.Heal)).ToList();
        var shielders = _items.All.Where(i => i.Effects.Any(e => e.Kind == EffectKind.Shield)).ToList();

        Assert.IsGreaterThan(5, healers.Count);
        Assert.IsGreaterThan(5, shielders.Count);
        Assert.IsTrue(shielders.Any(i => i.RiotId == Shieldbow));
    }
}
