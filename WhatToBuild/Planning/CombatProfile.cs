using WhatToBuild.Data;
using WhatToBuild.Forecasting;
using WhatToBuild.Game;
using WhatToBuild.Modeling;
using WhatToBuild.Modeling.Simulation;

namespace WhatToBuild.Planning;

public sealed record DamageStream(
    string Source,
    DamageType Type,
    HitFlags Flags,
    double RawPerSecond,
    double Burst = 0,
    double TargetMaxHealthPerSecond = 0);

public sealed record SustainSource(string Source, double Amount);

public sealed class CombatProfile
{
    public required ForecastPlayer Forecast { get; init; }

    public ChampionState Entity => Forecast.Entity;

    public Champion Champion => Forecast.Champion;

    public required IReadOnlyList<DamageStream> Streams { get; init; }

    public required double SelfHealPerSecond { get; init; }

    public required double AllyHealPerSecond { get; init; }

    public required IReadOnlyList<Shield> SelfShields { get; init; }

    public required double AllyShield { get; init; }

    public required double GrievousWounds { get; init; }

    public required double ShieldReduction { get; init; }

    public required double ArmorShred { get; init; }

    public required double MagicResistShred { get; init; }

    public double Threat { get; set; }

    public required IReadOnlyList<SustainSource> HealSources { get; init; }

    public required IReadOnlyList<SustainSource> ShieldSources { get; init; }

    public double RawDps => Streams.Sum(s => s.RawPerSecond);

    public double RawBurst => Streams.Sum(s => s.Burst);

    public bool IsSupport => string.Equals(Forecast.Player.Position, "UTILITY", StringComparison.OrdinalIgnoreCase);

    public override string ToString() => $"{Champion.Name}: {RawDps:0} dps, {SelfHealPerSecond:0} hps";
}

public sealed class CombatProfiler
{
    private readonly ModelSettings _settings;

    public CombatProfiler(ModelSettings settings)
    {
        _settings = settings;
    }

    public CombatProfile Profile(ForecastPlayer forecast)
    {
        var e = _settings.EnemyDamage;
        var s = _settings.Sustain;
        var fight = _settings.Fight;
        var champion = forecast.Champion;
        var entity = forecast.Entity;
        var level = forecast.Level;
        var stats = entity.Stats;

        var bonusAd = AttackerHits.BonusAttackDamage(entity);
        var bonusHealth = entity.BonusHealth;
        var effects = entity.Items.SelectMany(i => i.Effects.Select(x => (Item: i, Effect: x))).ToList();

        var streams = new List<DamageStream>();

        var autoShare = (champion.IsRanged ? e.AutoUptimeRanged : e.AutoUptimeMelee)
                        * Math.Max(e.MinAutoShare, champion.Tag("adDamageDealer"));
        var attacksPerSecond = stats.AttackSpeed * autoShare;
        var critChance = AttackerHits.CritChance(entity);
        var critDamage = AttackerHits.CritDamage(entity);

        streams.Add(new DamageStream("Attacks", DamageType.Physical, HitFlags.Attack, stats.AttackDamage * attacksPerSecond * (1 - critChance)));
        if (critChance > 0)
        {
            streams.Add(new DamageStream("Critical strikes", DamageType.Physical, HitFlags.Attack | HitFlags.Crit,
                stats.AttackDamage * critDamage * attacksPerSecond * critChance));
        }

        var cycle = e.AbilityCycleSeconds * 100 / (100 + stats.AbilityHaste);
        var castsPerSecond = e.CastsPerCycle / cycle;

        foreach (var (item, effect) in effects.Where(x => IsDamage(x.Effect) && !x.Effect.Splash && !x.Effect.ByCompanion && !OnlyVersusMonsters(x.Effect)))
        {
            var rate = effect.Trigger switch
            {
                EffectTrigger.OnAttack => effect.EveryAttacks > 0 ? attacksPerSecond / effect.EveryAttacks : attacksPerSecond,
                EffectTrigger.OnAbility => castsPerSecond,
                EffectTrigger.InCombat => effect.Cooldown > 0 ? 1 / effect.Cooldown : 1,
                _ => 0,
            };

            if (effect.Cooldown > 0 && effect.Trigger != EffectTrigger.InCombat)
            {
                rate = Math.Min(rate, 1 / effect.Cooldown);
            }

            if (rate <= 0)
            {
                continue;
            }

            var ranged = champion.IsRanged ? effect.RangedMultiplier : 1;
            var raw = AttackerHits.OwnerAmount(effect, entity) * ranged * (1 + effect.MissingHealthAmp * (1 - e.AverageTargetHealthPercent));
            var percent = (effect.PerTargetMaxHealth + effect.PerTargetCurrentHealth * e.AverageTargetHealthPercent) * ranged;
            var type = effect.Kind switch
            {
                EffectKind.PhysicalDamage => DamageType.Physical,
                EffectKind.MagicDamage => DamageType.Magic,
                EffectKind.TrueDamage => DamageType.True,
                _ => bonusAd >= stats.AbilityPower ? DamageType.Physical : DamageType.Magic,
            };
            var flags = effect.Trigger switch
            {
                EffectTrigger.OnAttack => HitFlags.Attack,
                EffectTrigger.OnAbility => HitFlags.Ability,
                _ => HitFlags.None,
            };

            streams.Add(new DamageStream(item.Name, type, flags, raw * rate, 0, percent * rate));
        }

        var ap = champion.Tag("apDamageDealer") * (e.ApBase + e.ApPerLevel * level + e.ApRatio * stats.AbilityPower);
        var ad = champion.Tag("adDamageDealer") * (champion.IsRanged ? e.AdRangedAbilityShare : 1)
                 * (e.AdBase + e.AdPerLevel * level + e.AdBonusRatio * bonusAd)
                 + forecast.AbilityDamageStacks * e.AbilityDamageStackRatio;
        var tank = champion.Tag("tank") * (e.TankBase + e.TankPerLevel * level + e.TankBonusHealthRatio * bonusHealth);
        var trueShare = Math.Clamp(champion.Tag("trueDamage") * e.TrueDamageShare, 0, 1);
        var burst = champion.Tag("burst") * e.BurstShare;

        void Ability(string source, DamageType type, double combo)
        {
            if (combo > 0)
            {
                streams.Add(new DamageStream(source, type, HitFlags.Ability, combo / cycle, combo * burst));
            }
        }

        Ability("Abilities (magic)", DamageType.Magic, (ap + tank) * (1 - trueShare));
        Ability("Abilities (physical)", DamageType.Physical, ad * (1 - trueShare));
        Ability("Abilities (true)", DamageType.True, (ap + tank + ad) * trueShare);

        var healPower = 1 + entity.Items.Sum(i => i.Stats.HealAndShieldPowerPercent)
                          + forecast.TeamBuffs.Where(m => m.Stat == Stats.HealAndShieldPowerPercent).Sum(m => m.Flat);
        var allyShare = string.Equals(forecast.Player.Position, "UTILITY", StringComparison.OrdinalIgnoreCase) ? s.SupportAllyShare : 0;

        var heals = new List<SustainSource>();
        var champHeal = champion.Tag("healing")
                        * (s.HealBase + s.HealPerLevel * level + s.HealApRatio * stats.AbilityPower
                           + s.HealBonusAdRatio * bonusAd + s.HealBonusHealthRatio * bonusHealth);
        if (champHeal > 0)
        {
            heals.Add(new SustainSource($"{champion.Name}'s abilities", champHeal * healPower));
        }

        var dealt = s.DamageDealtMitigation;
        var attackDps = streams.Where(x => x.Flags.HasFlag(HitFlags.Attack)).Sum(x => x.RawPerSecond);
        var lifeSteal = entity.Items.Sum(i => i.Stats.LifeStealPercent) * attackDps * dealt;
        var omnivamp = (entity.Items.Sum(i => i.Stats.OmnivampPercent)
                        + effects.Where(x => StatCalculator.IsPermanentStatBuff(x.Effect) && x.Effect.Stat == Stats.OmnivampPercent).Sum(x => x.Effect.Amount))
                       * streams.Sum(x => x.RawPerSecond) * dealt;
        if (lifeSteal + omnivamp > 0)
        {
            heals.Add(new SustainSource("Life steal and omnivamp", (lifeSteal + omnivamp) * healPower));
        }

        var shields = new List<Shield>();
        var shieldSources = new List<SustainSource>();
        var champShield = champion.Tag("shielding")
                          * (s.ShieldBase + s.ShieldPerLevel * level + s.ShieldApRatio * stats.AbilityPower + s.ShieldBonusHealthRatio * bonusHealth);
        if (champShield > 0)
        {
            shieldSources.Add(new SustainSource($"{champion.Name}'s abilities", champShield * healPower));
        }

        foreach (var (item, effect) in effects.Where(x => x.Effect.Kind is EffectKind.Heal or EffectKind.Shield))
        {
            var ranged = champion.IsRanged ? effect.RangedMultiplier : 1;
            var amount = AttackerHits.OwnerAmount(effect, entity) * ranged * healPower;
            if (amount <= 0)
            {
                continue;
            }

            if (effect.Kind == EffectKind.Shield || effect.Trigger == EffectTrigger.WhenLow)
            {
                var times = effect.Trigger == EffectTrigger.InCombat && effect.Cooldown > 0 && effect.Cooldown < fight.TeamfightSeconds
                    ? fight.TeamfightSeconds / effect.Cooldown
                    : effect.Trigger is EffectTrigger.InCombat or EffectTrigger.WhenLow ? 1 : 0;

                if (times > 0)
                {
                    shields.Add(new Shield(amount * times * (1 - allyShare), effect.Kind == EffectKind.Shield ? effect.Versus : DamageSource.All));
                    shieldSources.Add(new SustainSource(item.Name, amount * times));
                }

                continue;
            }

            var perSecond = effect.Trigger switch
            {
                EffectTrigger.InCombat => effect.Cooldown > 0 ? amount / effect.Cooldown : amount,
                EffectTrigger.OnAttack => amount * (effect.Cooldown > 0 ? Math.Min(attacksPerSecond, 1 / effect.Cooldown) : attacksPerSecond),
                _ => 0,
            };

            if (perSecond > 0)
            {
                heals.Add(new SustainSource(item.Name, perSecond));
            }
        }

        var totalHeal = heals.Sum(h => h.Amount);
        if (champShield > 0)
        {
            shields.Add(new Shield(champShield * healPower * (1 - allyShare)));
        }

        return new CombatProfile
        {
            Forecast = forecast,
            Streams = streams,
            SelfHealPerSecond = totalHeal * (1 - allyShare),
            AllyHealPerSecond = totalHeal * allyShare,
            SelfShields = shields,
            AllyShield = shieldSources.Sum(x => x.Amount) * allyShare,
            GrievousWounds = effects.Where(x => x.Effect.Kind == EffectKind.GrievousWounds).Select(x => x.Effect.Amount).DefaultIfEmpty(0).Max(),
            ShieldReduction = effects.Where(x => x.Effect.Kind == EffectKind.ShieldReduction)
                .Select(x => x.Effect.Amount * (champion.IsRanged ? x.Effect.RangedMultiplier : 1)).DefaultIfEmpty(0).Max(),
            ArmorShred = effects.Where(x => x.Effect.Kind == EffectKind.ArmorShred).Select(x => x.Effect.Amount).DefaultIfEmpty(0).Max(),
            MagicResistShred = effects.Where(x => x.Effect.Kind == EffectKind.MagicResistShred).Select(x => x.Effect.Amount).DefaultIfEmpty(0).Max(),
            HealSources = heals,
            ShieldSources = shieldSources,
        };
    }

    public void AssignThreat(IReadOnlyList<CombatProfile> team)
    {
        if (team.Count == 0)
        {
            return;
        }

        var t = _settings.Threat;
        var meanGold = Math.Max(1, team.Average(p => p.Forecast.Earned));

        foreach (var profile in team)
        {
            var champion = profile.Champion;
            var damage = Math.Clamp(
                Math.Max(champion.Tag("adDamageDealer"), champion.Tag("apDamageDealer")) + t.BurstTagWeight * champion.Tag("burst"), 0, 1);

            profile.Threat = Math.Pow(profile.Forecast.Earned / meanGold, t.GoldExponent) * (1 - t.DamageTagWeight + t.DamageTagWeight * damage);
        }

        var total = team.Sum(p => p.Threat);
        foreach (var profile in team)
        {
            profile.Threat = total > 0 ? profile.Threat / total : 1.0 / team.Count;
        }
    }

    public TargetSustain SustainFor(CombatProfile target, IReadOnlyList<CombatProfile> team, IReadOnlyList<CombatProfile> ourAllies, GameState state, NeutralRepository neutrals)
    {
        var s = _settings.Sustain;
        var others = team.Where(p => !ReferenceEquals(p, target)).ToList();
        var maxHealth = target.Entity.MaxHealth;
        var side = target.Forecast.Player.Team;

        var heal = target.SelfHealPerSecond
                   + others.Sum(p => p.AllyHealPerSecond) * s.AllyReceiveShare
                   + state.TeamTag(side, "healing", neutrals) * s.DragonHealPerTag * maxHealth;

        var shields = target.SelfShields.ToList();
        var allyShield = others.Sum(p => p.AllyShield) * s.AllyReceiveShare
                         + state.TeamTag(side, "shielding", neutrals) * s.DragonShieldPerTag * maxHealth;
        if (allyShield > 0)
        {
            shields.Add(new Shield(allyShield));
        }

        var grievous = ourAllies.Select(a => a.GrievousWounds).DefaultIfEmpty(0).Max() * _settings.Allies.GrievousCoverage;
        var reduction = ourAllies.Select(a => a.ShieldReduction).DefaultIfEmpty(0).Max() * _settings.Allies.ShieldReductionCoverage;

        var armorShred = ourAllies.Select(a => a.ArmorShred).DefaultIfEmpty(0).Max() * _settings.Allies.ShredCoverage;
        var magicShred = ourAllies.Select(a => a.MagicResistShred).DefaultIfEmpty(0).Max() * _settings.Allies.ShredCoverage;

        return new TargetSustain(heal, shields, grievous, reduction, armorShred, magicShred);
    }

    private static bool IsDamage(Effect effect) =>
        effect.Kind is EffectKind.PhysicalDamage or EffectKind.MagicDamage or EffectKind.TrueDamage or EffectKind.AdaptiveDamage;

    private static bool OnlyVersusMonsters(Effect effect) =>
        effect.When.Any(c => c.Property == ConditionProperty.IsMonster && c.Op == ConditionOp.AtLeast && c.Value >= 1);
}
