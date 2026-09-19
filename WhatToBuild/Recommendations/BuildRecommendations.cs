using System.Globalization;
using System.Text;
using WhatToBuild.Data;
using WhatToBuild.Dtos;
using WhatToBuild.Game;
using WhatToBuild.Modeling;
using WhatToBuild.Modeling.Simulation;
using WhatToBuild.Planning;
using WhatToBuild.SupportedChampions;

namespace WhatToBuild.Recommendations;

public interface IRecommendationSource
{
    RecommendationDto? For(GameState state, GameStack stack);

    event Action<RecommendationDto?>? Updated;
}

public sealed class NoRecommendations : IRecommendationSource
{
    public RecommendationDto? For(GameState state, GameStack stack) => null;

    public event Action<RecommendationDto?>? Updated
    {
        add { }
        remove { }
    }
}

public sealed class BuildRecommendations : IRecommendationSource
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private readonly ItemRepository _items;
    private readonly NeutralRepository _neutrals;
    private readonly ChampionKits _kits;
    private readonly ModelData _model;
    private readonly string _patch;
    private readonly object _lock = new();

    private string? _key;
    private double _plannedAt = double.NegativeInfinity;
    private Item? _target;
    private Planned? _planned;
    private int _stageDone;

    private readonly AutoResetEvent _wake = new(false);
    private Thread? _worker;
    private (GameState State, GameStack Stack)? _pending;
    private (GameState State, GameStack Stack)? _current;
    private volatile RecommendationDto? _latest;
    private volatile string? _latestGame;

    public BuildRecommendations(ItemRepository items, NeutralRepository neutrals, ChampionKits kits, ModelData model)
    {
        _items = items;
        _neutrals = neutrals;
        _kits = kits;
        _model = model;
        _patch = items.Patch;
    }

    public event Action<RecommendationDto?>? Updated;

    private IReadOnlyList<ModelSettings.PlanStage> Stages => _model.Settings.Planner.Stages;

    public RecommendationDto? For(GameState state, GameStack stack)
    {
        if (state.ActivePlayer is null || !state.Enemies.Any())
        {
            return null;
        }

        lock (_wake)
        {
            _pending = (state, stack.Copy());

            if (_worker is null)
            {
                _worker = new Thread(Work) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "Build planner" };
                _worker.Start();
            }
        }

        _wake.Set();

        return _latestGame == GameKey(state) ? _latest : null;
    }

    private void Work()
    {
        while (true)
        {
            var refine = _planned is not null && _stageDone + 1 < Stages.Count;
            var stale = _planned is not null && _current is { } latest
                        && latest.State.GameTime - _plannedAt >= _model.Settings.Planner.ReplanSeconds;
            _wake.WaitOne(refine || stale ? 0 : Timeout.Infinite);

            try
            {
                if (TakePending() is { } job)
                {
                    _current = job;
                    Publish(Compute(job.State, job.Stack), job.State);
                }
                else if (_current is { } current
                         && (refine ? Refine(new WorkerControl(this)) : stale && Refresh(current.State, current.Stack, new WorkerControl(this))))
                {
                    Publish(Current(current.State), current.State);
                }
            }
            catch (Exception)
            {
                lock (_lock)
                {
                    _planned = null;
                }
            }
        }
    }

    private (GameState State, GameStack Stack)? TakePending()
    {
        lock (_wake)
        {
            var job = _pending;
            _pending = null;
            return job;
        }
    }

    private void Publish(RecommendationDto? recommendation, GameState state)
    {
        _latest = recommendation;
        _latestGame = GameKey(state);
        Updated?.Invoke(recommendation);
    }

    private sealed class WorkerControl(BuildRecommendations owner) : IPlanControl
    {
        private const double TickSeconds = 0.25;
        private DateTime _lastTick = DateTime.UtcNow;

        public bool ShouldStop()
        {
            lock (owner._wake)
            {
                return owner._pending is { } pending && owner.NeedsReplan(pending.State);
            }
        }

        public void Tick()
        {
            if ((DateTime.UtcNow - _lastTick).TotalSeconds < TickSeconds || ShouldStop())
            {
                return;
            }

            _lastTick = DateTime.UtcNow;
            if (owner.TakePending() is { } job && owner._planned is { } planned)
            {
                owner._current = job;
                owner.Publish(owner.Render(planned, job.State), job.State);
            }
        }
    }

    private static string GameKey(GameState state) =>
        string.Join(',', state.Players.Select(p => p.Champion.Name).Order());

    private bool NeedsReplan(GameState state) =>
        _planned is null
        || StateKey(state) != _key
        || state.GameTime < _plannedAt;

    public RecommendationDto? Compute(GameState state, GameStack stack)
    {
        if (state.ActivePlayer is null || !state.Enemies.Any())
        {
            return null;
        }

        lock (_lock)
        {
            if (NeedsReplan(state))
            {
                if (_planned is not null && (state.GameTime < _plannedAt || GameKey(state) != GameKey(_planned.Context.State)))
                {
                    _target = null;
                }

                var evaluator = new BuildEvaluator(new BuildContext(state, stack, _items, _neutrals, _kits, _model));
                var opening = _model.Settings.Planner.Opening is { } whole
                              && state.GameTime < _model.Settings.Planner.OpeningSeconds
                              && (_planned is null || GameKey(state) != GameKey(_planned.Context.State) || state.GameTime < _plannedAt)
                    ? whole
                    : null;

                _planned = Explain(evaluator, new BuildPlanner(evaluator).Plan(_target, opening ?? Stages[0]));
                _stageDone = opening is not null ? Stages.Count - 1 : 0;
                _key = StateKey(state);
                _plannedAt = state.GameTime;
                _target = _planned.Plan.Steps.FirstOrDefault()?.Item;
            }

            return Render(_planned!, state);
        }
    }

    public bool Refine(IPlanControl? control = null)
    {
        lock (_lock)
        {
            if (_planned is not { } planned || _stageDone + 1 >= Stages.Count)
            {
                return false;
            }

            var plan = new BuildPlanner(planned.Evaluator).Plan(_target, Stages[_stageDone + 1], control);
            if (plan.Cancelled)
            {
                return false;
            }

            _planned = Explain(planned.Evaluator, plan);
            _stageDone++;
            _target = plan.Steps.FirstOrDefault()?.Item;
            return true;
        }
    }

    public bool Refresh(GameState state, GameStack stack, IPlanControl? control = null)
    {
        lock (_lock)
        {
            var evaluator = new BuildEvaluator(new BuildContext(state, stack, _items, _neutrals, _kits, _model));
            var plan = new BuildPlanner(evaluator).Plan(_target, Stages[^1], control);
            if (plan.Cancelled)
            {
                return false;
            }

            _planned = Explain(evaluator, plan);
            _stageDone = Stages.Count - 1;
            _key = StateKey(state);
            _plannedAt = state.GameTime;
            _target = plan.Steps.FirstOrDefault()?.Item;
            return true;
        }
    }

    public RecommendationDto? Current(GameState state)
    {
        lock (_lock)
        {
            return _planned is null ? null : Render(_planned, state);
        }
    }

    private sealed record Explained(PlanStep Step, Evaluation Before, Evaluation After, IReadOnlyList<string> Why);

    private sealed record Planned(BuildContext Context, BuildEvaluator Evaluator, BuildPlan Plan, IReadOnlyList<Explained> Steps, Evaluation Baseline);

    private Planned Explain(BuildEvaluator evaluator, BuildPlan plan)
    {
        var context = evaluator.Context;
        var explained = plan.Steps.Select(step =>
        {
            var before = evaluator.Evaluate(step.Before, step.At, step.StacksBefore, plan.Mode);
            var after = evaluator.Evaluate(step.After, step.At, step.StacksAfter, plan.Mode);
            return new Explained(step, before, after, Why(context, evaluator, plan, step, before, after));
        }).ToList();

        var baseline = evaluator.Evaluate(context.Owned, plan.Steps.FirstOrDefault()?.At ?? context.Now, null, plan.Mode);

        return new Planned(context, evaluator, plan, explained, baseline);
    }

    private RecommendationDto Render(Planned planned, GameState state)
    {
        var context = planned.Context;
        var me = state.ActivePlayer!;
        var forecaster = context.Forecaster;
        var gold = state.CurrentGold;

        var steps = new List<BuildStepDto>();
        foreach (var owned in context.Owned.Where(i => i.Cost >= 900 && !IsComponent(i)).DistinctBy(i => i.Id))
        {
            steps.Add(new BuildStepDto(Map(owned, context), BuildStepDto.Owned, null, null, 0, null, ["Already built"]));
        }

        var needSoFar = 0.0;
        var earnedNow = forecaster.EarnedAt(me, state.GameTime);
        for (var i = 0; i < planned.Steps.Count; i++)
        {
            var explained = planned.Steps[i];
            var step = explained.Step;
            needSoFar += step.Cost - (step.Sold is { } sold ? sold.Cost * _model.Settings.Planner.SellRefund : 0);
            var need = Math.Max(0, needSoFar - gold);
            var eta = need <= 0 ? state.GameTime : context.NextRecall(forecaster.TimeToEarn(me, earnedNow + need), state.GameTime);

            steps.Add(new BuildStepDto(
                Map(step.Item, context),
                i == 0 ? BuildStepDto.Next : BuildStepDto.Planned,
                eta,
                forecaster.Spread(eta) - 10 + (i == 0 ? 10 : 20),
                (int)Math.Ceiling(need),
                Impact(explained),
                i == 0 ? [.. explained.Why, GoldLine(need, forecaster.Outlook(me).GoldPerMinute)] : explained.Why,
                step.Sold?.Name));
        }

        return new RecommendationDto(
            IsSample: false,
            gold,
            state.GoldEarned,
            forecaster.Outlook(me).GoldPerMinute,
            BuyNow(planned, state),
            steps,
            [],
            TeamNeeds(planned),
            Model(planned),
            Assumptions(planned),
            Matchups(planned, state));
    }

    private PurchaseDto BuyNow(Planned planned, GameState state)
    {
        var context = planned.Context;
        var gold = state.CurrentGold;
        var owned = state.ActivePlayer!.Items.Where(i => i.Slot != 6).SelectMany(i => Enumerable.Repeat(i.Item, i.Count)).ToList();

        if (planned.Plan.Steps.FirstOrDefault() is not { } first)
        {
            return new PurchaseDto("", [], 0, gold, false, "Build complete: nothing beats what you have", []);
        }

        var inventory = owned.ToList();
        var why = new List<string>();
        if (first.Sold is { } sold && inventory.Remove(sold))
        {
            gold += sold.Cost * _model.Settings.Planner.SellRefund;
            why.Add($"Sell {sold.Name} first (+{sold.Cost * _model.Settings.Planner.SellRefund:0} gold): it adds the least to your build");
        }

        var scorer = new EvaluatorScorer(planned.Evaluator, state.GameTime);
        var start = inventory.ToList();
        var bought = new List<Item>();
        var cost = 0;
        var completes = false;

        foreach (var step in planned.Plan.Steps)
        {
            if (step != first && step.Sold is not null)
            {
                break;
            }

            var plan = ComponentPurchase.Plan(step.Item, inventory, gold - cost, _items, scorer);
            bought.AddRange(plan.Buy);
            cost += plan.Cost;
            inventory = plan.InventoryAfter.ToList();

            if (step == first)
            {
                completes = plan.CompletesTarget;
            }

            if (!plan.CompletesTarget || gold - cost < 1)
            {
                break;
            }
        }

        var summary = bought.Count > 0
            ? string.Join(" + ", bought.Select(i => i.Name))
            : $"Save up for {first.Item.Name}";

        if (bought.Count > 0)
        {
            var before = planned.Evaluator.Evaluate(start, state.GameTime);
            var after = planned.Evaluator.Evaluate(inventory, state.GameTime);
            why.Add($"+{Pct(Math.Exp(after.Score - before.Score) - 1)} fight value right away ({before.Dps:0} → {after.Dps:0} DPS)");
        }

        return new PurchaseDto(
            first.Item.Name,
            bought.Select(i => Map(i, context)).ToList(),
            cost,
            gold - cost,
            completes,
            summary,
            why);
    }

    private sealed class EvaluatorScorer(BuildEvaluator evaluator, double time) : IPurchaseScorer
    {
        private readonly double _base = evaluator.Evaluate(evaluator.Context.Owned, time).Score;

        public double Score(IReadOnlyList<Item> inventory) => Math.Exp(evaluator.Evaluate(inventory, time).Score - _base);
    }

    private IReadOnlyList<string> Why(BuildContext context, BuildEvaluator evaluator, BuildPlan plan, PlanStep step, Evaluation before, Evaluation after)
    {
        var item = step.Item;
        var reasons = new List<string>
        {
            $"+{Pct(Math.Exp(after.Score - before.Score) - 1)} fight value at ~{Clock(step.At)} (damage before death {before.DamageBeforeDeath:0} → {after.DamageBeforeDeath:0})",
            $"Damage: {before.Dps:0} → {after.Dps:0} DPS against the enemy team as forecast then (+{Pct(Gain(before.Dps, after.Dps))})",
        };

        if (Math.Abs(Gain(before.TimeAlive, after.TimeAlive)) >= 0.02)
        {
            reasons.Add($"Survival: {before.TimeAlive:0.0}s → {after.TimeAlive:0.0}s alive under focus ({before.IncomingDps:0} damage/s on you)");
        }

        if (after.Clear is { } clearAfter && before.Clear is { } clearBefore && after.ClearWeight > 0.05
            && Math.Abs(clearBefore.KillSeconds - clearAfter.KillSeconds) >= 0.5)
        {
            reasons.Add($"Full clear: {clearBefore.KillSeconds:0}s → {clearAfter.KillSeconds:0}s of fighting (+{context.Settings.Jungle.WalkSeconds:0}s walking); clear weighs {after.ClearWeight:0.00} at this stage");
        }

        if (after.MoveSpeed - before.MoveSpeed >= 1)
        {
            var walk = after.Clear is { } fast && before.Clear is { } slow && after.ClearWeight > 0.05
                ? $", {slow.WalkSeconds - fast.WalkSeconds:0.0}s less walking per full clear"
                : "";
            reasons.Add($"Movement: {before.MoveSpeed:0} → {after.MoveSpeed:0} speed, {Pct(after.Tempo / before.Tempo - 1)} more of the game spent acting instead of walking{walk}, and easier to kite and dodge");
        }

        var pairs = before.Targets.Zip(after.Targets, (b, a) => (Before: b, After: a)).ToList();
        if (pairs.Count > 1)
        {
            var ranked = pairs.OrderByDescending(p => Gain(p.Before.Fight.EffectiveDps, p.After.Fight.EffectiveDps)).ToList();
            reasons.Add($"Most against {ranked[0].After.Enemy.Champion.Name} (+{Pct(Gain(ranked[0].Before.Fight.EffectiveDps, ranked[0].After.Fight.EffectiveDps))}), least against {ranked[^1].After.Enemy.Champion.Name} (+{Pct(Gain(ranked[^1].Before.Fight.EffectiveDps, ranked[^1].After.Fight.EffectiveDps))})");
        }

        var effects = item.Effects;

        if (effects.Any(e => e.Kind == EffectKind.GrievousWounds))
        {
            var healers = pairs
                .Where(p => p.After.Sustain.HealPerSecond > 1)
                .OrderByDescending(p => p.After.Sustain.HealPerSecond)
                .Take(3)
                .Select(p => $"{p.After.Enemy.Champion.Name} {p.Before.Fight.Healed / Math.Max(0.1, p.Before.Fight.Seconds):0} → {p.After.Fight.Healed / Math.Max(0.1, p.After.Fight.Seconds):0} HP/s")
                .ToList();
            reasons.Add(healers.Count > 0
                ? $"Anti-heal, healing in your fights: {string.Join(", ", healers)}"
                : "Anti-heal, but the enemy team barely heals in your fights");

            if (pairs.FirstOrDefault().After?.Sustain.ExternalGrievousWounds > 0)
            {
                reasons.Add("An ally already applies Grievous Wounds part of the time, so this counts only for the rest");
            }
        }

        if (effects.Any(e => e.Kind == EffectKind.ShieldReduction))
        {
            var shielded = pairs.Where(p => p.After.Sustain.ShieldTotal > 1).OrderByDescending(p => p.After.Sustain.ShieldTotal).Take(3)
                .Select(p => $"{p.After.Enemy.Champion.Name} {p.After.Sustain.ShieldTotal:0} → {p.After.Fight.Shielded:0}")
                .ToList();
            reasons.Add(shielded.Count > 0
                ? $"Cuts shields per fight: {string.Join(", ", shielded)}"
                : "Cuts shields, but the enemy team barely shields");
        }

        if (item.Stats.ArmorPenetrationPercent > 0 || item.Stats.ArmorPenetrationFlat > 0)
        {
            var tank = after.Targets.MaxBy(t => t.Enemy.Entity.Stats.Armor)!;
            var penetration = StatCalculator.StackMultiplicatively(step.After.Where(i => i.Stats.ArmorPenetrationPercent > 0).Select(i => i.Stats.ArmorPenetrationPercent));
            var lethality = step.After.Sum(i => i.Stats.ArmorPenetrationFlat);
            var armor = tank.Enemy.Entity.Stats.Armor;
            reasons.Add($"{tank.Enemy.Champion.Name} is forecast at {armor:0} armor by then; with your penetration it counts as {Math.Max(0, armor * (1 - penetration) - lethality):0}");
        }

        if (effects.Any(e => e.PerTargetCurrentHealth > 0 || e.PerTargetMaxHealth > 0))
        {
            var healthiest = after.Targets.MaxBy(t => t.Enemy.Entity.MaxHealth)!;
            reasons.Add($"Health-scaling damage: {healthiest.Enemy.Champion.Name} is forecast at {healthiest.Enemy.Entity.MaxHealth:0} health by then");
        }

        var amps = effects.Where(e => e.Stat == Stats.DamageAmp && e.When.Any(c => c.Property == ConditionProperty.BonusHealth)).ToList();
        if (amps.Count > 0)
        {
            var active = after.Targets
                .Select(t => (t.Enemy, Amp: amps.Where(a => t.Enemy.Entity.Satisfies(a.When)).Sum(a => a.Amount)))
                .Where(t => t.Amp > 0)
                .Select(t => $"{t.Enemy.Champion.Name} +{Pct(t.Amp)}")
                .ToList();
            reasons.Add(active.Count > 0
                ? $"Bonus damage against high health by then: {string.Join(", ", active)}"
                : "Its bonus against high-health targets does not trigger on anyone by then");
        }

        if (effects.Any(e => e.Kind == EffectKind.Execute))
        {
            reasons.Add($"Executes champions below {Pct(effects.First(e => e.Kind == EffectKind.Execute).Amount)} health");
        }

        if (effects.Any(e => e.Kind is EffectKind.Revive or EffectKind.Shield) && after.IncomingBurst > 0)
        {
            reasons.Add($"Forecast burst on you: {after.IncomingBurst:0} of {after.HealthPool:0} effective health");
        }

        if (item.Stacking is not null && step.StacksAfter.TryGetValue(item.Id, out var stacks))
        {
            reasons.Add($"Stacking item: counted with ~{stacks:0} stacks (bought ~{Clock(step.At)}, plus {context.Settings.Planner.StackLookaheadSeconds / 60:0} minutes)");
        }

        if (step.Sold is { } sold)
        {
            reasons.Add($"Replaces {sold.Name}, which adds the least to your build (sold for {sold.Cost * context.Settings.Planner.SellRefund:0} gold)");
        }

        var usual = context.Projector.BuildOf(context.Champion);
        if (usual.Count > 0 && usual.All(u => u.Id != item.Id))
        {
            reasons.Add("Off-meta pick: not in the usual build for your champion, but it scores well in this game");
        }

        return reasons;
    }

    private ImpactDto Impact(Explained explained)
    {
        var pairs = explained.Before.Targets.Zip(explained.After.Targets, (b, a) => (Before: b, After: a));
        var perEnemy = pairs
            .Select(p => new EnemyImpactDto(
                p.After.Enemy.Champion.Name,
                GameStateMapper.ChampionIconUrl(_patch, p.After.Enemy.Champion.Icon),
                p.Before.Fight.EffectiveDps,
                p.After.Fight.EffectiveDps,
                p.Before.Fight.TimeToKill,
                p.After.Fight.TimeToKill,
                $"L{p.After.Enemy.Forecast.Level}: {p.After.Enemy.Entity.Stats.Armor:0} armor, {p.After.Enemy.Entity.Stats.MagicResist:0} MR, {p.After.Enemy.Entity.MaxHealth:0} HP"))
            .OrderByDescending(e => Gain(e.DpsBefore, e.DpsAfter))
            .ToList();

        return new ImpactDto(explained.Before.Dps, explained.After.Dps, perEnemy);
    }

    private sealed record NeedCategory(string Name, Func<Item, bool> Covers);

    private IReadOnlyList<TeamNeedDto> TeamNeeds(Planned planned)
    {
        var eval = planned.Steps.FirstOrDefault()?.After ?? planned.Baseline;
        var targets = eval.Targets;
        var result = new List<TeamNeedDto>();

        void Need(string name, string detail, Func<Item, bool> covers, string? allyCover = null)
        {
            var step = planned.Steps.FirstOrDefault(s => covers(s.Step.Item));
            var owned = planned.Context.Owned.FirstOrDefault(covers);

            if (owned is not null || step is not null || allyCover is not null)
            {
                var label = owned is not null ? $"{owned.Name} (built)" : step is not null ? step.Step.Item.Name : allyCover!;
                result.Add(new TeamNeedDto(name, detail, label, owned is null ? step?.Step.At : null, []));
                return;
            }

            var options = planned.Plan.Candidates
                .Where(c => covers(c.Item))
                .Take(3)
                .Select(c => Map(c.Item, planned.Context))
                .ToList();

            result.Add(new TeamNeedDto(name, detail + ". The model found other items worth more for you.", null, null, options));
        }

        var dps = Math.Max(1, eval.Dps);
        var healing = targets.Sum(t => t.Enemy.Threat * t.Sustain.HealPerSecond);
        if (healing / dps >= 0.08)
        {
            var sources = targets.SelectMany(t => t.Enemy.HealSources.Select(h => (t.Enemy.Champion.Name, h)))
                .OrderByDescending(x => x.h.Amount).Take(3).Select(x => x.h.Source.Contains(x.Name) ? x.h.Source : $"{x.h.Source} ({x.Name})");
            var ally = eval.Targets.FirstOrDefault()?.Sustain.ExternalGrievousWounds > 0 ? "an ally's Grievous Wounds (part of the time)" : null;
            Need("Anti-heal", $"Enemies heal ~{healing:0} HP/s in your fights, {Pct(healing / dps)} of your damage: {string.Join(", ", sources)}",
                i => i.Effects.Any(e => e.Kind == EffectKind.GrievousWounds), ally);
        }

        var shields = targets.Sum(t => t.Enemy.Threat * t.Sustain.ShieldTotal);
        var health = targets.Sum(t => t.Enemy.Threat * t.Enemy.Entity.MaxHealth);
        if (health > 0 && shields / health >= 0.06)
        {
            var sources = targets.SelectMany(t => t.Enemy.ShieldSources.Select(h => (t.Enemy.Champion.Name, h)))
                .OrderByDescending(x => x.h.Amount).Take(3).Select(x => x.h.Source.Contains(x.Name) ? x.h.Source : $"{x.h.Source} ({x.Name})");
            Need("Shield reduction", $"Enemies shield ~{shields:0} per fight, {Pct(shields / health)} of their health: {string.Join(", ", sources)}",
                i => i.Effects.Any(e => e.Kind == EffectKind.ShieldReduction));
        }

        var armored = targets.MaxBy(t => t.Enemy.Entity.Stats.Armor);
        if (armored is not null && armored.Enemy.Entity.Stats.Armor >= 120)
        {
            Need("Armor penetration", $"{armored.Enemy.Champion.Name} is forecast at {armored.Enemy.Entity.Stats.Armor:0} armor by ~{Clock(eval.Time)}",
                i => i.Stats.ArmorPenetrationPercent > 0 || i.Stats.ArmorPenetrationFlat > 0 || i.Effects.Any(e => e.Kind == EffectKind.ArmorShred));
        }

        var tanky = targets.MaxBy(t => t.Enemy.Entity.BonusHealth);
        if (tanky is not null && tanky.Enemy.Entity.BonusHealth >= 1200)
        {
            Need("Anti-tank", $"{tanky.Enemy.Champion.Name} is forecast at {tanky.Enemy.Entity.BonusHealth:0} bonus health by ~{Clock(eval.Time)}",
                i => i.Effects.Any(e => e.PerTargetCurrentHealth > 0 || e.PerTargetMaxHealth > 0 || e.Stat == Stats.DamageAmp && e.When.Any(c => c.Property == ConditionProperty.BonusHealth)));
        }

        var incoming = Math.Max(1, eval.IncomingDps);
        var magic = eval.IncomingByType.GetValueOrDefault(DamageType.Magic) / incoming;
        if (magic >= 0.45)
        {
            Need("Magic resist", $"{Pct(magic)} of the damage aimed at you is magic",
                i => i.Stats.MagicResist > 0 || i.Effects.Any(e => e.Kind == EffectKind.Shield && e.Versus == DamageSource.Magic));
        }

        if (eval.IncomingBurst >= 0.35 * eval.HealthPool)
        {
            Need("Anti-burst", $"Forecast burst on you is {eval.IncomingBurst:0} of your {eval.HealthPool:0} effective health",
                i => i.Effects.Any(e => e.Kind is EffectKind.Revive || e.Kind == EffectKind.Shield && e.Trigger == EffectTrigger.WhenLow));
        }

        return result;
    }

    private static IReadOnlyList<MatchupDto> Matchups(Planned planned, GameState state)
    {
        var now = planned.Evaluator.Evaluate(planned.Context.Owned, state.GameTime, null, EvaluationMode.Full);
        return now.Targets
            .Select(t => new MatchupDto(t.Enemy.Champion.Name, t.Fight.TimeToKill, t.Fight.EffectiveDps, []))
            .ToList();
    }

    private ModelDto Model(Planned planned)
    {
        var context = planned.Context;
        var eval = planned.Steps.FirstOrDefault()?.After ?? planned.Baseline;
        var field = planned.Evaluator.BattlefieldAt(eval.Time);
        var incoming = Math.Max(1e-9, eval.IncomingDps);
        var us = eval.Us;

        var enemies = eval.Targets.Select(t =>
        {
            var p = t.Enemy;
            var perSecond = p.Streams.Sum(s => s.RawPerSecond + s.TargetMaxHealthPerSecond * us.MaxHealth);
            var notes = new List<string>();
            if (p.Forecast.ItemStacks.Count > 0)
            {
                notes.AddRange(p.Forecast.ItemStacks.Select(s => $"{_items.ById(s.Key)?.Name}: ~{s.Value:0} stacks (estimated)"));
            }

            notes.AddRange(p.Forecast.ChampionStacks.Where(s => s.Value > 0).Select(s => $"~{s.Value:0} {s.Key.Name} from champion stacks (estimated)"));

            return new EnemyForecastDto(
                p.Champion.Name,
                GameStateMapper.ChampionIconUrl(_patch, p.Champion.Icon),
                p.Forecast.Build.Archetype,
                p.Forecast.Player.Level,
                p.Forecast.Level,
                p.Forecast.Items.Where(i => !p.Forecast.Player.Items.Any(o => o.Item.Id == i.Id)).Select(i => Map(i, context)).ToList(),
                p.Threat,
                field.Focus[p],
                p.Entity.MaxHealth,
                p.Entity.Stats.Armor,
                p.Entity.Stats.MagicResist,
                t.Sustain.HealPerSecond,
                t.Sustain.ShieldTotal,
                field.Focus[p] * perSecond,
                t.Fight.TimeToKill,
                notes);
        }).ToList();

        return new ModelDto(
            eval.Time,
            context.Objective.Damage,
            context.Objective.Survival,
            eval.ClearWeight,
            eval.Dps,
            eval.TimeAlive,
            eval.TimeAliveWithoutAbility,
            field.Survival?.Name,
            context.Settings.Fight.TeamfightSeconds,
            eval.IncomingDps,
            eval.IncomingBurst,
            eval.HealthPool,
            eval.IncomingByType.GetValueOrDefault(DamageType.Physical) / incoming,
            eval.IncomingByType.GetValueOrDefault(DamageType.Magic) / incoming,
            eval.IncomingByType.GetValueOrDefault(DamageType.True) / incoming,
            eval.Clear?.KillSeconds,
            enemies,
            planned.Plan.Milliseconds,
            planned.Plan.Evaluations,
            planned.Plan.Candidates.Count,
            planned.Plan.TimedOut,
            planned.Plan.Stage,
            _stageDone + 1 < Stages.Count);
    }

    private IReadOnlyList<string> Assumptions(Planned planned)
    {
        var context = planned.Context;
        var list = new List<string>
        {
            "Enemy gold is the value of their items; future gold extrapolates each player's own income rate, pulled toward the lobby's average trend",
            "Enemy items after today's follow a common build for their archetype (a prior, never advice for you)",
            "Enemy ability damage, healing and shielding are estimated from champion tags, level and stats (heuristic values in model.json)",
            "Stacks on stacking items (Heartsteel, Yun Tal, ...) and champion stacks are estimated from time owned",
            "Your fights on the board and in the plan include enemy healing, shields and allies' Grievous Wounds and shred",
            "Teamfights last ~" + context.Settings.Fight.TeamfightSeconds.ToString("0", Invariant) + "s; each enemy aims a share of their damage at you based on your team's frontline",
        };

        if (context.IsJungler)
        {
            list.Add("Clear time is simulated camp by camp with your kit and pet; walking is a fixed " + context.Settings.Jungle.WalkSeconds.ToString("0", Invariant) + "s, and the jungle pet is never sold");
        }

        if (context.Kits.For(context.Champion) is null)
        {
            list.Add($"{context.Champion.Name}'s abilities are not simulated: only attacks and item effects count");
        }

        if (planned.Plan.TimedOut)
        {
            list.Add("The search hit its time budget; the plan is the best found so far");
        }

        return list;
    }

    private static string StateKey(GameState state)
    {
        var key = new StringBuilder();
        foreach (var p in state.Players.OrderBy(p => p.Champion.Name))
        {
            key.Append(p.Champion.Name).Append(':').Append(p.Level).Append(':').Append(p.Kills).Append('/').Append(p.Deaths).Append('/').Append(p.Assists).Append(':');
            foreach (var item in p.Items.OrderBy(i => i.Item.RiotId))
            {
                key.Append(item.Item.RiotId).Append('x').Append(item.Count).Append(',');
            }

            key.Append('|');
        }

        foreach (var (team, objectives) in state.Objectives)
        {
            key.Append(team).Append(objectives.TotalDragons).Append(objectives.Elders).Append(objectives.Barons).Append(objectives.SoulType);
        }

        return key.ToString();
    }

    private bool IsComponent(Item item) => _items.All.Any(i => i.BuildPath.Contains(item.Id));

    private ItemDto Map(Item item, BuildContext context) =>
        GameStateMapper.MapItem(item, 1, _patch);

    private static string GoldLine(double need, double goldPerMinute) =>
        need <= 0
            ? "You can buy it now"
            : $"Needs {need:0} more gold, about {Clock(need / Math.Max(1, goldPerMinute) * 60)} of income at {goldPerMinute:0}/min";

    private static double Gain(double before, double after) => before > 0 ? after / before - 1 : 0;

    private static string Pct(double fraction) => (fraction * 100).ToString("0", Invariant) + "%";

    private static string Clock(double seconds)
    {
        var s = (int)Math.Round(seconds);
        return $"{s / 60}:{s % 60:00}";
    }
}
