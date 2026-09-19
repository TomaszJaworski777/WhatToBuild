namespace WhatToBuild.Modeling;

public sealed class DamageContext
{
    public DamageContext(Hit hit, Attacker attacker, Defender defender)
    {
        Hit = hit;
        Attacker = attacker;
        Defender = defender;
        Damage = hit.Raw;
    }

    public Hit Hit { get; }

    public Attacker Attacker { get; }

    public Defender Defender { get; }

    public double Damage { get; set; }

    public double EffectiveResist { get; set; }

    public double ShieldAbsorbed { get; set; }
}

public interface IDamageStep
{
    string Name { get; }

    void Apply(DamageContext context);
}

public sealed record DamageStepTrace(string Step, double DamageAfter);

public sealed record DamageResult(
    Hit Hit,
    double EffectiveResist,
    double ShieldAbsorbed,
    double HealthDamage,
    IReadOnlyList<DamageStepTrace> Trace)
{
    public double TotalDamage => HealthDamage + ShieldAbsorbed;
}

public sealed class DamageCalculation
{
    private readonly List<IDamageStep> _steps = new();

    public IReadOnlyList<IDamageStep> Steps => _steps;

    public static DamageCalculation Standard() =>
        new DamageCalculation()
            .Then(new AmplificationStep())
            .Then(new ResistanceStep())
            .Then(new MitigationStep())
            .Then(new ShieldStep());

    public DamageCalculation Then(IDamageStep step)
    {
        _steps.Add(step);
        return this;
    }

    public DamageCalculation Without<TStep>() where TStep : IDamageStep
    {
        _steps.RemoveAll(s => s is TStep);
        return this;
    }

    public DamageResult Run(Hit hit, Attacker attacker, Defender defender)
    {
        var context = new DamageContext(hit, attacker, defender);
        var trace = new List<DamageStepTrace>();

        foreach (var step in _steps)
        {
            step.Apply(context);
            trace.Add(new DamageStepTrace(step.Name, context.Damage));
        }

        return new DamageResult(hit, context.EffectiveResist, context.ShieldAbsorbed, context.Damage, trace);
    }
}
