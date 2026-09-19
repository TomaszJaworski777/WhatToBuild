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

    private static readonly int[] PlanOrder = [6672, 3006, 3031, 3036, 3026, 3033];
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private readonly ItemRepository _items;
    private readonly NeutralRepository _neutrals;
    private readonly string _patch;

    public SampleRecommendations(ItemRepository items, NeutralRepository neutrals)
    {
        _items = items;
        _neutrals = neutrals;
        _patch = items.Patch;
    }

    public RecommendationDto? For(GameState state, GameStack stack)
    {
        if (state.ActivePlayer is not { } me)
        {
            return null;
        }

        var owned = me.Items.SelectMany(i => Enumerable.Repeat(i.Item, i.Count)).ToList();
        var measured = stack.GoldEarnedPerMinute(IncomeWindowSeconds);
        var income = Math.Max(100, measured ?? FallbackGoldPerMinute);
        var enemies = state.Enemies.Select(p => (Player: p, Entity: state.EntityFor(p, _neutrals))).ToList();
        var enemyTeam = me.Team == Team.Order ? Team.Chaos : Team.Order;

        var steps = new List<BuildStepDto>();
        var goldAhead = state.CurrentGold;
        var etaSoFar = 0.0;
        var nextFound = false;
        PurchasePlan? buyNow = null;

        foreach (var item in PlanOrder.Select(_items.ByRiotId).OfType<Item>())
        {
            if (owned.Any(o => o.Id == item.Id))
            {
                steps.Add(new BuildStepDto(Map(item), BuildStepDto.Owned, null, null, ["Already built"]));
                continue;
            }

            var price = nextFound ? item.Cost : ComponentPurchase.Plan(item, owned, double.MaxValue, _items).Cost;
            var need = Math.Max(0, price - goldAhead);
            goldAhead = Math.Max(0, goldAhead - price);
            etaSoFar += need / income * 60;

            var status = nextFound ? BuildStepDto.Planned : BuildStepDto.Next;
            if (!nextFound)
            {
                buyNow = ComponentPurchase.Plan(item, owned, state.CurrentGold, _items);
                nextFound = true;
            }

            steps.Add(new BuildStepDto(
                Map(item),
                status,
                state.GameTime + etaSoFar,
                Math.Max(20, etaSoFar * 0.25),
                Why(item, enemies, state, enemyTeam)));
        }

        return new RecommendationDto(
            IsSample: true,
            state.CurrentGold,
            state.GoldEarned,
            measured,
            MapPurchase(buyNow, state.CurrentGold),
            steps,
            TeamNeeds(enemies, state, enemyTeam),
            [
                "Sample plan: the item order is fixed until the planner is built. The numbers are real.",
                measured is null
                    ? $"Times assume {FallbackGoldPerMinute:0} gold per minute until there is enough history."
                    : "Times use your gold income over the last 5 minutes.",
                "Enemy stats are rebuilt from level, items and dragons.",
            ]);
    }

    private IReadOnlyList<string> Why(Item item, IReadOnlyList<(PlayerState Player, ChampionState Entity)> enemies, GameState state, Team enemyTeam)
    {
        var reasons = new List<string>();

        if (item.Stats.ArmorPenetrationPercent > 0 && enemies.Count > 0)
        {
            var tank = enemies.MaxBy(e => e.Entity.Stats.Armor);
            var gain = PenetrationGain(tank.Entity, item.Stats.ArmorPenetrationPercent);
            reasons.Add($"{Pct(item.Stats.ArmorPenetrationPercent)} armor penetration: +{Pct(gain)} damage to {tank.Player.Champion.Name} ({tank.Entity.Stats.Armor:0} armor)");
        }

        if (item.Effects.Any(e => e.Stat == Stats.DamageAmp && e.When.Any(c => c.Property == ConditionProperty.BonusHealth)))
        {
            var threshold = item.Effects.SelectMany(e => e.When).First(c => c.Property == ConditionProperty.BonusHealth).Value;
            var tanky = enemies.Where(e => e.Entity.BonusHealth >= threshold).Select(e => e.Player.Champion.Name).ToList();
            reasons.Add(tanky.Count > 0
                ? $"Giant Slayer active on {string.Join(", ", tanky)}"
                : $"Giant Slayer not active yet: no enemy has {threshold:0} bonus health");
        }

        if (item.Effects.Any(e => e.Kind == EffectKind.GrievousWounds))
        {
            var (score, sources) = EnemyHealing(enemies, state, enemyTeam);
            reasons.Add(sources.Count > 0
                ? $"Enemy healing {score.ToString("0.0", Invariant)}: {string.Join(", ", sources)}"
                : "Enemy healing is low");
        }

        if (item.Effects.Any(e => e.Kind == EffectKind.Revive))
        {
            var burst = enemies.Where(e => e.Player.Champion.Tag("burst") >= 0.7).Select(e => e.Player.Champion.Name).ToList();
            reasons.Add(burst.Count > 0
                ? $"Enemy burst threats: {string.Join(", ", burst)}"
                : "No heavy burst on the enemy team");
        }

        if (reasons.Count == 0)
        {
            reasons.Add("Core damage for this build");
        }

        return reasons;
    }

    private IReadOnlyList<string> TeamNeeds(IReadOnlyList<(PlayerState Player, ChampionState Entity)> enemies, GameState state, Team enemyTeam)
    {
        var needs = new List<string>();

        var (healing, healers) = EnemyHealing(enemies, state, enemyTeam);
        if (healing >= HeavyHealing)
        {
            needs.Add($"Anti-heal: enemy healing is high ({string.Join(", ", healers)})");
        }

        if (enemies.Count > 0)
        {
            var tank = enemies.MaxBy(e => e.Entity.Stats.Armor);
            if (tank.Entity.Stats.Armor >= HeavyArmor)
            {
                needs.Add($"Armor penetration: {tank.Player.Champion.Name} has {tank.Entity.Stats.Armor:0} armor");
            }
        }

        var shielding = enemies.Sum(e => e.Player.Champion.Tag("shielding")) + state.TeamTag(enemyTeam, "shielding", _neutrals);
        if (shielding >= 1.0)
        {
            needs.Add("Shield reduction: the enemy team shields a lot");
        }

        return needs;
    }

    private (double Score, List<string> Sources) EnemyHealing(
        IReadOnlyList<(PlayerState Player, ChampionState Entity)> enemies, GameState state, Team enemyTeam)
    {
        var sources = enemies
            .Where(e => e.Player.Champion.Tag("healing") > 0)
            .OrderByDescending(e => e.Player.Champion.Tag("healing"))
            .Select(e => e.Player.Champion.Name)
            .ToList();

        var dragons = state.TeamTag(enemyTeam, "healing", _neutrals);
        if (dragons > 0)
        {
            var soul = state.Objectives[enemyTeam].SoulType is { } s ? _neutrals.SoulFor(s)?.Name : null;
            sources.Add(soul ?? "Ocean drake");
        }

        var score = enemies.Sum(e => e.Player.Champion.Tag("healing")) + dragons;
        return (score, sources);
    }

    private static double PenetrationGain(ChampionState target, double percent)
    {
        var without = DamageCalculator.Create(target).AdDamage(100).Run().HealthDamage;
        var with = DamageCalculator.Create(target).AdDamage(100).ArmorPenetration(percent).Run().HealthDamage;
        return without > 0 ? with / without - 1 : 0;
    }

    private PurchaseDto MapPurchase(PurchasePlan? plan, double gold)
    {
        if (plan is null)
        {
            return new PurchaseDto("", [], 0, gold, false, "Build complete");
        }

        var summary = plan.CompletesTarget
            ? $"Buy {plan.Target.Name}"
            : plan.Buy.Count > 0
                ? $"Buy {string.Join(" + ", plan.Buy.Select(i => i.Name))} toward {plan.Target.Name}"
                : $"Save up: nothing from {plan.Target.Name} fits in {Math.Floor(gold):0} gold";

        return new PurchaseDto(
            plan.Target.Name,
            plan.Buy.Select(Map).ToList(),
            plan.Cost,
            gold - plan.Cost,
            plan.CompletesTarget,
            summary);
    }

    private ItemDto Map(Item item) => GameStateMapper.MapItem(item, 1, _patch);

    private static string Pct(double fraction) => (fraction * 100).ToString("0", Invariant) + "%";
}
