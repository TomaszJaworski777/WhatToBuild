using WhatToBuild.Data;
using WhatToBuild.Modeling;
using WhatToBuild.Modeling.Simulation;

namespace WhatToBuild.SupportedChampions.Vi;

public sealed class ViChampion : ISupportedChampion
{
    private readonly ViKitData _data;

    public ViChampion(ViKitData data)
    {
        _data = data;
    }

    public string ChampionName => _data.Champion;

    public IReadOnlyList<string> SkillOrder => _data.SkillOrder;

    public IChampionKit NewFight() => new ViKit(_data);

    public IReadOnlyList<CastHint> Hints(FightSetup setup) => [];

    public SurvivalAbility? Survival(AbilityRanks ranks) =>
        new("Blast Shield", 0, 1, 0, _data.Passive.CooldownLevel1);

    /// <summary>Blast Shield: a shield of her max health after she hits with an ability, counted once a fight.</summary>
    public SurvivalAbility? Survival(AbilityRanks ranks, ChampionState us, string? form, double enemyHealth)
    {
        var p = _data.Passive;
        var cooldown = p.CooldownLevel1 + p.CooldownPerLevel * (Math.Clamp(us.Level, 1, 18) - 1);
        return new SurvivalAbility("Blast Shield", 0, 1, p.ShieldMaxHealth * us.MaxHealth, cooldown);
    }
}

/// <summary>
/// Vi weaves her combo with an attack after each ability: R, E, fully charged Q, E. Each
/// ability resets the attack timer, so an attack lands between every two casts. Q is charged in
/// full before it is let go, E empowers the attack that follows it, and every third attack on the
/// target procs Denting Blows (W): a slice of its max health, armor shred and attack speed.
/// </summary>
public sealed class ViKit : IChampionKit
{
    public const string VaultBreaker = "Q Vault Breaker";
    public const string DentingBlows = "W Denting Blows";
    public const string RelentlessForce = "E Relentless Force";
    public const string CeaseAndDesist = "R Cease and Desist";

    private readonly ViKitData _data;

    private double _qReadyAt;
    private double _qReleaseAt = double.MaxValue;
    private double _rReadyAt;
    private double _rHitAt = double.MaxValue;
    private double _eCharges = -1;
    private double _eRechargeAt = double.MaxValue;
    private double _eReadyAt;
    private bool _empowered;
    private bool _weave = true;
    private int _hits;

    public ViKit(ViKitData data)
    {
        _data = data;
    }

    public void Update(Fight fight)
    {
        Recharge(fight);

        if (fight.Time >= _qReleaseAt)
        {
            ReleaseQ(fight);
        }

        if (fight.Time >= _rHitAt)
        {
            LandR(fight);
        }

        if (fight.Time < fight.AttacksBlockedUntil || !_weave)
        {
            return;
        }

        // The combo, an attack after each: R, E, fully charged Q, E. A step that is not ready
        // is passed over for the next one that is, and the combo carries on after whatever was cast.
        for (var k = 0; k < Combo.Length; k++)
        {
            var step = (_step + k) % Combo.Length;
            if (Cast(fight, Combo[step]))
            {
                _step = (step + 1) % Combo.Length;
                return;
            }
        }
    }

    private static readonly char[] Combo = ['R', 'E', 'Q', 'E'];

    private int _step;

    private bool Cast(Fight fight, char ability) => ability switch
    {
        'R' => CastR(fight),
        'E' => CastE(fight),
        'Q' => CastQ(fight),
        _ => false,
    };

    public void OnAttack(Fight fight)
    {
        _weave = true;

        if (_empowered)
        {
            _empowered = false;
            // The empowered attack replaces the plain one: add what it deals beyond it.
            var extra = EDamage(fight) - fight.Attacker.Stats.AttackDamage;
            if (extra > 0)
            {
                fight.Deal(RelentlessForce, fight.Physical(extra).Attack());
            }
        }

        ProcDentingBlows(fight);
    }

    private static void Casting(Fight fight, double seconds) =>
        fight.AttacksBlockedUntil = Math.Max(fight.AttacksBlockedUntil, fight.Time + seconds);

    private void Woven(Fight fight)
    {
        _weave = false;
        fight.ResetAttack();
    }

    private bool CastQ(Fight fight)
    {
        var rank = fight.Ranks.Q;
        if (rank <= 0 || fight.Time < _qReadyAt || _qReleaseAt != double.MaxValue)
        {
            return false;
        }

        // Charging: no attacks until it is let go, and nothing else is cast meanwhile.
        _qReleaseAt = fight.Time + _data.Q.ChargeSeconds;
        Casting(fight, _data.Q.ChargeSeconds);
        _weave = false;
        return true;
    }

    private void ReleaseQ(Fight fight)
    {
        _qReleaseAt = double.MaxValue;
        fight.Deal(VaultBreaker, fight.Physical(QDamage(fight, fight.Ranks.Q)).Ability());
        fight.Cast();
        _qReadyAt = fight.Time + fight.Cooldown(ViKitData.AtRank(_data.Q.Cooldown, fight.Ranks.Q));
        Woven(fight);
    }

    public double QDamage(Fight fight, int rank) =>
        (ViKitData.AtRank(_data.Q.Damage, rank) + _data.Q.BonusAdRatio * AttackerHits.BonusAttackDamage(fight.Attacker))
        * _data.Q.MaxDamageMultiplier;

    private bool CastR(Fight fight)
    {
        var rank = fight.Ranks.R;
        // Her engage: it needs no earlier hit, only a champion to lock on to.
        if (rank <= 0 || fight.Time < _rReadyAt || fight.Target is not ChampionState)
        {
            return false;
        }

        _rHitAt = fight.Time + _data.R.CastTime + _data.R.TravelSeconds;
        Casting(fight, _data.R.CastTime + _data.R.TravelSeconds);
        _rReadyAt = fight.Time + fight.Cooldown(ViKitData.AtRank(_data.R.Cooldown, rank));
        _weave = false;
        return true;
    }

    private void LandR(Fight fight)
    {
        _rHitAt = double.MaxValue;
        fight.Deal(CeaseAndDesist, fight.Physical(RDamage(fight)).Ability());
        fight.Cast();
        fight.Ultimate();
        Woven(fight);
    }

    public double RDamage(Fight fight) =>
        ViKitData.AtRank(_data.R.Damage, fight.Ranks.R) + _data.R.BonusAdRatio * AttackerHits.BonusAttackDamage(fight.Attacker);

    private void Recharge(Fight fight)
    {
        var e = _data.E;
        if (_eCharges < 0)
        {
            _eCharges = e.Charges;
        }

        if (fight.Time >= _eRechargeAt)
        {
            _eCharges++;
            _eRechargeAt = _eCharges < e.Charges
                ? fight.Time + fight.Cooldown(ViKitData.AtRank(e.Recharge, fight.Ranks.E))
                : double.MaxValue;
        }
    }

    private bool CastE(Fight fight)
    {
        var rank = fight.Ranks.E;
        if (rank <= 0 || _eCharges < 1 || fight.Time < _eReadyAt || _empowered)
        {
            return false;
        }

        _eCharges--;
        if (_eRechargeAt == double.MaxValue)
        {
            _eRechargeAt = fight.Time + fight.Cooldown(ViKitData.AtRank(_data.E.Recharge, rank));
        }

        _eReadyAt = fight.Time + _data.E.StaticCooldown;
        _empowered = true;
        fight.Cast();
        Woven(fight);
        return true;
    }

    public double EDamage(Fight fight) =>
        ViKitData.AtRank(_data.E.Damage, fight.Ranks.E)
        + _data.E.TotalAdRatio * fight.Attacker.Stats.AttackDamage
        + _data.E.ApRatio * fight.Attacker.Stats.AbilityPower;

    private void ProcDentingBlows(Fight fight)
    {
        var rank = fight.Ranks.W;
        var w = _data.W;
        if (rank <= 0 || ++_hits < w.HitsToProc)
        {
            return;
        }

        _hits = 0;
        var damage = WDamage(fight, rank);
        fight.Deal(DentingBlows, fight.Physical(damage));
        fight.ShredArmor(w.ArmorShred, w.BuffDuration);
        fight.AddAttackSpeed(ViKitData.AtRank(w.AttackSpeed, rank), w.BuffDuration);
    }

    public double WDamage(Fight fight, int rank)
    {
        var w = _data.W;
        var damage = (ViKitData.AtRank(w.MaxHealth, rank) + w.MaxHealthPerBonusAd * AttackerHits.BonusAttackDamage(fight.Attacker))
                     * fight.Target.MaxHealth;

        return fight.Target is NeutralState { Neutral.Kind: not NeutralKind.Minion } && w.MonsterCap > 0
            ? Math.Min(damage, w.MonsterCap)
            : damage;
    }
}
