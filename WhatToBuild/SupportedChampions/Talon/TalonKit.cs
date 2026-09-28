using WhatToBuild.Data;
using WhatToBuild.Modeling;
using WhatToBuild.Modeling.Simulation;

namespace WhatToBuild.SupportedChampions.Talon;

public sealed class TalonChampion : ISupportedChampion
{
    private readonly TalonKitData _data;

    public TalonChampion(TalonKitData data)
    {
        _data = data;
    }

    public string ChampionName => _data.Champion;

    public IReadOnlyList<string> SkillOrder => _data.SkillOrder;

    public IChampionKit NewFight() => new TalonKit(_data);

    public IReadOnlyList<CastHint> Hints(FightSetup setup) => [];

    /// <summary>Shadow Assault: invisible for its duration, so nothing targets him meanwhile.</summary>
    public SurvivalAbility? Survival(AbilityRanks ranks) =>
        ranks.R <= 0 ? null : new SurvivalAbility("Shadow Assault", _data.R.Duration, 1, 0, TalonKitData.AtRank(_data.R.Cooldown, ranks.R));
}

/// <summary>
/// Talon's script, in priority: Shadow Assault first against a champion (his engage, from its
/// range: a ring of blades out, invisibility, and the blades back into his target when he next
/// attacks or casts Q, or back to him when it ends), then Rake, then Noxian Diplomacy, one spell
/// in each gap between attacks. Rake's blades hit on the way out and again, harder, on the way
/// back. Q leaps onto a target out of melee range and critically strikes one inside it. None of
/// them resets his attack timer. Every ability hit stacks Blade's End on the target, and an
/// attack at full stacks makes it bleed. Burst: R, W, Q, attack, which is exactly enough to
/// stack the bleed and proc it. He sticks to the target and never kites.
/// </summary>
public sealed class TalonKit : ScriptedKit
{
    public const string NoxianDiplomacy = "Q Noxian Diplomacy";
    public const string Rake = "W Rake";
    public const string ShadowAssault = "R Shadow Assault";
    public const string BladesEnd = "Passive Blade's End";

    private readonly TalonKitData _data;
    private readonly List<double> _stacks = new();

    private double _qReadyAt;
    private double _wReadyAt;
    private double _wReturnAt = double.MaxValue;
    private double _rReadyAt;
    private double _rReturnAt = double.MaxValue;

    public TalonKit(TalonKitData data)
    {
        _data = data;
    }

    protected override Playstyle DefinePlaystyle() => new(
        ComboOrder.Priority,
        [
            new ScriptedAbility("R", CastTiming.BetweenAttacks, CastR, Woven: true, OpensFromRange: true, Range: _data.R.Range),
            new ScriptedAbility("W", CastTiming.BetweenAttacks, CastW, Woven: true, Range: _data.W.Range),
            new ScriptedAbility("Q", CastTiming.BetweenAttacks, CastQ, Woven: true, Range: _data.Q.Range),
        ],
        Kiting.StandAndFight,
        Burst: ["R", "W", "Q", Attack]);

    protected override ScriptedKit Fresh(bool burst) => new TalonKit(_data);

    /// <summary>Rake's blades come back, and Shadow Assault's when his invisibility runs out.</summary>
    protected override void BeforeCasts(Fight fight)
    {
        if (fight.Time >= _wReturnAt)
        {
            _wReturnAt = double.MaxValue;
            var w = _data.W;
            Hit(fight, Rake, TalonKitData.AtRank(w.ReturnDamage, fight.Ranks.W) + w.ReturnBonusAdRatio * AttackerHits.BonusAttackDamage(fight.Attacker));
        }

        if (fight.Time >= _rReturnAt)
        {
            ReturnBlades(fight);
        }
    }

    protected override bool HitsPending(Fight fight) => _wReturnAt != double.MaxValue || _rReturnAt != double.MaxValue;

    /// <summary>An attack out of invisibility brings the blades back into the target, and one at full stacks makes it bleed.</summary>
    protected override void Attacked(Fight fight)
    {
        if (_rReturnAt != double.MaxValue)
        {
            ReturnBlades(fight);
        }

        var p = _data.Passive;
        _stacks.RemoveAll(t => fight.Time - t > p.StackDuration);
        if (_stacks.Count < p.Stacks || !Stacks(fight.Target))
        {
            return;
        }

        _stacks.Clear();
        var level = Math.Clamp(fight.Attacker.Level, 1, 18);
        var bleed = p.BleedLevel1 + (p.BleedLevel18 - p.BleedLevel1) * (level - 1) / 17.0
                    + p.BleedBonusAdRatio * AttackerHits.BonusAttackDamage(fight.Attacker);

        // The bleed runs over two seconds; it is dealt at once, as the burst is judged on it.
        fight.Deal(BladesEnd, fight.Physical(bleed));
    }

    private bool CastQ(Fight fight)
    {
        var rank = fight.Ranks.Q;
        if (rank <= 0 || fight.Time < _qReadyAt)
        {
            return false;
        }

        var q = _data.Q;
        var damage = TalonKitData.AtRank(q.Damage, rank) + q.BonusAdRatio * AttackerHits.BonusAttackDamage(fight.Attacker);
        if (fight.Distance > q.MeleeRange)
        {
            // A leap onto the target: it lands on him, and he stays on it.
            fight.Dash(q.Range);
        }
        else
        {
            damage *= q.MeleeCritMultiplier + AttackerHits.CritDamage(fight.Attacker) - AttackerHits.BaseCritDamage;
        }

        // Q out of invisibility brings the blades back into the target.
        if (_rReturnAt != double.MaxValue)
        {
            ReturnBlades(fight);
        }

        Hit(fight, NoxianDiplomacy, damage);
        fight.Cast();
        fight.Casting(q.CastTime);
        _qReadyAt = fight.Time + fight.Cooldown(TalonKitData.AtRank(q.Cooldown, rank));
        return true;
    }

    private bool CastW(Fight fight)
    {
        var rank = fight.Ranks.W;
        if (rank <= 0 || fight.Time < _wReadyAt)
        {
            return false;
        }

        var w = _data.W;
        Hit(fight, Rake, TalonKitData.AtRank(w.Damage, rank) + w.BonusAdRatio * AttackerHits.BonusAttackDamage(fight.Attacker));
        fight.Cast();
        fight.Casting(w.CastTime);
        _wReturnAt = fight.Time + w.ReturnSeconds;
        _wReadyAt = fight.Time + fight.Cooldown(TalonKitData.AtRank(w.Cooldown, rank));
        return true;
    }

    private bool CastR(Fight fight)
    {
        var rank = fight.Ranks.R;
        if (rank <= 0 || fight.Time < _rReadyAt || fight.Target is not ChampionState)
        {
            return false;
        }

        var r = _data.R;
        Hit(fight, ShadowAssault, RDamage(fight));
        fight.Cast();
        fight.Ultimate();
        fight.Casting(r.CastTime);
        _rReturnAt = fight.Time + r.Duration;
        _rReadyAt = fight.Time + fight.Cooldown(TalonKitData.AtRank(r.Cooldown, rank));
        return true;
    }

    private void ReturnBlades(Fight fight)
    {
        _rReturnAt = double.MaxValue;
        Hit(fight, ShadowAssault, RDamage(fight));
    }

    public double RDamage(Fight fight) =>
        TalonKitData.AtRank(_data.R.Damage, fight.Ranks.R) + _data.R.BonusAdRatio * AttackerHits.BonusAttackDamage(fight.Attacker);

    /// <summary>An ability hit: its damage, and a Blade's End stack on a champion or large monster.</summary>
    private void Hit(Fight fight, string source, double damage)
    {
        fight.Deal(source, fight.Physical(damage).Ability());
        if (Stacks(fight.Target))
        {
            _stacks.Add(fight.Time);
            if (_stacks.Count > _data.Passive.Stacks)
            {
                _stacks.RemoveAt(0);
            }
        }
    }

    private static bool Stacks(Entity target) => target is ChampionState or NeutralState { Neutral.Kind: not NeutralKind.Minion };
}
