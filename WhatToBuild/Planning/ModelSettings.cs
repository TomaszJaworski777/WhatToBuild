using System.Text.Json;
using WhatToBuild.Data;
using WhatToBuild.Forecasting;

namespace WhatToBuild.Planning;

/// <summary>
/// Every tunable number of the build model, read from <c>GameData/Model/model.json</c>. None of these are
/// game data: they are judgement calls (how much damage a champion tag stands for, how long a teamfight
/// lasts) and live in a file so they can be tuned without touching code. See the README's Model section.
/// </summary>
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

    public static ModelSettings Load(string dataRoot) =>
        JsonSerializer.Deserialize<ModelSettings>(
            File.ReadAllText(Path.Combine(dataRoot, FolderName, FileName)), GameDataJson.Options)
        ?? throw new InvalidDataException($"{FileName} is empty.");

    public sealed class FightSettings
    {
        /// <summary>Seconds a teamfight lasts; survival beyond it is worth less and less.</summary>
        public double TeamfightSeconds { get; set; } = 12;
        public double MaxFightSeconds { get; set; } = 30;
        public List<double> AttackPhases { get; set; } = [0, 0.25, 0.5, 0.75];
        public List<double> ScreenAttackPhases { get; set; } = [0, 0.5];
        public List<double> CheapAttackPhases { get; set; } = [0.5];
        /// <summary>How many of the biggest threats the pre-screen fights.</summary>
        public int CheapTargets { get; set; } = 3;
        public double MaxTimeAliveSeconds { get; set; } = 60;
        /// <summary>Typical time between teamfights: a survival ability on a longer cooldown is not always up.</summary>
        public double TeamfightIntervalSeconds { get; set; } = 120;
    }

    public sealed class FocusSettings
    {
        public int TeamSize { get; set; } = 5;
        /// <summary>Extra share of enemy damage a full tank (tag 1) on your team draws.</summary>
        public double FrontlineWeight { get; set; } = 1.5;
        public double MeleeWeight { get; set; } = 0.3;
        /// <summary>How far burst champions skip the frontline to reach a ranged carry, per burst tag.</summary>
        public double DiveBias { get; set; } = 1.2;
        /// <summary>Extra focus on squishy damage dealers (tank tag under 0.3): enemies go for the carries first.</summary>
        public double CarryFocusBias { get; set; } = 1.0;
        /// <summary>Share of melee enemies' attacks a ranged champion avoids by kiting and positioning.</summary>
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
        /// <summary>Share of an ability aimed at someone else that still hits you (area damage).</summary>
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

        public ObjectiveWeights For(Champion champion) =>
            Champions.GetValueOrDefault(champion.Name) ?? Default;
    }

    /// <summary>
    /// What a champion wants from its items. The score is
    /// <c>damage·ln(damage before death) + survival·ln(time alive) + clear·phase·ln(clear speed)</c>,
    /// so each weight says how many percent of one is worth a percent of the other.
    /// </summary>
    public sealed class ObjectiveWeights
    {
        public double Damage { get; set; } = 1;
        public double Survival { get; set; } = 0.25;
        public double Clear { get; set; } = 1;

        /// <summary>How much faster movement around the map counts (see <see cref="MovementSettings"/>).</summary>
        public double Movement { get; set; } = 1;
    }

    /// <summary>
    /// Movement speed is worth time: the share of a game you spend walking between camps, lanes and
    /// fights shrinks as you get faster, and that time goes into farming and fighting. Tempo is
    /// <c>1 / (walkShare · base speed / speed + 1 − walkShare)</c>, and the score adds
    /// <c>tempoWeight · ln(tempo)</c>. <c>tempoWeight</c> is calibrated so that one point of movement speed is
    /// worth about 12 gold of your other stats, the value the League wiki gives flat movement speed (Boots:
    /// 300 gold for 25).
    /// </summary>
    public sealed class MovementSettings
    {
        public Dictionary<string, double> WalkShare { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public double DefaultWalkShare { get; set; } = 0.3;
        public double TempoWeight { get; set; } = 1;

        /// <summary>Incoming damage scales with (enemy team's average speed / yours) to this power: dodging and repositioning.</summary>
        public double EvasionExponent { get; set; } = 1;

        /// <summary>How strongly kiting a melee champion grows with your speed over theirs.</summary>
        public double KiteSpeedExponent { get; set; } = 2;

        public double MaxKiteReduction { get; set; } = 0.7;

        /// <summary>
        /// [minute, weight] points, linearly interpolated. Measured so that one point of movement speed is worth
        /// about 12 gold of your other stats at that point in the game: early on stats are worth more per gold,
        /// so movement needs a larger weight to keep up. Empty means <see cref="TempoWeight"/> throughout.
        /// </summary>
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
        /// <summary>Below this clear weight the clear is not simulated at all: its effect on the score would be noise.</summary>
        public double MinClearWeight { get; set; } = 0.05;
        /// <summary>Clear weight starts fading at this many completed items and is gone at <see cref="ClearFadeToItems"/>.</summary>
        public double ClearFadeFromItems { get; set; } = 1.5;
        public double ClearFadeToItems { get; set; } = 2.5;
        /// <summary>The same for the pre-screen, which has to stay cheap.</summary>
        public double CheapMinClearWeight { get; set; } = 0.25;

        public double ClearWeightAt(double seconds)
        {
            var span = FadeToSeconds - FadeFromSeconds;
            var fade = span <= 0 ? (seconds >= FadeToSeconds ? 1 : 0) : Math.Clamp((seconds - FadeFromSeconds) / span, 0, 1);
            var smooth = fade * fade * (3 - 2 * fade);
            return ClearWeightEarly + (ClearWeightLate - ClearWeightEarly) * smooth;
        }
    }

    /// <summary>
    /// One pass of the planner. The first stage has to answer inside a second of one core; later stages
    /// run in the background for longer, look at more options with every attack timing, and replace the
    /// build when they finish.
    /// </summary>
    public sealed class PlanStage
    {
        public string Name { get; set; } = "quick";
        public double BudgetMilliseconds { get; set; } = 850;
        /// <summary>How many pre-screened candidates get a proper score as your next item.</summary>
        public int ScreenCount { get; set; } = 10;
        public int BeamWidth { get; set; } = 4;
        public int Branching { get; set; } = 8;
        public int Depth { get; set; } = 3;
        public Planning.EvaluationMode Mode { get; set; } = Planning.EvaluationMode.Screen;
    }

    public sealed class PlannerSettings
    {
        public List<PlanStage> Stages { get; set; } = [new()];
        public double HorizonGold { get; set; } = 8000;
        public double MinHorizonSeconds { get; set; } = 480;
        public double DiscountSeconds { get; set; } = 600;
        public double StackLookaheadSeconds { get; set; } = 300;
        public double ReplaceMargin { get; set; } = 0.03;
        /// <summary>The same for selling a finished item (2000 gold or more).</summary>
        public double ReplaceFinishedMargin { get; set; } = 0.1;
        public double KeepMargin { get; set; } = 0.02;
        /// <summary>Share of the budget the cheap pre-screen may use before it stops.</summary>
        public double PrescreenShare { get; set; } = 0.4;
        public double TimeBucketSeconds { get; set; } = 15;
        /// <summary>Coarser time grid for the pre-screen, so it reuses a few forecasts instead of building one per candidate.</summary>
        public double CheapTimeBucketSeconds { get; set; } = 60;
        public double ReplanSeconds { get; set; } = 30;
        public double SellRefund { get; set; } = 0.7;
    }

    public sealed class IncomeSettings
    {
        public double StartingGold { get; set; } = 500;
        public double RateUncertainty { get; set; } = 0.15;
        public double MinPace { get; set; } = 0.5;
        public double MaxPace { get; set; } = 2;
        public double PaceWindowSeconds { get; set; } = 300;
        public double MinRecentSpanSeconds { get; set; } = 120;
        /// <summary>Share of the recent rate (vs. the whole-game average) in a player's own pace.</summary>
        public double RecentWeight { get; set; } = 0.3;
        /// <summary>Share of the lobby's average pace in everyone else's pace, since their items refresh only on recall.</summary>
        public double TrendWeight { get; set; } = 0.4;
        public double BaselineOnlyUntilSeconds { get; set; } = 120;
        public double ObservedOnlyFromSeconds { get; set; } = 300;
        public double LevelReversionSeconds { get; set; } = 900;
    }
}

/// <summary>Everything the build model reads besides the game data itself.</summary>
public sealed class ModelData
{
    public ModelData(ModelSettings settings, BaselineCurves baseline, BuildArchetypes archetypes)
    {
        Settings = settings;
        Baseline = baseline;
        Archetypes = archetypes;
    }

    public ModelSettings Settings { get; }

    public BaselineCurves Baseline { get; }

    public BuildArchetypes Archetypes { get; }

    public static ModelData Load(string dataRoot) =>
        new(ModelSettings.Load(dataRoot), BaselineCurves.Load(dataRoot), BuildArchetypes.Load(dataRoot));
}
