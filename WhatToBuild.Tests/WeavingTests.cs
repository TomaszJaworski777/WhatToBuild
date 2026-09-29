using WhatToBuild.Data;
using WhatToBuild.Modeling;
using WhatToBuild.Modeling.Simulation;
using WhatToBuild.SupportedChampions;
using WhatToBuild.SupportedChampions.Kayn;
using WhatToBuild.SupportedChampions.Nasus;
using WhatToBuild.SupportedChampions.Talon;
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

        public double AttackUptimeIn(Fight fight) => inner.AttackUptimeIn(fight);

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
    [DataRow("Talon", null, 1, new[] { "R", "W", "Q" })]
    public void BurstIsTheScriptedSequence(string champion, string? form, int attacks, string[] spells)
    {
        var burst = Burst(champion, form, new AbilityRanks(3, 3, 3, 2));

        Assert.AreEqual(attacks, burst.Attacks, "Only the sequence's attacks, empowered ones included.");
        CollectionAssert.AreEquivalent(spells, burst.DamageBySource.Keys.Where(IsSpell).Select(s => s[..1]).Distinct().ToList());
        Assert.IsLessThan(5.0, burst.Seconds, "It ends with the sequence, not a fixed window.");
    }

    [TestMethod]
    public void TalonsBurstStacksBladesEndAndProcsIt()
    {
        var burst = Burst("Talon", null, new AbilityRanks(3, 3, 1, 2));

        // R out and back (his Q brings the blades into the target), W out and back, Q: past three
        // stacks by the attack, which makes the target bleed.
        Assert.IsTrue(burst.DamageBySource.ContainsKey(TalonKit.BladesEnd), string.Join(", ", burst.DamageBySource.Keys));
        var us = new ChampionState(_champions.ByName("Talon")!, 13, [_items.ByRiotId(3071)!]);
        var kit = (TalonKit)_kits.NewFight(us.Champion)!;
        var probe = new Modeling.Simulation.Fight(new FightSetup(us, Target(), new AbilityRanks(3, 3, 1, 2)));
        var rHit = probe.Physical(kit.RDamage(probe)).Ability().Run().HealthDamage;
        Assert.IsGreaterThan(rHit * 1.5, burst.DamageBySource[TalonKit.ShadowAssault], "Both of R's hits land.");
    }

    [TestMethod]
    public void TalonWeavesAnAttackBetweenEveryTwoSpells()
    {
        var hits = Fight("Talon", null, new AbilityRanks(5, 3, 1, 2));
        var order = Sequence(hits, TalonKit.BladesEnd);

        Assert.AreEqual("spell", order[0], "He opens with R.");
        Assert.Contains("attack", order);
        Assert.IsTrue(hits.Any(h => h.Source == TalonKit.NoxianDiplomacy), "Q goes out.");
        Assert.IsTrue(hits.Any(h => h.Source == TalonKit.BladesEnd), "The bleed procs in a fight too.");
    }

    [TestMethod]
    public void ViEsExtraDamageCritsWithHerCritChance()
    {
        // A Cloak of Agility adds crit chance and nothing else: E's extra damage grows by exactly
        // the expected crit, 15% of the way to her crit damage.
        FightResult BurstWith(params int[] items)
        {
            var vi = new ChampionState(_champions.ByName("Vi")!, 13, items.Select(id => _items.ByRiotId(id)!).ToList());
            return FightSimulator.Burst(new FightSetup(vi, Target(), new AbilityRanks(3, 3, 3, 2)), (ScriptedKit)_kits.NewFight(vi.Champion)!);
        }

        var plain = BurstWith(3071).DamageBySource[ViKit.RelentlessForce];
        var crit = BurstWith(3071, 1018).DamageBySource[ViKit.RelentlessForce];

        Assert.AreEqual(1 + 0.15 * (AttackerHits.BaseCritDamage - 1), crit / plain, 1e-6);
    }

    [TestMethod]
    public void NasusQCritsWithHisCritChance()
    {
        // Siphoning Strike's damage, stacks included, crits with the attack it empowers.
        FightResult BurstWith(params int[] items)
        {
            var nasus = new ChampionState(_champions.ByName("Nasus")!, 13, items.Select(id => _items.ByRiotId(id)!).ToList());
            return FightSimulator.Burst(new FightSetup(nasus, Target(), new AbilityRanks(3, 3, 3, 2), Stacks: 200), (ScriptedKit)_kits.NewFight(nasus.Champion)!);
        }

        var plain = BurstWith(3071).DamageBySource[NasusKit.SiphoningStrike];
        var crit = BurstWith(3071, 1018).DamageBySource[NasusKit.SiphoningStrike];

        Assert.AreEqual(1 + 0.15 * (AttackerHits.BaseCritDamage - 1), crit / plain, 1e-6);
    }

    [TestMethod]
    public void AChosenBurstIsPlayedInsteadOfTheKitsOwn()
    {
        var us = new ChampionState(_champions.ByName("Nasus")!, 13, [_items.ByRiotId(3071)!]);
        var ranks = new AbilityRanks(3, 3, 3, 2);
        var setup = new FightSetup(us, Target(), ranks, BurstCombo: ["Q", ScriptedKit.Attack, "X"]);
        var burst = FightSimulator.Burst(setup, (ScriptedKit)_kits.NewFight(us.Champion)!);

        Assert.AreEqual(2, burst.Attacks, "Q's empowered attack, then the attack step; the unknown step is left out.");
        CollectionAssert.AreEquivalent(new[] { "Q" }, burst.DamageBySource.Keys.Where(IsSpell).Select(s => s[..1]).Distinct().ToList());
        Assert.AreEqual(burst.Damage, FightSimulator.Run(setup, _kits.NewFight(us.Champion)).EarlyDamage, 1e-9, "The fight's burst is the chosen one.");
    }

    [TestMethod]
    public void TheBurstIsMadeOfTheCombosKeysAndAttacks()
    {
        var kit = (ScriptedKit)_kits.NewFight(_champions.ByName("Talon")!)!;

        CollectionAssert.IsSubsetOf(kit.Playstyle.Burst.ToList(), kit.BurstSteps.ToList());
        Assert.AreEqual(ScriptedKit.Attack, kit.BurstSteps[^1]);
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

        // Rank 5 Wither: 11 s cooldown from the cast, so one 5 s cast in a 10 s fight, taking
        // practically all of the attacker's attack speed while it lasts.
        Assert.AreEqual(0.5, nasus.AttackCut(new AbilityRanks(5, 5, 5, 3), us, 10), 1e-9);
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

    [TestMethod]
    public void NasusRarelyAttacksBetweenHisQs()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "GameData");
        var weaving = SupportedChampions.Nasus.NasusKitData.Load(Path.Combine(root, ChampionKits.FolderName, SupportedChampions.Nasus.NasusKitData.FileName));
        weaving.AttacksBetweenQs = 1;

        List<(double Time, string Source)> Hits(IChampionKit kit)
        {
            var us = new ChampionState(_champions.ByName("Nasus")!, 13, [_items.ByRiotId(3078)!]);
            var recorder = new Recorder(kit);
            FightSimulator.Run(new FightSetup(us, Target(), new AbilityRanks(5, 0, 0, 0), MaxSeconds: 20, Sustained: true), recorder);
            return recorder.Hits;
        }

        static int PlainAttacks(List<(double Time, string Source)> hits)
        {
            var qs = hits.Where(h => h.Source == SupportedChampions.Nasus.NasusKit.SiphoningStrike).Select(h => h.Time).ToHashSet();
            return hits.Where(h => h.Source == FightSimulator.Attacks).Select(h => h.Time).Distinct().Count(t => !qs.Contains(t));
        }

        var held = PlainAttacks(Hits(_kits.NewFight(_champions.ByName("Nasus")!)!));
        var woven = PlainAttacks(Hits(new SupportedChampions.Nasus.NasusKit(weaving)));

        Assert.IsGreaterThan(0, woven);
        Assert.IsLessThan(woven / 2.0, held, $"{held} attacks between Qs, against {woven} if he weaved every one.");
    }

    [TestMethod]
    public void WitherOpensAndKeepsARangedTargetFromKiting()
    {
        var us = new ChampionState(_champions.ByName("Nasus")!, 13, [_items.ByRiotId(3078)!]);
        List<(double Time, string Source)> Chase(AbilityRanks ranks)
        {
            var recorder = new Recorder(_kits.NewFight(us.Champion)!);
            FightSimulator.Run(new FightSetup(us, Target(), ranks, MaxSeconds: 6, Sustained: true, StartDistance: 700,
                AttackUptime: 0.4), recorder);
            return recorder.Hits;
        }

        var withered = Chase(new AbilityRanks(5, 1, 0, 0));
        var kited = Chase(new AbilityRanks(5, 0, 0, 0));

        Assert.IsGreaterThan(
            kited.Count(h => h.Source == FightSimulator.Attacks),
            withered.Count(h => h.Source == FightSimulator.Attacks),
            "Caitlyn can't walk away from him while withered, so he lands attacks a chase would lose.");
    }

    [TestMethod]
    public void ViStaysOnHerTargetAfterEveryDashAndKnockUp()
    {
        var us = new ChampionState(_champions.ByName("Vi")!, 13, [_items.ByRiotId(3071)!]);
        FightResult Chase(double contact) => FightSimulator.Run(
            new FightSetup(us, Target(), new AbilityRanks(5, 3, 1, 2), MaxSeconds: 10, Sustained: true, AttackUptime: 0.4, DashContactSeconds: contact),
            _kits.NewFight(us.Champion));

        var kited = Chase(0);
        var stuck = Chase(1);

        Assert.IsGreaterThan(kited.Attacks, stuck.Attacks, "Her Q and R put her back on a kiting target.");
        Assert.AreEqual(1.3 + 0.25, kited.Disabled, 0.3, "R's knock-up and Q's knock-back lock the target.");
    }

    [TestMethod]
    public void AChampionWalksInFromRangeBeforeItAttacks()
    {
        var us = new ChampionState(_champions.ByName("Nasus")!, 13, [_items.ByRiotId(3078)!]);
        var recorder = new Recorder(_kits.NewFight(us.Champion)!);
        FightSimulator.Run(new FightSetup(us, Target(), new AbilityRanks(1, 0, 1, 0), MaxSeconds: 10, Sustained: true, StartDistance: 800), recorder);

        var walk = (800 - us.Stats.AttackRange) / us.Stats.MoveSpeed;
        var firstAttack = recorder.Hits.First(h => h.Source == FightSimulator.Attacks).Time;
        var firstE = recorder.Hits.First(h => h.Source == SupportedChampions.Nasus.NasusKit.SpiritFire).Time;

        Assert.IsGreaterThanOrEqualTo(walk - 2 * Modeling.Simulation.Fight.Step, firstAttack, "No attack before he is in range.");
        Assert.IsLessThan(firstAttack, firstE, "Spirit Fire reaches farther than his attack, so it goes out on the way in.");
    }

    [TestMethod]
    public void AChaseLandsFewerAttacks()
    {
        var us = new ChampionState(_champions.ByName("Vi")!, 13, [_items.ByRiotId(3071)!]);
        FightResult Chase(double uptime) =>
            FightSimulator.Run(new FightSetup(us, Target(), AbilityRanks.None, MaxSeconds: 20, Sustained: true, AttackUptime: uptime));

        var standing = Chase(1).Attacks;
        var chasing = Chase(0.4).Attacks;

        Assert.AreEqual(0.4 * standing, chasing, 0.1 * standing, $"{chasing} attacks chasing, {standing} standing.");
    }
}
