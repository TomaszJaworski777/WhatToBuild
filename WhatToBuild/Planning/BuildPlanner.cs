using System.Diagnostics;
using WhatToBuild.Data;
using WhatToBuild.Forecasting;
using WhatToBuild.Modeling;

namespace WhatToBuild.Planning;

/// <summary>One purchase on the plan: what, when it completes, and the inventory around it.</summary>
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

/// <summary>A completed item scored as your very next purchase, whether or not it made the plan.</summary>
public sealed record CandidateValue(Item Item, double At, double Value, double Gain);

public sealed class BuildPlan
{
    public required IReadOnlyList<PlanStep> Steps { get; init; }

    public required double Value { get; init; }

    public required double Horizon { get; init; }

    /// <summary>Every candidate scored as the next purchase, best first.</summary>
    public required IReadOnlyList<CandidateValue> Candidates { get; init; }

    public required int Evaluations { get; init; }

    public required double Milliseconds { get; init; }

    public required bool TimedOut { get; init; }

    public required bool KeptPreviousTarget { get; init; }

    public required string Stage { get; init; }

    /// <summary>The evaluation mode the stage scored with; explanations reuse it so they cost nothing extra.</summary>
    public required EvaluationMode Mode { get; init; }

    public required bool Cancelled { get; init; }
}

/// <summary>Lets the caller stop a long planning stage and do small jobs while it runs.</summary>
public interface IPlanControl
{
    /// <summary>True when the result is no longer wanted (the game changed).</summary>
    bool ShouldStop();

    /// <summary>Called between evaluations; a chance to refresh gold-only parts of the advice.</summary>
    void Tick();
}

/// <summary>
/// Beam search over your next purchases. Each purchase is scored at the moment you are forecast to
/// afford it, against the enemies as they are forecast to be at that moment.
///
/// A plan's value is the score gain over your current items, integrated over time until a horizon a few
/// items away, with a discount so near purchases count most. Buying something earlier makes it count for
/// longer, so cheap high-impact items naturally go first, and an item that only pays off after the
/// horizon is worth nothing now. When the inventory is full, a purchase sells the item that contributes
/// least, and is only allowed if it beats keeping it by <c>replaceMargin</c>.
/// </summary>
public sealed class BuildPlanner
{
    private readonly BuildEvaluator _evaluator;
    private readonly BuildContext _context;
    private readonly ModelSettings.PlannerSettings _settings;
    private readonly HashSet<Guid> _components;
    private ModelSettings.PlanStage _stage = new();
    private IPlanControl? _control;
    private bool _cancelled;

    public BuildPlanner(BuildEvaluator evaluator)
    {
        _evaluator = evaluator;
        _context = evaluator.Context;
        _settings = _context.Settings.Planner;
        // A tier 2 pair of boots is a component of its tier 3 upgrade, but that upgrade is not a shop
        // purchase, so being part of one does not make the tier 2 pair a mere component.
        _components = _context.Items.All.Where(i => !IsUpgradedBoots(i)).SelectMany(i => i.BuildPath).ToHashSet();
    }

    /// <summary>Completed items the planner considers: everything you can finish, off-meta included.</summary>
    /// <remarks>
    /// Plain Boots are a candidate too, although they are a component: a 300 gold purchase that gets you
    /// around the map faster long before finished boots, which the planner should be able to suggest.
    /// </remarks>
    public IReadOnlyList<Item> CandidatePool() =>
        _context.Items.All
            .Where(i => i.Cost >= 900 && !_components.Contains(i.Id) || IsBasicBoots(i))
            .Where(i => !IsUpgradedBoots(i))
            .Where(i => !i.Groups.Any(g => g is "GoldItems" or "DoransItems" or "HuntersTalismanGroup" or "GuardianItems" or "Potion" or "TheBlackSpear"))
            .OrderBy(i => i.RiotId)
            .ToList();

    /// <param name="previousTarget">The item the last plan led with; kept unless something is clearly better.</param>
    /// <param name="stage">How hard to search. Defaults to the first (quick) stage in model.json.</param>
    /// <param name="control">Optional cancellation and between-evaluation callback.</param>
    private static bool IsBasicBoots(Item item) => item.Groups.Contains("Boots") && item.BuildPath.Count == 0;

    /// <summary>
    /// Tier 3 boots (Gunmetal Greaves, Swiftmarch, ...) upgrade a finished pair for no gold once your team
    /// earns Feats of Strength. They are not a shop decision, so they are never suggested; owned ones still count.
    /// </summary>
    private bool IsUpgradedBoots(Item item) =>
        item.Groups.Contains("Boots")
        && item.BuildPath.Any(id => _context.Items.ById(id) is { } part && part.Groups.Contains("Boots") && part.BuildPath.Count > 0);

    public BuildPlan Plan(Item? previousTarget = null, ModelSettings.PlanStage? stage = null, IPlanControl? control = null)
    {
        _stage = stage ?? _settings.Stages.FirstOrDefault() ?? new ModelSettings.PlanStage();
        _control = control;
        _cancelled = false;

        var watch = Stopwatch.StartNew();
        var budget = _stage.BudgetMilliseconds;
        var now = _context.Now;
        var me = _context.Me;
        var forecaster = _context.Forecaster;

        var horizon = Math.Max(
            now + _settings.MinHorizonSeconds,
            forecaster.TimeToEarn(me, forecaster.EarnedAt(me, now) + _settings.HorizonGold));

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
        // Plain Boots never win on their own against a whole item, but can win as the step before one.
        var firstLayer = Score(screened.Take(_stage.ScreenCount)
            .Concat(screened.Where(n => n.Item!.Id == previousTarget?.Id || IsBasicBoots(n.Item!)))
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
        beam.AddRange(firstLayer.Where(n => IsBasicBoots(n.Item!) && !beam.Contains(n)));

        // Plain Boots are always worth a look between big items, whatever the first layer ranked.
        foreach (var boots in firstLayer.Where(n => IsBasicBoots(n.Item!)).Select(n => n.Item!))
        {
            if (branching.All(b => b.Id != boots.Id))
            {
                branching.Add(boots);
            }
        }

        // Depth counts major purchases: a 300 gold pair of Boots does not use up one of them, so one extra
        // layer lets a plan hold Boots and still reach `depth` real items.
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

        var best = all.Count > 0 ? all.MaxBy(n => Value(n, horizon))! : root;
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

    /// <summary>Stacks a stacking item has when scored: from purchase to a short look-ahead past the moment it is scored.</summary>
    public IReadOnlyDictionary<Guid, double> StacksAt(IReadOnlyDictionary<Guid, double> boughtAt, double time)
    {
        var stacks = new Dictionary<Guid, double>();

        foreach (var (id, at) in boughtAt)
        {
            if (_context.Items.ById(id)?.Stacking is { } stacking)
            {
                var minutes = (time + _settings.StackLookaheadSeconds - at) / 60;
                stacks[id] = stacking.StacksAfter(minutes, _context.Champion.IsRanged);
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

    /// <summary>
    /// Every candidate as your next purchase, scored cheaply (one attack timing, the biggest threats only)
    /// and ranked by the value of buying it alone. Only the best of these get a proper score.
    /// </summary>
    private List<Node> Prescreen(Node root, double horizon, Item? previousTarget, Stopwatch watch, double budget, ref bool timedOut)
    {
        if (ItemRules.Slots(root.Inventory) >= ItemRules.InventorySlots)
        {
            root.SellCandidate = LeastValuable(root);
        }

        var specs = CandidatePool().Select(item => Child(root, item, horizon)).OfType<Node>().ToList();

        foreach (var spec in specs)
        {
            if (Stop(watch, budget * _settings.PrescreenShare) && spec.Item!.Id != previousTarget?.Id)
            {
                timedOut = true;
                continue;
            }

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
        foreach (var spec in specs)
        {
            if (Stop(watch, budget))
            {
                timedOut = true;
                break;
            }

            Score(spec);
        }

        return specs.Where(s => s.Scored).Where(s => s.Sold is null || s.ReplacementGain >= ReplaceMargin(s.Sold)).ToList();
    }

    /// <summary>
    /// How much better a swap must be than keeping the item it sells. Selling a finished item costs 30% of it
    /// and invites swapping back later, so it needs a clear win; starters and consumables go for less.
    /// </summary>
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
        var time = need <= 0 ? parent.Time : TimeToAfford(parent, need, bonus);
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
            Depth = parent.Depth + (IsBasicBoots(item) ? 0 : 1),
            Closed = parent.Closed + parent.Gain * Discount(parent.Time, time),
        };
    }

    /// <summary>
    /// When <paramref name="need"/> more gold is in hand after <paramref name="parent"/>, counting the forecast
    /// income plus <paramref name="bonus"/> gold per second from gold items bought along the plan.
    /// </summary>
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

    /// <summary>
    /// Gold per second your gold items earn (The Collector per kill, Cull per minion). Per-kill income uses
    /// your own kill rate this game when there is one, so it is worth more to a player who is snowballing.
    /// Your current income already includes what owned items earn, so the plan only adds the difference.
    /// </summary>
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

        if (node.Sold is not null)
        {
            var parent = node.Parent!;
            var kept = _evaluator.Evaluate(parent.Inventory, node.Time, StacksAt(parent.BoughtAt, node.Time), _stage.Mode);
            node.ReplacementGain = after.Score - kept.Score;
        }

        node.Scored = true;
    }

    /// <summary>
    /// The item whose loss hurts least, among those that can be sold. Never the jungle pet, and never boots:
    /// the model cannot price movement speed, so it would sell them for any stat.
    /// </summary>
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

    private double Value(Node node, double horizon) => node.Closed + node.Gain * Discount(node.Time, horizon);

    private double Discount(double from, double to)
    {
        var tau = _settings.DiscountSeconds;
        var now = _context.Now;
        return tau * (Math.Exp(-(from - now) / tau) - Math.Exp(-(to - now) / tau));
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

        public double Gain { get; set; }

        public double ReplacementGain { get; set; }

        public bool Scored { get; set; }

        public bool Prescreened { get; set; }

        public double CheapValue { get; set; }

        /// <summary>Extra gold per second from gold items bought along the plan so far.</summary>
        public double BonusGoldPerSecond { get; init; }

        public Item? SellCandidate { get; set; }
    }
}

