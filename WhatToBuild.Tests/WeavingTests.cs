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
    public void KaynWeavesAnAttackBetweenEveryTwoSpells()
    {
        var hits = Fight("Kayn", KaynForm.Darkin, new AbilityRanks(5, 3, 1, 2));
        var order = Sequence(hits);

        Assert.AreEqual("spell", order[0], "He opens with a spell.");
        for (var i = 1; i < order.Count; i++)
        {
            Assert.IsFalse(order[i] == "spell" && order[i - 1] == "spell", $"Two spells in a row at step {i}: {string.Join(", ", order)}");
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
        Assert.AreEqual(_vi.Q.ChargeSeconds, q.Time - before.Time, 0.05, "Q is charged in full after the attack before it.");

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
        Assert.IsLessThan(0.1, firstAttack - qs[0], "Q resets her attack timer, so an attack follows it at once.");
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

    [TestMethod]
    public void ViIsSupportedAndNamesHerShield()
    {
        var vi = _kits.For(_champions.ByName("Vi")!)!;

        Assert.AreEqual("Blast Shield", vi.Survival(new AbilityRanks(1, 0, 0, 0))?.Name);
        Assert.HasCount(18, vi.SkillOrder);
    }
}
