namespace WhatToBuild.Modeling.Simulation;

/// <summary>When a spell fits against the attack timer.</summary>
public enum CastTiming
{
    /// <summary>No animation worth an attack: goes out even ahead of a ready attack (Nasus's Q, Vi's E, Kindred's Q).</summary>
    Instant,

    /// <summary>Has an animation: waits for the gap after an attack, never ahead of a ready one.</summary>
    BetweenAttacks,
}

/// <summary>How the combo's steps are picked each tick.</summary>
public enum ComboOrder
{
    /// <summary>Every step that is ready and fits goes out, in list order; one that does not fit is passed over.</summary>
    Priority,

    /// <summary>
    /// A fixed rotation: the next step goes out when it fits, one step a tick. A step on cooldown
    /// is passed over for the next one that is ready; a ready step that does not fit yet is waited for.
    /// </summary>
    Sequence,
}

/// <summary>
/// One ability in a champion's script. <paramref name="TryCast"/> checks rank, cooldown and
/// anything the kit needs, casts and says whether it did; when it fits the attack timer is the
/// script's job.
/// </summary>
/// <param name="Woven">An attack must land after it before the next woven spell: spell, attack, spell, attack.</param>
/// <param name="ResetsAttack">It resets the attack timer, so the next attack comes as soon as nothing blocks it.</param>
/// <param name="OpensFromRange">It is the engage: before the first hit lands it goes out without waiting on the attack timer.</param>
/// <param name="EmpowersAttack">Its hit rides on the next attack (Nasus's Q, Vi's E): it is not done until that attack lands.</param>
public sealed record ScriptedAbility(
    string Key,
    CastTiming Timing,
    Func<Fight, bool> TryCast,
    bool Woven = false,
    bool ResetsAttack = false,
    bool OpensFromRange = false,
    bool EmpowersAttack = false);

/// <summary>How a champion moves while it fights.</summary>
/// <param name="AttackUptime">The share of the time it is attacking rather than walking to or around the target (1: never lets go).</param>
public sealed record Kiting(double AttackUptime = 1)
{
    public static readonly Kiting StandAndFight = new();
}

/// <summary>
/// Everything about how a champion plays a fight: its combo, how the combo weaves into attacks,
/// how it kites, and its burst: one opening sequence of ability keys and <see cref="ScriptedKit.Attack"/>,
/// in the order they go out. What that sequence deals is the champion's burst.
/// </summary>
public sealed record Playstyle(ComboOrder Order, IReadOnlyList<ScriptedAbility> Combo, Kiting Kiting, IReadOnlyList<string> Burst);

/// <summary>
/// A champion kit written as a script: it declares its <see cref="Playstyle"/> and the effects of
/// its abilities, and this runs the combo against the attack timer, or the burst sequence once.
/// Timed effects (burns, channels, dashes landing) go in <see cref="BeforeCasts"/> and <see cref="AfterCasts"/>.
/// </summary>
public abstract class ScriptedKit : IChampionKit
{
    /// <summary>A basic attack as a burst step.</summary>
    public const string Attack = "AA";

    private Playstyle? _playstyle;
    private bool _attackLanded = true;
    private int _step;

    private bool _bursting;
    private int _burstStep;
    private bool _awaitingEmpoweredAttack;

    public Playstyle Playstyle => _playstyle ??= DefinePlaystyle();

    public double AttackUptime => Playstyle.Kiting.AttackUptime;

    /// <summary>Running the burst: every step has gone out and every hit it started has landed.</summary>
    public bool BurstDone { get; private set; }

    protected abstract Playstyle DefinePlaystyle();

    /// <summary>A new kit, as this one was built, with nothing cast yet; <paramref name="burst"/> when it will play the burst.</summary>
    protected abstract ScriptedKit Fresh(bool burst);

    /// <summary>A new kit that plays the burst sequence once instead of the combo.</summary>
    public ScriptedKit ForBurst()
    {
        var kit = Fresh(burst: true);
        kit._bursting = true;
        return kit;
    }

    public void Update(Fight fight)
    {
        BeforeCasts(fight);

        if (_bursting)
        {
            RunBurst(fight);
        }
        else if (Playstyle.Order == ComboOrder.Sequence)
        {
            RunSequence(fight);
        }
        else
        {
            RunPriority(fight);
        }

        AfterCasts(fight);
    }

    public void OnAttack(Fight fight)
    {
        _attackLanded = true;

        if (_bursting && (_awaitingEmpoweredAttack || CurrentBurstStep == Attack))
        {
            _awaitingEmpoweredAttack = false;
            _burstStep++;
        }

        Attacked(fight);
    }

    /// <summary>In the burst, attacks go out only where the sequence has one: an attack step or an empowered attack.</summary>
    public bool HoldsAttack(Fight fight) =>
        _bursting && !_awaitingEmpoweredAttack && CurrentBurstStep != Attack;

    public virtual void OnDamage(Fight fight, string source, double damage)
    {
    }

    protected virtual void BeforeCasts(Fight fight)
    {
    }

    protected virtual void AfterCasts(Fight fight)
    {
    }

    /// <summary>A basic attack landed; the script already counted it for weaving and the burst.</summary>
    protected virtual void Attacked(Fight fight)
    {
    }

    /// <summary>The next woven spell waits for an attack (a spell whose hit lands after its cast, like a charged dash).</summary>
    protected void AwaitAttack() => _attackLanded = false;

    private string? CurrentBurstStep => _burstStep < Playstyle.Burst.Count ? Playstyle.Burst[_burstStep] : null;

    /// <summary>
    /// The burst, step by step with nothing woven in between: each ability goes out as soon as it
    /// fits, and each attack step waits for an attack to land. Everything is off cooldown when it
    /// starts, so an ability that fits and still does not go out (no rank yet, no champion to lock
    /// on to) is left out. Done once the last step is and nothing it started is still coming.
    /// </summary>
    private void RunBurst(Fight fight)
    {
        while (!_awaitingEmpoweredAttack && CurrentBurstStep is { } key && key != Attack)
        {
            var ability = Playstyle.Combo.First(a => a.Key == key);
            if (!Fits(fight, ability, weave: false))
            {
                return;
            }

            if (TryCast(fight, ability) && ability.EmpowersAttack)
            {
                _awaitingEmpoweredAttack = true;
                return;
            }

            _burstStep++;
        }

        if (CurrentBurstStep is null && !_awaitingEmpoweredAttack
            && fight.Time >= fight.AttacksBlockedUntil && fight.Time >= fight.WindupUntil)
        {
            BurstDone = true;
        }
    }

    private void RunPriority(Fight fight)
    {
        foreach (var ability in Playstyle.Combo)
        {
            if (Fits(fight, ability))
            {
                TryCast(fight, ability);
            }
        }
    }

    private void RunSequence(Fight fight)
    {
        var combo = Playstyle.Combo;
        for (var k = 0; k < combo.Count; k++)
        {
            var step = (_step + k) % combo.Count;
            if (!Fits(fight, combo[step]))
            {
                return;
            }

            if (TryCast(fight, combo[step]))
            {
                _step = (step + 1) % combo.Count;
                return;
            }
        }
    }

    private bool Fits(Fight fight, ScriptedAbility ability, bool weave = true)
    {
        if (weave && ability.Woven && !_attackLanded)
        {
            return false;
        }

        var timing = ability.OpensFromRange && fight.DamageDealt <= 0 ? CastTiming.Instant : ability.Timing;
        return timing == CastTiming.Instant ? fight.CanCastInstant : fight.CanCast;
    }

    private bool TryCast(Fight fight, ScriptedAbility ability)
    {
        if (!ability.TryCast(fight))
        {
            return false;
        }

        if (ability.Woven)
        {
            _attackLanded = false;
        }

        if (ability.ResetsAttack)
        {
            fight.ResetAttack();
        }

        return true;
    }
}
