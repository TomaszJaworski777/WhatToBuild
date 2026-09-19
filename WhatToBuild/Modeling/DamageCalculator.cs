using WhatToBuild.Data;

namespace WhatToBuild.Modeling;

public sealed class DamageCalculator
{
    private readonly Entity _defender;
    private readonly DamageCalculation _pipeline = DamageCalculation.Standard();
    private readonly List<Item> _attackerItems = new();

    private DamageType _type = DamageType.Physical;
    private double _raw;
    private HitFlags _flags;

    private double _armorPenetrationPercent;
    private double _lethality;
    private double _magicPenetrationPercent;
    private double _magicPenetrationFlat;
    private double _damageAmp;
    private double _shieldReduction;
    private double _armorReductionFlat;
    private double _armorReductionPercent;
    private double _magicResistReductionFlat;
    private double _magicResistReductionPercent;
    private double? _attacksLanded;
    private bool _ranged;

    private DamageCalculator(Entity defender)
    {
        _defender = defender;
    }

    public static DamageCalculator Create(Entity defender) => new(defender);

    public DamageCalculator AdDamage(double amount) => Hit(DamageType.Physical, amount);

    public DamageCalculator ApDamage(double amount) => Hit(DamageType.Magic, amount);

    public DamageCalculator TrueDamage(double amount) => Hit(DamageType.True, amount);

    public DamageCalculator Attack() => Flag(HitFlags.Attack);

    public DamageCalculator Ability() => Flag(HitFlags.Ability);

    public DamageCalculator Crit() => Flag(HitFlags.Attack | HitFlags.Crit);

    public DamageCalculator ArmorPenetration(double percent) => Set(() => _armorPenetrationPercent = Stack(_armorPenetrationPercent, percent));

    public DamageCalculator Lethality(double flat) => Set(() => _lethality += flat);

    public DamageCalculator MagicPenetration(double percent) => Set(() => _magicPenetrationPercent = Stack(_magicPenetrationPercent, percent));

    public DamageCalculator FlatMagicPenetration(double flat) => Set(() => _magicPenetrationFlat += flat);

    public DamageCalculator DamageAmp(double percent) => Set(() => _damageAmp += percent);

    public DamageCalculator ShieldReduction(double percent) => Set(() => _shieldReduction = Stack(_shieldReduction, percent));

    public DamageCalculator ArmorReduction(double flat) => Set(() => _armorReductionFlat += flat);

    public DamageCalculator ArmorShred(double percent) => Set(() => _armorReductionPercent = Stack(_armorReductionPercent, percent));

    public DamageCalculator MagicResistReduction(double flat) => Set(() => _magicResistReductionFlat += flat);

    public DamageCalculator MagicResistShred(double percent) => Set(() => _magicResistReductionPercent = Stack(_magicResistReductionPercent, percent));

    public DamageCalculator AttacksLanded(double attacks) => Set(() => _attacksLanded = attacks);

    public DamageCalculator AttackerItems(IEnumerable<Item> items, bool ranged = false)
    {
        _attackerItems.AddRange(items);
        _ranged = ranged;
        return this;
    }

    public DamageCalculator With(IDamageStep step)
    {
        _pipeline.Then(step);
        return this;
    }

    public DamageCalculator Without<TStep>() where TStep : IDamageStep
    {
        _pipeline.Without<TStep>();
        return this;
    }

    public DamageResult Run()
    {
        var hit = new Hit(_type, _raw, _flags);
        var armorPen = _armorPenetrationPercent;
        var damageAmp = _damageAmp;
        var shieldReduction = _shieldReduction;
        var armorShred = _armorReductionPercent;
        var magicResistShred = _magicResistReductionPercent;

        foreach (var item in _attackerItems)
        {
            foreach (var effect in item.Effects.Where(e => ConditionsMet(e) && hit.Matches(e.Versus)))
            {
                var amount = effect.Amount * Built(effect, hit);

                switch (effect.Kind)
                {
                    case EffectKind.ShieldReduction:
                        shieldReduction = Stack(shieldReduction, amount * (_ranged ? effect.RangedMultiplier : 1));
                        break;
                    case EffectKind.ArmorShred:
                        armorShred = Stack(armorShred, amount);
                        break;
                    case EffectKind.MagicResistShred:
                        magicResistShred = Stack(magicResistShred, amount);
                        break;
                    case EffectKind.StatBuff when effect.Stat == Stats.DamageAmp:
                        damageAmp += amount;
                        break;
                    case EffectKind.StatBuff when effect.Stat == Stats.ArmorPenetrationPercent:
                        armorPen = Stack(armorPen, amount);
                        break;
                }
            }
        }

        var attacker = new Attacker
        {
            ArmorPenetrationPercent = armorPen,
            Lethality = _lethality,
            MagicPenetrationPercent = _magicPenetrationPercent,
            MagicPenetrationFlat = _magicPenetrationFlat,
            DamageAmp = damageAmp,
            ShieldReduction = shieldReduction,
        };

        var defender = new Defender
        {
            Armor = _defender.Stats.Armor,
            MagicResist = _defender.Stats.MagicResist,
            ArmorReductionFlat = _armorReductionFlat,
            ArmorReductionPercent = armorShred,
            MagicResistReductionFlat = _magicResistReductionFlat,
            MagicResistReductionPercent = magicResistShred,
            Mitigations = _defender.Mitigations,
            Shields = _defender.Shields.ToList(),
        };

        return _pipeline.Run(hit, attacker, defender);
    }

    private bool ConditionsMet(Effect effect) => _defender.Satisfies(effect.When);

    private double Built(Effect effect, Hit hit)
    {
        if (effect.StacksTo <= 0 || _attacksLanded is not { } landed)
        {
            return 1;
        }

        var before = landed - (hit.IsAttack ? 1 : 0);
        return Math.Clamp(before / effect.StacksTo, 0, 1);
    }

    private DamageCalculator Hit(DamageType type, double amount)
    {
        _type = type;
        _raw = amount;
        return this;
    }

    private DamageCalculator Flag(HitFlags flags)
    {
        _flags |= flags;
        return this;
    }

    private DamageCalculator Set(Action change)
    {
        change();
        return this;
    }

    private static double Stack(double current, double added) => 1 - (1 - current) * (1 - added);
}
