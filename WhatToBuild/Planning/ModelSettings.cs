using System.Globalization;
using System.Text.Json;
using WhatToBuild.Data;
using WhatToBuild.Forecasting;

namespace WhatToBuild.Planning;

public sealed class ModelSettings
{
    public const string FolderName = "Model";
    public const string FileName = "model.json";

    public FightSettings Fight { get; set; } = new();
    public FocusSettings Focus { get; set; } = new();
    public EnemyDamageSettings EnemyDamage { get; set; } = new();
    public SustainSettings Sustain { get; set; } = new();
    public AllySettings Allies { get; set; } = new();
    public ThreatSettings Threat { get; set; } = new();
    public ObjectiveSettings Objectives { get; set; } = new();
    public JungleSettings Jungle { get; set; } = new();
    public PlannerSettings Planner { get; set; } = new();
    public IncomeSettings Income { get; set; } = new();
    public MovementSettings Movement { get; set; } = new();
    public FormSettings Forms { get; set; } = new();
    public StackSettings Stacks { get; set; } = new();

    public sealed class FormSettings
    {
        public double TransformSeconds { get; set; } = 600;
        public double PreferDefaultMargin { get; set; } = 0.1;

        /// <summary>Only one form shows in the live data; past this moment its absence means the other one.</summary>
        public double UndetectedIsDefaultFromSeconds { get; set; } = 900;
    }

    /// <summary>How far a champion's own stacking pace may pull the forecast away from the standard pace.</summary>
    public sealed class StackSettings
    {
        public double TrendFromSeconds { get; set; } = 480;
        public double TrendFullSeconds { get; set; } = 1500;
        public double MaxTrendWeight { get; set; } = 0.3;
    }

    public static ModelSettings Load(string dataRoot) =>
        JsonSerializer.Deserialize<ModelSettings>(
            File.ReadAllText(Path.Combine(dataRoot, FolderName, FileName)), GameDataJson.Options)
        ?? throw new InvalidDataException($"{FileName} is empty.");

    public sealed class FightSettings
    {
        public double TeamfightSeconds { get; set; } = 12;
        public double UltimateSeconds { get; set; } = 100;
        public double MaxFightSeconds { get; set; } = 30;
        public List<double> AttackPhases { get; set; } = [0, 0.25, 0.5, 0.75];
        public List<double> ScreenAttackPhases { get; set; } = [0, 0.5];
        public List<double> CheapAttackPhases { get; set; } = [0.5];
        public int CheapTargets { get; set; } = 3;
        public double MaxTimeAliveSeconds { get; set; } = 60;
        public double CaughtShare { get; set; } = 0.35;
        public double TeamfightIntervalSeconds { get; set; } = 120;
    }

    public sealed class FocusSettings
    {
        public int TeamSize { get; set; } = 5;
        public double FrontlineWeight { get; set; } = 1.5;
        public double MeleeWeight { get; set; } = 0.3;
        public double DiveBias { get; set; } = 1.2;
        public double CarryFocusBias { get; set; } = 1.0;
        public double KiteReduction { get; set; } = 0.35;
    }

    public sealed class EnemyDamageSettings
    {
        public double AutoUptimeRanged { get; set; } = 0.8;
        public double AutoUptimeMelee { get; set; } = 0.6;
        public double MinAutoShare { get; set; } = 0.35;
        public double AbilityCycleSeconds { get; set; } = 8;
        public double CastsPerCycle { get; set; } = 3;
        public double ApBase { get; set; }
        public double ApPerLevel { get; set; }
        public double ApRatio { get; set; }
        public double AdBase { get; set; }
        public double AdPerLevel { get; set; }
        public double AdBonusRatio { get; set; }
        public double AdRangedAbilityShare { get; set; }
        public double TankBase { get; set; }
        public double TankPerLevel { get; set; }
        public double TankBonusHealthRatio { get; set; }
        public double TrueDamageShare { get; set; }
        public double BurstShare { get; set; }
        public double AbilityDamageStackRatio { get; set; }
        public double AverageTargetHealthPercent { get; set; } = 0.6;
        public double AbilityAreaShare { get; set; } = 0.3;
    }

    public sealed class SustainSettings
    {
        public double HealBase { get; set; }
        public double HealPerLevel { get; set; }
        public double HealApRatio { get; set; }
        public double HealBonusAdRatio { get; set; }
        public double HealBonusHealthRatio { get; set; }
        public double ShieldBase { get; set; }
        public double ShieldPerLevel { get; set; }
        public double ShieldApRatio { get; set; }
        public double ShieldBonusHealthRatio { get; set; }
        public double SupportAllyShare { get; set; }
        public double AllyReceiveShare { get; set; }
        public double DragonHealPerTag { get; set; }
        public double DragonShieldPerTag { get; set; }
        public double DamageDealtMitigation { get; set; } = 0.65;
        public double EnemyGrievousCoverage { get; set; } = 0.6;
    }

    public sealed class AllySettings
    {
        public double GrievousCoverage { get; set; } = 0.5;
        public double ShieldReductionCoverage { get; set; } = 0.5;
        public double ShredCoverage { get; set; } = 0.5;
    }

    public sealed class ThreatSettings
    {
        public double GoldExponent { get; set; } = 1;
        public double DamageTagWeight { get; set; } = 0.6;
        public double BurstTagWeight { get; set; } = 0.3;
    }

    public sealed class ObjectiveSettings
    {
        public ObjectiveWeights Default { get; set; } = new();
        public Dictionary<string, ObjectiveWeights> Champions { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public const string DefaultKey = "default";

        public ObjectiveWeights For(Champion champion, string? form = null) =>
            KeyFor(champion, form) is var key && key == DefaultKey ? Default : Champions[key];

        /// <summary>Which entry <see cref="For"/> reads: the form's, else the champion's, else <c>default</c>.</summary>
        public string KeyFor(Champion champion, string? form = null) =>
            form is not null && Champions.ContainsKey($"{champion.Name}/{form}") ? $"{champion.Name}/{form}"
            : Champions.ContainsKey(champion.Name) ? champion.Name
            : DefaultKey;
    }

    public sealed class ObjectiveWeights
    {
        public double Damage { get; set; } = 1;
        public double Survival { get; set; } = 0.25;
        public double Clear { get; set; } = 1;

        public double Movement { get; set; } = 1;
        public double Uptime { get; set; } = 1;
        public double Burst { get; set; }

        /// <summary>The most any one weight can be set to from the page.</summary>
        public const double MaxWeight = 3;

        public ObjectiveWeights Clamped() => new()
        {
            Damage = Math.Clamp(Damage, 0, MaxWeight),
            Survival = Math.Clamp(Survival, 0, MaxWeight),
            Clear = Math.Clamp(Clear, 0, MaxWeight),
            Movement = Math.Clamp(Movement, 0, MaxWeight),
            Uptime = Math.Clamp(Uptime, 0, MaxWeight),
            Burst = Math.Clamp(Burst, 0, MaxWeight),
        };

        public string Key => string.Create(CultureInfo.InvariantCulture,
            $"{Damage:0.00}/{Burst:0.00}/{Uptime:0.00}/{Survival:0.00}");

        /// <summary>
        /// These weights with the constant, champion-specific ones (damage, burst, uptime,
        /// survival) taken from <paramref name="chosen"/>. Clear and movement stay: they already
        /// change with game time, so they are the model's to set, not a build preference.
        /// </summary>
        public ObjectiveWeights WithChampionWeights(ObjectiveWeights chosen) => new()
        {
            Damage = chosen.Damage,
            Burst = chosen.Burst,
            Uptime = chosen.Uptime,
            Survival = chosen.Survival,
            Clear = Clear,
            Movement = Movement,
        };
    }

    public sealed class MovementSettings
    {
        public Dictionary<string, double> WalkShare { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public double DefaultWalkShare { get; set; } = 0.3;
        public double TempoWeight { get; set; } = 1;

        public double EvasionExponent { get; set; } = 1;

        public double KiteSpeedExponent { get; set; } = 2;

        public double MaxKiteReduction { get; set; } = 0.7;

        public List<List<double>> TempoWeightByMinute { get; set; } = new();

        public double WalkShareFor(string position) => WalkShare.GetValueOrDefault(position, DefaultWalkShare);

        public double TempoWeightAt(double seconds)
        {
            var points = TempoWeightByMinute.Where(p => p.Count >= 2).OrderBy(p => p[0]).ToList();
            if (points.Count == 0)
            {
                return TempoWeight;
            }

            var minute = seconds / 60;
            if (minute <= points[0][0])
            {
                return points[0][1];
            }

            for (var i = 1; i < points.Count; i++)
            {
                if (minute <= points[i][0])
                {
                    var share = (minute - points[i - 1][0]) / (points[i][0] - points[i - 1][0]);
                    return points[i - 1][1] + share * (points[i][1] - points[i - 1][1]);
                }
            }

            return points[^1][1];
        }
    }

    public sealed class JungleSettings
    {
        public List<string> Camps { get; set; } = new();
        public double WalkSeconds { get; set; } = 60;
        public double ClearWeightEarly { get; set; } = 1;
        public double ClearWeightLate { get; set; } = 0.1;
        public double FadeFromSeconds { get; set; } = 720;
        public double FadeToSeconds { get; set; } = 1500;
        public double MinClearWeight { get; set; } = 0.05;
        public double ClearFadeFromItems { get; set; } = 1.5;
        public double ClearFadeToItems { get; set; } = 2.5;
        public double CheapMinClearWeight { get; set; } = 0.25;

        public double ClearWeightAt(double seconds)
        {
            var span = FadeToSeconds - FadeFromSeconds;
            var fade = span <= 0 ? (seconds >= FadeToSeconds ? 1 : 0) : Math.Clamp((seconds - FadeFromSeconds) / span, 0, 1);
            var smooth = fade * fade * (3 - 2 * fade);
            return ClearWeightEarly + (ClearWeightLate - ClearWeightEarly) * smooth;
        }
    }

    public sealed class PlanStage
    {
        public string Name { get; set; } = "quick";
        public double BudgetMilliseconds { get; set; } = 850;
        public int ScreenCount { get; set; } = 10;
        public int BeamWidth { get; set; } = 4;
        public int Branching { get; set; } = 8;
        public int Depth { get; set; } = 3;
        public Planning.EvaluationMode Mode { get; set; } = Planning.EvaluationMode.Screen;

        public double? HorizonGold { get; set; }
    }

    public sealed class PlannerSettings
    {
        public List<PlanStage> Stages { get; set; } = [new()];

        public PlanStage? Opening { get; set; }

        public List<PlanStage> OpeningStages { get; set; } = [];

        public IReadOnlyList<PlanStage> Ladder(double gameTime)
        {
            if (gameTime >= OpeningSeconds)
            {
                return Stages;
            }

            if (OpeningStages.Count > 0)
            {
                return OpeningStages;
            }

            return Opening is { } opening ? [opening] : Stages;
        }

        public double OpeningSeconds { get; set; } = 90;
        public double HorizonGold { get; set; } = 8000;
        public double MinHorizonSeconds { get; set; } = 480;
        public double DiscountSeconds { get; set; } = 600;
        public double StackLookaheadSeconds { get; set; } = 300;
        public double ReplaceMargin { get; set; } = 0.03;
        public double ReplaceFinishedMargin { get; set; } = 0.1;
        public double KeepMargin { get; set; } = 0.02;
        public double ComponentValueShare { get; set; } = 0.6;
        public double PrescreenShare { get; set; } = 0.4;
        public double SpikeWeight { get; set; } = 0.25;
        public int SpikeChecks { get; set; } = 2;
        public double SpikeWindowSeconds { get; set; } = 150;
        public int ReorderPasses { get; set; } = 2;
        public double CompareSeconds { get; set; } = 2400;
        public bool RequireBoots { get; set; } = true;
        public int BootsLines { get; set; } = 2;
        public int MinScreened { get; set; } = 12;
        public int MinScored { get; set; } = 4;
        public double TimeBucketSeconds { get; set; } = 15;
        public double CheapTimeBucketSeconds { get; set; } = 60;
        public double ReplanSeconds { get; set; } = 30;
        public double SellRefund { get; set; } = 0.7;
        public double FirstRecallSeconds { get; set; } = 210;
    }

    public sealed class IncomeSettings
    {
        public double StartingGold { get; set; } = 500;
        public double RateUncertainty { get; set; } = 0.15;
        public double PaceWindowSeconds { get; set; } = 300;
        public double MinRecentSpanSeconds { get; set; } = 120;
        public double RecentWeight { get; set; } = 0.3;
        public double TrendWeight { get; set; } = 0.4;
        public double BaselineOnlyUntilSeconds { get; set; } = 120;
        public double ObservedOnlyFromSeconds { get; set; } = 300;
        public double LevelReversionSeconds { get; set; } = 900;
        public double PaceBucket { get; set; } = 0.1;
        public double TakedownBucket { get; set; } = 2;

        /// <summary>An enemy this close to affording their next item is forecast to have it.</summary>
        public double EnemyGoldGrace { get; set; } = 300;
    }
}

public sealed class ModelData
{
    public ModelData(ModelSettings settings, BaselineCurves baseline, MetaBuilds meta)
    {
        Settings = settings;
        Baseline = baseline;
        Meta = meta;
    }

    public ModelSettings Settings { get; }

    public BaselineCurves Baseline { get; }

    public MetaBuilds Meta { get; }

    public static ModelData Load(string dataRoot) =>
        new(ModelSettings.Load(dataRoot), BaselineCurves.Load(dataRoot),
            new MetaBuilds(ChampionRepository.Load(dataRoot), ItemRepository.Load(dataRoot)));
}
