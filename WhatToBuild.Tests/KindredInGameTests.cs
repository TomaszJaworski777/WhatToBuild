using WhatToBuild.Data;
using WhatToBuild.Modeling;
using WhatToBuild.Modeling.Simulation;
using WhatToBuild.SupportedChampions;
using WhatToBuild.SupportedChampions.Kindred;

namespace WhatToBuild.Tests;

[TestClass]
public class KindredInGameTests
{
    private const double MeasuredAttackDamage = 75;
    private const double PressTheAttackExposure = 0.08;

    private static ChampionRepository _champions = null!;
    private static ItemRepository _items = null!;
    private static NeutralRepository _neutrals = null!;
    private static ChampionKits _kits = null!;

    private static Champion Kindred => _champions.ByName("Kindred")!;
    private static Item Gustwalker => _items.ByRiotId(1102)!;
    private static readonly AbilityRanks LevelThreeRanks = new(1, 1, 1, 0);

    [ClassInitialize]
    public static void Load(TestContext context)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "GameData");
        _champions = ChampionRepository.Load(root);
        _items = ItemRepository.Load(root);
        _neutrals = NeutralRepository.Load(root);
        _kits = ChampionKits.Load(root);
    }

    private static ChampionState LevelThreeKindred(params Item[] items)
    {
        var runes = new StatSheet { AttackDamage = MeasuredAttackDamage - new ChampionState(Kindred, 3).Stats.AttackDamage };
        return new ChampionState(Kindred, 3, items, adjustment: runes);
    }

    private static KindredKit Kit() => (KindredKit)_kits.NewFight(Kindred)!;

    private static double Attack(ChampionState kindred, Entity target) =>
        AttackerHits.Physical(kindred, target, MeasuredAttackDamage).Attack().Run().HealthDamage;

    [TestMethod]
    public void DummyWithHundredArmorTakes37FromAnAttack()
    {
        Assert.AreEqual(37, Attack(LevelThreeKindred(), new Dummy(1000, 100)), 1);
    }

    [TestMethod]
    public void DummyWithFourHundredArmorTakes15FromAnAttack()
    {
        Assert.AreEqual(15, Attack(LevelThreeKindred(), new Dummy(1000, 400)), 1);
    }

    [TestMethod]
    public void PounceShowsUpMergedWithItsAttack()
    {
        var kindred = LevelThreeKindred();
        var dummy = new Dummy(1000, 400) { CurrentHealth = 650 };
        var fight = new Fight(new FightSetup(kindred, dummy, LevelThreeRanks));

        var shown = (Attack(kindred, dummy) + Kit().PounceDamage(fight)) * (1 + PressTheAttackExposure);

        Assert.AreEqual(38, shown, 1.5);
    }

    [TestMethod]
    public void RedTakes58PlusA37PetBiteFromAnAttack()
    {
        var red = new NeutralState(_neutrals.ByInternalName("SRU_Red")!, _neutrals.Scaling, 90);
        var kindred = LevelThreeKindred(Gustwalker);
        var bite = AttackerHits.ForEffect(Gustwalker.Effects.First(e => e.Kind == EffectKind.TrueDamage), kindred, red)!.Attack().Run().HealthDamage;

        Assert.AreEqual(58, Attack(kindred, red), 1);
        Assert.AreEqual(37, bite, 0.001);
    }

    [TestMethod]
    public void RedHintIs435WithAttacksAndPetOnly()
    {
        var red = new NeutralState(_neutrals.ByInternalName("SRU_Red")!, _neutrals.Scaling, 90);
        var hint = _kits.For(Kindred)!.Hints(new FightSetup(LevelThreeKindred(Gustwalker), red, LevelThreeRanks)).Single();

        Assert.AreEqual(435, hint.CastAtHealth, 3);
        Assert.AreEqual(149, hint.KillingHealth, 2);
        Assert.AreEqual(33, hint.Additions.Single(a => a.When.Contains('Q')).Damage, 2);
    }

    private sealed class Dummy(double health, double armor) : Entity
    {
        public override string Name => "Target dummy";

        public override StatSheet Stats { get; } = new() { Health = health, Armor = armor };
    }
}
