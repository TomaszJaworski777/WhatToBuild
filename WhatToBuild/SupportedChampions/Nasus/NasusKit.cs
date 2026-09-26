using WhatToBuild.Data;
using WhatToBuild.Modeling;
using WhatToBuild.Modeling.Simulation;

namespace WhatToBuild.SupportedChampions.Nasus;

public sealed class NasusChampion : ISupportedChampion
{
    private readonly NasusKitData _data;

    public NasusChampion(NasusKitData data)
    {
        _data = data;
    }

    public string ChampionName => _data.Champion;

    public IReadOnlyList<string> SkillOrder => _data.SkillOrder;

    public IChampionKit NewFight() => new NasusKit(_data);

    public IReadOnlyList<CastHint> Hints(FightSetup setup) => [];

    /// <summary>Soul Eater: lifesteal that grows at levels 7 and 13.</summary>
    public double LifeSteal(ChampionState us, string? form) =>
        _data.Passive.Lifesteal + _data.Passive.LifestealSteps.Where(s => s.Count >= 2 && us.Level >= s[0]).Sum(s => s[1]);

    /// <summary>Fury of the Sands: bonus health, armor and magic resist while it is up.</summary>
    public FightStats? Stats(AbilityRanks ranks)
    {
        if (ranks.R <= 0)
        {
            return null;
        }

        var r = _data.R;
        var resists = NasusKitData.AtRank(r.Resists, ranks.R);
        var stats = new StatSheet
        {
            Health = NasusKitData.AtRank(r.BonusHealth, ranks.R),
            Armor = resists,
            MagicResist = resists,
        };

        return new FightStats(stats, NasusKitData.AtRank(r.Cooldown, ranks.R));
    }

    /// <summary>
    /// Wither on whoever hits you hardest: its slow ramps from the base to the rank's maximum
    /// over its duration and takes attack speed at a share of the slow. The cooldown starts when
    /// it is cast, so a fight holds one cast, and a second only if the cooldown runs out inside it.
    /// </summary>
    public double AttackCut(AbilityRanks ranks, ChampionState us, double fightSeconds)
    {
        if (ranks.W <= 0 || fightSeconds <= 0)
        {
            return 0;
        }

        var w = _data.W;
        var cooldown = NasusKitData.AtRank(w.Cooldown, ranks.W) * 100 / (100 + us.Stats.AbilityHaste);
        var covered = 0.0;
        for (var cast = 0.0; cast < fightSeconds; cast += Math.Max(cooldown, 0.1))
        {
            covered += Math.Min(w.Duration, fightSeconds - cast);
        }

        var averageSlow = Math.Min(1, (w.SlowBase + NasusKitData.AtRank(w.SlowMax, ranks.W)) / 2);
        return Math.Clamp(covered / fightSeconds, 0, 1) * w.AttackSpeedSlowRatio * averageSlow;
    }
}

/// <summary>
/// Nasus's script, in priority each tick: Fury of the Sands first against a champion (it halves Q's
/// cooldown and burns a share of the target's max health every tick), then Siphoning Strike whenever
/// it is up, then Spirit Fire. Q has no animation and resets the attack timer: the attack after it
/// carries its damage and your stacks, and its cooldown starts when that attack lands. R and E have
/// animations and go between attacks. Spirit Fire hits, burns and shreds armor while it lasts. He
/// walks up and stays on the target.
/// </summary>
public sealed class NasusKit : ScriptedKit
{
    public const string SiphoningStrike = "Q Siphoning Strike";
    public const string SpiritFire = "E Spirit Fire";
    public const string FuryOfTheSands = "R Fury of the Sands";

    private readonly NasusKitData _data;

    private double _qReadyAt;
    private bool _empowered;
    private double _eReadyAt;
    private double _burnUntil = double.MinValue;
    private double _nextBurn;
    private double _rUntil = double.MinValue;
    private double _rReadyAt;
    private double _nextFury;

    public NasusKit(NasusKitData data)
    {
        _data = data;
    }

    protected override Playstyle DefinePlaystyle() => new(
        ComboOrder.Priority,
        [
            new ScriptedAbility("R", CastTiming.BetweenAttacks, CastR),
            new ScriptedAbility("Q", CastTiming.Instant, CastQ, ResetsAttack: true, EmpowersAttack: true),
            new ScriptedAbility("E", CastTiming.BetweenAttacks, CastE),
        ],
        Kiting.StandAndFight,
        Burst: ["E", "Q"]);

    protected override ScriptedKit Fresh(bool burst) => new NasusKit(_data);

    /// <summary>Spirit Fire's burn and Fury's aura tick.</summary>
    protected override void BeforeCasts(Fight fight)
    {
        Burn(fight);
        Fury(fight);
    }

    protected override void Attacked(Fight fight)
    {
        if (!_empowered)
        {
            return;
        }

        // The empowered attack is a plain attack plus Q's damage and the stacks.
        _empowered = false;
        var q = _data.Q;
        fight.Deal(SiphoningStrike, fight.Physical(NasusKitData.AtRank(q.Damage, fight.Ranks.Q) + fight.Stacks).Attack());

        var cooldown = NasusKitData.AtRank(q.Cooldown, fight.Ranks.Q) * (fight.Time < _rUntil ? 1 - _data.R.QCooldownReduction : 1);
        _qReadyAt = fight.Time + fight.Cooldown(cooldown);
    }

    private bool CastQ(Fight fight)
    {
        if (fight.Ranks.Q <= 0 || _empowered || fight.Time < _qReadyAt)
        {
            return false;
        }

        _empowered = true;
        fight.Cast();
        return true;
    }

    private bool CastE(Fight fight)
    {
        var rank = fight.Ranks.E;
        if (rank <= 0 || fight.Time < _eReadyAt)
        {
            return false;
        }

        var e = _data.E;
        fight.Deal(SpiritFire, fight.Magic(NasusKitData.AtRank(e.Damage, rank) + e.ApRatio * fight.Attacker.Stats.AbilityPower).Ability());
        fight.ShredArmor(NasusKitData.AtRank(e.ArmorShred, rank), e.Duration);
        fight.Cast();
        _burnUntil = fight.Time + e.Duration;
        _nextBurn = fight.Time + 1;
        fight.Casting(e.CastTime);
        _eReadyAt = fight.Time + fight.Cooldown(e.Cooldown);
        return true;
    }

    private void Burn(Fight fight)
    {
        if (fight.Time > _burnUntil || fight.Time < _nextBurn)
        {
            return;
        }

        var e = _data.E;
        fight.Deal(SpiritFire, fight.Magic(NasusKitData.AtRank(e.TickDamage, fight.Ranks.E) + e.TickApRatio * fight.Attacker.Stats.AbilityPower).Ability());
        _nextBurn += 1;
    }

    private bool CastR(Fight fight)
    {
        var rank = fight.Ranks.R;
        if (rank <= 0 || fight.Time < _rReadyAt || fight.Target is not ChampionState)
        {
            return false;
        }

        var r = _data.R;
        _rUntil = fight.Time + r.Duration;
        _nextFury = fight.Time + r.TickSeconds;
        _rReadyAt = fight.Time + fight.Cooldown(NasusKitData.AtRank(r.Cooldown, rank));
        fight.Casting(r.CastTime);
        fight.Cast();
        fight.Ultimate();
        return true;
    }

    /// <summary>Fury's aura: a share of the target's max health every tick while R is up.</summary>
    private void Fury(Fight fight)
    {
        if (fight.Time > _rUntil || fight.Time < _nextFury)
        {
            return;
        }

        var r = _data.R;
        var perSecond = NasusKitData.AtRank(r.MaxHealthPerSecond, fight.Ranks.R) + r.MaxHealthPerSecondPerAp * fight.Attacker.Stats.AbilityPower;
        var damage = perSecond * r.TickSeconds * fight.Target.MaxHealth;
        if (fight.Target is NeutralState && r.MonsterCap > 0)
        {
            damage = Math.Min(damage, r.MonsterCap);
        }

        fight.Deal(FuryOfTheSands, fight.Magic(damage));
        _nextFury += r.TickSeconds;
    }
}
