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
        Projector = new BuildProjector(items, model.Meta, Settings.Income.EnemyGoldGrace);
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

    public ModelSettings.ObjectiveWeights Objective => ObjectiveFor(Form);

    /// <summary>Objective weights you chose on the page, keyed like model.json's objectives; the rest play the model's.</summary>
    public IReadOnlyDictionary<string, ModelSettings.ObjectiveWeights> ChosenWeights { get; init; } =
        new Dictionary<string, ModelSettings.ObjectiveWeights>();

    /// <summary>The burst sequence you chose on the page for this champion; none plays the kit's own.</summary>
    public IReadOnlyList<string>? BurstCombo { get; init; }

    /// <summary>Enemies (by champion name) left out of the burst: it is the share of the others' health it takes.</summary>
    public IReadOnlySet<string> BurstExcluded { get; init; } = new HashSet<string>();

    public ModelSettings.ObjectiveWeights ObjectiveFor(string? form)
    {
        var model = Settings.Objectives.For(Me.Champion, form);
        return ChosenWeights.GetValueOrDefault(Settings.Objectives.KeyFor(Me.Champion, form)) is { } chosen
            ? model.WithChampionWeights(chosen)
            : model;
    }

    public ISupportedChampion? Supported => Kits.For(Me.Champion);

    public string? DetectedForm => Supported?.DetectForm(State.ActivePlayerAbilityIds) ?? InferredForm;

    /// <summary>
    /// The form the live data cannot show: only one of Kayn's forms changes an ability, so once
    /// everyone has transformed, not seeing it means the other one (Rhaast).
    /// </summary>
    public string? InferredForm =>
        Supported is { Forms.Count: > 0 } supported
        && supported.DetectForm(State.ActivePlayerAbilityIds) is null
        && Now >= Settings.Forms.UndetectedIsDefaultFromSeconds
            ? supported.DefaultForm
            : null;

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

    /// <summary>
    /// Stacks per minute owned: the item's own preset, never this game's trend. A stacking item
    /// is bought for what it grows into, and a few early takedowns (or a dry spell) should not
    /// decide that.
    /// </summary>
    public double StackRate(ItemStacking stacking) =>
        stacking.StacksPerMinute * (Champion.IsRanged ? stacking.RangedMultiplier : 1);

    public double StacksAfter(ItemStacking stacking, double minutes)
    {
        var stacks = StackRate(stacking) * Math.Max(0, minutes);
        return stacking.Max > 0 ? Math.Min(stacking.Max, stacks) : stacks;
    }

    public double NextRecall(double time, double now)
    {
        // You back once the gold for the item is in, never before the first recall. A fixed grid
        // of recalls made anything cheap free whenever the big item would land on the same
        // recall anyway, and one counted from now slid every tick and made times jump.
        return Math.Max(time, Math.Max(now, Settings.Planner.FirstRecallSeconds));
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

    public required double FightSeconds { get; init; }

    public required double MoveSpeed { get; init; }

    public required double Tempo { get; init; }

    /// <summary>What you deal in a teamfight before you die, if you do: for weighing forms, not part of the score.</summary>
    public double DamageBeforeDeath => Dps * Math.Min(FightSeconds, TimeAlive);

    public required double IncomingDps { get; init; }

    public required double IncomingBurst { get; init; }

    public required IReadOnlyDictionary<DamageType, double> IncomingByType { get; init; }

    /// <summary>What each enemy deals you per second in a teamfight, after focus and your resists.</summary>
    public required IReadOnlyDictionary<CombatProfile, double> IncomingByEnemy { get; init; }

    /// <summary>The share of a teamfight the enemy team keeps you locked down, dealing nothing.</summary>
    public required double LockedShare { get; init; }

    /// <summary>The share of a teamfight you keep the enemy that hits you hardest locked down.</summary>
    public required double LockdownShare { get; init; }

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

        var theirDps = Incoming(enemy, us, Reach(us, _context.Kits.NewFight(_context.Champion, evaluation.Form))).PerSecond;

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

        return new ChampionState(_context.Champion, field.OurLevel, inventory.Concat(_context.Trinkets), _context.TeamBuffs, FightAdjustment(inventory, stacks, field.OurRanks), stacks);
    }

    public double TakedownBuffUptime(double takedownsPerMinute, double duration)
    {
        var carried = 1 - Math.Exp(-takedownsPerMinute * duration / 60);
        var perFight = takedownsPerMinute * _context.Settings.Fight.TeamfightIntervalSeconds / 60;
        var inFight = perFight > 1e-9 ? 1 - (1 - Math.Exp(-perFight)) / perFight : 0;

        return carried + (1 - carried) * inFight;
    }

    /// <summary>
    /// What your stats are in a fight beyond items and level: the measured adjustment, takedown
    /// buffs as often as they are up, and a cooldown's stats (Nasus's R) for as many teamfights as
    /// its cooldown lets it be up for.
    /// </summary>
    private StatSheet? FightAdjustment(IReadOnlyList<Item> inventory, IReadOnlyDictionary<Guid, double> stacks, AbilityRanks ranks)
    {
        var buffs = inventory.DistinctBy(i => i.Id)
            .SelectMany(i => i.Effects.Where(e => e.Trigger == EffectTrigger.OnTakedown && e.Kind == EffectKind.StatBuff && e.Duration > 0 && e.Stat is not null)
                .Select(e => (Item: i, Effect: e)))
            .ToList();
        var ability = _context.Supported?.Stats(ranks);

        if (buffs.Count == 0 && ability is null)
        {
            return _context.Adjustment;
        }

        var sheet = new StatSheet();
        StatCalculator.Apply(sheet, _context.Adjustment);

        foreach (var (item, effect) in buffs)
        {
            // A stacking item's buff follows its own preset takedown rate, like its stacks do.
            var rate = item.Stacking is { } stacking ? _context.StackRate(stacking) : _context.TakedownsPerMinute ?? 0;
            var active = TakedownBuffUptime(rate, effect.Duration);
            StatCalculator.AddStat(sheet, effect.Stat!, active * (effect.Amount + effect.PerStack * stacks.GetValueOrDefault(item.Id)));
        }

        if (ability is not null)
        {
            var haste = inventory.Sum(i => i.Stats.AbilityHaste);
            var cooldown = ability.Cooldown * 100 / (100 + haste);
            var up = Math.Clamp(_context.Settings.Fight.TeamfightIntervalSeconds / Math.Max(1, cooldown), 0, 1);
            StatCalculator.AddStat(sheet, Stats.Health, up * ability.Stats.Health);
            StatCalculator.AddStat(sheet, Stats.Armor, up * ability.Stats.Armor);
            StatCalculator.AddStat(sheet, Stats.MagicResist, up * ability.Stats.MagicResist);
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

        var lifeSteal = _context.Supported?.LifeSteal(us, form) ?? 0;
        var omnivamp = _context.Supported?.Omnivamp(us, form) ?? 0;
        var ourReach = Reach(us, _context.Kits.NewFight(_context.Champion, form));
        var targets = new List<TargetResult>();
        foreach (var enemy in opponents)
        {
            var sustain = field.Sustain[enemy];
            var target = enemy.Forecast.NewEntity();
            // Both start as far apart as the longer reach of the two, and we walk in.
            var start = Math.Max(ourReach, Reach(enemy.Entity, EnemyKit(enemy)));
            var uptime = settings.Fight.AttackUptime.For(us.Champion.IsRanged, enemy.Champion.IsRanged);
            var results = phases
                .Select(phase => FightSimulator.Run(
                    new FightSetup(us, target, field.OurRanks, field.OurMarks, settings.Fight.TeamfightSeconds, phase, sustain, Sustained: true,
                        LifeSteal: lifeSteal, Omnivamp: omnivamp, StartDistance: start, AttackUptime: uptime, DashContactSeconds: settings.Fight.DashContactSeconds,
                        BurstCombo: _context.BurstCombo),
                    _context.Kits.NewFight(_context.Champion, form)))
                .ToList();

            var probe = new Fight(new FightSetup(us, target, field.OurRanks, field.OurMarks, Sustain: sustain));
            targets.Add(new TargetResult(enemy, FightResult.Average(results), sustain, probe.GrievousWounds, probe.ShieldReduction));
        }

        var fightSeconds = settings.Fight.TeamfightSeconds;
        var incomingFrom = field.Enemies.ToDictionary(e => e, e => Incoming(e, us, ourReach));

        // Time the enemy team keeps you locked down is time you deal nothing: their hard crowd
        // control on you, weighed like their damage by how much of it comes your way.
        var locked = Math.Min(settings.CrowdControl.MaxLockedShare,
            field.Enemies.Sum(e => field.Focus[e] * incomingFrom[e].LocksUs) / fightSeconds);

        var dps = (1 - locked) * targets.Sum(t => t.Enemy.Threat * t.Fight.EffectiveDps);
        var opening = Burst(field, targets);
        var survival = Survival(us, field, targets, incomingFrom, form);

        var clearWeight = _context.ClearWeightAt(time);
        ClearResult? clear = null;
        if (clearWeight >= (mode == EvaluationMode.Cheap ? settings.Jungle.CheapMinClearWeight : settings.Jungle.MinClearWeight))
        {
            clear = _clear.Clear(us, field.OurRanks, field.OurMarks, time, () => _context.Kits.NewFight(_context.Champion, form), phases);
        }

        var walkShare = settings.Movement.WalkShareFor(_context.Me.Position);
        var tempo = 1 / (walkShare * _context.Champion.Base.MoveSpeed / Math.Max(1, us.Stats.MoveSpeed) + 1 - walkShare);

        // Each objective stands alone: damage is sustained DPS and burst what the burst combo takes,
        // neither minding whether you live; dying only counts through survival.
        var objective = _context.ObjectiveFor(form);
        var score =objective.Damage * Math.Log(Math.Max(MinimumValue, dps))
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
            FightSeconds = fightSeconds,
            MoveSpeed = us.Stats.MoveSpeed,
            Tempo = tempo,
            IncomingDps = survival.Incoming,
            IncomingBurst = survival.Burst,
            IncomingByType = survival.ByType,
            IncomingByEnemy = survival.ByEnemy,
            LockedShare = locked,
            LockdownShare = survival.Lockdown,
            HealthPool = survival.Pool,
            HealingPerSecond = survival.Healing,
            Clear = clear,
            ClearWeight = clearWeight,
            Targets = targets,
            Us = us,
        };
    }

    /// <summary>The share of one enemy's health the burst takes; past a kill it takes nothing more.</summary>
    public static double BurstShare(FightResult fight) => Math.Min(1, fight.EarlyDamage / Math.Max(1, fight.TargetHealth));

    /// <summary>Whether an enemy counts for the burst: every one does unless you left it out (all left out counts them all).</summary>
    public bool CountsForBurst(CombatProfile enemy, Battlefield field) =>
        !_context.BurstExcluded.Contains(enemy.Champion.Name)
        || field.Enemies.All(e => _context.BurstExcluded.Contains(e.Champion.Name));

    /// <summary>
    /// The share of the enemy team's health the burst takes, weighed by threat over the enemies it
    /// counts: leaving one out spreads its weight over the rest, so the whole still reads 0 to 1.
    /// </summary>
    private double Burst(Battlefield field, List<TargetResult> targets)
    {
        var weight = field.Enemies.Where(e => CountsForBurst(e, field)).Sum(e => e.Threat);
        return weight <= 0
            ? 0
            : targets.Where(t => CountsForBurst(t.Enemy, field)).Sum(t => t.Enemy.Threat * BurstShare(t.Fight)) / weight;
    }

    private (double TimeAlive, double WithoutAbility, double Incoming, double Burst, double Pool, double Healing, Dictionary<DamageType, double> ByType, Dictionary<CombatProfile, double> ByEnemy, double Lockdown) Survival(
        ChampionState us, Battlefield field, List<TargetResult> targets, IReadOnlyDictionary<CombatProfile, IncomingDamage> incomingFrom, string? form)
    {
        var settings = _context.Settings;
        var byType = new Dictionary<DamageType, double> { [DamageType.Physical] = 0, [DamageType.Magic] = 0, [DamageType.True] = 0 };
        double incoming = 0, burst = 0;

        // Our hard crowd control goes on whoever hits us hardest: while locked down they do nothing,
        // so that share of the teamfight comes off all of their damage.
        var biggest = field.Enemies.MaxBy(e => field.Focus[e] * incomingFrom[e].PerSecond);
        var lockdown = biggest is null ? 0 : Math.Clamp(Lockdown(biggest, targets) / settings.Fight.TeamfightSeconds, 0, 1);

        var byEnemy = new Dictionary<CombatProfile, double>();
        var attacksBy = new Dictionary<CombatProfile, Dictionary<DamageType, double>>();

        foreach (var enemy in field.Enemies)
        {
            var focus = field.Focus[enemy] * (enemy == biggest ? 1 - lockdown : 1);
            var from = incomingFrom[enemy];

            foreach (var (type, perSecond) in from.ByType)
            {
                byType[type] += focus * perSecond;
            }

            incoming += focus * from.PerSecond;
            burst += field.Focus[enemy] * from.Burst;
            byEnemy[enemy] = focus * from.PerSecond;
            attacksBy[enemy] = from.Attacks.ToDictionary(a => a.Key, a => focus * a.Value);
        }

        // An ability that slows attacks (Nasus's Wither) goes on whoever hits you hardest with them.
        var cut = _context.Supported?.AttackCut(field.OurRanks, us, settings.Fight.TeamfightSeconds) ?? 0;
        if (cut > 0 && attacksBy.Count > 0)
        {
            foreach (var (type, perSecond) in attacksBy.Values.MaxBy(a => a.Values.Sum())!)
            {
                incoming -= cut * perSecond;
                byType[type] -= cut * perSecond;
            }
        }

        var magicShare = incoming > 0 ? byType[DamageType.Magic] / incoming : 0;
        var physicalShare = incoming > 0 ? byType[DamageType.Physical] / incoming : 0;
        var healPower = 1 + us.Items.Sum(i => i.Stats.HealAndShieldPowerPercent);

        var pool = us.MaxHealth;
        double revive = 0, healing = 0;
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

        // Life steal and omnivamp as the fights simulated them, hit by hit, weighed like the damage.
        healing += targets.Sum(t => t.Enemy.Threat * t.Fight.SelfHealed / Math.Max(0.1, t.Fight.Seconds)) * healPower;

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
        return (Math.Clamp(alive, 0.25, cap), Math.Clamp(withoutAbility, 0.25, cap), incoming, burst, pool, healing, byType, byEnemy, lockdown);
    }

    /// <summary>What one enemy deals you in a teamfight, per second and after your resists, before focus, and how many seconds of it they keep you locked down.</summary>
    private sealed record IncomingDamage(
        double PerSecond,
        IReadOnlyDictionary<DamageType, double> ByType,
        double Burst,
        IReadOnlyDictionary<DamageType, double> Attacks,
        double LocksUs);

    /// <summary>
    /// A supported enemy's own shield or heal in a fight (Vi's Blast Shield, Rhaast's Umbral
    /// Trespass heal) on top of what its tags and items give it: once a teamfight, more we have
    /// to get through.
    /// </summary>
    private TargetSustain WithKitShield(CombatProfile enemy, TargetSustain sustain)
    {
        if (_context.Kits.For(enemy.Champion) is not { } supported)
        {
            return sustain;
        }

        var ranks = _context.Kits.RanksFor(enemy.Champion, enemy.Forecast.Level, null);
        var ourHealth = _context.State.EntityFor(_context.Me, _context.Neutrals).MaxHealth;
        return supported.Survival(ranks, enemy.Entity, supported.DefaultForm, ourHealth) is { Heal: > 0 } ability
            ? sustain with { Shields = [.. sustain.Shields, new Shield(ability.Heal)] }
            : sustain;
    }

    /// <summary>
    /// Seconds of a teamfight our kit keeps this enemy locked down, from our fight against it; an
    /// enemy we did not simulate (a cheap evaluation fights only the biggest threats) takes the
    /// most we managed on any of them.
    /// </summary>
    private static double Lockdown(CombatProfile enemy, IReadOnlyList<TargetResult> targets) =>
        targets.FirstOrDefault(t => t.Enemy == enemy)?.Fight.Disabled
        ?? targets.Select(t => t.Fight.Disabled).DefaultIfEmpty(0).Max();

    /// <summary>The farthest a champion reaches a target from: its attack range, or its longest ability.</summary>
    private static double Reach(ChampionState champion, IChampionKit? kit) =>
        kit is ScriptedKit scripted ? scripted.Reach(champion.Stats.AttackRange) : champion.Stats.AttackRange;

    /// <summary>A supported enemy's own script, fresh; null for one the model only knows by its tags.</summary>
    private ScriptedKit? EnemyKit(CombatProfile enemy) =>
        _context.Kits.For(enemy.Champion) is { } supported ? supported.NewFight(supported.DefaultForm) as ScriptedKit : null;

    /// <summary>
    /// One enemy's damage on you. A supported champion plays its own script against you: it walks
    /// in from the longer reach of the two, weaves its combo into its attacks at the matchup's
    /// uptime, and its burst is its burst combo. Any other is its tag-built streams: attacks at the
    /// matchup's uptime and only once it has walked into range, abilities throughout.
    /// </summary>
    private IncomingDamage Incoming(CombatProfile enemy, ChampionState us, double ourReach)
    {
        var settings = _context.Settings;
        var fightSeconds = settings.Fight.TeamfightSeconds;
        var kit = EnemyKit(enemy);
        var start = Math.Max(ourReach, Reach(enemy.Entity, kit));
        var uptime = settings.Fight.AttackUptime.For(enemy.Champion.IsRanged, us.Champion.IsRanged);

        if (kit is not null)
        {
            var ranks = _context.Kits.RanksFor(enemy.Champion, enemy.Forecast.Level, null);
            var stacks = enemy.Champion.Stacking.FirstOrDefault() is { } stacking ? enemy.Forecast.ChampionStacks.GetValueOrDefault(stacking.Stat) : 0;
            var fight = FightSimulator.Run(
                new FightSetup(enemy.Entity, us, ranks, stacks, fightSeconds, 0.5, Sustained: true, StartDistance: start, AttackUptime: uptime,
                    DashContactSeconds: settings.Fight.DashContactSeconds),
                kit);
            var seconds = Math.Max(0.1, fight.Seconds);
            var byType = (fight.DamageByType ?? new Dictionary<DamageType, double>()).ToDictionary(d => d.Key, d => d.Value / seconds);
            var attacks = new Dictionary<DamageType, double>
            {
                [DamageType.Physical] = fight.DamageBySource.GetValueOrDefault(FightSimulator.Attacks) / seconds,
            };

            return new IncomingDamage(byType.Values.Sum(), byType, fight.EarlyDamage, attacks, fight.Disabled);
        }

        var approach = Math.Max(0, start - enemy.Entity.Stats.AttackRange) / Math.Max(1, enemy.Entity.Stats.MoveSpeed);
        var attackShare = uptime * Math.Clamp((fightSeconds - approach) / fightSeconds, 0, 1);
        var streamsByType = new Dictionary<DamageType, double>();
        var attacksByType = new Dictionary<DamageType, double>();
        double burst = 0;

        foreach (var stream in enemy.Streams)
        {
            var isAttack = stream.Flags.HasFlag(HitFlags.Attack);
            var mitigated = Mitigation(enemy.Entity, us, stream);
            var perSecond = (isAttack ? attackShare : 1) * mitigated * (stream.RawPerSecond + stream.TargetMaxHealthPerSecond * us.MaxHealth);
            streamsByType[stream.Type] = streamsByType.GetValueOrDefault(stream.Type) + perSecond;
            burst += mitigated * stream.Burst;

            if (isAttack)
            {
                attacksByType[stream.Type] = attacksByType.GetValueOrDefault(stream.Type) + perSecond;
            }
        }

        var locks = enemy.Champion.Tag("crowdControl") * settings.CrowdControl.SecondsPerTag * (1 - Math.Clamp(us.Stats.Tenacity, 0, 1));
        return new IncomingDamage(streamsByType.Values.Sum(), streamsByType, burst, attacksByType, locks);
    }

    /// <summary>
    /// The share of an enemy's damage that reaches you. Item effects that build up over a fight
    /// (Terminus's penetration, Black Cleaver's shred) start empty: they are averaged over the
    /// attacks the enemy lands in a teamfight, from the first one, not taken fully stacked.
    /// </summary>
    private double Mitigation(ChampionState attacker, ChampionState us, DamageStream stream)
    {
        if (!attacker.Items.Any(i => i.Effects.Any(e => e.StacksTo > 0)))
        {
            return Mitigated(attacker, us, stream, null);
        }

        var attacks = Math.Max(1, (int)Math.Round(attacker.Stats.AttackSpeed * _context.Settings.Fight.TeamfightSeconds));
        return Enumerable.Range(1, attacks).Average(landed => Mitigated(attacker, us, stream, landed));
    }

    private static double Mitigated(ChampionState attacker, ChampionState us, DamageStream stream, int? landed)
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

        if (landed is { } count)
        {
            hit.AttacksLanded(count);
        }

        return hit.Run().HealthDamage / probe;
    }

    private Battlefield BuildBattlefield(double time)
    {
        var world = _context.World;
        var enemies = world.EnemiesAt(time).Select(_profiler.Profile).ToList();
        var allies = world.AlliesAt(time).Select(_profiler.Profile).ToList();
        _profiler.AssignThreat(enemies);

        var sustain = enemies.ToDictionary(e => e, e => WithKitShield(e, _profiler.SustainFor(e, enemies, allies, _context.State, _context.Neutrals)));

        var level = _context.Forecaster.LevelAt(_context.Me, time);
        var champion = _context.Champion;
        var marks = champion.Stacking.FirstOrDefault() is { } stacking ? _context.Forecaster.StacksAt(_context.Me, stacking, time) : 0;

        return new Battlefield
        {
            Time = time,
            Enemies = enemies,
            Allies = allies,
            Sustain = sustain,
            Focus = enemies.ToDictionary(e => e, e => _context.Settings.Focus.For(champion.IsRanged, e.Champion.IsRanged)),
            OurLevel = level,
            OurRanks = _context.Kits.RanksAt(champion, level, _context.State.ActivePlayerRanks),
            OurMarks = marks,
            Survival = _context.Kits.For(champion)?.Survival(_context.Kits.RanksAt(champion, level, _context.State.ActivePlayerRanks)),
        };
    }

    private static string Key(IReadOnlyList<Item> inventory, double time, IReadOnlyDictionary<Guid, double>? stacks, EvaluationMode mode)
    {
        var ids = string.Join(',', inventory.Select(i => i.RiotId).Order());
        var stackText = stacks is null ? "" : string.Join(',', stacks.Where(s => inventory.Any(i => i.Id == s.Key)).OrderBy(s => s.Key).Select(s => $"{s.Key}:{s.Value:0}"));
        return $"{mode}|{time:0}|{ids}|{stackText}";
    }
}
