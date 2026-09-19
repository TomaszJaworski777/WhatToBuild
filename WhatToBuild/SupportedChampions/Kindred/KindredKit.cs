using WhatToBuild.Data;
using WhatToBuild.Modeling;
using WhatToBuild.Modeling.Simulation;

namespace WhatToBuild.SupportedChampions.Kindred;

public sealed class KindredChampion : ISupportedChampion
{
    private readonly KindredKitData _data;

    public KindredChampion(KindredKitData data)
    {
        _data = data;
    }

    public string ChampionName => _data.Champion;

    public IReadOnlyList<string> SkillOrder => _data.SkillOrder;

    public IChampionKit NewFight() => new KindredKit(_data);

    public SurvivalAbility? Survival(AbilityRanks ranks)
    {
        var r = _data.R;
        return ranks.R <= 0
            ? null
            : new SurvivalAbility("Lamb's Respite", r.Duration, r.MinimumHealthPercent, KindredKitData.AtRank(r.Heal, ranks.R), KindredKitData.AtRank(r.Cooldown, ranks.R));
    }

    public IReadOnlyList<CastHint> Hints(FightSetup setup) =>
        new KindredKit(_data).EHint(new Fight(setup)) is { } e ? [e] : [];
}

public sealed class KindredKit : IChampionKit
{
    public const string DanceOfArrows = "Q Dance of Arrows";
    public const string WolfsFrenzy = "W Wolf's Frenzy";
    public const string MountingDread = "E Mounting Dread";

    private const double ProbeDamage = 1000;

    private const double CastPointRefresh = 0.1;

    private readonly KindredKitData _data;
    private readonly bool _holdE;

    private double _qReadyAt;
    private double _wReadyAt;
    private double _eReadyAt;
    private double _zoneUntil = double.MinValue;
    private double _nextBite;
    private int _eAttacksLeft;
    private double _eExpiresAt;
    private double _castAt;
    private double _castAtUntil = double.MinValue;

    public KindredKit(KindredKitData data, bool holdE = true)
    {
        _data = data;
        _holdE = holdE;
    }

    public bool InZone(Fight fight) => fight.Time < _zoneUntil;

    public void Update(Fight fight)
    {
        CastW(fight);
        CastQ(fight);
        CastE(fight);
        WolfBites(fight);
    }

    public void OnAttack(Fight fight)
    {
        if (_eAttacksLeft <= 0 || fight.Time > _eExpiresAt)
        {
            return;
        }

        _eAttacksLeft--;
        _eExpiresAt = fight.Time + _data.E.Window;

        if (_eAttacksLeft == 0)
        {
            Pounce(fight);
        }
    }

    private void CastW(Fight fight)
    {
        var rank = fight.Ranks.W;
        if (rank <= 0 || fight.Time < _wReadyAt)
        {
            return;
        }

        _zoneUntil = fight.Time + _data.W.ZoneDuration;
        fight.Cast();
        _nextBite = fight.Time;
        _wReadyAt = fight.Time + fight.Cooldown(KindredKitData.AtRank(_data.W.Cooldown, rank));
    }

    private void CastQ(Fight fight)
    {
        var rank = fight.Ranks.Q;
        if (rank <= 0 || fight.Time < _qReadyAt)
        {
            return;
        }

        var q = _data.Q;
        fight.Deal(DanceOfArrows, fight.Physical(QDamage(fight)).Ability());
        fight.Cast();
        fight.AddAttackSpeed(q.AttackSpeed + q.AttackSpeedPerMark * fight.Stacks, q.AttackSpeedDuration);

        var cooldown = InZone(fight) ? KindredKitData.AtRank(q.CooldownInW, rank) : q.Cooldown;
        _qReadyAt = fight.Time + fight.Cooldown(cooldown);
    }

    private void CastE(Fight fight)
    {
        var rank = fight.Ranks.E;
        if (rank <= 0 || fight.Time < _eReadyAt || !WorthCastingE(fight, rank))
        {
            return;
        }

        _eAttacksLeft = _data.E.AttacksAfterCast;
        fight.Cast();
        _eExpiresAt = fight.Time + _data.E.Window;
        _eReadyAt = fight.Time + fight.Cooldown(KindredKitData.AtRank(_data.E.Cooldown, rank));
    }

    private bool WorthCastingE(Fight fight, int rank)
    {
        if (!_holdE)
        {
            return true;
        }

        var target = fight.Target;
        if (fight.Time >= _castAtUntil)
        {
            _castAt = OptimalCastHealth(fight, rank);
            _castAtUntil = fight.Time + CastPointRefresh;
        }

        var castAt = _castAt;

        if (target.CurrentHealth <= castAt)
        {
            return true;
        }

        var dps = fight.DamageDealt / Elapsed(fight);
        if (dps <= 0)
        {
            return false;
        }

        var timeToCastPoint = (target.CurrentHealth - castAt) / dps;
        return timeToCastPoint >= fight.Cooldown(KindredKitData.AtRank(_data.E.Cooldown, rank));
    }

    public double OptimalCastHealth(Fight fight, int rank)
    {
        var killingHealth = KillingHealth(fight, rank);
        var window = _data.E.AttacksAfterCast / fight.AttackSpeed;
        var castAt = killingHealth + AttacksBeforePounce(fight);

        if (fight.Ranks.W > 0)
        {
            var interval = 1 / WolfAttackSpeed(fight);
            var end = Math.Min(fight.Time + window, _zoneUntil);

            for (var bite = Math.Max(_nextBite, fight.Time); bite < end; bite += interval)
            {
                castAt += WolfBiteHit(fight, killingHealth);
            }
        }

        if (fight.Ranks.Q > 0 && _qReadyAt <= fight.Time + window)
        {
            castAt += QHit(fight);
        }

        return castAt;
    }

    public CastHint? EHint(Fight fight)
    {
        var rank = fight.Ranks.E;
        if (rank <= 0)
        {
            return null;
        }

        var killingHealth = KillingHealth(fight, rank);
        var additions = new List<CastAddition>();

        if (fight.Ranks.Q > 0)
        {
            additions.Add(new CastAddition("if Q hits in between", QHit(fight)));
        }

        if (fight.Ranks.W > 0)
        {
            additions.Add(new CastAddition("per wolf bite in between", WolfBiteHit(fight, killingHealth)));
        }

        return new CastHint("E", killingHealth + AttacksBeforePounce(fight), killingHealth, additions);
    }

    private double KillingHealth(Fight fight, int rank)
    {
        var e = _data.E;
        var flat = KindredKitData.AtRank(e.Damage, rank) + e.BonusAdRatio * AttackerHits.BonusAttackDamage(fight.Attacker);
        var percent = e.MissingHealth + e.MissingHealthPerMark * fight.Stacks;

        var mitigation = PounceHit(fight, ProbeDamage).Run().HealthDamage / ProbeDamage;
        var scale = mitigation * CritBonus(fight.Attacker);

        var killing = scale * (flat + percent * fight.Target.MaxHealth) / (1 + scale * percent);

        if (IsMonster(fight.Target) && percent * (fight.Target.MaxHealth - killing) > _data.E.MonsterCap)
        {
            killing = scale * (flat + _data.E.MonsterCap);
        }

        return killing;
    }

    private static bool IsMonster(Entity target) => target is NeutralState { Neutral.Kind: not NeutralKind.Minion };

    private double AttacksBeforePounce(Fight fight)
    {
        var attacks = _data.E.AttacksAfterCast;

        var counterProcs = fight.Attacker.Items
            .SelectMany(i => i.Effects)
            .Where(e => e.Trigger == EffectTrigger.OnAttack && !e.Splash && e.EveryAttacks > 0)
            .Where(e => fight.Target.Satisfies(e.When))
            .Select(e => (Procs: Math.Floor(attacks / e.EveryAttacks), Hit: AttackerHits.ForEffect(e, fight.Attacker, fight.Target)))
            .Where(p => p.Hit is not null)
            .Sum(p => p.Procs * p.Hit!.Attack().Run().HealthDamage);

        return attacks * ExpectedAttackHit(fight) + counterProcs;
    }

    private double QHit(Fight fight) => fight.Physical(QDamage(fight)).Ability().Run().HealthDamage;

    private double WolfBiteHit(Fight fight, double targetHealth) =>
        fight.Magic(WolfBiteDamage(fight, targetHealth)).Ability().Run().HealthDamage;

    private static DamageCalculator PounceHit(Fight fight, double amount) => fight.Physical(amount).Ability();

    private double QDamage(Fight fight) =>
        KindredKitData.AtRank(_data.Q.Damage, fight.Ranks.Q)
        + _data.Q.BonusAdRatio * AttackerHits.BonusAttackDamage(fight.Attacker);

    private double WolfBiteDamage(Fight fight, double targetHealth)
    {
        var w = _data.W;
        var damage = KindredKitData.AtRank(w.Damage, fight.Ranks.W)
                     + w.BonusAdRatio * AttackerHits.BonusAttackDamage(fight.Attacker)
                     + w.ApRatio * fight.Attacker.Stats.AbilityPower
                     + (w.CurrentHealth + w.CurrentHealthPerMark * fight.Stacks) * targetHealth;

        return IsMonster(fight.Target) ? damage * (1 + w.MonsterBonusDamage) : damage;
    }

    private static double ExpectedAttackHit(Fight fight)
    {
        var attacker = fight.Attacker;
        var critChance = AttackerHits.CritChance(attacker);
        var ad = attacker.Stats.AttackDamage;

        var normal = fight.Physical(ad).Attack().Run().HealthDamage;
        var crit = critChance > 0 ? fight.Physical(ad * AttackerHits.CritDamage(attacker)).Crit().Run().HealthDamage : 0;

        return normal * (1 - critChance) + crit * critChance + EveryAttackProcs(fight);
    }

    private static double EveryAttackProcs(Fight fight) =>
        fight.Attacker.Items
            .SelectMany(i => i.Effects)
            .Where(e => e.Trigger == EffectTrigger.OnAttack && !e.Splash && e.Cooldown == 0 && e.EveryAttacks == 0)
            .Where(e => fight.Target.Satisfies(e.When))
            .Select(e => AttackerHits.ForEffect(e, fight.Attacker, fight.Target))
            .OfType<DamageCalculator>()
            .Sum(hit => hit.Attack().Run().HealthDamage);

    private double CritBonus(ChampionState attacker) =>
        1 + _data.E.CritChanceRatio * AttackerHits.CritChance(attacker) * (AttackerHits.CritDamage(attacker) - 1);

    private static double Elapsed(Fight fight) => Math.Max(fight.Time, 1 / fight.AttackSpeed);

    private void WolfBites(Fight fight)
    {
        var rank = fight.Ranks.W;
        if (rank <= 0 || !InZone(fight) || fight.Time < _nextBite)
        {
            return;
        }

        fight.Deal(WolfsFrenzy, fight.Magic(WolfBiteDamage(fight, fight.Target.CurrentHealth)).Ability());
        _nextBite = fight.Time + 1 / WolfAttackSpeed(fight);
    }

    private double WolfAttackSpeed(Fight fight)
    {
        var w = _data.W;
        var bonus = w.WolfAttackSpeedPerLevel * StatCalculator.GrowthMultiplier(fight.Attacker.Level)
                    + w.WolfShareOfBonusAttackSpeed * fight.BonusAttackSpeed;

        return Math.Min(StatCalculator.AttackSpeedCap, w.WolfAttackSpeed + w.WolfAttackSpeedRatio * bonus);
    }

    private void Pounce(Fight fight)
    {
        fight.Deal(MountingDread, PounceHit(fight, PounceRaw(fight)));
    }

    public double PounceDamage(Fight fight) => PounceHit(fight, PounceRaw(fight)).Run().HealthDamage;

    private double PounceRaw(Fight fight)
    {
        var e = _data.E;
        var attacker = fight.Attacker;
        var missing = fight.Target.MaxHealth - fight.Target.CurrentHealth;

        var fromMissing = (e.MissingHealth + e.MissingHealthPerMark * fight.Stacks) * missing;
        if (IsMonster(fight.Target))
        {
            fromMissing = Math.Min(fromMissing, e.MonsterCap);
        }

        var damage = KindredKitData.AtRank(e.Damage, fight.Ranks.E)
                     + e.BonusAdRatio * AttackerHits.BonusAttackDamage(attacker)
                     + fromMissing;

        return damage * CritBonus(attacker);
    }
}
