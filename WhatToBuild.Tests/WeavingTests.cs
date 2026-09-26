using WhatToBuild.Data;
using WhatToBuild.Modeling;
using WhatToBuild.Modeling.Simulation;
using WhatToBuild.SupportedChampions;
using WhatToBuild.SupportedChampions.Kayn;
using WhatToBuild.SupportedChampions.Vi;

namespace WhatToBuild.Tests;

[TestClass]
public class WeavingTests
{
    private static ChampionRepository _champions = null!;
    private static ItemRepository _items = null!;
    private static ChampionKits _kits = null!;
    private static ViKitData _vi = null!;

    [ClassInitialize]
    public static void Load(TestContext context)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "GameData");
        _champions = ChampionRepository.Load(root);
        _items = ItemRepository.Load(root);
        _kits = ChampionKits.Load(root);
        _vi = ViKitData.Load(Path.Combine(root, ChampionKits.FolderName, ViKitData.FileName));
    }

    /// <summary>Passes everything to the kit and writes down what dealt damage, in order.</summary>
    private sealed class Recorder(IChampionKit inner) : IChampionKit
    {
        public List<(double Time, string Source)> Hits { get; } = new();

        public void Update(Fight fight) => inner.Update(fight);

        public void OnAttack(Fight fight) => inner.OnAttack(fight);

        public double AttackUptime => inner.AttackUptime;

        public void OnDamage(Fight fight, string source, double damage)
        {
            Hits.Add((fight.Time, source));
            inner.OnDamage(fight, source, damage);
        }
    }

    private static ChampionState Target() => new(_champions.ByName("Caitlyn")!, 13, [_items.ByRiotId(3031)!]);

    private static List<(double Time, string Source)> Fight(string champion, string? form, AbilityRanks ranks, double seconds = 10)
    {
        var us = new ChampionState(_champions.ByName(champion)!, 13, [_items.ByRiotId(3071)!]);
        var recorder = new Recorder(_kits.NewFight(us.Champion, form)!);
        FightSimulator.Run(new FightSetup(us, Target(), ranks, MaxSeconds: seconds, Sustained: true), recorder);
        return recorder.Hits;
    }

    private static bool IsSpell(string source) => source.Length > 2 && source[1] == ' ' && "QWER".Contains(source[0]);

    /// <summary>The spells and attacks in the order they landed, a spell's extra hits folded into it.</summary>
    private static List<string> Sequence(IEnumerable<(double Time, string Source)> hits, params string[] ignore) =>
        hits.Where(h => (IsSpell(h.Source) || h.Source == FightSimulator.Attacks) && !ignore.Contains(h.Source))
            .Select(h => IsSpell(h.Source) ? "spell" : "attack")
            .Aggregate(new List<string>(), (list, kind) =>
            {
                if (list.Count == 0 || list[^1] != kind || kind == "attack")
                {
                    list.Add(kind);
                }

                return list;
            });

    [TestMethod]
    public void EverySupportedChampionDeclaresItsPlaystyle()
    {
        foreach (var name in _kits.Supported)
        {
            var kit = _kits.NewFight(_champions.ByName(name)!) as ScriptedKit;
            Assert.IsNotNull(kit, $"{name} is scripted.");
            Assert.IsNotEmpty(kit.Playstyle.Combo, $"{name} has a combo.");
        }
    }

    private static FightResult Burst(string champion, string? form, AbilityRanks ranks)
    {
        var us = new ChampionState(_champions.ByName(champion)!, 13, [_items.ByRiotId(3071)!]);
        var kit = (ScriptedKit)_kits.NewFight(us.Champion, form)!;
        return FightSimulator.Burst(new FightSetup(us, Target(), ranks), kit);
    }

    [TestMethod]
    [DataRow("Nasus", null, 1, new[] { "E", "Q" })]
    [DataRow("Vi", null, 2, new[] { "R", "Q", "E" })]
    [DataRow("Kayn", KaynForm.Darkin, 1, new[] { "W", "Q" })]
    [DataRow("Kindred", null, 3, new[] { "W", "E", "Q" })]
    public void BurstIsTheScriptedSequence(string champion, string? form, int attacks, string[] spells)
    {
        var burst = Burst(champion, form, new AbilityRanks(3, 3, 3, 2));

        Assert.AreEqual(attacks, burst.Attacks, "Only the sequence's attacks, empowered ones included.");
        CollectionAssert.AreEquivalent(spells, burst.DamageBySource.Keys.Where(IsSpell).Select(s => s[..1]).Distinct().ToList());
        Assert.IsLessThan(5.0, burst.Seconds, "It ends with the sequence, not a fixed window.");
    }

    [TestMethod]
    public void TheFightReportsItsBurstSequence()
    {
        var us = new ChampionState(_champions.ByName("Nasus")!, 13, [_items.ByRiotId(3071)!]);
        var ranks = new AbilityRanks(3, 3, 3, 2);
        var fight = FightSimulator.Run(new FightSetup(us, Target(), ranks), _kits.NewFight(us.Champion));
        var burst = Burst("Nasus", null, ranks);

        Assert.AreEqual(burst.Damage, fight.EarlyDamage, 1e-9);
    }

    [TestMethod]
    public void KaynWeavesAnAttackBetweenEveryTwoSpells()
    {
        var hits = Fight("Kayn", KaynForm.Darkin, new AbilityRanks(5, 3, 1, 2));
        var order = Sequence(hits);

        for (var i = 1; i < order.Count; i++)
        {
            Assert.IsFalse(order[i] == "spell" && order[i - 1] == "spell", $"Two spells in a row at step {i}: {string.Join(", ", order)}");
        }

        // Attack, Q, attack, W, attack: the attack is ready at the start, so it goes first.
        var opening = hits.Where(h => IsSpell(h.Source) || h.Source == FightSimulator.Attacks)
            .Select(h => h.Source == FightSimulator.Attacks ? "AA" : h.Source[..1])
            .Aggregate(new List<string>(), (list, s) =>
            {
                if (list.Count == 0 || list[^1] != s || s == "AA")
                {
                    list.Add(s);
                }

                return list;
            })
            .Take(5);
        Assert.AreEqual("AA Q AA W AA", string.Join(" ", opening));

        // No ability resets his attack: attacks are never closer than his attack timer allows.
        var attacks = hits.Where(h => h.Source == FightSimulator.Attacks).Select(h => h.Time).Distinct().ToList();
        var us = new ChampionState(_champions.ByName("Kayn")!, 13, [_items.ByRiotId(3071)!]);
        for (var i = 1; i < attacks.Count; i++)
        {
            Assert.IsGreaterThanOrEqualTo(1 / us.Stats.AttackSpeed - 2 * Modeling.Simulation.Fight.Step, attacks[i] - attacks[i - 1], $"Attacks {i - 1} and {i}.");
        }

        var first = hits.Where(h => IsSpell(h.Source)).Select(h => h.Time).Distinct().Take(3).ToList();
        Assert.HasCount(3, first);
        Assert.IsTrue(first[1] - first[0] > 0.3, "W's cast time passes before the next spell, not the same instant.");
    }

    [TestMethod]
    public void ViWeavesTooAndHerEmpoweredAttackFollowsE()
    {
        var hits = Fight("Vi", null, new AbilityRanks(5, 3, 1, 2));
        var order = Sequence(hits, ViKit.DentingBlows, ViKit.RelentlessForce);

        Assert.AreEqual("spell", order[0], "She opens with R.");
        for (var i = 1; i < order.Count; i++)
        {
            Assert.IsFalse(order[i] == "spell" && order[i - 1] == "spell", $"Two spells in a row at step {i}: {string.Join(", ", order)}");
        }

        // The combo: R, attack, E (its empowered attack), fully charged Q, attack, E.
        var combo = hits
            .Where(h => h.Source is ViKit.CeaseAndDesist or ViKit.RelentlessForce or ViKit.VaultBreaker)
            .Select(h => h.Source[0])
            .Take(4);
        Assert.AreEqual("REQE", string.Concat(combo));

        var q = hits.First(h => h.Source == ViKit.VaultBreaker);
        var before = hits.Last(h => h.Source == FightSimulator.Attacks && h.Time < q.Time);
        Assert.AreEqual(_vi.Q.ChargeSeconds + _vi.Q.ReleaseSeconds, q.Time - before.Time, 0.05,
            "Q is charged in full after the attack before it, then she dashes.");

        var empowered = hits.Where(h => h.Source == ViKit.RelentlessForce).ToList();
        Assert.IsNotEmpty(empowered, "E's attack lands.");
        Assert.IsTrue(empowered.All(e => hits.Any(h => h.Source == FightSimulator.Attacks && Math.Abs(h.Time - e.Time) < 1e-9)),
            "Every empowered hit is an attack.");
    }

    [TestMethod]
    public void KindredAttacksBetweenEveryTwoQs()
    {
        var hits = Fight("Kindred", null, new AbilityRanks(5, 3, 1, 2));
        var qs = hits.Where(h => h.Source == SupportedChampions.Kindred.KindredKit.DanceOfArrows).Select(h => h.Time).Distinct().ToList();

        Assert.IsGreaterThanOrEqualTo(2, qs.Count);
        for (var i = 1; i < qs.Count; i++)
        {
            Assert.IsTrue(hits.Any(h => h.Source == FightSimulator.Attacks && h.Time > qs[i - 1] && h.Time < qs[i]),
                $"No attack between the Qs at {qs[i - 1]:0.00}s and {qs[i]:0.00}s.");
        }

        var firstAttack = hits.First(h => h.Source == FightSimulator.Attacks).Time;
        var kindred = new ChampionState(_champions.ByName("Kindred")!, 13, [_items.ByRiotId(3071)!]);
        Assert.IsLessThan(kindred.Champion.AttackWindup / kindred.Stats.AttackSpeed + 0.1, firstAttack - qs[0],
            "Q resets her attack timer, so an attack starts right after it and lands after its windup.");
    }

    [TestMethod]
    public void DentingBlowsProcsOnEveryThirdAttack()
    {
        var hits = Fight("Vi", null, new AbilityRanks(0, 1, 0, 0));
        var attacks = hits.Count(h => h.Source == FightSimulator.Attacks);
        var procs = hits.Count(h => h.Source == ViKit.DentingBlows);

        Assert.IsGreaterThanOrEqualTo(6, attacks);
        Assert.AreEqual(attacks / _vi.W.HitsToProc, procs);
    }

    private static List<(double Time, string Source)> NasusFight(AbilityRanks ranks, double stacks, double seconds = 10)
    {
        var us = new ChampionState(_champions.ByName("Nasus")!, 13, [_items.ByRiotId(3078)!]);
        var recorder = new Recorder(_kits.NewFight(us.Champion)!);
        FightSimulator.Run(new FightSetup(us, Target(), ranks, stacks, MaxSeconds: seconds, Sustained: true), recorder);
        return recorder.Hits;
    }

    [TestMethod]
    public void NasusQCarriesHisStacksAndResetsHisAttack()
    {
        var none = NasusFight(new AbilityRanks(5, 0, 0, 0), 0);
        var stacked = NasusFight(new AbilityRanks(5, 0, 0, 0), 300);

        var qs = none.Where(h => h.Source == SupportedChampions.Nasus.NasusKit.SiphoningStrike).ToList();
        Assert.IsNotEmpty(qs);
        Assert.IsTrue(qs.All(q => none.Any(h => h.Source == FightSimulator.Attacks && Math.Abs(h.Time - q.Time) < 1e-9)), "Q lands on an attack.");
        var nasus = new ChampionState(_champions.ByName("Nasus")!, 13, [_items.ByRiotId(3078)!]);
        Assert.AreEqual(nasus.Champion.AttackWindup / nasus.Stats.AttackSpeed, qs[0].Time, 2 * Modeling.Simulation.Fight.Step,
            "Q has no animation: it goes out at once and the first attack carries it, landing after the windup.");

        double PerQ(double stacks)
        {
            var us = new ChampionState(_champions.ByName("Nasus")!, 13, [_items.ByRiotId(3078)!]);
            var result = FightSimulator.Run(new FightSetup(us, Target(), new AbilityRanks(5, 0, 0, 0), stacks, MaxSeconds: 10, Sustained: true), _kits.NewFight(us.Champion));
            var count = Math.Max(1, (stacks == 0 ? none : stacked).Count(h => h.Source == SupportedChampions.Nasus.NasusKit.SiphoningStrike));
            return result.DamageBySource[SupportedChampions.Nasus.NasusKit.SiphoningStrike] / count;
        }

        Assert.IsGreaterThan(PerQ(0) * 2, PerQ(300), "Each Q carries the stacks.");
    }

    [TestMethod]
    public void FuryOfTheSandsHalvesQsCooldownAndBurns()
    {
        var withoutR = NasusFight(new AbilityRanks(5, 0, 0, 0), 0).Count(h => h.Source == SupportedChampions.Nasus.NasusKit.SiphoningStrike);
        var withR = NasusFight(new AbilityRanks(5, 0, 0, 1), 0);

        Assert.IsGreaterThan(withoutR, withR.Count(h => h.Source == SupportedChampions.Nasus.NasusKit.SiphoningStrike), "More Qs while R halves the cooldown.");
        Assert.IsNotEmpty(withR.Where(h => h.Source == SupportedChampions.Nasus.NasusKit.FuryOfTheSands), "R burns the target.");
    }

    [TestMethod]
    public void NasusRGivesStatsAndWitherCutsAnAttacker()
    {
        var nasus = _kits.For(_champions.ByName("Nasus")!)!;
        var us = new ChampionState(_champions.ByName("Nasus")!, 16, []);

        var fury = nasus.Stats(new AbilityRanks(5, 5, 5, 3))!;
        Assert.AreEqual(600, fury.Stats.Health, 1e-9);
        Assert.AreEqual(70, fury.Stats.Armor, 1e-9);
        Assert.AreEqual(70, fury.Stats.MagicResist, 1e-9);
        Assert.IsNull(nasus.Stats(new AbilityRanks(5, 5, 5, 0)));

        // Rank 5 Wither: 11 s cooldown from the cast, so one 5 s cast in a 10 s fight, the slow
        // ramping 35% → 95% (65% on average), attack speed taking 75% of it.
        Assert.AreEqual(0.5 * 0.75 * 0.65, nasus.AttackCut(new AbilityRanks(5, 5, 5, 3), us, 10), 1e-9);
        Assert.AreEqual(0, nasus.AttackCut(new AbilityRanks(5, 0, 5, 3), us, 10), 1e-9);
    }

    [TestMethod]
    public void SoulEaterLifestealGrowsAtSevenAndThirteen()
    {
        var nasus = _kits.For(_champions.ByName("Nasus")!)!;
        double At(int level) => nasus.LifeSteal(new ChampionState(_champions.ByName("Nasus")!, level, []), null);

        Assert.AreEqual(0.10, At(6), 1e-9);
        Assert.AreEqual(0.15, At(7), 1e-9);
        Assert.AreEqual(0.20, At(13), 1e-9);
    }

    [TestMethod]
    public void ViIsSupportedAndNamesHerShield()
    {
        var vi = _kits.For(_champions.ByName("Vi")!)!;

        Assert.AreEqual("Blast Shield", vi.Survival(new AbilityRanks(1, 0, 0, 0))?.Name);
        Assert.HasCount(18, vi.SkillOrder);
    }
}
