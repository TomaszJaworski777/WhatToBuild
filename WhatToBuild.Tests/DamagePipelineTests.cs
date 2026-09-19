using WhatToBuild.Data;
using WhatToBuild.Modeling;

namespace WhatToBuild.Tests;

[TestClass]
public class DamagePipelineTests
{
    private static readonly Attacker NoPen = new();

    private static DamageResult Run(Hit hit, Attacker attacker, Defender defender) =>
        DamageCalculation.Standard().Run(hit, attacker, defender);

    private static Hit Physical(double raw) => new(DamageType.Physical, raw);

    private static Hit Magic(double raw) => new(DamageType.Magic, raw);

    private static Hit True(double raw) => new(DamageType.True, raw);

    [TestMethod]
    [DataRow(0, 1.0)]
    [DataRow(50, 0.666667)]
    [DataRow(100, 0.5)]
    [DataRow(300, 0.25)]
    [DataRow(-50, 1.333333)]
    public void ResistMultiplierMatchesTheGameFormula(double resist, double expected)
    {
        Assert.AreEqual(expected, ResistanceStep.Multiplier(resist), 0.0001);
    }

    [TestMethod]
    public void ArmorHalvesDamageAtOneHundred()
    {
        Assert.AreEqual(50, Run(Physical(100), NoPen, new Defender { Armor = 100 }).HealthDamage, 0.001);
    }

    [TestMethod]
    public void LethalityIsFlatPenetration()
    {
        var result = Run(Physical(100), new Attacker { Lethality = 20 }, new Defender { Armor = 100 });

        Assert.AreEqual(80, result.EffectiveResist, 0.001);
        Assert.AreEqual(100.0 * 100 / 180, result.HealthDamage, 0.001);
    }

    [TestMethod]
    public void PercentPenetrationAppliesBeforeLethality()
    {
        var attacker = new Attacker { ArmorPenetrationPercent = 0.35, Lethality = 20 };

        Assert.AreEqual(45, Run(Physical(100), attacker, new Defender { Armor = 100 }).EffectiveResist, 0.001);
    }

    [TestMethod]
    public void PenetrationCannotPushArmorBelowZero()
    {
        var result = Run(Physical(100), new Attacker { Lethality = 30 }, new Defender { Armor = 10 });

        Assert.AreEqual(0, result.EffectiveResist, 0.001);
        Assert.AreEqual(100, result.HealthDamage, 0.001);
    }

    [TestMethod]
    public void ReductionCanPushArmorBelowZero()
    {
        var result = Run(Physical(100), NoPen, new Defender { Armor = 10, ArmorReductionFlat = 30 });

        Assert.AreEqual(-20, result.EffectiveResist, 0.001);
        Assert.AreEqual(100 * (2 - 100.0 / 120), result.HealthDamage, 0.001);
    }

    [TestMethod]
    public void NegativeArmorIgnoresPenetration()
    {
        var attacker = new Attacker { ArmorPenetrationPercent = 0.35, Lethality = 20 };

        var result = Run(Physical(100), attacker, new Defender { Armor = 10, ArmorReductionFlat = 30 });

        Assert.AreEqual(-20, result.EffectiveResist, 0.001);
    }

    [TestMethod]
    public void ReductionAppliesBeforePenetration()
    {
        var attacker = new Attacker { ArmorPenetrationPercent = 0.35 };
        var defender = new Defender { Armor = 100, ArmorReductionPercent = 0.3 };

        Assert.AreEqual(100 * 0.7 * 0.65, Run(Physical(100), attacker, defender).EffectiveResist, 0.001);
    }

    [TestMethod]
    public void MagicUsesMagicResistAndIgnoresArmor()
    {
        var attacker = new Attacker { MagicPenetrationFlat = 10 };
        var defender = new Defender { Armor = 200, MagicResist = 50 };

        var result = Run(Magic(100), attacker, defender);

        Assert.AreEqual(40, result.EffectiveResist, 0.001);
        Assert.AreEqual(100.0 * 100 / 140, result.HealthDamage, 0.001);
    }

    [TestMethod]
    public void PhysicalIgnoresMagicPenetration()
    {
        var attacker = new Attacker { MagicPenetrationPercent = 0.4, MagicPenetrationFlat = 20 };

        Assert.AreEqual(100, Run(Physical(100), attacker, new Defender { Armor = 100 }).EffectiveResist, 0.001);
    }

    [TestMethod]
    public void TrueDamageIgnoresResistances()
    {
        Assert.AreEqual(100, Run(True(100), NoPen, new Defender { Armor = 500, MagicResist = 500 }).HealthDamage, 0.001);
    }

    [TestMethod]
    public void AttackerAmpMultipliesDamage()
    {
        Assert.AreEqual(115, Run(Physical(100), new Attacker { DamageAmp = 0.15 }, new Defender()).HealthDamage, 0.001);
    }

    [TestMethod]
    public void RanduinsOnlyReducesCrits()
    {
        var randuins = new Defender { Mitigations = [new Mitigation(DamageSource.Crit, Percent: 0.3)] };

        Assert.AreEqual(70, Run(Hit.Attack(100, crit: true), NoPen, randuins).HealthDamage, 0.001);
        Assert.AreEqual(100, Run(Hit.Attack(100, crit: false), NoPen, randuins).HealthDamage, 0.001);
        Assert.AreEqual(100, Run(Hit.Ability(DamageType.Physical, 100), NoPen, randuins).HealthDamage, 0.001);
    }

    [TestMethod]
    public void SteelcapsReducesAttacksButNotAbilities()
    {
        var steelcaps = new Defender { Mitigations = [new Mitigation(DamageSource.Attacks, Percent: 0.1)] };

        Assert.AreEqual(90, Run(Hit.Attack(100), NoPen, steelcaps).HealthDamage, 0.001);
        Assert.AreEqual(90, Run(Hit.Attack(100, crit: true), NoPen, steelcaps).HealthDamage, 0.001);
        Assert.AreEqual(100, Run(Hit.Ability(DamageType.Physical, 100), NoPen, steelcaps).HealthDamage, 0.001);
    }

    [TestMethod]
    public void FlatMitigationAppliesAfterPercent()
    {
        var defender = new Defender
        {
            Mitigations =
            [
                new Mitigation(DamageSource.Attacks, Percent: 0.1),
                new Mitigation(DamageSource.Attacks, Flat: 15),
            ],
        };

        Assert.AreEqual(100 * 0.9 - 15, Run(Hit.Attack(100), NoPen, defender).HealthDamage, 0.001);
    }

    [TestMethod]
    public void FlatMitigationCannotHeal()
    {
        var defender = new Defender { Mitigations = [new Mitigation(DamageSource.All, Flat: 50)] };

        Assert.AreEqual(0, Run(Physical(20), NoPen, defender).HealthDamage, 0.001);
    }

    [TestMethod]
    public void NegativeMitigationAmplifies()
    {
        var abyssal = new Defender { Mitigations = [new Mitigation(DamageSource.Magic, Percent: -0.12)] };

        Assert.AreEqual(112, Run(Magic(100), NoPen, abyssal).HealthDamage, 0.001);
        Assert.AreEqual(100, Run(Physical(100), NoPen, abyssal).HealthDamage, 0.001);
    }

    [TestMethod]
    public void ShieldsAbsorbBeforeHealth()
    {
        var result = Run(True(500), NoPen, new Defender { Shields = [new Shield(300)] });

        Assert.AreEqual(300, result.ShieldAbsorbed, 0.001);
        Assert.AreEqual(200, result.HealthDamage, 0.001);
        Assert.AreEqual(500, result.TotalDamage, 0.001);
    }

    [TestMethod]
    public void ShieldLargerThanDamageTakesItAll()
    {
        var result = Run(True(100), NoPen, new Defender { Shields = [new Shield(1000)] });

        Assert.AreEqual(100, result.ShieldAbsorbed, 0.001);
        Assert.AreEqual(0, result.HealthDamage, 0.001);
    }

    [TestMethod]
    public void MagicShieldOnlyStopsMagicDamage()
    {
        var defender = new Defender { Shields = [new Shield(300, DamageSource.Magic)] };

        Assert.AreEqual(0, Run(Physical(200), NoPen, defender).ShieldAbsorbed, 0.001);
        Assert.AreEqual(200, Run(Magic(200), NoPen, defender).ShieldAbsorbed, 0.001);
        Assert.AreEqual(0, Run(True(200), NoPen, defender).ShieldAbsorbed, 0.001);
    }

    [TestMethod]
    public void ShieldReductionShrinksShields()
    {
        var result = Run(True(500), new Attacker { ShieldReduction = 0.5 }, new Defender { Shields = [new Shield(300)] });

        Assert.AreEqual(150, result.ShieldAbsorbed, 0.001);
        Assert.AreEqual(350, result.HealthDamage, 0.001);
    }

    [TestMethod]
    public void TraceRecordsEveryStep()
    {
        var defender = new Defender { Armor = 100, Shields = [new Shield(20)] };

        var result = Run(Physical(100), new Attacker { DamageAmp = 0.2 }, defender);

        CollectionAssert.AreEqual(
            new[] { "Amplification", "Resistance", "Mitigation", "Shield" },
            result.Trace.Select(t => t.Step).ToList());

        Assert.AreEqual(120, result.Trace[0].DamageAfter, 0.001);
        Assert.AreEqual(60, result.Trace[1].DamageAfter, 0.001);
        Assert.AreEqual(40, result.Trace[3].DamageAfter, 0.001);
    }

    [TestMethod]
    public void StepsCanBeRemoved()
    {
        var calculation = DamageCalculation.Standard().Without<ShieldStep>();

        var result = calculation.Run(True(100), NoPen, new Defender { Shields = [new Shield(1000)] });

        Assert.AreEqual(100, result.HealthDamage, 0.001);
        Assert.IsFalse(calculation.Steps.Any(s => s is ShieldStep));
    }

    [TestMethod]
    public void CustomStepsCanBeAdded()
    {
        var calculation = DamageCalculation.Standard().Then(new DoubleDamageStep());

        var result = calculation.Run(True(100), NoPen, new Defender());

        Assert.AreEqual(200, result.HealthDamage, 0.001);
        Assert.AreEqual("Double", result.Trace.Last().Step);
    }

    [TestMethod]
    public void ItemDataDrivesTheCalculation()
    {
        var items = ItemRepository.Load(Path.Combine(AppContext.BaseDirectory, "GameData"));

        var randuins = items.ByRiotId(3143)!.Effects.Single(e => e.Kind == EffectKind.DamageReduction);
        var defender = new Defender { Mitigations = [new Mitigation(randuins.Versus, Percent: randuins.Amount)] };

        Assert.AreEqual(70, Run(Hit.Attack(100, crit: true), NoPen, defender).HealthDamage, 0.001);
        Assert.AreEqual(100, Run(Hit.Attack(100), NoPen, defender).HealthDamage, 0.001);

        var fang = items.ByRiotId(6695)!;
        var reduction = fang.Effects.Single(e => e.Kind == EffectKind.ShieldReduction).Amount;
        var shielded = new Defender { Shields = [new Shield(400)] };

        Assert.AreEqual(200, Run(Physical(600), new Attacker { ShieldReduction = reduction }, shielded).ShieldAbsorbed, 0.001);
    }

    private sealed class DoubleDamageStep : IDamageStep
    {
        public string Name => "Double";

        public void Apply(DamageContext context) => context.Damage *= 2;
    }
}
