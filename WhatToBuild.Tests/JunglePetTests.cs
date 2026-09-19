using WhatToBuild.Data;
using WhatToBuild.Dtos;
using WhatToBuild.Modeling;
using WhatToBuild.Modeling.Simulation;

namespace WhatToBuild.Tests;

[TestClass]
public class JunglePetTests
{
    private static ItemRepository _items = null!;
    private static ChampionRepository _champions = null!;
    private static NeutralRepository _neutrals = null!;

    private static Champion Kindred => _champions.ByName("Kindred")!;
    private static Item Gustwalker => _items.ByRiotId(1102)!;

    [ClassInitialize]
    public static void Load(TestContext context)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "GameData");
        _items = ItemRepository.Load(root);
        _champions = ChampionRepository.Load(root);
        _neutrals = NeutralRepository.Load(root);
    }

    private static NeutralState RedBuff() => new(_neutrals.ByInternalName("SRU_Red")!, _neutrals.Scaling, 90);

    private static FightResult Fight(Entity target, params Item[] items) =>
        FightSimulator.Run(new FightSetup(new ChampionState(Kindred, 3, items), target, AbilityRanks.None, MaxSeconds: 10));

    [TestMethod]
    public void EveryJunglePetHasTheCompanionEffects()
    {
        var pets = _items.All.Where(i => i.Groups.Contains("HuntersTalismanGroup")).ToList();

        Assert.HasCount(6, pets);
        foreach (var pet in pets)
        {
            Assert.IsTrue(pet.Effects.Any(e => e.Trigger == EffectTrigger.OnAttack && e.Amount == 37 && e.Area && e.ByCompanion), pet.Name);
            Assert.IsTrue(pet.Effects.Any(e => e.Stat == Stats.DamageAmp && e.Amount == 0.1), pet.Name);
        }
    }

    [TestMethod]
    public void PetBitesTheMonsterOnEveryAttack()
    {
        var red = RedBuff();
        var result = Fight(red, Gustwalker);
        var bite = 37.0;

        Assert.AreEqual(result.Attacks * bite, result.DamageBySource[Gustwalker.Name], 0.001);
    }

    [TestMethod]
    public void PetAddsTenPercentAgainstMonsters()
    {
        var withPet = Fight(RedBuff(), Gustwalker);
        var without = Fight(RedBuff());

        var perAttackWith = withPet.DamageBySource[FightSimulator.Attacks] / withPet.Attacks;
        var perAttackWithout = without.DamageBySource[FightSimulator.Attacks] / without.Attacks;

        Assert.AreEqual(1.1, perAttackWith / perAttackWithout, 0.0001);
    }

    [TestMethod]
    public void PetDoesNothingAgainstChampions()
    {
        var garen = new ChampionState(_champions.ByName("Garen")!, 3);

        var withPet = Fight(garen, Gustwalker);
        var without = Fight(garen);

        Assert.IsFalse(withPet.DamageBySource.ContainsKey(Gustwalker.Name));
        Assert.AreEqual(without.Damage, withPet.Damage, 0.001);
    }

    [TestMethod]
    public void TooltipSaysItIsForMonsters()
    {
        var lines = ItemDescriber.EffectLines(Gustwalker.Effects);

        CollectionAssert.Contains(lines.ToList(), "On attack: 37 true damage to the target and every enemy around it, dealt by the companion when the target is a monster");
    }
}
