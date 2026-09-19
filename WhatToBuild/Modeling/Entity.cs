using WhatToBuild.Data;

namespace WhatToBuild.Modeling;

public sealed record CombatAssumption(double Stacks);

public abstract class Entity
{
    private double? _currentHealth;

    public abstract string Name { get; }

    public abstract StatSheet Stats { get; }

    public virtual double BonusHealth => 0;

    public virtual IReadOnlyList<Mitigation> Mitigations => [];

    public List<Shield> Shields { get; } = new();

    public double MaxHealth => Stats.Health;

    public double CurrentHealth
    {
        get => _currentHealth ?? MaxHealth;
        set => _currentHealth = Math.Clamp(value, 0, MaxHealth);
    }

    public double HealthPercent => MaxHealth > 0 ? CurrentHealth / MaxHealth : 0;

    public double? Measure(ConditionProperty property) => property switch
    {
        ConditionProperty.HealthPercent => HealthPercent,
        ConditionProperty.BonusHealth => BonusHealth,
        ConditionProperty.MaxHealth => MaxHealth,
        ConditionProperty.Armor => Stats.Armor,
        ConditionProperty.MagicResist => Stats.MagicResist,
        ConditionProperty.Level => (this as ChampionState)?.Level,
        _ => null,
    };

    public bool Satisfies(IEnumerable<EffectCondition> conditions) =>
        conditions.All(c => c.Subject == ConditionSubject.Target
                            && Measure(c.Property) is { } value
                            && c.IsMet(value));

    public Entity WithShield(double amount, DamageSource versus = DamageSource.All)
    {
        Shields.Add(new Shield(amount, versus));
        return this;
    }

    public Entity AtHealthPercent(double percent)
    {
        CurrentHealth = MaxHealth * percent;
        return this;
    }

    public override string ToString() => Name;
}

public sealed class ChampionState : Entity
{
    public ChampionState(
        Champion champion,
        int level,
        IEnumerable<Item>? items = null,
        IEnumerable<StatModifier>? teamBuffs = null,
        CombatAssumption? combat = null)
    {
        Champion = champion;
        Combat = combat;
        Level = Math.Clamp(level, StatCalculator.MinLevel, StatCalculator.MaxLevel);
        Items = (items ?? []).ToList();
        TeamBuffs = (teamBuffs ?? []).ToList();
        Stats = StatCalculator.ForChampion(champion, Level, Items, TeamBuffs, combat);
        BonusHealth = Items.Sum(i => i.Stats.Health);
        Mitigations = Items
            .SelectMany(i => i.Effects)
            .Where(e => e.Kind == EffectKind.DamageReduction && e.When.Count == 0)
            .Select(e => e.Amount < 1
                ? new Mitigation(e.Versus, Percent: e.Amount)
                : new Mitigation(e.Versus, Flat: e.Amount))
            .ToList();
    }

    public Champion Champion { get; }

    public int Level { get; }

    public IReadOnlyList<Item> Items { get; }

    public IReadOnlyList<StatModifier> TeamBuffs { get; }

    public CombatAssumption? Combat { get; }

    public override string Name => Champion.Name;

    public override StatSheet Stats { get; }

    public override double BonusHealth { get; }

    public override IReadOnlyList<Mitigation> Mitigations { get; }
}

public sealed class NeutralState : Entity
{
    public NeutralState(Neutral neutral, NeutralScaling scaling, double gameTimeSeconds = 0)
    {
        Neutral = neutral;
        GameTimeSeconds = gameTimeSeconds;
        Stats = StatCalculator.ForNeutral(neutral, scaling, gameTimeSeconds);
    }

    public Neutral Neutral { get; }

    public double GameTimeSeconds { get; }

    public override string Name => Neutral.Name;

    public override StatSheet Stats { get; }
}
