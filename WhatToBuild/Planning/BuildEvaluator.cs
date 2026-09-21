using System.Collections.Concurrent;
using WhatToBuild.Data;
using WhatToBuild.Forecasting;
using WhatToBuild.Game;
using WhatToBuild.Modeling;
using WhatToBuild.Modeling.Simulation;
using WhatToBuild.SupportedChampions;

namespace WhatToBuild.Planning;

public enum EvaluationMode
{
    Cheap,

    Screen,

    Full,
}

public sealed class BuildContext
{
    public BuildContext(GameState state, GameStack stack, ItemRepository items, NeutralRepository neutrals, ChampionKits kits, ModelData model)
    {
        State = state;
        Me = state.ActivePlayer ?? throw new InvalidOperationException("No active player.");
        Items = items;
        Neutrals = neutrals;
        Kits = kits;
        Model = model;
        Settings = model.Settings;
        Forecaster = new GameForecaster(state, stack, model);
        Projector = new BuildProjector(items, model.Meta);
        World = new WorldForecast(Forecaster, Projector, neutrals, Settings.Planner.TimeBucketSeconds);
        Owned = Me.Items.Where(i => i.Slot != 6).SelectMany(i => Enumerable.Repeat(i.Item, i.Count)).ToList();
        Trinkets = Me.Items.Where(i => i.Slot == 6).Select(i => i.Item).ToList();
        TeamBuffs = state.TeamBuffs(Me.Team, neutrals).ToList();
        IsJungler = string.Equals(Me.Position, "JUNGLE", StringComparison.OrdinalIgnoreCase)
                    || Owned.Any(i => i.Groups.Contains("HuntersTalismanGroup"));
        CompletedItems = Owned.Count(i => i.Cost >= 2000 && !i.Groups.Contains("Boots") && !items.All.Any(o => o.BuildPath.Contains(i.Id)));
        Adjustment = state.ActivePlayerStats is { } observed
            ? StatCalculator.Adjustment(observed, state.EntityFor(Me, neutrals).Stats)
            : null;
    }

    public GameState State { get; }

    public PlayerState Me { get; }

    public Champion Champion => Me.Champion;

    public ItemRepository Items { get; }

    public NeutralRepository Neutrals { get; }

    public ChampionKits Kits { get; }

    public ModelData Model { get; }

    public ModelSettings Settings { get; }

    public GameForecaster Forecaster { get; }

    public BuildProjector Projector { get; }

    public WorldForecast World { get; }

    public ModelSettings.ObjectiveWeights Objective => Settings.Objectives.For(Me.Champion, Form);

    public ISupportedChampion? Supported => Kits.For(Me.Champion);

    public string? DetectedForm => Supported?.DetectForm(State.ActivePlayerAbilityIds);

    public string? Form { get; set; }

    public string? FormAt(double time, string? form = null)
    {
        var chosen = form ?? Form;
        if (Supported is not { } supported || chosen is null || supported.Forms.Count == 0)
        {
            return chosen;
        }

        return DetectedForm is null && time < Settings.Forms.TransformSeconds ? supported.BaseForm : chosen;
    }

    public IReadOnlyList<Item> Owned { get; }

    public IReadOnlyList<Item> Trinkets { get; }

    public IReadOnlyList<StatModifier> TeamBuffs { get; }

    public bool IsJungler { get; }

    public StatSheet? Adjustment { get; }

    public double Now => State.GameTime;

    public double? TakedownsPerMinute =>
        Now >= Settings.Income.BaselineOnlyUntilSeconds * 2.5 ? (Me.Kills + Me.Assists) / (Now / 60) : null;

    public double? KillsPerMinute =>
        Now >= Settings.Income.BaselineOnlyUntilSeconds * 2.5 ? Me.Kills / (Now / 60) : null;

    public double StackRate(ItemStacking stacking)
    {
        var observed = stacking.Per.Contains("champion takedown", StringComparison.OrdinalIgnoreCase) ? TakedownsPerMinute
            : stacking.Per.Contains("champion kill", StringComparison.OrdinalIgnoreCase) ? KillsPerMinute
            : null;

        return observed ?? stacking.StacksPerMinute * (Champion.IsRanged ? stacking.RangedMultiplier : 1);
    }

    public double StacksAfter(ItemStacking stacking, double minutes)
    {
        var stacks = StackRate(stacking) * Math.Max(0, minutes);
        return stacking.Max > 0 ? Math.Min(stacking.Max, stacks) : stacks;
    }

    public double NextRecall(double time, double now)
    {
        var planner = Settings.Planner;
        var first = Math.Max(now, planner.FirstRecallSeconds);
        if (time <= first)
        {
            return first;
        }

        return first + Math.Ceiling((time - first) / planner.RecallIntervalSeconds) * planner.RecallIntervalSeconds;
    }

    public double ClearWeightAt(double time)
    {
        if (!IsJungler)
        {
            return 0;
        }

        var jungle = Settings.Jungle;
        var span = Math.Max(1e-9, jungle.ClearFadeToItems - jungle.ClearFadeFromItems);
        var byItems = Math.Clamp((jungle.ClearFadeToItems - CompletedItems) / span, 0, 1);

        return jungle.ClearWeightAt(time) * byItems * Objective.Clear;
    }

    public int CompletedItems { get; }
}

public sealed class Battlefield
{
    public required double Time { get; init; }

    public required IReadOnlyList<CombatProfile> Enemies { get; init; }

    public required IReadOnlyList<CombatProfile> Allies { get; init; }

    public required IReadOnlyDictionary<CombatProfile, TargetSustain> Sustain { get; init; }

    public required IReadOnlyDictionary<CombatProfile, double> Focus { get; init; }

    public required int OurLevel { get; init; }

    public required AbilityRanks OurRanks { get; init; }

    public required double OurMarks { get; init; }

    public required SurvivalAbility? Survival { get; init; }
}

public sealed record TargetResult(CombatProfile Enemy, FightResult Fight, TargetSustain Sustain, double GrievousWounds, double ShieldReduction);

/// <summary>An enemy finishing an item at a given moment.</summary>
public sealed record Spike(double Time, CombatProfile Enemy, Item Item);

/// <summary>Seconds each side needs to kill the other, one against one.</summary>
public sealed record Duel(double OurKillSeconds, double TheirKillSeconds)
{
    /// <summary>Above 1 we win the duel; at 0.5 they kill us twice as fast as we kill them.</summary>
    public double Edge => OurKillSeconds <= 0 ? 2 : TheirKillSeconds / OurKillSeconds;

    public double Weakness => Math.Clamp(1 - Edge, 0, 1);
}

public sealed class Evaluation
{
    public required double Time { get; init; }

    public required double Score { get; init; }

    public required double Dps { get; init; }

    public required double TimeAlive { get; init; }

    public required double TimeAliveWithoutAbility { get; init; }

    public string? Form { get; init; }

    public required double Burst { get; init; }

    public required double Uptime { get; init; }

    public required double MoveSpeed { get; init; }

    public required double Tempo { get; init; }

    public double DamageBeforeDeath => Dps * Uptime;

    public required double IncomingDps { get; init; }

    public required double IncomingBurst { get; init; }

    public required IReadOnlyDictionary<DamageType, double> IncomingByType { get; init; }

    public required double HealthPool { get; init; }

    public required double HealingPerSecond { get; init; }

    public required ClearResult? Clear { get; init; }

    public required double ClearWeight { get; init; }

    public required IReadOnlyList<TargetResult> Targets { get; init; }

    public required ChampionState Us { get; init; }
}

public sealed class BuildEvaluator
{
    private const double MinimumValue = 1e-3;

    private readonly BuildContext _context;
    private readonly CombatProfiler _profiler;
    private readonly ClearSimulator _clear;
    private readonly ConcurrentDictionary<int, Lazy<Battlefield>> _battlefields = new();
    private readonly ConcurrentDictionary<string, Lazy<Evaluation>> _evaluations = new();

    public BuildEvaluator(BuildContext context)
    {
        _context = context;
        _profiler = new CombatProfiler(context.Settings);
        _clear = new ClearSimulator(context.Neutrals, context.Settings);
    }

    public BuildContext Context => _context;

    public int EvaluationCount => _evaluations.Count;

    public Battlefield BattlefieldAt(double time)
    {
        var snapped = _context.World.Snap(time);
        var key = (int)Math.Round((snapped - _context.Now) / _context.World.BucketSeconds);
        return _battlefields.GetOrAdd(key, _ => new Lazy<Battlefield>(() => BuildBattlefield(snapped))).Value;
    }

    /// <summary>
    /// The moments an enemy finishes an item, from their forecast build. These are the minutes
    /// they spike: what we hold then decides whether the spike is survivable.
    /// </summary>
    public IReadOnlyList<Spike> SpikesBetween(double from, double to)
    {
        if (to <= from)
        {
            return [];
        }

        return BattlefieldAt(to).Enemies
            .SelectMany(enemy => enemy.Forecast.Build.Purchases
                .Where(p => p.At > from && p.At <= to)
                .Select(p => new Spike(p.At, enemy, p.Item)))
            .OrderBy(s => s.Time)
            .ToList();
    }

    /// <summary>One against one at a moment: how long each side needs to kill the other.</summary>
    public Duel DuelAt(Evaluation evaluation, CombatProfile enemy)
    {
        var us = evaluation.Us;
        var target = evaluation.Targets.FirstOrDefault(t => t.Enemy.Champion.Id == enemy.Champion.Id);
        var ourKill = target?.Fight.TimeToKill ?? _context.Settings.Fight.MaxFightSeconds;

        var theirDps = enemy.Streams.Sum(stream =>
            Mitigation(enemy.Entity, us, stream) * (stream.RawPerSecond + stream.TargetMaxHealthPerSecond * us.MaxHealth));

        var theirKill = theirDps <= evaluation.HealingPerSecond
            ? _context.Settings.Fight.MaxFightSeconds
            : Math.Min(_context.Settings.Fight.MaxFightSeconds, evaluation.HealthPool / (theirDps - evaluation.HealingPerSecond));

        return new Duel(ourKill, theirKill);
    }

    public Evaluation Evaluate(IReadOnlyList<Item> inventory, double time, IReadOnlyDictionary<Guid, double>? newStacks = null, EvaluationMode mode = EvaluationMode.Screen, string? form = null)
    {
        var snapped = mode == EvaluationMode.Cheap
            ? _context.World.Snap(_context.Now + Math.Round((time - _context.Now) / _context.Settings.Planner.CheapTimeBucketSeconds) * _context.Settings.Planner.CheapTimeBucketSeconds)
            : _context.World.Snap(time);
        var activeForm = _context.FormAt(snapped, form);
        var key = Key(inventory, snapped, newStacks, mode) + "|" + activeForm;

        return _evaluations.GetOrAdd(key, _ => new Lazy<Evaluation>(() => Run(inventory, snapped, newStacks, mode, activeForm))).Value;
    }

    public ChampionState Us(IReadOnlyList<Item> inventory, double time, IReadOnlyDictionary<Guid, double>? newStacks = null)
    {
        var field = BattlefieldAt(time);
        var stacks = new Dictionary<Guid, double>();

        foreach (var item in inventory.Where(i => i.Stacking is not null).DistinctBy(i => i.Id))
        {
            if (_context.Owned.Any(o => o.Id == item.Id))
            {
                var minutes = (_context.State.MinutesOwned(_context.Me, item) ?? 0) + (field.Time - _context.Now) / 60;
                stacks[item.Id] = _context.StacksAfter(item.Stacking!, minutes);
            }
            else
            {
                stacks[item.Id] = newStacks?.GetValueOrDefault(item.Id) ?? 0;
            }
        }

        return new ChampionState(_context.Champion, field.OurLevel, inventory.Concat(_context.Trinkets), _context.TeamBuffs, WithTakedownBuffs(inventory, stacks), stacks);
    }

    public double TakedownBuffUptime(double takedownsPerMinute, double duration)
    {
        var carried = 1 - Math.Exp(-takedownsPerMinute * duration / 60);
        var perFight = takedownsPerMinute * _context.Settings.Fight.TeamfightIntervalSeconds / 60;
        var inFight = perFight > 1e-9 ? 1 - (1 - Math.Exp(-perFight)) / perFight : 0;

        return carried + (1 - carried) * inFight;
    }

    private StatSheet? WithTakedownBuffs(IReadOnlyList<Item> inventory, IReadOnlyDictionary<Guid, double> stacks)
    {
        var buffs = inventory.DistinctBy(i => i.Id)
            .SelectMany(i => i.Effects.Where(e => e.Trigger == EffectTrigger.OnTakedown && e.Kind == EffectKind.StatBuff && e.Duration > 0 && e.Stat is not null)
                .Select(e => (Item: i, Effect: e)))
            .ToList();

        if (buffs.Count == 0)
        {
            return _context.Adjustment;
        }

        var sheet = new StatSheet();
        StatCalculator.Apply(sheet, _context.Adjustment);

        foreach (var (item, effect) in buffs)
        {
            var rate = _context.TakedownsPerMinute ?? item.Stacking?.StacksPerMinute ?? 0;
            var active = TakedownBuffUptime(rate, effect.Duration);
            StatCalculator.AddStat(sheet, effect.Stat!, active * (effect.Amount + effect.PerStack * stacks.GetValueOrDefault(item.Id)));
        }

        return sheet;
    }

    private Evaluation Run(IReadOnlyList<Item> inventory, double time, IReadOnlyDictionary<Guid, double>? newStacks, EvaluationMode mode, string? form)
    {
        var settings = _context.Settings;
        var field = BattlefieldAt(time);
        var us = Us(inventory, time, newStacks);
        var phases = mode switch
        {
            EvaluationMode.Full => settings.Fight.AttackPhases,
            EvaluationMode.Screen => settings.Fight.ScreenAttackPhases,
            _ => settings.Fight.CheapAttackPhases,
        };

        var opponents = mode == EvaluationMode.Cheap
            ? field.Enemies.OrderByDescending(e => e.Threat).Take(settings.Fight.CheapTargets).ToList()
            : field.Enemies;

        var targets = new List<TargetResult>();
        foreach (var enemy in opponents)
        {
            var sustain = field.Sustain[enemy];
            var target = enemy.Forecast.NewEntity();
            var results = phases
                .Select(phase => FightSimulator.Run(
                    new FightSetup(us, target, field.OurRanks, field.OurMarks, settings.Fight.TeamfightSeconds, phase, sustain, Sustained: true),
                    _context.Kits.NewFight(_context.Champion, form)))
                .ToList();

            var probe = new Fight(new FightSetup(us, target, field.OurRanks, field.OurMarks, Sustain: sustain));
            targets.Add(new TargetResult(enemy, FightResult.Average(results), sustain, probe.GrievousWounds, probe.ShieldReduction));
        }

        var dps = targets.Sum(t => t.Enemy.Threat * t.Fight.EffectiveDps);
        var opening = targets.Sum(t => t.Enemy.Threat * Math.Min(1, t.Fight.EarlyDamage / Math.Max(1, t.Fight.TargetHealth)));
        var survival = Survival(us, field, targets, dps, form);
        var fightSeconds = settings.Fight.TeamfightSeconds;
        var caught = settings.Fight.CaughtShare;
        var uptime = fightSeconds * ((1 - caught) + caught * (1 - Math.Exp(-survival.TimeAlive / fightSeconds)));

        var clearWeight = _context.ClearWeightAt(time);
        ClearResult? clear = null;
        if (clearWeight >= (mode == EvaluationMode.Cheap ? settings.Jungle.CheapMinClearWeight : settings.Jungle.MinClearWeight))
        {
            clear = _clear.Clear(us, field.OurRanks, field.OurMarks, time, () => _context.Kits.NewFight(_context.Champion, form), phases);
        }

        var walkShare = settings.Movement.WalkShareFor(_context.Me.Position);
        var tempo = 1 / (walkShare * _context.Champion.Base.MoveSpeed / Math.Max(1, us.Stats.MoveSpeed) + 1 - walkShare);

        var objective = _context.Settings.Objectives.For(_context.Champion, form);
        var score = objective.Damage * Math.Log(Math.Max(MinimumValue, dps))
                    + objective.Uptime * Math.Log(Math.Max(MinimumValue, uptime))
                    + objective.Burst * Math.Log(Math.Max(MinimumValue, opening))
                    + objective.Movement * settings.Movement.TempoWeightAt(time) * Math.Log(tempo)
                    + objective.Survival * Math.Log(Math.Max(MinimumValue, survival.TimeAlive))
                    + (clear is { } c ? clearWeight * Math.Log(1 / Math.Max(1, c.TotalSeconds)) : 0);

        return new Evaluation
        {
            Time = time,
            Score = score,
            Dps = dps,
            TimeAlive = survival.TimeAlive,
            TimeAliveWithoutAbility = survival.WithoutAbility,
            Form = form,
            Burst = opening,
            Uptime = uptime,
            MoveSpeed = us.Stats.MoveSpeed,
            Tempo = tempo,
            IncomingDps = survival.Incoming,
            IncomingBurst = survival.Burst,
            IncomingByType = survival.ByType,
            HealthPool = survival.Pool,
            HealingPerSecond = survival.Healing,
            Clear = clear,
            ClearWeight = clearWeight,
            Targets = targets,
            Us = us,
        };
    }

    private (double TimeAlive, double WithoutAbility, double Incoming, double Burst, double Pool, double Healing, Dictionary<DamageType, double> ByType) Survival(
        ChampionState us, Battlefield field, List<TargetResult> targets, double dps, string? form)
    {
        var settings = _context.Settings;
        var byType = new Dictionary<DamageType, double> { [DamageType.Physical] = 0, [DamageType.Magic] = 0, [DamageType.True] = 0 };
        double incoming = 0, burst = 0;

        var movement = settings.Movement;
        var enemySpeed = field.Enemies.Count > 0 ? field.Enemies.Average(e => e.Entity.Stats.MoveSpeed) : us.Stats.MoveSpeed;
        var evasion = Math.Pow(enemySpeed / Math.Max(1, us.Stats.MoveSpeed), movement.EvasionExponent);

        foreach (var enemy in field.Enemies)
        {
            foreach (var stream in enemy.Streams)
            {
                var aimed = field.Focus[enemy];
                var focus = stream.Flags.HasFlag(HitFlags.Ability)
                    ? aimed + (1 - aimed) * settings.EnemyDamage.AbilityAreaShare
                    : aimed;
                if (stream.Flags.HasFlag(HitFlags.Attack) && !enemy.Champion.IsRanged && us.Champion.IsRanged)
                {
                    var kiting = settings.Focus.KiteReduction
                                 * Math.Pow(us.Stats.MoveSpeed / Math.Max(1, enemy.Entity.Stats.MoveSpeed), movement.KiteSpeedExponent);
                    focus *= 1 - Math.Clamp(kiting, 0, movement.MaxKiteReduction);
                }

                focus *= evasion;

                var mitigated = Mitigation(enemy.Entity, us, stream);
                var perSecond = focus * mitigated * (stream.RawPerSecond + stream.TargetMaxHealthPerSecond * us.MaxHealth);
                incoming += perSecond;
                burst += focus * mitigated * stream.Burst;
                byType[stream.Type] += perSecond;
            }
        }

        var magicShare = incoming > 0 ? byType[DamageType.Magic] / incoming : 0;
        var physicalShare = incoming > 0 ? byType[DamageType.Physical] / incoming : 0;
        var healPower = 1 + us.Items.Sum(i => i.Stats.HealAndShieldPowerPercent);

        var pool = us.MaxHealth;
        double revive = 0, healing = 0;
        var attackShare = AttackShare(targets, us);
        var effects = us.Items.SelectMany(i => i.Effects).ToList();

        foreach (var effect in effects)
        {
            var amount = AttackerHits.OwnerAmount(effect, us) * (us.Champion.IsRanged ? effect.RangedMultiplier : 1);

            switch (effect.Kind)
            {
                case EffectKind.Shield when effect.Trigger is EffectTrigger.WhenLow or EffectTrigger.InCombat:
                    var times = effect.Trigger == EffectTrigger.InCombat && effect.Cooldown > 0 && effect.Cooldown < settings.Fight.TeamfightSeconds
                        ? settings.Fight.TeamfightSeconds / effect.Cooldown
                        : 1;
                    var share = effect.Versus switch
                    {
                        DamageSource.Magic => magicShare,
                        DamageSource.Physical => physicalShare,
                        _ => 1,
                    };
                    pool += amount * times * share * healPower;
                    break;

                case EffectKind.Heal when effect.Trigger == EffectTrigger.WhenLow:
                    pool += amount * healPower;
                    break;

                case EffectKind.Heal when effect.Trigger == EffectTrigger.InCombat:
                    healing += (effect.Cooldown > 0 ? amount / effect.Cooldown : amount) * healPower;
                    break;

                case EffectKind.Revive:
                    revive += effect.Amount * us.MaxHealth;
                    break;
            }
        }

        var lifeSteal = us.Items.Sum(i => i.Stats.LifeStealPercent);
        var omnivamp = us.Items.Sum(i => i.Stats.OmnivampPercent)
                       + effects.Where(e => StatCalculator.IsPermanentStatBuff(e) && e.Stat == Stats.OmnivampPercent).Sum(e => e.Amount);
        healing += (lifeSteal * attackShare + omnivamp) * dps * healPower;
        healing += (_context.Supported?.DamageHealShare(us, form) ?? 0) * dps * healPower;

        var enemyGrievous = field.Enemies.Select(e => e.GrievousWounds).DefaultIfEmpty(0).Max() * settings.Sustain.EnemyGrievousCoverage;
        healing *= 1 - enemyGrievous;

        var net = Math.Max(incoming - healing, 0.05 * incoming);
        double alive;
        double withoutAbility;
        if (net <= 0)
        {
            alive = withoutAbility = settings.Fight.MaxTimeAliveSeconds;
        }
        else
        {
            alive = burst >= pool ? 0.25 : (pool - burst) / net;
            alive += revive / net;
            withoutAbility = alive;

            var enemyHealth = field.Enemies.Count > 0 ? field.Enemies.Average(e => e.Entity.MaxHealth) : us.MaxHealth;
            if (_context.Supported?.Survival(field.OurRanks, us, form, enemyHealth) is { } ability)
            {
                var cooldown = ability.Cooldown * 100 / (100 + us.Stats.AbilityHaste);
                var availability = Math.Clamp(settings.Fight.TeamfightIntervalSeconds / Math.Max(1, cooldown), 0, 1);
                alive += availability * (ability.UndyingSeconds + ability.Heal * healPower * (1 - enemyGrievous) / net);
            }
        }

        var cap = settings.Fight.MaxTimeAliveSeconds;
        return (Math.Clamp(alive, 0.25, cap), Math.Clamp(withoutAbility, 0.25, cap), incoming, burst, pool, healing, byType);
    }

    private static double AttackShare(List<TargetResult> targets, ChampionState us)
    {
        var total = targets.Sum(t => t.Fight.Damage);
        if (total <= 0)
        {
            return 1;
        }

        var onHit = us.Items
            .Where(i => i.Effects.Any(e => e.Trigger == EffectTrigger.OnAttack))
            .Select(i => i.Name)
            .Append(FightSimulator.Attacks)
            .ToHashSet();

        var attacks = targets.Sum(t => t.Fight.DamageBySource.Where(d => onHit.Contains(d.Key)).Sum(d => d.Value));
        return Math.Clamp(attacks / total, 0, 1);
    }

    private static double Mitigation(ChampionState attacker, ChampionState us, DamageStream stream)
    {
        const double probe = 1000;

        var hit = stream.Type switch
        {
            DamageType.Physical => AttackerHits.Physical(attacker, us, probe),
            DamageType.Magic => AttackerHits.Magic(attacker, us, probe),
            _ => DamageCalculator.Create(us).TrueDamage(probe).AttackerItems(attacker.Items, attacker.Champion.IsRanged),
        };

        if (stream.Flags.HasFlag(HitFlags.Crit))
        {
            hit.Crit();
        }
        else if (stream.Flags.HasFlag(HitFlags.Attack))
        {
            hit.Attack();
        }

        if (stream.Flags.HasFlag(HitFlags.Ability))
        {
            hit.Ability();
        }

        return hit.Run().HealthDamage / probe;
    }

    private Battlefield BuildBattlefield(double time)
    {
        var world = _context.World;
        var enemies = world.EnemiesAt(time).Select(_profiler.Profile).ToList();
        var allies = world.AlliesAt(time).Select(_profiler.Profile).ToList();
        _profiler.AssignThreat(enemies);

        var sustain = enemies.ToDictionary(e => e, e => _profiler.SustainFor(e, enemies, allies, _context.State, _context.Neutrals));

        var level = _context.Forecaster.LevelAt(_context.Me, time);
        var champion = _context.Champion;
        var marks = champion.Stacking.FirstOrDefault() is { } stacking ? _context.Forecaster.StacksAt(_context.Me, stacking, time) : 0;

        return new Battlefield
        {
            Time = time,
            Enemies = enemies,
            Allies = allies,
            Sustain = sustain,
            Focus = enemies.ToDictionary(e => e, e => Focus(e, allies)),
            OurLevel = level,
            OurRanks = _context.Kits.RanksAt(champion, level, _context.State.ActivePlayerRanks),
            OurMarks = marks,
            Survival = _context.Kits.For(champion)?.Survival(_context.Kits.RanksAt(champion, level, _context.State.ActivePlayerRanks)),
        };
    }

    private double Focus(CombatProfile enemy, IReadOnlyList<CombatProfile> allies)
    {
        var f = _context.Settings.Focus;
        var me = _context.Champion;

        double Weight(Champion c)
        {
            var weight = 1 + f.FrontlineWeight * c.Tag("tank") + f.MeleeWeight * c.Tag("melee");
            var damage = Math.Max(c.Tag("adDamageDealer"), c.Tag("apDamageDealer"));
            return c.Tag("tank") < 0.3 ? weight * (1 + f.CarryFocusBias * damage) : weight;
        }

        var ours = Weight(me);
        if (me.Tag("tank") < 0.3)
        {
            ours *= 1 + f.DiveBias * enemy.Champion.Tag("burst");
        }

        var missing = Math.Max(0, f.TeamSize - 1 - allies.Count);
        var others = allies.Sum(a => Weight(a.Champion)) + missing;

        return ours / (ours + others);
    }

    private static string Key(IReadOnlyList<Item> inventory, double time, IReadOnlyDictionary<Guid, double>? stacks, EvaluationMode mode)
    {
        var ids = string.Join(',', inventory.Select(i => i.RiotId).Order());
        var stackText = stacks is null ? "" : string.Join(',', stacks.Where(s => inventory.Any(i => i.Id == s.Key)).OrderBy(s => s.Key).Select(s => $"{s.Key}:{s.Value:0}"));
        return $"{mode}|{time:0}|{ids}|{stackText}";
    }
}
