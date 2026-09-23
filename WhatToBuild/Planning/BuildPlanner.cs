using System.Diagnostics;
using WhatToBuild.Data;
using WhatToBuild.Forecasting;
using WhatToBuild.Modeling;

namespace WhatToBuild.Planning;

public sealed record PlanStep(
    Item Item,
    double At,
    double Spread,
    int Cost,
    IReadOnlyList<Item> Before,
    IReadOnlyList<Item> After,
    Item? Sold,
    IReadOnlyDictionary<Guid, double> StacksBefore,
    IReadOnlyDictionary<Guid, double> StacksAfter);

public sealed record CandidateValue(Item Item, double At, double Value, double Gain);

public sealed class BuildPlan
{
    public required IReadOnlyList<PlanStep> Steps { get; init; }

    public required double Value { get; init; }

    public required double Horizon { get; init; }

    public required IReadOnlyList<CandidateValue> Candidates { get; init; }

    public required int Evaluations { get; init; }

    public required double Milliseconds { get; init; }

    public required bool TimedOut { get; init; }

    public required bool KeptPreviousTarget { get; init; }

    public required string Stage { get; init; }

    public required EvaluationMode Mode { get; init; }

    public required bool Cancelled { get; init; }

    /// <summary>The order was already chosen by walking the orders of the set; nothing downstream should reorder it.</summary>
    public bool Ordered { get; init; }
}

public interface IPlanControl
{
    bool ShouldStop();

    void Tick();
}

public sealed class BuildPlanner
{
    private readonly BuildEvaluator _evaluator;
    private readonly BuildContext _context;
    private readonly ModelSettings.PlannerSettings _settings;
    private readonly HashSet<Guid> _components;
    private const double SpikeHorizonSeconds = 45 * 60;

    private readonly Dictionary<int, IReadOnlyList<(double From, double To)>> _spikeWindows = new();
    private ModelSettings.PlanStage _stage = new();
    private IPlanControl? _control;
    private bool _cancelled;

    public BuildPlanner(BuildEvaluator evaluator)
    {
        _evaluator = evaluator;
        _context = evaluator.Context;
        _settings = _context.Settings.Planner;
        _components = _context.Items.All.Where(i => !IsUpgradedBoots(i)).SelectMany(i => i.BuildPath).ToHashSet();
    }

    public IReadOnlyList<Item> CandidatePool() =>
        _context.Items.All
            .Where(i => i.Cost >= 900 && !_components.Contains(i.Id) || IsBasicBoots(i))
            .Where(i => !IsUpgradedBoots(i))
            .Where(i => !i.Groups.Any(g => g is "GoldItems" or "DoransItems" or "HuntersTalismanGroup" or "GuardianItems" or "Potion" or "TheBlackSpear"))
            .OrderBy(i => i.RiotId)
            .ToList();

    private static bool IsBasicBoots(Item item) => item.Groups.Contains("Boots") && item.BuildPath.Count == 0;

    private static bool IsAnyBoots(Item item) => item.Groups.Contains("Boots");

    private List<Node> BestBoots(List<Node> nodes, double horizon) =>
        nodes.Where(n => IsAnyBoots(n.Item!)).OrderByDescending(n => Value(n, horizon)).Take(_settings.BootsLines).ToList();

    /// <summary>
    /// Which items to own, decided on what the finished build is worth rather than on the way
    /// there: among the builds that reached full depth, the one whose inventory fights best,
    /// all measured at the same moment so none is flattered by finishing sooner. What order to
    /// buy them in is a separate question, answered by what each is worth when it lands.
    /// </summary>
    private Node? BestSet(List<Node> all, double horizon)
    {
        if (all.Count == 0)
        {
            return null;
        }

        var byValue = all.MaxBy(n => Value(n, horizon))!;
        if (_stage.Depth <= 1)
        {
            return byValue;
        }

        var deepest = all.Max(n => n.Depth);
        var finished = all.Where(n => n.Depth == deepest && n.Scored).ToList();
        if (finished.Count < 2)
        {
            return byValue;
        }

        var when = finished.Max(n => n.Time);

        return finished.MaxBy(n => _evaluator.Evaluate(n.Inventory, when, StacksAt(n.BoughtAt, when), _stage.Mode, _context.Form).Score) ?? byValue;
    }

    /// <summary>
    /// Everyone buys boots. Their place in the build is left to what they are worth; the only
    /// rule is that a build cannot end without them, so a build that never bought them gets
    /// the pair that scores best appended.
    /// </summary>
    private Node WithBoots(Node best, double horizon)
    {
        if (!_settings.RequireBoots || best.Item is null || best.Inventory.Any(IsAnyBoots))
        {
            return best;
        }

        var options = CandidatePool()
            .Where(IsAnyBoots)
            .Select(item => Child(best, item, double.MaxValue))
            .OfType<Node>()
            .ToList();

        foreach (var option in options)
        {
            Score(option);
        }

        return options.Count == 0 ? best : options.MaxBy(n => Value(n, Math.Max(horizon, n.Time + 1)))!;
    }

    private bool IsUpgradedBoots(Item item) =>
        item.Groups.Contains("Boots")
        && item.BuildPath.Any(id => _context.Items.ById(id) is { } part && part.Groups.Contains("Boots") && part.BuildPath.Count > 0);

    /// <param name="horizon">Score to this moment instead of the build's own end, so that two
    /// sequences that finish at different times can be compared on the same footing.</param>
    public BuildPlan Replay(IReadOnlyList<Item> sequence, ModelSettings.PlanStage? stage = null, double? horizon = null)
    {
        _stage = stage ?? _settings.Stages.FirstOrDefault() ?? new ModelSettings.PlanStage();
        _control = null;
        _cancelled = false;

        var watch = Stopwatch.StartNew();
        var (last, _) = Walk(sequence);

        return new BuildPlan
        {
            Steps = Steps(last),
            Value = Value(last, horizon ?? last.Time + _settings.MinHorizonSeconds),
            Horizon = last.Time,
            Candidates = [],
            Evaluations = _evaluator.EvaluationCount,
            Milliseconds = watch.Elapsed.TotalMilliseconds,
            TimedOut = false,
            KeptPreviousTarget = true,
            Stage = _stage.Name,
            Mode = _stage.Mode,
            Cancelled = false,
        };
    }

    private Node Root()
    {
        var now = _context.Now;
        return new Node
        {
            Time = now,
            Earned = _context.Forecaster.EarnedAt(_context.Me, now),
            GoldLeft = _context.State.CurrentGold,
            Inventory = _context.Owned.ToList(),
            BoughtAt = new Dictionary<Guid, double>(),
        };
    }

    /// <summary>Buys the items in this order, as soon as each is affordable. Stops at the first one that cannot be bought.</summary>
    private (Node Last, int Bought) Walk(IReadOnlyList<Item> sequence)
    {
        var node = Root();
        var bought = 0;

        foreach (var item in sequence)
        {
            if (ItemRules.Slots(node.Inventory) >= ItemRules.InventorySlots)
            {
                node.SellCandidate ??= LeastValuable(node);
            }

            if (Child(node, item, double.MaxValue) is not { } child)
            {
                break;
            }

            Score(child);
            node = child;
            bought++;
        }

        return (node, bought);
    }

    public BuildPlan Plan(Item? previousTarget = null, ModelSettings.PlanStage? stage = null, IPlanControl? control = null)
    {
        _stage = stage ?? _settings.Stages.FirstOrDefault() ?? new ModelSettings.PlanStage();
        _control = control;
        _cancelled = false;

        var watch = Stopwatch.StartNew();
        var budget = _stage.BudgetMilliseconds;

        if (SetFits())
        {
            return PlanSet(previousTarget, watch, budget);
        }
        var now = _context.Now;
        var me = _context.Me;
        var forecaster = _context.Forecaster;

        var horizon = Math.Max(
            now + _settings.MinHorizonSeconds,
            forecaster.TimeToEarn(me, forecaster.EarnedAt(me, now) + (_stage.HorizonGold ?? _settings.HorizonGold)));

        var root = new Node
        {
            Time = now,
            Earned = forecaster.EarnedAt(me, now),
            GoldLeft = _context.State.CurrentGold,
            Inventory = _context.Owned.ToList(),
            BoughtAt = new Dictionary<Guid, double>(),
        };

        var timedOut = false;
        var screened = Prescreen(root, horizon, previousTarget, watch, budget, ref timedOut);
        var firstLayer = Score(screened.Take(_stage.ScreenCount)
            .Concat(screened.Where(n => n.Item!.Id == previousTarget?.Id))
            .Concat(screened.Where(n => IsAnyBoots(n.Item!)).OrderByDescending(n => n.CheapValue).Take(_settings.BootsLines))
            .Distinct()
            .ToList(), watch, budget, ref timedOut);

        var candidates = firstLayer
            .OrderByDescending(n => Value(n, horizon))
            .Select(n => new CandidateValue(n.Item!, n.Time, Value(n, horizon), n.Gain))
            .Concat(screened.Where(n => !firstLayer.Contains(n)).Select(n => new CandidateValue(n.Item!, n.Time, n.CheapValue, n.Gain)))
            .ToList();

        var branching = firstLayer.OrderByDescending(n => Value(n, horizon)).Take(_stage.Branching).Select(n => n.Item!).ToList();
        if (previousTarget is not null && firstLayer.Any(n => n.Item!.Id == previousTarget.Id) && branching.All(b => b.Id != previousTarget.Id))
        {
            branching.Add(previousTarget);
        }

        var all = new List<Node>(firstLayer);
        var beam = Beam(firstLayer, horizon, previousTarget);
        // Boots are compulsory, so a boots line always stays in the beam to be compared on value.
        beam.AddRange(BestBoots(firstLayer, horizon).Where(n => !beam.Contains(n)));

        foreach (var boots in BestBoots(firstLayer, horizon).Select(n => n.Item!))
        {
            if (branching.All(b => b.Id != boots.Id))
            {
                branching.Add(boots);
            }
        }

        for (var layer = 2; layer <= _stage.Depth + 1 && beam.Count > 0 && !Stop(watch, budget); layer++)
        {
            var open = beam.Where(p => p.Depth < _stage.Depth).ToList();
            foreach (var parent in open.Where(p => ItemRules.Slots(p.Inventory) >= ItemRules.InventorySlots))
            {
                parent.SellCandidate ??= LeastValuable(parent);
            }

            var children = Score(open.SelectMany(p => branching.Select(item => Child(p, item, horizon))).OfType<Node>().ToList(), watch, budget, ref timedOut);
            all.AddRange(children);
            beam = Beam(children, horizon, null);
        }

        var best = BestSet(all, horizon) ?? root;
        var kept = false;

        if (previousTarget is not null && best != root && First(best).Item!.Id != previousTarget.Id)
        {
            var alternative = all.Where(n => First(n).Item!.Id == previousTarget.Id).MaxBy(n => Value(n, horizon));
            if (alternative is not null
                && Value(alternative, horizon) >= Value(best, horizon) - _settings.KeepMargin * Math.Abs(Value(best, horizon)))
            {
                best = alternative;
                kept = true;
            }
        }

        best = WithBoots(best, horizon);

        if (best != root && Value(best, horizon) <= 0)
        {
            best = root;
        }

        return new BuildPlan
        {
            Steps = Steps(best),
            Value = Value(best, horizon),
            Horizon = horizon,
            Candidates = candidates,
            Evaluations = _evaluator.EvaluationCount,
            Milliseconds = watch.Elapsed.TotalMilliseconds,
            TimedOut = timedOut && !_cancelled,
            KeptPreviousTarget = kept,
            Stage = _stage.Name,
            Mode = _stage.Mode,
            Cancelled = _cancelled,
        };
    }

    private const int ExhaustiveOrderItems = 4;

    private bool OwnsShoes => _context.Owned.Any(i => IsAnyBoots(i) && !IsBasicBoots(i));

    private bool NeedsShoes => _settings.RequireBoots && !OwnsShoes;

    /// <summary>The core and its shoes fit in the free slots, so nothing has to be sold for it.</summary>
    private bool SetFits() =>
        ItemRules.Slots(_context.Owned) + _stage.Depth + (NeedsShoes && !_context.Owned.Any(IsAnyBoots) ? 1 : 0) <= ItemRules.InventorySlots;

    /// <summary>Owned items with these bought on top, components folded into what they build; null if that is not a legal inventory.</summary>
    private (List<Item> Inventory, double Cost)? Holding(IEnumerable<Item> set)
    {
        var inventory = _context.Owned.ToList();
        var cost = 0.0;
        foreach (var item in set)
        {
            var purchase = ComponentPurchase.Plan(item, inventory, double.MaxValue, _context.Items);
            if (purchase.Buy.Count == 0 || !ItemRules.IsLegal(purchase.InventoryAfter))
            {
                return null;
            }

            inventory = purchase.InventoryAfter.ToList();
            cost += purchase.Cost;
        }

        return ItemRules.Slots(inventory) <= ItemRules.InventorySlots ? (inventory, cost) : null;
    }

    /// <summary>
    /// The build the slider asks for, in two separate questions. First which items: every set of
    /// the core's size (plus shoes) is judged on how well the finished inventory fights, all at
    /// one moment, the time a core of that size is done, so a set is never preferred for being
    /// cheap or for having a piece affordable right now. Then in which order: every order of that
    /// set is walked, each item bought as soon as the gold allows, and the order that is worth
    /// most over the game wins. Only the order is about timing.
    /// </summary>
    private BuildPlan PlanSet(Item? previousTarget, Stopwatch watch, double budget)
    {
        var now = _context.Now;
        var me = _context.Me;
        var forecaster = _context.Forecaster;
        var mode = _stage.Mode;
        var timedOut = false;

        var pool = CandidatePool().Where(i => !IsAnyBoots(i)).ToList();
        var shoes = NeedsShoes ? CandidatePool().Where(i => IsAnyBoots(i) && !IsBasicBoots(i)).ToList() : [];

        // A core whose items are all built still wants its shoes, and only them.
        var depth = shoes.Count > 0 ? Math.Max(0, _stage.Depth) : Math.Max(1, _stage.Depth);

        var legendary = pool.Where(i => i.Cost >= 2000).Select(i => (double)i.Cost).DefaultIfEmpty(3000).Average();
        var shoeCost = shoes.Select(i => (double)i.Cost).DefaultIfEmpty(0).Average();
        var need = depth * legendary + shoeCost - _context.State.CurrentGold;
        var when = Math.Max(now + _settings.MinHorizonSeconds / 2, forecaster.TimeToEarn(me, forecaster.EarnedAt(me, now) + Math.Max(0, need)));

        // Stacking items are assumed bought halfway there: which of them comes first is the
        // order's question, not the set's.
        IReadOnlyDictionary<Guid, double> Stacks(IEnumerable<Item> inventory) =>
            StacksAt(inventory.Where(i => i.Stacking is not null && _context.Owned.All(o => o.Id != i.Id))
                .DistinctBy(i => i.Id).ToDictionary(i => i.Id, _ => now + (when - now) / 2), when);

        var baseline = _evaluator.Evaluate(_context.Owned, when, null, mode).Score;

        // Components you already hold that the set leaves unused are sold at a loss: a set
        // that abandons an item you have started pays for it, at the set's own score per gold.
        var stranded = StrandedGold(_context.Owned);

        double? Strength(IReadOnlyList<Item> set, EvaluationMode how)
        {
            if (Holding(set) is not var (inventory, cost))
            {
                return null;
            }

            var gain = _evaluator.Evaluate(inventory, when, Stacks(inventory), how).Score - baseline;
            var left = StrandedGold(inventory);
            if (stranded > 0 && left > 0 && gain > 0)
            {
                gain -= (1 - _settings.SellRefund) * left * gain / Math.Max(1, cost + left);
            }

            return gain;
        }

        Item? BestShoes(IReadOnlyList<Item> set, EvaluationMode how) =>
            shoes.Select(s => (Shoes: s, Value: Strength([.. set, s], how)))
                .Where(x => x.Value is not null)
                .MaxBy(x => x.Value!.Value).Shoes;

        // Shoes for the search: the best pair on their own. The final set gets its own pick below.
        var provisional = shoes.Count > 0 ? BestShoes([], EvaluationMode.Cheap) : null;
        IReadOnlyList<Item> WithShoes(IEnumerable<Item> set, Item? pair) => pair is null ? set.ToList() : [.. set, pair];

        var screened = new List<(Item Item, double Gain)>();
        foreach (var item in pool)
        {
            if (screened.Count >= _settings.MinScreened && Stop(watch, budget * _settings.PrescreenShare) && item.Id != previousTarget?.Id)
            {
                timedOut = true;
                continue;
            }

            if (Strength(WithShoes([item], provisional), EvaluationMode.Cheap) is { } gain)
            {
                screened.Add((item, gain));
            }
        }

        var candidates = screened.OrderByDescending(s => s.Gain).ToList();
        var branch = candidates.Take(_stage.ScreenCount).Select(s => s.Item).ToList();
        if (previousTarget is not null && branch.All(i => i.Id != previousTarget.Id) && candidates.Any(c => c.Item.Id == previousTarget.Id))
        {
            branch.Add(previousTarget);
        }

        // Grow sets one item at a time, keeping the strongest few of each size.
        var beam = new List<(List<Item> Set, double Value)> { ([], 0) };
        var finished = new List<(List<Item> Set, double Value)>();
        for (var size = 1; size <= depth && beam.Count > 0; size++)
        {
            var seen = new HashSet<string>();
            var grown = new List<(List<Item> Set, double Value)>();

            foreach (var (set, _) in beam)
            {
                foreach (var item in branch.Where(i => set.All(s => s.Id != i.Id)))
                {
                    List<Item> next = [.. set, item];
                    if (!seen.Add(string.Join(',', next.Select(i => i.RiotId).Order())))
                    {
                        continue;
                    }

                    if (grown.Count >= _settings.MinScored && Stop(watch, budget))
                    {
                        timedOut = true;
                        break;
                    }

                    if (Strength(WithShoes(next, provisional), mode) is { } value)
                    {
                        grown.Add((next, value));
                    }
                }
            }

            if (grown.Count == 0)
            {
                break;
            }

            finished = grown;
            var kept = grown.OrderByDescending(g => g.Value).Take(_stage.BeamWidth).ToList();
            if (previousTarget is not null && kept.All(k => k.Set.All(i => i.Id != previousTarget.Id))
                && grown.Where(g => g.Set.Any(i => i.Id == previousTarget.Id)).OrderByDescending(g => g.Value).FirstOrDefault() is { Set: not null } loyal)
            {
                kept.Add(loyal);
            }

            beam = kept;
        }

        if (depth == 0)
        {
            finished = [([], 0)];
        }

        if (_cancelled || finished.Count == 0)
        {
            return Empty(watch, timedOut, candidates, when);
        }

        var best = finished.MaxBy(f => f.Value);
        var keptTarget = false;
        var margin = _settings.KeepMargin;

        // Loyalty to what you are saving for: a set without it has to be clearly stronger.
        if (previousTarget is not null && best.Set.All(i => i.Id != previousTarget.Id)
            && finished.Where(f => f.Set.Any(i => i.Id == previousTarget.Id)).OrderByDescending(f => f.Value).FirstOrDefault() is { Set: not null } holding
            && holding.Value >= best.Value - margin * Math.Abs(best.Value))
        {
            best = holding;
            keptTarget = true;
        }

        var items = best.Set.ToList();
        if (shoes.Count > 0 && BestShoes(items, mode) is { } pair)
        {
            items.Add(pair);
        }

        var horizon = now + _settings.CompareSeconds;
        var orders = Orders(items, horizon, watch, budget, ref timedOut);
        if (_cancelled || orders.Count == 0)
        {
            return Empty(watch, timedOut, candidates, when);
        }

        var first = orders.MaxBy(o => o.Value);
        if (previousTarget is not null && first.Order[0].Id != previousTarget.Id
            && orders.Where(o => o.Order[0].Id == previousTarget.Id).OrderByDescending(o => o.Value).FirstOrDefault() is { Order: not null } saving
            && saving.Value >= first.Value - margin * Math.Abs(first.Value))
        {
            first = saving;
            keptTarget = true;
        }

        return new BuildPlan
        {
            Steps = Steps(first.Last),
            Value = first.Value,
            Horizon = horizon,
            Candidates = candidates.Select(c => new CandidateValue(c.Item, when, c.Gain, c.Gain)).ToList(),
            Evaluations = _evaluator.EvaluationCount,
            Milliseconds = watch.Elapsed.TotalMilliseconds,
            TimedOut = timedOut && !_cancelled,
            KeptPreviousTarget = keptTarget,
            Stage = _stage.Name,
            Mode = _stage.Mode,
            Cancelled = false,
            Ordered = true,
        };
    }

    /// <summary>
    /// The orders worth comparing: all of them for a small core, otherwise the best-looking one
    /// improved by swapping neighbours and moving each item to the front.
    /// </summary>
    private List<(List<Item> Order, Node Last, double Value)> Orders(List<Item> items, double horizon, Stopwatch watch, double budget, ref bool timedOut)
    {
        var walked = new Dictionary<string, (List<Item> Order, Node Last, double Value)>();

        bool Try(List<Item> order)
        {
            var key = string.Join(',', order.Select(i => i.RiotId));
            if (walked.ContainsKey(key))
            {
                return true;
            }

            if (walked.Count > 0 && Stop(watch, budget * 2))
            {
                return false;
            }

            var (last, bought) = Walk(order);
            if (bought == order.Count)
            {
                walked[key] = (order, last, Value(last, horizon));
            }

            return true;
        }

        if (items.Count <= ExhaustiveOrderItems)
        {
            foreach (var order in Permutations(items))
            {
                if (!Try(order))
                {
                    timedOut = true;
                    break;
                }
            }

            return walked.Values.ToList();
        }

        var current = items.ToList();
        Try(current);
        for (var pass = 0; pass < _settings.ReorderPasses + 1; pass++)
        {
            var improved = false;
            var moves = new List<List<Item>>();
            for (var i = 0; i + 1 < current.Count; i++)
            {
                var swapped = current.ToList();
                (swapped[i], swapped[i + 1]) = (swapped[i + 1], swapped[i]);
                moves.Add(swapped);
            }

            for (var i = 1; i < current.Count; i++)
            {
                moves.Add([current[i], .. current.Where((_, j) => j != i)]);
            }

            foreach (var move in moves)
            {
                if (!Try(move))
                {
                    timedOut = true;
                    return walked.Values.ToList();
                }
            }

            var bestNow = walked.Values.MaxBy(w => w.Value);
            if (bestNow.Order is not null && !bestNow.Order.SequenceEqual(current))
            {
                current = bestNow.Order;
                improved = true;
            }

            if (!improved)
            {
                break;
            }
        }

        return walked.Values.ToList();
    }

    private static IEnumerable<List<Item>> Permutations(List<Item> items)
    {
        if (items.Count <= 1)
        {
            yield return items.ToList();
            yield break;
        }

        for (var i = 0; i < items.Count; i++)
        {
            var rest = items.Where((_, j) => j != i).ToList();
            foreach (var tail in Permutations(rest))
            {
                yield return [items[i], .. tail];
            }
        }
    }

    private BuildPlan Empty(Stopwatch watch, bool timedOut, List<(Item Item, double Gain)> candidates, double when) => new()
    {
        Steps = [],
        Value = 0,
        Horizon = _context.Now,
        Candidates = candidates.Select(c => new CandidateValue(c.Item, when, c.Gain, c.Gain)).ToList(),
        Evaluations = _evaluator.EvaluationCount,
        Milliseconds = watch.Elapsed.TotalMilliseconds,
        TimedOut = timedOut && !_cancelled,
        KeptPreviousTarget = false,
        Stage = _stage.Name,
        Mode = _stage.Mode,
        Cancelled = _cancelled,
    };

    public IReadOnlyDictionary<Guid, double> StacksAt(IReadOnlyDictionary<Guid, double> boughtAt, double time)
    {
        var stacks = new Dictionary<Guid, double>();

        foreach (var (id, at) in boughtAt)
        {
            if (_context.Items.ById(id)?.Stacking is { } stacking)
            {
                var minutes = (time + _settings.StackLookaheadSeconds - at) / 60;
                stacks[id] = _context.StacksAfter(stacking, minutes);
            }
        }

        return stacks;
    }

    private List<Node> Beam(List<Node> nodes, double horizon, Item? keep)
    {
        var beam = nodes.OrderByDescending(n => Value(n, horizon)).Take(_stage.BeamWidth).ToList();

        if (keep is not null && beam.All(n => n.Item!.Id != keep.Id)
            && nodes.Where(n => n.Item!.Id == keep.Id).MaxBy(n => Value(n, horizon)) is { } kept)
        {
            beam.Add(kept);
        }

        return beam;
    }

    private List<Node> Prescreen(Node root, double horizon, Item? previousTarget, Stopwatch watch, double budget, ref bool timedOut)
    {
        if (ItemRules.Slots(root.Inventory) >= ItemRules.InventorySlots)
        {
            root.SellCandidate = LeastValuable(root);
        }

        var specs = CandidatePool().Select(item => Child(root, item, horizon)).OfType<Node>().ToList();
        var done = 0;

        foreach (var spec in specs)
        {
            if (done >= _settings.MinScreened
                && Stop(watch, budget * _settings.PrescreenShare)
                && spec.Item!.Id != previousTarget?.Id)
            {
                timedOut = true;
                continue;
            }

            done++;

            var baseline = _evaluator.Evaluate(_context.Owned, spec.Time, null, EvaluationMode.Cheap);
            var after = _evaluator.Evaluate(spec.Inventory, spec.Time, StacksAt(spec.BoughtAt, spec.Time), EvaluationMode.Cheap);
            spec.Gain = after.Score - baseline.Score;
            spec.CheapValue = spec.Gain * Discount(spec.Time, horizon);
            spec.Prescreened = true;
        }

        return specs.Where(s => s.Prescreened).OrderByDescending(s => s.CheapValue).ToList();
    }

    private List<Node> Score(List<Node> specs, Stopwatch watch, double budget, ref bool timedOut)
    {
        var done = 0;

        foreach (var spec in specs)
        {
            if (done >= _settings.MinScored && Stop(watch, budget))
            {
                timedOut = true;
                break;
            }

            done++;
            Score(spec);
        }

        return specs.Where(s => s.Scored).Where(s => s.Sold is null || s.ReplacementGain >= ReplaceMargin(s.Sold)).ToList();
    }

    private double ReplaceMargin(Item sold) =>
        sold.Cost >= 2000 && !_components.Contains(sold.Id) ? _settings.ReplaceFinishedMargin : _settings.ReplaceMargin;

    private Node? Child(Node parent, Item item, double horizon)
    {
        var purchase = ComponentPurchase.Plan(item, parent.Inventory, double.MaxValue, _context.Items);
        if (purchase.Buy.Count == 0 || !ItemRules.IsLegal(purchase.InventoryAfter))
        {
            return null;
        }


        var after = purchase.InventoryAfter.ToList();
        Item? sold = null;
        var refund = 0.0;

        if (ItemRules.Slots(after) > ItemRules.InventorySlots)
        {
            sold = parent.SellCandidate;
            if (sold is null || !after.Remove(sold) || ItemRules.Slots(after) > ItemRules.InventorySlots)
            {
                return null;
            }

            refund = sold.Cost * _settings.SellRefund;
        }

        var forecaster = _context.Forecaster;
        var need = purchase.Cost - refund - parent.GoldLeft;
        var bonus = parent.BonusGoldPerSecond;
        var time = need <= 0 ? parent.Time : _context.NextRecall(TimeToAfford(parent, need, bonus), _context.Now);
        if (time > horizon)
        {
            return null;
        }

        var earned = forecaster.EarnedAt(_context.Me, time);
        var extra = bonus * (time - parent.Time);
        var boughtAt = new Dictionary<Guid, double>(parent.BoughtAt);
        if (item.Stacking is not null)
        {
            boughtAt[item.Id] = time;
        }

        if (sold is not null)
        {
            boughtAt.Remove(sold.Id);
        }

        return new Node
        {
            Parent = parent,
            Item = item,
            Sold = sold,
            Cost = purchase.Cost,
            Time = time,
            Earned = earned,
            GoldLeft = Math.Max(0, parent.GoldLeft + (earned - parent.Earned) + extra + refund - purchase.Cost),
            BonusGoldPerSecond = Math.Max(0, GoldIncome(after) - GoldIncome(_context.Owned)),
            Inventory = after,
            BoughtAt = boughtAt,
            Depth = parent.Depth + (IsAnyBoots(item) ? 0 : 1),
            Closed = parent.Closed + parent.Credit + parent.Gain * Discount(parent.Time, time),
        };
    }

    private double TimeToAfford(Node parent, double need, double bonus)
    {
        var forecaster = _context.Forecaster;
        var late = forecaster.TimeToEarn(_context.Me, parent.Earned + need);
        if (bonus <= 0)
        {
            return late;
        }

        double low = parent.Time, high = late;
        for (var i = 0; i < 30 && high - low > 0.5; i++)
        {
            var mid = (low + high) / 2;
            var have = forecaster.EarnedAt(_context.Me, mid) - parent.Earned + bonus * (mid - parent.Time);
            if (have >= need)
            {
                high = mid;
            }
            else
            {
                low = mid;
            }
        }

        return high;
    }

    public double GoldIncome(IEnumerable<Item> inventory)
    {
        var me = _context.Me;
        var minutes = Math.Max(1, _context.Now / 60);
        var killsPerMinute = _context.Now >= 300 ? me.Kills / minutes : (double?)null;

        return inventory
            .Where(i => i.Stacking is not null)
            .DistinctBy(i => i.Id)
            .Sum(i =>
            {
                var stacking = i.Stacking!;
                var perMinute = stacking.Per.Contains("champion kill", StringComparison.OrdinalIgnoreCase) && killsPerMinute is { } observed
                    ? observed
                    : stacking.StacksPerMinute * (_context.Champion.IsRanged ? stacking.RangedMultiplier : 1);

                return stacking.Gains.Where(g => g.Stat == Data.Stats.Gold).Sum(g => g.Amount) * perMinute / 60;
            });
    }

    private bool Stop(Stopwatch watch, double budget)
    {
        _control?.Tick();
        _cancelled |= _control?.ShouldStop() ?? false;
        return _cancelled || watch.Elapsed.TotalMilliseconds > budget;
    }

    private void Score(Node node)
    {
        var baseline = _evaluator.Evaluate(_context.Owned, node.Time, null, _stage.Mode);
        var after = _evaluator.Evaluate(node.Inventory, node.Time, StacksAt(node.BoughtAt, node.Time), _stage.Mode);
        node.Gain = after.Score - baseline.Score;

        var pathCost = 0.0;
        for (var step = node; step.Parent is not null; step = step.Parent)
        {
            pathCost += step.Cost;
        }

        var stranded = StrandedGold(node.Inventory) - StrandedGold(_context.Owned);
        if (stranded != 0 && node.Gain > 0 && pathCost > 0)
        {
            node.Gain -= (1 - _settings.SellRefund) * stranded * node.Gain / pathCost;
        }

        // Saving for an item is not sitting on gold: its components are bought on the way and
        // fight for you before it completes. Their worth grows with the gold put in, to a share
        // of what the finished item adds. Without this a cheap item bought whole always looked
        // better first, only because it was the one thing that counted before a big one landed.
        if (node.Parent is { } before && node.Time > before.Time && _settings.ComponentValueShare > 0)
        {
            node.Credit = _settings.ComponentValueShare * Math.Max(0, node.Gain - before.Gain) * Ramp(before.Time, node.Time);
        }

        if (node.Sold is not null)
        {
            var parent = node.Parent!;
            var kept = _evaluator.Evaluate(parent.Inventory, node.Time, StacksAt(parent.BoughtAt, node.Time), _stage.Mode);
            node.ReplacementGain = after.Score - kept.Score;
        }

        node.Scored = true;
    }

    private double StrandedGold(IEnumerable<Item> inventory) =>
        inventory.Where(i => _components.Contains(i.Id) && !IsBasicBoots(i)).Sum(i => i.Cost);

    private Item? LeastValuable(Node node)
    {
        var sellable = node.Inventory
            .Where(i => !i.Groups.Contains("HuntersTalismanGroup") && !i.Groups.Contains("Boots"))
            .DistinctBy(i => i.Id)
            .ToList();

        return sellable
            .Select(i =>
            {
                var without = node.Inventory.ToList();
                without.Remove(i);
                return (Item: i, Score: _evaluator.Evaluate(without, node.Time, StacksAt(node.BoughtAt, node.Time), _stage.Mode).Score);
            })
            .MaxBy(x => x.Score)
            .Item;
    }

    private double Value(Node node, double horizon) => node.Closed + node.Credit + node.Gain * Discount(node.Time, horizon);

    private double Discount(double from, double to)
    {
        var plain = Plain(from, to);
        if (_settings.SpikeWeight <= 0 || to <= from)
        {
            return plain;
        }

        var inSpikes = SpikeWindows(to).Sum(w => Plain(Math.Max(from, w.From), Math.Min(to, w.To)));

        return plain + _settings.SpikeWeight * inSpikes;
    }

    /// <summary>The discounted weight of a value that grows evenly from nothing at <paramref name="from"/> to all of it at <paramref name="to"/>.</summary>
    private double Ramp(double from, double to)
    {
        const int slices = 8;
        var width = (to - from) / slices;
        var total = 0.0;

        for (var i = 0; i < slices; i++)
        {
            var start = from + i * width;
            total += (i + 0.5) / slices * Discount(start, start + width);
        }

        return total;
    }

    private double Plain(double from, double to)
    {
        if (to <= from)
        {
            return 0;
        }

        var tau = _settings.DiscountSeconds;
        var now = _context.Now;
        return tau * (Math.Exp(-(from - now) / tau) - Math.Exp(-(to - now) / tau));
    }

    /// <summary>
    /// The minutes after an enemy finishes an item, merged. Being strong inside one of these is
    /// what decides whether their spike is a fight you lose, so value there counts for more.
    /// </summary>
    private IReadOnlyList<(double From, double To)> SpikeWindows(double until)
    {
        var key = (int)Math.Round(Math.Min(until, _context.Now + SpikeHorizonSeconds) / 60);
        if (_spikeWindows.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var window = _settings.SpikeWindowSeconds;
        var merged = new List<(double From, double To)>();

        foreach (var spike in _evaluator.SpikesBetween(_context.Now, _context.Now + SpikeHorizonSeconds))
        {
            var next = (From: spike.Time, To: spike.Time + window);
            if (merged.Count > 0 && next.From <= merged[^1].To)
            {
                merged[^1] = (merged[^1].From, Math.Max(merged[^1].To, next.To));
            }
            else
            {
                merged.Add(next);
            }
        }

        _spikeWindows[key] = merged;
        return merged;
    }

    private static Node First(Node node)
    {
        while (node.Parent is { Parent: not null } parent)
        {
            node = parent;
        }

        return node;
    }

    private List<PlanStep> Steps(Node best)
    {
        var chain = new List<Node>();
        for (var node = best; node.Parent is not null; node = node.Parent)
        {
            chain.Add(node);
        }

        chain.Reverse();

        return chain
            .Select(n => new PlanStep(
                n.Item!,
                n.Time,
                _context.Forecaster.Spread(n.Time),
                n.Cost,
                n.Parent!.Inventory,
                n.Inventory,
                n.Sold,
                StacksAt(n.Parent.BoughtAt, n.Time),
                StacksAt(n.BoughtAt, n.Time)))
            .ToList();
    }

    private sealed class Node
    {
        public Node? Parent { get; init; }

        public Item? Item { get; init; }

        public Item? Sold { get; init; }

        public int Cost { get; init; }

        public required double Time { get; init; }

        public required double Earned { get; init; }

        public required double GoldLeft { get; init; }

        public required List<Item> Inventory { get; init; }

        public required Dictionary<Guid, double> BoughtAt { get; init; }

        public int Depth { get; init; }

        public double Closed { get; init; }

        /// <summary>What the components bought while saving for this item were worth before it completed.</summary>
        public double Credit { get; set; }

        public double Gain { get; set; }

        public double ReplacementGain { get; set; }

        public bool Scored { get; set; }

        public bool Prescreened { get; set; }

        public double CheapValue { get; set; }

        public double BonusGoldPerSecond { get; init; }

        public Item? SellCandidate { get; set; }
    }
}

