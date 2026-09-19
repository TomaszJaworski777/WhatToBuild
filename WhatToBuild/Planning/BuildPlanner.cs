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

        foreach (var boots in firstLayer.Where(n => IsBasicBoots(n.Item!)).Select(n => n.Item!))
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
            Depth = parent.Depth + (IsBasicBoots(item) ? 0 : 1),
            Closed = parent.Closed + parent.Gain * Discount(parent.Time, time),
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

        public double BonusGoldPerSecond { get; init; }

        public Item? SellCandidate { get; set; }
    }
}

