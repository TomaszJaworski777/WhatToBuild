using WhatToBuild.Data;
using WhatToBuild.Game;
using WhatToBuild.Modeling;

namespace WhatToBuild.Forecasting;

public sealed class ForecastPlayer
{
    private static readonly Stat[] SheetStats =
    [
        Stats.Health, Stats.AttackDamage, Stats.AbilityPower, Stats.Armor, Stats.MagicResist,
    ];

    public ForecastPlayer(
        PlayerState player,
        double time,
        int level,
        double earned,
        ProjectedBuild build,
        IReadOnlyDictionary<Guid, double> itemStacks,
        IReadOnlyDictionary<Stat, double> championStacks,
        IReadOnlyList<StatModifier> teamBuffs)
    {
        Player = player;
        Time = time;
        Level = level;
        Earned = earned;
        Build = build;
        ItemStacks = itemStacks;
        ChampionStacks = championStacks;
        TeamBuffs = teamBuffs;

        StackStats = new StatSheet();
        foreach (var (stat, stacks) in championStacks.Where(s => SheetStats.Contains(s.Key)))
        {
            StatCalculator.AddStat(StackStats, stat, stacks);
        }

        Entity = NewEntity();
    }

    public PlayerState Player { get; }

    public Champion Champion => Player.Champion;

    public double Time { get; }

    public int Level { get; }

    public double Earned { get; }

    public ProjectedBuild Build { get; }

    public IReadOnlyList<Item> Items => Build.Items;

    public IReadOnlyList<Item> NewItems => Build.Purchases.Select(p => p.Item).ToList();

    public IReadOnlyDictionary<Guid, double> ItemStacks { get; }

    public IReadOnlyDictionary<Stat, double> ChampionStacks { get; }

    public double AbilityDamageStacks => ChampionStacks.GetValueOrDefault(Stats.AbilityDamage);

    public IReadOnlyList<StatModifier> TeamBuffs { get; }

    public StatSheet StackStats { get; }

    public ChampionState Entity { get; }

    public ChampionState NewEntity() => new(Champion, Level, Items, TeamBuffs, StackStats, ItemStacks);

    public override string ToString() => $"{Champion.Name} L{Level} @ {Time:0}s";
}

public sealed class WorldForecast
{
    private readonly Dictionary<int, (IReadOnlyList<ForecastPlayer> Enemies, IReadOnlyList<ForecastPlayer> Allies)> _cache = new();
    private readonly object _lock = new();

    public WorldForecast(GameForecaster forecaster, BuildProjector projector, NeutralRepository neutrals, double bucketSeconds)
    {
        Forecaster = forecaster;
        Projector = projector;
        Neutrals = neutrals;
        BucketSeconds = bucketSeconds;
    }

    public GameForecaster Forecaster { get; }

    public BuildProjector Projector { get; }

    public NeutralRepository Neutrals { get; }

    public double BucketSeconds { get; }

    public GameState State => Forecaster.State;

    public double Now => Forecaster.Now;

    public PlayerState Me => State.ActivePlayer ?? throw new InvalidOperationException("No active player.");

    public double Snap(double time) => Now + Bucket(time) * BucketSeconds;

    public IReadOnlyList<ForecastPlayer> EnemiesAt(double time) => At(time).Enemies;

    public IReadOnlyList<ForecastPlayer> AlliesAt(double time) => At(time).Allies;

    private int Bucket(double time) => (int)Math.Round(Math.Max(0, time - Now) / BucketSeconds);

    private (IReadOnlyList<ForecastPlayer> Enemies, IReadOnlyList<ForecastPlayer> Allies) At(double time)
    {
        var bucket = Bucket(time);

        lock (_lock)
        {
            if (_cache.TryGetValue(bucket, out var cached))
            {
                return cached;
            }
        }

        var snapped = Now + bucket * BucketSeconds;
        var enemies = State.Enemies.Select(p => Forecast(p, snapped)).ToList();
        var allies = State.Allies.Where(p => !p.IsActivePlayer).Select(p => Forecast(p, snapped)).ToList();

        lock (_lock)
        {
            _cache[bucket] = (enemies, allies);
        }

        return (enemies, allies);
    }

    private ForecastPlayer Forecast(PlayerState player, double time)
    {
        var outlook = Forecaster.Outlook(player);
        var earned = Forecaster.EarnedAt(player, time);
        var spendable = earned - player.ItemValue;
        var spentBefore = player.ItemValue;

        var build = Projector.Project(player, spendable, spent => Forecaster.TimeToEarn(player, spentBefore + spent));

        var stacks = new Dictionary<Guid, double>();
        foreach (var item in build.Items.Where(i => i.Stacking is not null).DistinctBy(i => i.Id))
        {
            var projected = build.Purchases.FirstOrDefault(p => p.Item.Id == item.Id);
            var minutes = projected is not null
                ? (time - projected.At) / 60
                : (State.MinutesOwned(player, item) ?? 0) + (time - Now) / 60;

            stacks[item.Id] = item.Stacking!.StacksAfter(minutes, player.Champion.IsRanged);
        }

        var championStacks = player.Champion.Stacking
            .GroupBy(s => s.Stat)
            .ToDictionary(g => g.Key, g => g.Sum(s => Forecaster.StacksAt(player, s, time)));

        return new ForecastPlayer(
            player,
            time,
            Forecaster.LevelAt(player, time),
            earned,
            build,
            stacks,
            championStacks,
            State.TeamBuffs(player.Team, Neutrals).ToList());
    }
}
