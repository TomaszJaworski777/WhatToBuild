using WhatToBuild.Data;
using WhatToBuild.Modeling;
using WhatToBuild.Modeling.Simulation;

namespace WhatToBuild.SupportedChampions.Kayn;

public static class KaynForm
{
    public const string Base = "Base";
    public const string Darkin = "Darkin";
    public const string ShadowAssassin = "ShadowAssassin";

    public static string Label(string form) => form switch
    {
        Darkin => "Rhaast",
        ShadowAssassin => "Shadow Assassin",
        _ => "Kayn",
    };
}

public sealed class KaynChampion : ISupportedChampion
{
    private readonly KaynKitData _data;

    public KaynChampion(KaynKitData data)
    {
        _data = data;
    }

    public string ChampionName => _data.Champion;

    public IReadOnlyList<string> SkillOrder => _data.SkillOrder;

    public IReadOnlyList<string> Forms => [KaynForm.Darkin, KaynForm.ShadowAssassin];

    public string DefaultForm => KaynForm.Darkin;

    public string BaseForm => KaynForm.Base;

    public string FormLabel(string form) => KaynForm.Label(form);

    public IChampionKit NewFight() => new KaynKit(_data, KaynForm.Base);

    public IChampionKit NewFight(string? form) => new KaynKit(_data, form ?? KaynForm.Base);

    public string? DetectForm(IReadOnlyDictionary<string, string> abilityIds) =>
        abilityIds.TryGetValue("W", out var w) && w.Contains("KaynAss", StringComparison.OrdinalIgnoreCase)
            ? KaynForm.ShadowAssassin
            : null;

    public IReadOnlyList<CastHint> Hints(FightSetup setup) => [];

    public SurvivalAbility? Survival(AbilityRanks ranks) =>
        ranks.R <= 0 ? null : new SurvivalAbility("Umbral Trespass", _data.R.InfestDuration, 1, 0, KaynKitData.AtRank(_data.R.Cooldown, ranks.R));

    public SurvivalAbility? Survival(AbilityRanks ranks, ChampionState us, string? form, double enemyHealth)
    {
        if (Survival(ranks) is not { } ability || form != KaynForm.Darkin)
        {
            return Survival(ranks);
        }

        var r = _data.R;
        return ability with { Heal = r.DarkinHeal * (r.DarkinMaxHealth + r.DarkinMaxHealthPerBonusAd * AttackerHits.BonusAttackDamage(us)) * enemyHealth };
    }

    public double DamageHealShare(ChampionState us, string? form) =>
        form == KaynForm.Darkin
            ? _data.Passive.DarkinHealing + _data.Passive.DarkinHealingPerBonusHealth * us.BonusHealth
            : 0;
}

public sealed class KaynKit : IChampionKit
{
    public const string ReapingSlash = "Q Reaping Slash";
    public const string BladesReach = "W Blade's Reach";
    public const string UmbralTrespass = "R Umbral Trespass";
    public const string ShadowPassive = "Passive The Darkin Scythe";

    private readonly KaynKitData _data;
    private readonly string _form;

    private double _qReadyAt;
    private double _wReadyAt;
    private double _rReadyAt;
    private double _exitAt = double.MaxValue;
    private bool _started;
    private double _passiveUntil = double.MinValue;
    private double _passiveReadyAt;

    public KaynKit(KaynKitData data, string form)
    {
        _data = data;
        _form = form;
    }

    public string Form => _form;

    private bool Darkin => _form == KaynForm.Darkin;

    private bool Assassin => _form == KaynForm.ShadowAssassin;

    public void Update(Fight fight)
    {
        if (!_started)
        {
            _started = true;
            OpenPassive(fight);
        }

        if (fight.Time >= _exitAt)
        {
            ExitR(fight);
        }

        if (fight.Time < fight.AttacksBlockedUntil || !_weave)
        {
            return;
        }

        // Spell, attack, spell, attack: each ability resets the attack timer, so one lands
        // between every two casts, and never two casts in the same instant.
        _ = CastW(fight) || CastQ(fight) || CastR(fight);
    }

    public void OnAttack(Fight fight)
    {
        _weave = true;
    }

    private bool _weave = true;

    private void Woven(Fight fight)
    {
        _weave = false;
        fight.ResetAttack();
    }

    public double AttackUptime => _data.AttackUptime;

    private static void Casting(Fight fight, double seconds) =>
        fight.AttacksBlockedUntil = Math.Max(fight.AttacksBlockedUntil, fight.Time + seconds);

    public void OnDamage(Fight fight, string source, double damage)
    {
        if (!Assassin || source == ShadowPassive || fight.Time > _passiveUntil || damage <= 0)
        {
            return;
        }

        fight.Deal(ShadowPassive, fight.Magic(PassiveShare(fight.Attacker.Level) * damage));
    }

    public double PassiveShare(int level)
    {
        var p = _data.Passive;
        return p.AssassinMin + (p.AssassinMax - p.AssassinMin) * (Math.Clamp(level, 1, 18) - 1) / 17.0;
    }

    private void OpenPassive(Fight fight)
    {
        if (!Assassin || fight.Time < _passiveReadyAt)
        {
            return;
        }

        _passiveUntil = fight.Time + _data.Passive.AssassinDuration;
        _passiveReadyAt = fight.Time + _data.Passive.AssassinCooldown;
    }

    private bool CastQ(Fight fight)
    {
        var rank = fight.Ranks.Q;
        if (rank <= 0 || fight.Time < _qReadyAt)
        {
            return false;
        }

        for (var hit = 0; hit < _data.Q.Hits; hit++)
        {
            fight.Deal(ReapingSlash, fight.Physical(QHit(fight, rank)).Ability());
        }

        fight.Cast();
        Casting(fight, _data.Q.CastTime);
        _qReadyAt = fight.Time + fight.Cooldown(KaynKitData.AtRank(_data.Q.Cooldown, rank));
        Woven(fight);
        return true;
    }

    public double QHit(Fight fight, int rank)
    {
        var q = _data.Q;
        var bonusAd = AttackerHits.BonusAttackDamage(fight.Attacker);

        var damage = Darkin
            ? q.DarkinTotalAdRatio * fight.Attacker.Stats.AttackDamage
              + (q.DarkinMaxHealth + q.DarkinMaxHealthPer100BonusAd * bonusAd / 100) * fight.Target.MaxHealth
            : KaynKitData.AtRank(q.Damage, rank) + q.BonusAdRatio * bonusAd;

        if (fight.Target is NeutralState { Neutral.Kind: not NeutralKind.Minion })
        {
            damage += q.MonsterBonus;
            var cap = KaynKitData.AtRank(q.MonsterCap, rank);
            if (cap > 0)
            {
                damage = Math.Min(damage, cap);
            }
        }

        return damage;
    }

    private bool CastW(Fight fight)
    {
        var rank = fight.Ranks.W;
        if (rank <= 0 || fight.Time < _wReadyAt)
        {
            return false;
        }

        var w = _data.W;
        var baseDamage = KaynKitData.AtRank(Assassin ? w.AssassinDamage : w.Damage, rank);
        fight.Deal(BladesReach, fight.Physical(baseDamage + w.BonusAdRatio * AttackerHits.BonusAttackDamage(fight.Attacker)).Ability());
        fight.Cast();
        Casting(fight, Assassin ? w.AssassinCastTime : w.CastTime);
        _wReadyAt = fight.Time + fight.Cooldown(KaynKitData.AtRank(w.Cooldown, rank));
        Woven(fight);
        return true;
    }

    private bool CastR(Fight fight)
    {
        var rank = fight.Ranks.R;
        if (rank <= 0 || fight.Time < _rReadyAt || fight.DamageDealt <= 0 || fight.Target is not ChampionState)
        {
            return false;
        }

        _exitAt = fight.Time + _data.R.CastTime + _data.R.MinimumInfest;
        Casting(fight, _data.R.CastTime + _data.R.MinimumInfest);
        _rReadyAt = fight.Time + fight.Cooldown(KaynKitData.AtRank(_data.R.Cooldown, rank));
        _weave = false;
        return true;
    }

    private void ExitR(Fight fight)
    {
        _exitAt = double.MaxValue;
        fight.ResetAttack();
        fight.Deal(UmbralTrespass, fight.Physical(RDamage(fight)).Ability());
        fight.Cast();
        fight.Ultimate();

        if (Assassin)
        {
            _passiveReadyAt = fight.Time;
            OpenPassive(fight);
        }
    }

    public double RDamage(Fight fight)
    {
        var r = _data.R;
        var bonusAd = AttackerHits.BonusAttackDamage(fight.Attacker);

        return Darkin
            ? (r.DarkinMaxHealth + r.DarkinMaxHealthPerBonusAd * bonusAd) * fight.Target.MaxHealth
            : KaynKitData.AtRank(r.Damage, fight.Ranks.R) + r.BonusAdRatio * bonusAd;
    }
}
