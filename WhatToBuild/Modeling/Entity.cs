using WhatToBuild.Data;

namespace WhatToBuild.Modeling;

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
        ConditionProperty.IsMonster => this is NeutralState ? 1 : 0,
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
    private static readonly Stat[] SheetStats =
    [
        Data.Stats.Health, Data.Stats.AttackDamage, Data.Stats.AbilityPower,
        Data.Stats.Armor, Data.Stats.MagicResist, Data.Stats.Mana,
    ];

    private readonly double _healthBeforeStacks;

    public ChampionState(
        Champion champion,
        int level,
        IEnumerable<Item>? items = null,
        IEnumerable<StatModifier>? teamBuffs = null,
        StatSheet? adjustment = null,
        IReadOnlyDictionary<Guid, double>? itemStacks = null)
    {
        Champion = champion;
        Level = Math.Clamp(level, StatCalculator.MinLevel, StatCalculator.MaxLevel);
        Items = (items ?? []).ToList();
        TeamBuffs = (teamBuffs ?? []).ToList();
        ItemStacks = itemStacks ?? new Dictionary<Guid, double>();
        Stats = StatCalculator.ForChampion(champion, Level, Items, TeamBuffs);
        _healthBeforeStacks = Stats.Health;

        foreach (var stat in SheetStats)
        {
            StatCalculator.AddStat(Stats, stat, StackValue(stat));
        }

        StatCalculator.Apply(Stats, adjustment);
        BonusHealth = Items.Sum(i => i.Stats.Health) + StackValue(Data.Stats.Health);
        Mitigations = Items
            .SelectMany(i => i.Effects)
            .Where(e => e.Kind == EffectKind.DamageReduction && e.When.Count == 0)
            .Select(e => e.Amount < 1
                ? new Mitigation(e.Versus, Percent: e.Amount * (champion.IsRanged ? e.RangedMultiplier : 1))
                : new Mitigation(e.Versus, Flat: e.Amount))
            .ToList();
    }

    public Champion Champion { get; }

    public int Level { get; }

    public IReadOnlyList<Item> Items { get; }

    public IReadOnlyList<StatModifier> TeamBuffs { get; }

    public IReadOnlyDictionary<Guid, double> ItemStacks { get; }

    public double StacksOf(Item item) => ItemStacks.GetValueOrDefault(item.Id);

    public double StackValue(Stat stat) =>
        Items.Distinct()
            .Where(i => i.Stacking is not null)
            .SelectMany(i => i.Stacking!.Gains.Where(g => g.Stat == stat).Select(g => GainValue(i, g)))
            .Sum();

    public double GainValue(Item item, StackGain gain)
    {
        return (gain.Amount + gain.PerMaxHealth * _healthBeforeStacks) * StacksOf(item);
    }

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
