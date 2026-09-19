using System.Globalization;
using WhatToBuild.Data;
using WhatToBuild.Dtos;
using WhatToBuild.Game;
using WhatToBuild.Modeling;

namespace WhatToBuild.Recommendations;

public interface IRecommendationSource
{
    RecommendationDto? For(GameState state, GameStack stack);
}

public sealed class NoRecommendations : IRecommendationSource
{
    public RecommendationDto? For(GameState state, GameStack stack) => null;
}

public sealed class SampleRecommendations : IRecommendationSource
{
    public const double IncomeWindowSeconds = 300;
    public const double FallbackGoldPerMinute = 400;
    public const double HeavyHealing = 1.0;
    public const double HeavyArmor = 150;
    public const double HeavyShielding = 1.0;
    public const int InventorySlots = 6;
    public const int CompletedItemMinCost = 2000;
    public const int NeedOptionCount = 3;

    private static readonly int[] PlanOrder = [6672, 3006, 3031, 3036, 3033, 3026, 3072, 3046, 3094];
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private readonly ItemRepository _items;
    private readonly NeutralRepository _neutrals;
    private readonly string _patch;
    private readonly HashSet<Guid> _components;

    public SampleRecommendations(ItemRepository items, NeutralRepository neutrals)
    {
        _items = items;
        _neutrals = neutrals;
        _patch = items.Patch;
        _components = items.All.SelectMany(i => i.BuildPath).ToHashSet();
    }

    public RecommendationDto? For(GameState state, GameStack stack)
    {
        if (state.ActivePlayer is not { } me)
        {
            return null;
        }

        var context = new PlanContext(state, me, _neutrals, stack);
        var owned = me.Items.SelectMany(i => Enumerable.Repeat(i.Item, i.Count)).ToList();
        var inventory = owned.ToList();

        var steps = new List<BuildStepDto>();
        var planned = new List<PlannedItem>();
        var skipped = new List<SkippedItemDto>();
        var goldAhead = state.CurrentGold;
        var etaSoFar = 0.0;
        PurchasePlan? buyNow = null;

        foreach (var item in PlanOrder.Select(_items.ByRiotId).OfType<Item>())
        {
            if (steps.Count == InventorySlots)
            {
                break;
            }

            if (owned.Any(o => o.Id == item.Id))
            {
                steps.Add(new BuildStepDto(Map(item), BuildStepDto.Owned, null, null, 0, null, ["Already built"]));
                planned.Add(new PlannedItem(item, BuildStepDto.Owned, null));
                continue;
            }

            var full = ComponentPurchase.Plan(item, inventory, double.MaxValue, _items);
            if (!ItemRules.IsLegal(full.InventoryAfter))
            {
                var rest = full.InventoryAfter.ToList();
                rest.Remove(item);
                var reason = ItemRules.Conflict(rest, item)?.Reason ?? "the game does not allow this combination";
                skipped.Add(new SkippedItemDto(Map(item), $"Skipped: {reason}."));
                continue;
            }

            var isNext = buyNow is null;
            var need = Math.Max(0, full.Cost - goldAhead);
            goldAhead = Math.Max(0, goldAhead - full.Cost);
            etaSoFar += need / context.Income * 60;

            if (isNext)
            {
                buyNow = ComponentPurchase.Plan(item, inventory, state.CurrentGold, _items, context.Scorer);
            }

            var status = isNext ? BuildStepDto.Next : BuildStepDto.Planned;
            var eta = state.GameTime + etaSoFar;
            var impact = Impact(context, inventory, full.InventoryAfter);

            steps.Add(new BuildStepDto(
                Map(item),
                status,
                eta,
                Math.Max(20, etaSoFar * 0.25),
                (int)Math.Ceiling(need),
                impact,
                Why(item, context, impact, full.InventoryAfter, isNext ? need : null)));

            planned.Add(new PlannedItem(item, status, eta));
            inventory = full.InventoryAfter.ToList();
        }

        return new RecommendationDto(
            IsSample: true,
            state.CurrentGold,
            state.GoldEarned,
            context.MeasuredIncome,
            MapPurchase(buyNow, owned, context),
            steps,
            skipped,
            TeamNeeds(context, planned, inventory),
            [
                "Sample plan: the item order is fixed until the planner is built. The numbers are real.",
                context.MeasuredIncome is null
                    ? $"Times assume {FallbackGoldPerMinute:0} gold per minute until there is enough history."
                    : "Times use your gold income over the last 5 minutes.",
                "Damage is auto attacks plus on-hit item effects at your current level, averaged over the enemy team. Abilities are not simulated.",
                "Enemy stats are rebuilt from level, items and dragons.",
            ]);
    }

    private sealed record PlannedItem(Item Item, string Status, double? Eta);

    private sealed record Need(string Name, string Detail, Func<Item, bool> Covers);

    private sealed class PlanContext
    {
        public PlanContext(GameState state, PlayerState me, NeutralRepository neutrals, GameStack stack)
        {
            State = state;
            Me = me;
            EnemyTeam = me.Team == Team.Order ? Team.Chaos : Team.Order;
            Buffs = state.TeamBuffs(me.Team, neutrals).ToList();
            Enemies = state.Enemies.Select(p => (p, state.EntityFor(p, neutrals))).ToList();
            Combat = new CombatAssumption(me.EstimatedStacks.FirstOrDefault()?.Stacks ?? 0);
            Scorer = new AttackDpsScorer(me.Champion, me.Level, Buffs, Enemies.Select(e => (Entity)e.Entity), Combat);
            MeasuredIncome = stack.GoldEarnedPerMinute(IncomeWindowSeconds);
            Income = Math.Max(100, MeasuredIncome ?? FallbackGoldPerMinute);
        }

        public GameState State { get; }

        public PlayerState Me { get; }

        public Team EnemyTeam { get; }

        public IReadOnlyList<StatModifier> Buffs { get; }

        public IReadOnlyList<(PlayerState Player, ChampionState Entity)> Enemies { get; }

        public CombatAssumption Combat { get; }

        public AttackDpsScorer Scorer { get; }

        public double? MeasuredIncome { get; }

        public double Income { get; }

        public ChampionState Us(IEnumerable<Item> inventory) => new(Me.Champion, Me.Level, inventory, Buffs, Combat);
    }

    private ImpactDto Impact(PlanContext context, IReadOnlyList<Item> before, IReadOnlyList<Item> after)
    {
        var usBefore = context.Us(before);
        var usAfter = context.Us(after);

        var perEnemy = context.Enemies
            .Select(e => new EnemyImpactDto(
                e.Player.Champion.Name,
                GameStateMapper.ChampionIconUrl(_patch, e.Player.Champion.Icon),
                AttackDps.Against(usBefore, e.Entity).Total,
                AttackDps.Against(usAfter, e.Entity).Total,
                $"{e.Entity.Stats.Armor:0} armor, {e.Entity.Stats.Health:0} HP"))
            .OrderByDescending(e => Gain(e.DpsBefore, e.DpsAfter))
            .ToList();

        return new ImpactDto(
            perEnemy.Count > 0 ? perEnemy.Average(e => e.DpsBefore) : 0,
            perEnemy.Count > 0 ? perEnemy.Average(e => e.DpsAfter) : 0,
            perEnemy);
    }

    private IReadOnlyList<string> Why(Item item, PlanContext context, ImpactDto impact, IReadOnlyList<Item> after, double? goldNeeded)
    {
        var reasons = new List<string>
        {
            $"+{Pct(Gain(impact.DpsBefore, impact.DpsAfter))} auto-attack damage against the enemy team ({impact.DpsBefore:0} → {impact.DpsAfter:0} DPS)",
        };

        if (impact.PerEnemy.Count > 1)
        {
            var best = impact.PerEnemy[0];
            var worst = impact.PerEnemy[^1];
            reasons.Add($"Most against {best.Champion} (+{Pct(Gain(best.DpsBefore, best.DpsAfter))}), least against {worst.Champion} (+{Pct(Gain(worst.DpsBefore, worst.DpsAfter))})");
        }

        var enemies = context.Enemies;

        if (item.Stats.ArmorPenetrationPercent > 0 && enemies.Count > 0)
        {
            var tank = enemies.MaxBy(e => e.Entity.Stats.Armor);
            var penetration = StatCalculator.StackMultiplicatively(
                after.Where(i => i.Stats.ArmorPenetrationPercent > 0).Select(i => i.Stats.ArmorPenetrationPercent));
            var lethality = after.Sum(i => i.Stats.ArmorPenetrationFlat);
            var effective = Math.Max(0, tank.Entity.Stats.Armor * (1 - penetration) - lethality);
            reasons.Add($"{Pct(item.Stats.ArmorPenetrationPercent)} armor penetration: {tank.Player.Champion.Name}'s {tank.Entity.Stats.Armor:0} armor counts as {effective:0} for you");
        }

        var giantSlayer = item.Effects
            .Where(e => e.Stat == Stats.DamageAmp && e.When.Any(c => c.Property == ConditionProperty.BonusHealth))
            .ToList();

        if (giantSlayer.Count > 0 && enemies.Count > 0)
        {
            var active = enemies
                .Select(e => (e.Player, e.Entity, Amp: giantSlayer.Where(g => e.Entity.Satisfies(g.When)).Sum(g => g.Amount)))
                .Where(e => e.Amp > 0)
                .Select(e => $"{e.Player.Champion.Name} +{Pct(e.Amp)}")
                .ToList();

            var threshold = giantSlayer.SelectMany(e => e.When).Where(c => c.Property == ConditionProperty.BonusHealth).Min(c => c.Value);
            var tankiest = enemies.MaxBy(e => e.Entity.BonusHealth);

            reasons.Add(active.Count > 0
                ? $"Giant Slayer bonus: {string.Join(", ", active)}"
                : $"Giant Slayer bonus not active yet: starts at {threshold:0} bonus health, {tankiest.Player.Champion.Name} has {tankiest.Entity.BonusHealth:0}");
        }

        if (item.Effects.Any(e => e.Kind == EffectKind.GrievousWounds))
        {
            var (score, sources) = EnemyHealing(context);
            reasons.Add(sources.Count > 0
                ? $"Anti-heal against {string.Join(", ", sources)} (healing score {score.ToString("0.0", Invariant)})"
                : "Anti-heal, but the enemy team heals little");
        }

        if (item.Effects.Any(e => e.Kind == EffectKind.Revive))
        {
            var burst = enemies.Where(e => e.Player.Champion.Tag("burst") >= 0.7).Select(e => e.Player.Champion.Name).ToList();
            reasons.Add(burst.Count > 0
                ? $"Revive against burst from {string.Join(", ", burst)}"
                : "Revive, though no enemy has heavy burst");
        }

        foreach (var need in Needs(context).Where(n => n.Covers(item)))
        {
            reasons.Add($"Covers a team need: {need.Name}");
        }

        if (goldNeeded is { } needed)
        {
            reasons.Add(needed <= 0
                ? "You can buy it now"
                : $"Needs {needed:0} more gold, about {Clock(needed / context.Income * 60)} of income at {context.Income:0}/min");
        }

        return reasons;
    }

    private List<Need> Needs(PlanContext context)
    {
        var needs = new List<Need>();
        var enemies = context.Enemies;

        var (healing, healers) = EnemyHealing(context);
        if (healing >= HeavyHealing)
        {
            needs.Add(new Need(
                "Anti-heal",
                $"Enemy healing is high: {string.Join(", ", healers)}",
                i => i.Effects.Any(e => e.Kind == EffectKind.GrievousWounds)));
        }

        if (enemies.Count > 0)
        {
            var tank = enemies.MaxBy(e => e.Entity.Stats.Armor);
            if (tank.Entity.Stats.Armor >= HeavyArmor)
            {
                needs.Add(new Need(
                    "Armor penetration",
                    $"{tank.Player.Champion.Name} has {tank.Entity.Stats.Armor:0} armor",
                    i => i.Stats.ArmorPenetrationPercent > 0
                         || i.Stats.ArmorPenetrationFlat > 0
                         || i.Effects.Any(e => e.Kind == EffectKind.ArmorShred || e.Stat == Stats.ArmorPenetrationPercent)));
            }
        }

        var shielding = enemies.Sum(e => e.Player.Champion.Tag("shielding"))
                        + context.State.TeamTag(context.EnemyTeam, "shielding", _neutrals);
        if (shielding >= HeavyShielding)
        {
            needs.Add(new Need(
                "Shield reduction",
                "The enemy team shields a lot",
                i => i.Effects.Any(e => e.Kind == EffectKind.ShieldReduction)));
        }

        return needs;
    }

    private IReadOnlyList<TeamNeedDto> TeamNeeds(PlanContext context, List<PlannedItem> plan, List<Item> finalInventory)
    {
        var result = new List<TeamNeedDto>();

        foreach (var need in Needs(context))
        {
            var covering = plan.FirstOrDefault(p => need.Covers(p.Item));

            if (covering is not null)
            {
                var label = covering.Status == BuildStepDto.Owned ? $"{covering.Item.Name} (built)" : covering.Item.Name;
                result.Add(new TeamNeedDto(need.Name, need.Detail, label, covering.Eta, []));
                continue;
            }

            var baseline = context.Scorer.Score(finalInventory);
            var options = _items.All
                .Where(i => need.Covers(i) && i.Cost >= CompletedItemMinCost && !_components.Contains(i.Id))
                .Where(i => ItemRules.Conflict(finalInventory, i) is null)
                .Select(i => (Item: i, Gain: context.Scorer.Score([.. finalInventory, i]) - baseline))
                .Where(o => o.Gain > 0)
                .OrderByDescending(o => o.Gain)
                .Take(NeedOptionCount)
                .Select(o => Map(o.Item))
                .ToList();

            result.Add(new TeamNeedDto(need.Name, need.Detail, null, null, options));
        }

        return result;
    }

    private (double Score, List<string> Sources) EnemyHealing(PlanContext context)
    {
        var enemies = context.Enemies;
        var sources = enemies
            .Where(e => e.Player.Champion.Tag("healing") > 0)
            .OrderByDescending(e => e.Player.Champion.Tag("healing"))
            .Select(e => e.Player.Champion.Name)
            .ToList();

        var dragons = context.State.TeamTag(context.EnemyTeam, "healing", _neutrals);
        if (dragons > 0)
        {
            var soul = context.State.Objectives[context.EnemyTeam].SoulType is { } s ? _neutrals.SoulFor(s)?.Name : null;
            sources.Add(soul ?? "Ocean drake");
        }

        var score = enemies.Sum(e => e.Player.Champion.Tag("healing")) + dragons;
        return (score, sources);
    }

    private PurchaseDto MapPurchase(PurchasePlan? plan, List<Item> owned, PlanContext context)
    {
        var gold = context.State.CurrentGold;

        if (plan is null)
        {
            return new PurchaseDto("", [], 0, gold, false, "Build complete", []);
        }

        var summary = plan.CompletesTarget
            ? $"Buy {plan.Target.Name}"
            : plan.Buy.Count > 0
                ? $"Buy {string.Join(" + ", plan.Buy.Select(i => i.Name))} toward {plan.Target.Name}"
                : $"Save up: nothing from {plan.Target.Name} fits in {Math.Floor(gold):0} gold";

        var why = new List<string>();
        if (plan.Buy.Count > 0)
        {
            var before = context.Scorer.Score(owned);
            var after = context.Scorer.Score(plan.InventoryAfter);
            why.Add($"+{Pct(Gain(before, after))} auto-attack damage right away ({before:0} → {after:0} DPS)");
        }

        if (!plan.CompletesTarget && plan.Buy.Count > 0)
        {
            why.Add("Components are picked for the most damage now; when options are about equal, the more expensive ones go first");
        }

        return new PurchaseDto(
            plan.Target.Name,
            plan.Buy.Select(Map).ToList(),
            plan.Cost,
            gold - plan.Cost,
            plan.CompletesTarget,
            summary,
            why);
    }

    private ItemDto Map(Item item) => GameStateMapper.MapItem(item, 1, _patch);

    private static double Gain(double before, double after) => before > 0 ? after / before - 1 : 0;

    private static string Pct(double fraction) => (fraction * 100).ToString("0", Invariant) + "%";

    private static string Clock(double seconds)
    {
        var s = (int)Math.Round(seconds);
        return $"{s / 60}:{s % 60:00}";
    }
}
