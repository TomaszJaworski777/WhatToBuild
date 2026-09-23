# Game data convention

Every file here is hand-written and committed. Nothing fetches or rewrites them at
runtime. `_patch.json` records which patch the numbers were taken from; moving to a
new patch means editing files and committing, so a patch bump is always a reviewed
change.

## Items

One file per item, in `Items/`, named `<riotId>-<slug>.json`.

```json
{
  "id": "206b362f-fe5e-0538-c3fb-5b9b7145d61f",
  "riotId": 6672,
  "name": "Kraken Slayer",
  "icon": "6672.png",
  "cost": 3000,
  "unique": true,
  "buildPath": ["<component guid>", "..."],
  "stats": { "attackDamage": 45, "attackSpeedPercent": 0.4 },
  "effects": [
    { "trigger": "OnAttack", "kind": "PhysicalDamage", "amount": 175, "everyAttacks": 3, "rangedMultiplier": 0.8 }
  ]
}
```

- `id` is ours and never changes. `riotId` is the join key: the Live Client API
  reports inventory by it.
- `cost` is total gold. Combine cost is `cost` minus the components' costs.
- `buildPath` holds component `id`s, and components are full items in their own
  right. The planner scores partial builds, so it can tell that a B. F. Sword first
  buy is 1300 gold of pure AD with no attack speed.
- `stats` omits anything that is zero. Flat values are game units; percent values
  are fractions, so `0.25` means 25%.
- `unique` and `groups` are the game's purchase limits, copied from the item groups in
  CommunityDragon's `items.cdtb.bin.json` (`mItemGroups` + `mMaxGroupOwnable`).
  `unique: true` means you can own only one copy. Each name in `groups` allows one
  item from that group: `LastWhisper`, `LifelineItems`, `Boots`, `VoidPen`, `TearItems`,
  `Quicksilver`, `EternityItems` and so on. Four groups have only a hash in the game
  data, so they are named here: `Hydra`, `Spellblade`, `Annul` (spell shields) and
  `Thorns`. Components are consumed when the item is built, so building Lord
  Dominik's from a Last Whisper is fine.

## Effects

An effect is a **ballpark of what an item contributes**: how much, and how often.
That is all a buy decision needs.

Proc mechanics are kept only as far as they change the answer. Kraken Slayer fires on
every third attack, so it is written as `everyAttacks: 3` and gains value with
attack speed, which is why it pairs with attack speed items. How much of its bonus
comes from the target's missing health is left out: it does not change whether you
buy it. When a number is simplified, it is checked against the item's data values in
CommunityDragon's `items.cdtb.bin.json` rather than written from memory.

```json
{ "trigger": "OnAttack", "kind": "PhysicalDamage", "amount": 175, "everyAttacks": 3, "rangedMultiplier": 0.8 }
```

### `trigger` — roughly when

| Value | Meaning |
|---|---|
| `Always` | Permanently on |
| `OnAttack` | Comes from attacking |
| `OnAbility` | Comes from casting |
| `InCombat` | Repeats while fighting, every `cooldown` seconds |
| `WhenLow` | Fires when you drop low on health |
| `OnTakedown` | Fires on a kill or assist |
| `OutOfCombat` | Only while not fighting (Warmog's regeneration), so it never counts in a fight |

### `kind` — what it contributes

| Value | `amount` is |
|---|---|
| `PhysicalDamage` `MagicDamage` `TrueDamage` | damage, in game units |
| `Heal` `Shield` | health restored or absorbed |
| `DamageReduction` | a fraction (`0.1`), or flat units when above 1 |
| `StatBuff` | the value of the stat named in `stat` |
| `ArmorShred` `MagicResistShred` | fraction of the target's resist removed |
| `GrievousWounds` | fraction of healing denied |
| `ShieldReduction` | fraction of shielding denied |
| `Execute` | health fraction below which targets die |
| `Revive` | fraction of health restored on revival |

### Fields

- `amount` — flat magnitude per occurrence.
- `cooldown` — seconds between occurrences. `0` means every time.
- `everyAttacks` — the effect triggers on every Nth attack, so it scales with attack
  speed. Kraken Slayer is `3` (`AttackCount` in its bin data). Use this instead of a
  `cooldown` whenever the game counts attacks.
- `splash` — `true` when the damage hits enemies *around* the target but not the
  target itself (Tiamat, the Hydras, Stridebreaker, Runaan's bolts). Single-target
  damage leaves it out.
- `area` — `true` when the damage hits the target *and* every enemy around it (the
  jungle pets, which hit the whole camp). Fights are one target for now, so it only
  matters once camps are simulated as a group.
- `byCompanion` — `true` when another unit deals the damage (the jungle pet). Our
  damage amps and penetration do not apply to it.
- `rangedMultiplier` — the game gives many item effects a smaller value on ranged
  champions. `amount` and the scalings are the melee values, and ranged champions get
  them times this (Kraken 0.8, Ruined King 0.6667, Titanic 0.5). The numbers come from
  each item's bin data values (`RangedDamageMultiplier`, `RangedValue` / `MeleeValue`, ...).
- `meleeMultiplier` — the same the other way, for effects a melee owner gets less of, or
  none: Hexoptics' 10% damage amplifier is `0` here, so only ranged champions are credited
  with it. Both multipliers apply wherever the effect is read — in a fight, in the stat sheet
  and in the enemy damage estimate.
- `stat` — for `StatBuff` and shreds, which stat. It is written as a name but parses
  into a `Stat` type, so an unknown name fails the load instead of reaching the
  simulation. Each type knows whether it is a fraction and whether it applies to the
  enemy rather than to you, which is how `enemyAttackSpeedPercent` and
  `enemyMagicDamageAmp` stay distinguishable from your own stats. `Stats.All` is the
  full list.
- `versus` — for `DamageReduction`, `Shield` and damage amps, what it applies to: `All` (the
  default), `Attacks`, `Abilities`, `Crit`, `Physical` or `Magic`. This is the one
  place the ballpark is not allowed to round off, because it decides *who* an item
  is good against. Randuin's 30% is `Crit`, Plated Steelcaps' 10% is `Attacks`, and
  Maw's shield is `Magic`; calling any of them blanket mitigation would recommend
  them against comps they do nothing to.
- Scaling, any of which may be combined with `amount`:
  `perBaseAd`, `perTotalAd`, `perBonusAd`, `perAp`, `perMaxHealth`, `perBonusHealth`,
  `perBonusArmor`, `perBonusMagicResist`, `perTargetMaxHealth`, `perTargetCurrentHealth`.
- `missingHealthAmp` — damage times 1 + this × the target's missing health fraction.
  Kraken Slayer is `0.75` (`MaxAmpNumber` 1.75 in its bin data).
- `stacksTo` — the effect builds up evenly over this many attacks in a fight instead of
  applying in full from the first hit. Black Cleaver is `5` (`MaxStacks`, 6% `ShredPerStack`);
  Terminus's 30% penetration is `6`, 10% per hit alternating Light and Dark. Every fight starts
  it empty: your hits count the attacks landed so far, and an enemy's damage on you is averaged
  over the attacks it lands in a teamfight (attack speed × `teamfightSeconds`), from the first.
  "Bonus" is the stat minus the champion's base at its level, so items, runes and
  dragons all count.
  `perLethality` scales with the lethality from items, `perCritChance` with crit
  chance (50 means +50 at 100% crit). `perLevel` grows from level 2 unless
  `perLevelFrom` says otherwise (Bloodthirster's shield grows from level 9).
  Permanent `StatBuff`s may use `perBaseAd` and `perBonusHealth` (Sterak's +50% base AD,
  Riftmaker's 2% bonus health as AP).
- Where an item's bin has a formula for an effect (`mItemCalculations` in
  `items.cdtb.bin.json`), the effect copies it: Sterak's shield is `ShieldSize` = 60%
  bonus health, Sunfire is `DamagePerTick` = 20 + 1.5% bonus health, and so on. The stat
  numbers in those formulas read as 0 AP, 1 armor, 2 AD, 6 magic resist, 8 crit
  chance, 12 health, 29 lethality, with formula 1 = base and 2 = bonus.

Anything absent is zero, so the simulation always reads a number and never has to
check whether a key exists.

### `when` — conditions

Some items are only worth their number against the right target. Lord Dominik's 15%
does nothing to a squishy, and pricing it as a flat amp would recommend it into
comps it barely touches. `when` holds requirements that **all** have to hold:

```json
{ "trigger": "Always", "kind": "StatBuff", "stat": "damageAmp", "amount": 0.15,
  "when": [ { "subject": "Target", "property": "BonusHealth", "op": "AtLeast", "value": 1500 } ] }
```

- `subject` — `Self` or `Target`
- `property` — `HealthPercent` (0–1), `BonusHealth`, `MaxHealth`, `Armor`,
  `MagicResist`, `Level`, `IsMonster` (1 for jungle monsters, 0 otherwise; use
  `AtLeast 1` for "only against monsters")
- `op` — `AtLeast` or `AtMost`
- `value` — the threshold

An empty or absent `when` means the effect always counts.

The conditions are thresholds, not curves. A ramp is written as steps: Lord Dominik's
reaches its full 15% at 1500 bonus health (`MaxBonusHealth` in its data), so it has
two effects, 7.5% from 750 to 1499 and 15% from 1500. The evaluator needs to know
*who this is good against*, not the exact curve.

`when` and `versus` are the two halves of the same idea: `versus` conditions on the
kind of damage, `when` conditions on the state of the fight. Neither is optional
detail — they are what stop the simulator recommending an item into a matchup it
does nothing in.

### What is left out

An item only gets an effect if it changes damage, survivability or a counter-item
decision. Vision, mana restoration, ghosting, stasis and cooldown refunds are all
omitted: they are real, but nothing in the evaluator can price them, and inventing a
number for them would let hand-tuned guesses outvote the parts we can actually compute.
Movement speed is a stat (see Model → Movement speed). Gold generation lives in `stacking`
gains of `gold` and is priced by the planner as earlier income (see Model → Planner).

That means an empty `effects` array reads as "nothing here that changes a build
decision", not "this item does nothing". 105 of the 219 items are in that state,
including every pure-stat component.

Positional effects are the honest casualty of this rule. Attack range, ghosting and
slows only have value relative to a threat, which needs a fight model the evaluator
does not have yet. If one is added later, the place to put it is attack uptime —
the fraction of a fight you can spend attacking — because that converts range and
mobility into damage, which is already the scoring currency.

## Neutrals

Jungle camps, epic monsters and minions, one file per unit in `Neutrals/`.

The game files still contain units that are no longer in the game — Atakhan is in the
16.18 map data but was removed from the Rift, so it is not shipped here. Being present
in the data is not proof a unit is live.

```json
{
  "internalName": "SRU_Murkwolf",
  "name": "Murkwolf",
  "kind": "JungleCamp",
  "camp": "Wolves",
  "countPerCamp": 1,
  "base": { "health": 1600, "attackDamage": 30, "armor": 42, "magicResist": 42,
            "attackSpeed": 0.625, "moveSpeed": 350, "attackRange": 175 },
  "goldOnDeath": 55,
  "expOnDeath": 50
}
```

A camp is not a file: units carry `camp` and `countPerCamp`, so Wolves is one
Murkwolf plus two Lesser Murkwolves. That keeps a clear simulation to a group-by.

None of this is in Data Dragon — it comes from the per-character game data, same
source as champion attack damage growth. Each file has several records (Root plus
game-mode variants such as URF); Root is the one used.

Attack interval is not stored, because it is `1 / attackSpeed`. The game data also
carries an explicit attack animation time, and where both exist they agree — Murkwolf
is 1.6s either way — but four units have no explicit value, so deriving it is both
shorter and more complete.

### Dragons

`_dragons.json` holds, per dragon type, the buff one drake grants per stack and what
its soul does:

```json
"Earth": {
  "drake": "Mountain Drake",
  "perStack": [ { "stat": "armor", "percent": 0.05 }, { "stat": "magicResist", "percent": 0.05 } ],
  "stackTags": {},
  "soul": { "name": "Mountain Soul", "tags": { "shielding": 0.6 } }
}
```

- `perStack` modifiers feed straight into the stat sheet. `percent` scales the
  **total** stat after items, which is how Infernal and Mountain work; `flat` adds.
  Hextech's bonus attack speed goes through the attack speed ratio like item attack
  speed does. Stats with no place on the sheet (tenacity, haste) are kept for later.
- `stackTags` and `soul.tags` use the champion tag vocabulary. They carry what does
  not become a stat: Ocean stacks regenerate missing health, and souls are team-wide
  effects. `GameState.TeamTag(team, "healing", neutrals)` sums both, so an enemy
  Ocean Soul reads as healing even when none of their champions heal.

The per-stack numbers and the soul effects come from the wiki's Dragon Slayer page,
not from the game data — the buffs are script-defined and not in anything
CommunityDragon exports. That page's patch history ends at V25.S1.3, so the values
should be checked against 16.18. The soul tag weights are judgement.

The soul type is worked out from the game rather than stored: the third dragon fixes
the element of the Rift, so a team's fourth dragon is always that element. The parser
takes the type of the dragon that brings a team to four, ignoring Elder, after
sorting events by time.

### Armor and magic resist

They are separate values, but Riot's data makes them uniform per tier: every jungle
camp is 42/42 and every epic monster 34/32, including Baron at 34 armor / 32 magic
resist. That is what the character records hold; per-camp differences are not in
there, and the map data's armor entries turned out to be debuff scripts, not base
stats. If monsters do differ in practice, the difference is applied somewhere this
data does not reach — the same pattern as the incomplete units below.

### Time scaling

`_scaling.json` holds the camp scaling from the map data: nothing until 600s, then
+0.5% every 30s, capped at +150%, with the rate changing at 900s, 1800s and 2700s.
`NeutralScaling.MultiplierAt(seconds)` applies it.

One limitation worth knowing: the map file has eight scaling tables and their keys
are hashed, so which table belongs to which camp cannot be read out. The jungle
table is used for everything. Epic monsters almost certainly scale differently.

### Incomplete units

Four units carry `"statsIncomplete": true`, because their character record holds
placeholder values — the buff-camp minis list **1 health**, and Lesser Murkwolf has
no damage field at all. Their real values are applied somewhere the character data
does not reach.

They are shipped with the placeholders visible rather than with numbers invented to
look plausible, since a clear simulation that quietly treats a camp as free is worse
than one that reports a gap. A test pins the list to exactly those four.

## Runes

One file per rune in `Runes/`, named `<riotId>-<slug>.json`. Stat shards live here
too, under the pseudo-tree `Shard`.

```json
{
  "id": "44e6aef2-1875-addc-f3e2-20cf975445a1",
  "riotId": 8112,
  "key": "Electrocute",
  "name": "Electrocute",
  "icon": "perk-images/Styles/Domination/Electrocute/Electrocute.png",
  "tree": "Domination",
  "slot": 0,
  "effects": [
    { "trigger": "OnAttack", "kind": "AdaptiveDamage", "amount": 70,
      "perLevel": 10, "perBaseAd": 0.1, "perAp": 0.05, "cooldown": 20 }
  ]
}
```

`slot` is the row: `0` is the keystone, `1`–`3` the minor rows. For shards it is the
shard row instead. 62 tree runes, 10 shards.

Runes use the **same effect DSL as items**, which is why the type is `Effect` rather
than `ItemEffect`. Two additions exist because runes need them:

- `perLevel` — many runes scale with level (`70 - 240`), so `amount` is the level 1
  value and `perLevel` is the step. `amount + perLevel * 17` should equal the level
  18 number, and a test checks exactly that for Electrocute and the scaling shards.
- `AdaptiveDamage` — a kind of its own, because adaptive damage resolves to physical
  or magic from the holder's build. Writing it as one or the other would be wrong for
  half the champions who take it.

Unlike champion tooltips, rune descriptions carry **real numbers**, so keystone and
shard values are read off Riot's own text rather than estimated. Shards are exact.

Three keystones are deliberately unmodelled — Glacial Augment, Unsealed Spellbook
and Stormraider's Surge — because they are crowd control, summoner spells and
movement speed, which the evaluator cannot price. A test pins that list.

The 45 minor runes currently have empty effects. That is a real gap, not a decision:
several of them (Coup de Grace, Cut Down, Legend: Alacrity) change damage and will be
needed before rune pages can be recommended.

## Champions

One file per champion, in `Champions/`, named `<riotId>-<internalName>.json`.

```json
{
  "id": "54868bae-e663-138e-97e9-7f47d48bf4f9",
  "riotId": 203,
  "internalName": "Kindred",
  "name": "Kindred",
  "icon": "Kindred.png",
  "attackSpeedRatio": 0.625,
  "base":     { "health": 595, "attackDamage": 65, "armor": 29, "attackSpeed": 0.625 },
  "perLevel": { "health": 104, "attackDamage": 3.25, "armor": 4.7, "attackSpeed": 0.035 },
  "tags": { "adDamageDealer": 0.9, "ranged": 1.0, "healing": 0.4 },
  "stacking": []
}
```

Both ids are needed: champ select reports the numeric `riotId`, the Live Client API
reports `internalName`, and the two differ more often than expected — Wukong is
`MonkeyKing`.

`base` and `perLevel` are the same stat sheet twice: level 1 values, and growth per
level. The growth curve is not linear, but that formula belongs in `StatCalculator`;
these files are only data.

Two traps, both of which silently produce wrong damage:

- Data Dragon publishes `attackdamageperlevel` as **0 for every champion**, so
  `perLevel.attackDamage` comes from CommunityDragon's game data instead. Senna is
  the one champion where 0 is real — she gains attack damage from souls.
- Attack speed growth is a percent in Data Dragon (`3.5`) and a fraction here
  (`0.035`), like every other percent in this repo.

`attackSpeedRatio` sits outside the sheets because it does not grow. Bonus attack
speed scales off it rather than off base attack speed, and Data Dragon does not
publish it at all. Jhin is the exception with no ratio: his attack speed is fixed.

### Stacking

Champions that permanently gain a stat as the game goes on:

```json
"stacking": [ { "stat": "armor", "initialStacksPerMinute": 10, "max": 30 } ]
```

`initialStacksPerMinute` is the standard pace, and the forecast mostly keeps it. The
current stack count comes from game state; the pace from there on is the standard one,
leaned toward your own measured pace only from `stacks.trendFromSeconds` (8:00), by at most
`stacks.maxTrendWeight` (30%) reached at `stacks.trendFullSeconds` (25:00). Gold pace is not
applied on top: the measured pace already carries it. `max` caps the forecast, not what you
already have: Kindred's marks are forecast to at most 10, and 14 real marks stay 14.

Thirteen champions stack. Six grow an ordinary stat — Veigar, Thresh, Swain, Garen,
Senna, Bel'Veth — and three more grow max health: Cho'Gath, Sion and Swain again.
The rest grow `abilityDamage`: Nasus, Smolder, Kindred, Aurelion Sol and Viktor.

`abilityDamage` is not a stat anything can apply, because champion abilities are not
simulated. It exists so that threat estimation can tell a 900-stack Nasus from a
fresh one; without it he reads as his base kit all game, which is badly wrong.

This matters only for enemies. Your own stats arrive from the Live Client API with
stacks already included, but enemies are reconstructed from base + growth + items,
so without this a 30-minute Veigar reads as having only his item AP.

The Live Client API never reports stack counts. A champion whose stacks move a stat we
can see can carry a `stackReading` rule instead, and then our own count is read from
that stat rather than estimated:

```json
"stackReading": { "stat": "attackRange", "firstStacks": 4, "firstBonus": 75, "stepStacks": 3, "stepBonus": 25 }
```

`bonus` is the observed stat minus the base value and what items give. A bonus of 0
means fewer than `firstStacks`. `firstBonus` means `firstStacks` to one step below the
next threshold, and every further `stepBonus` adds `stepStacks`. A bonus that does not
land on a step (±1), such as a temporary range buff, falls back to the estimate. Only
Kindred has one, with numbers from `KindredPassiveManager` in her CommunityDragon bin:
`InitialMarkThreshold` 4, `RangeIncrease` 25 × `FirstTierMultiplier` 3, then
`AdditionalMarkThreshold` 3.


Two of these champions show it in their growth as well: Senna has no attack damage
per level and Thresh has no armor per level, because both gain it from stacks.

Champion files carry **weighted tags** instead of a full ability model, so the
evaluator can reason about matchups it does not simulate:

```json
"tags": { "adDamageDealer": 0.9, "burst": 0.7, "ranged": 1.0, "healing": 0.3 }
```

Each weight is 0–1 strength, not a yes/no. A champion with `healing: 0.9` makes
Grievous Wounds urgent; `healing: 0.2` does not.

Vocabulary: `tank`, `adDamageDealer`, `apDamageDealer`, `burst`, `trueDamage`,
`healing`, `shielding`, `crowdControl`, `ranged`, `melee`. A tag that does not apply
is omitted, not written as `0`.

Every tag has to change which items you buy. Playstyle traits like mobility or
split-pushing were deliberately dropped: they describe the champion without telling
the planner anything.

`healing` and `shielding` are derived from Riot's own spell data - the structured
`leveltip` labels, which say what a spell scales rather than what its flavour text
mentions - and weighted by how many abilities provide it. Nine champions whose
single source dominates their kit carry a hand-set value instead.

`ranged` and `melee` come from attack range, with the cutoff at 325.

`tank`, `adDamageDealer`, `apDamageDealer`, `burst`, `trueDamage` and `crowdControl`
are hand-assigned opinions and should be read as such.

Three champions — Yunara, Locke and Zaahen — have no damage profile, because they
were released after these were written and guessing would be worse than a gap. Their
`healing` and `shielding` are still correct, since those come from the data. A test
pins the list so it stays visible rather than silent.

These answer questions the item data alone cannot. Whether anti-heal is worth buying
depends on enemy healing from **both** sides: champion tags cover abilities, while
item-granted healing and shielding fall out of the effect data automatically — any
enemy item with `kind: "Heal"`, `"Shield"`, or `lifeStealPercent` counts. The same
pairing decides `ShieldReduction`: Serpent's Fang is worth buying when enemy
`shielding` tags plus shield-granting items clear a threshold, and dead weight when
they do not.

## Kits

Champions whose abilities are simulated have a file in `Kits/`, read by their code in
`SupportedChampions/<Name>/`: Kindred (`Kits/kindred.json`), Kayn (`Kits/kayn.json`), Vi (`Kits/vi.json`) and
Nasus (`Kits/nasus.json`).

### Kayn

Numbers from `kayn.bin.json` (spells `KaynQ`, `KaynW`, `KaynAssW`, `KaynE`, `KaynR`, `KaynPassive`),
meanings from the `game_spell_Kayn_*_main_<form>` strings, where form 0 is base, 1 Shadow Assassin
and 2 Darkin Slayer (Rhaast).

- `q` — Reaping Slash hits twice (dash, then spin): `BaseDamage` + 85% bonus AD each. Rhaast
  instead deals 65% total AD + (6% + 3.5% per 100 bonus AD) of max health each. Against
  monsters each hit gets `FlatBonusDmgToMonsters` 40, capped at `MaxDmgToMonsters` for the rank.
- `w` — Blade's Reach: `BaseDamage` + 110% bonus AD. Shadow Assassin casts `KaynAssW`, whose
  base damage (90–270) is its own; the 110% ratio is assumed to carry over, as its entry has no
  ratio of its own.
- `r` — Umbral Trespass needs a recently damaged champion; Kayn cannot attack while inside
  (`minimumInfest` 0.5s in fights, recast as soon as allowed). Base and Shadow Assassin deal
  `BaseDamage` + 150% bonus AD; Shadow Assassin also refreshes the passive on exit. Rhaast deals
  15% + 0.1% per bonus AD of max health and heals 75% of that. For survival it counts as
  `infestDuration` 2.5s untargetable, as often as its cooldown allows.
- `passive` — Rhaast heals 25% (+0.005% per bonus health) of physical damage dealt to
  champions. Shadow Assassin deals 20% → 40% (level 1 → 18) of damage dealt as bonus magic
  damage for 3 seconds after entering combat, then not again for 8 seconds unless R resets it.
- E (Shadow Step) moves through walls and does no damage, so fights leave it out.
- `castTime` (Q 0.15s, W 0.55s / Shadow Assassin 0.6s, R) blocks basic attacks while casting.
- **Weaving**: Kayn fights spell, attack, spell, attack. Every ability resets the attack timer,
  so an attack lands as soon as the cast ends, and the next spell waits for that attack. Two
  spells never land in the same instant. Between spells, while everything is on cooldown, he
  attacks at his attack speed. `attackUptime` is 1: the weave itself is the time he spends
  casting, so there is no separate discount on his attacks any more.

Development mode replays `Replays/kayn`: the Kindred demo game with the active player turned
into a level 13 Kayn (Profane Hydra, Ionian Boots, Youmuu's Ghostblade, Long Sword, Scorchclaw
Pup, ranks Q5 W5 E1 R2, stats from the model). Set `GameSource:ReplayFolder` to `Replays/demo`
for the Kindred game.

The form is decided once and kept. If the live data shows which form you are (its W is
`KaynAssW` for Shadow Assassin), that wins outright. Otherwise both forms are fought on the
build you will be holding once the core stands — what you own now, filled up from the standard
build — and the winner is locked for the game, because the build follows the form and swapping
it later throws away everything planned behind it. A new lobby clears the lock.

Forms: before `forms.transformSeconds` (10:00) Kayn is planned in base form, after that in the
locked form. Both are scored on damage over a teamfight weighted by time alive, and Rhaast is
kept unless Shadow Assassin beats it by `preferDefaultMargin` (10%).
Objectives per form are in `objectives`. `Kayn/ShadowAssassin` is pure damage — full damage
weight, full burst (the share of each enemy's health removed in the first three seconds), and
nothing at all for staying alive, so he never buys a survival item. `Kayn/Darkin` wants damage
*and* to live through the fight, so Rhaast pays for time alive (0.5) and fight uptime. Rhaast
has no ability of his own in the live data (only Shadow Assassin's `KaynAssW` shows), so he is
told apart by elimination: from `forms.undetectedIsDefaultFromSeconds` (15:00) a Kayn without
Shadow Assassin's W is Rhaast, and that overrides whatever form was locked before.

### Vi

Numbers from `vi.bin.json` (patch 16.18 client data, which is the 26.18 patch): spells `ViQ`,
`ViW`, `ViE`, `ViR`, `ViPassive`. Per-rank lists keep index 0 unused, as for the others;
percentages are fractions.

- **Combo**: R, attack, E, attack, fully charged Q, attack, E, attack, and round again. Every
  ability resets the attack timer, so the attack after it lands as soon as it ends, and the next
  ability waits for that attack. A step whose ability is not ready is passed over for the next
  one that is, in combo order, and the combo carries on from there.
- `q` — Vault Breaker is charged in full (`chargeSeconds`, the spell's 1.25s charge) with no
  attacks meanwhile, then deals `MinDamage` + 60% bonus AD times `MaxDamageMult` 2.5. The
  cooldown starts when it is let go.
- `w` — Denting Blows: every third attack on the target (`StacksBeforeEffect` 2) deals 4–8%
  (+0.035% per bonus AD) of its max health, capped at 300 on monsters, shreds 20% armor and
  gives 30–50% attack speed for 4 seconds. Only attacks count toward it here.
- `e` — Relentless Force: two charges (`mMaxAmmo`), recharging in `mAmmoRechargeTime`, 1 second
  apart. The attack after it deals `BaseDamage` + 110% AD + 100% AP instead of a plain attack
  (the fight adds the difference on top of the attack); it counts toward Denting Blows.
- `r` — Cease and Desist: `RBaseDamage` + 90% bonus AD after its 0.25s cast and `travelSeconds`
  (0.5s, an estimate: the dash is 800 range at 800 speed) on the way in, champions only. It is
  her engage, so it needs no earlier hit.
- `passive` — Blast Shield: 12% max health (`TotalShield`), counted once a fight for survival;
  its cooldown is 16s at level 1, 0.5s less each level.

`objectives.champions.Vi` points her toward the bruiser build her meta build follows (Eclipse,
Plated Steelcaps, Black Cleaver, Sterak's, Death's Dance, Guardian Angel) without copying it:
damage 1, uptime 1, survival 0.5 like Rhaast, and burst 0.25 for her R into a charged Q. With the
default weights (survival 0.25) she built pure on-hit. Her Focus can be changed on
its own.

### Nasus

Numbers from `nasus.bin.json` (patch 16.18 client data): spells `NasusQ`, `NasusW`, `NasusE`,
`NasusR`, `NasusPassive`. Per-rank lists keep index 0 unused; percentages are fractions.

- **Stacks**: Siphoning Strike stacks come at 20 a minute of game (`initialStacksPerMinute` in
  his champion file), the same preset whether you are Nasus or facing him.
- `q` — Siphoning Strike resets the attack timer and the attack after it adds `BonusDamage` plus
  your stacks (the game's `TotalDamage`: `BonusDamage` + AD + stacks, the AD being the attack's
  own). The cooldown starts when that attack lands.
- `w` — Wither is not damage, so it lives in survival: it goes on whoever hits you hardest with
  attacks and takes away `AttackSpeedSlowMult` (75%) of its slow, which ramps from `SlowBase` 35%
  to the rank's maximum over `Duration` 5 s. The cooldown starts the moment it is cast, so a
  10 s teamfight holds one cast, and a second only once haste brings it under the fight.
- `e` — Spirit Fire: `InitialHitDamage` + 60% AP, then `DamagePerTick` + 12% AP every second for
  5 s, and `ArmorShredPercent` for those 5 s.
- `r` — Fury of the Sands is cast at the start of a fight against a champion. For its 15 s it
  halves Q's cooldown (`QCDR`) and burns the target for `AOEDamagePercent` (+0.01% per AP) of its
  max health each second, in 0.5 s ticks capped at 240 on monsters. Its `BonusHealth` and
  `InitialResistGain` armor and magic resist count as stats in fights, for as many teamfights as
  its cooldown allows (`teamfightIntervalSeconds` / cooldown).
- `passive` — Soul Eater: 10% lifesteal, +5% at level 7 and 13, healing from his fight damage.

`objectives.champions.Nasus` leans survival (1, twice Vi's): his meta build is Trinity Force into
tank items. The fight model does not yet rate Sunfire, Thornmail or Frozen Heart as highly as
the meta does, so the plan keeps some damage items where the meta goes tank.

### Kindred

Every number is copied from the champion's CommunityDragon bins (`kindred.bin.json`
and `kindredwolf.bin.json`), and what each number means comes from the game's own
tooltip text in `lol.stringtable.json`. Per-rank lists are copied exactly as the game
stores them, so index 1 is rank 1 and index 0 is unused (E's cooldown is
`[14, 14, 12.5, 11, 9.5, 8, 8]`: rank 1 is 14s, rank 5 is 8s).

- `q` — `BaseDamage` plus 75% bonus AD (the ratio is in `mSpellCalculations`), attack
  speed `BaseBonusAS` + `ASPerMark` per mark for `BaseASDuration`, cooldown 9s or
  `CDNewValue` when cast inside W. Q, attack, Q, attack: the dash resets her attack timer, so
  an attack follows it at once, and the next Q waits for that attack. W and E do not reset it.
- `w` — the wolf bites the target for `CloneDamageFlat` + 20% bonus AD + 20% AP plus
  `CloneBasePercentDamage` + `ClonePercentDamagePerBounty` per mark of **current**
  health, as magic damage, for `ZoneDuration`. The wolf attacks at its own speed from
  `kindredwolf.bin.json` (0.558, +2.7% per level) plus
  `LambToWolfAttackSpeedConversionPercent` of Kindred's bonus attack speed.
- `e` — after the cast, the 3rd attack triggers the pounce, each attack within
  `TotalDuration` of the previous one ("within 4 seconds of each other"). The pounce
  lands together with that attack (`attacksAfterCast: 3`, confirmed in game and
  matching `StacksToProc` 4: the cast plus three attacks). The pounce is `BaseDamage` + 100% bonus AD plus `BasePercentDamage` +
  `EDamagePerMark` per mark of **missing** health, physical. The bin multiplies it by
  `1 + CritMod × crit chance × (crit damage − 1)`. Reading the two stat numbers in
  that formula (8 and 9) as crit chance and crit damage is our inference: the same bin
  names the other formula that uses stat 9 `CritDamage`.
- When to cast E is computed, not configured. The pounce grows with missing health,
  so its best use is as the killing blow with nothing wasted: after armor and the crit
  bonus, `pounce(h) = h`, which gives `h* = s·(F + p·Max) / (1 + s·p)` (`s` is
  mitigation × crit bonus, `F` the flat part, `p` the missing-health ratio). E is
  cast when the target reaches `h*` plus the damage that lands before the pounce:
  the three attacks at their exact expected damage (including on-hit effects that fire on
  every attack, such as the jungle pet), the wolf bites that really fall in that
  window (the kit knows when W ends and when the next bite is), and Q if it comes back
  in time. Attack-counter procs count by how many land in those three attacks (Kraken
  fires exactly once in any three); procs with a cooldown are left out on purpose. The
  build panel shows this cast point for each enemy with your current items, plus what
  to add when Q or wolf bites land in between. Underestimating only casts E a little
  later, which costs nothing, while overestimating leaves the target alive and costs
  an attack. Early on (level 3, rank 1, no marks, 75 AD) this is about 340 health on a
  1:30 Red Brambleback with a jungle pet, and more when wolf bites or Q land in the
  window. Later, with more bonus AD, crit and marks, the point moves
  up on its own (about 220 → 660 health on a level 13 Garen). E is also cast
  immediately when it would come off cooldown again before the target reaches that
  point, so long fights do not lose casts.
- `skillOrder` is **not** game data. It is the usual Kindred order, used only when
  the Live Client API does not report ability ranks (it does for your own champion).

- Against jungle and epic monsters (not minions), Wolf's bites deal `MonsterBonusDmg` (50%)
  more ("Against jungle monsters, Wolf deals 50% increased damage"), and E's missing-health
  part is capped at `MonsterCap` (200) ("Missing health damage is capped at 200 against
  jungle monsters"). The cap applies before the crit bonus, and the E cast point accounts for
  it. Both numbers are from `kindred.bin.json`, the wording from `lol.stringtable.json`.
- `r` is Lamb's Respite, from `KindredR`: for `BuffDuration` (4s) nothing inside can drop below
  10% health, then everyone inside heals `HealFlat` (225 / 300 / 375). It deals no damage, so
  fights ignore it; the build model counts it as survival (see the Model section).

Left out: the W passive heal, the Q dash, and cast times. Smite is not part of the simulation. Fights are one enemy standing still, opening with W and Q (E as above), and they end at the kill or after 30 seconds. The kill time is interpolated
between hits and averaged over four start timings of the first attack, so one hit
more or fewer does not swing the result.

### Measured in game, not in the data

Some behaviour is not in any file we can read, so it comes from in-game tests and is
pinned by `KindredInGameTests`. Each entry records the measurement that justifies it.

- Damage numbers in game merge hits that land at the same moment. E's pounce lands
  with the attack that triggers it, so the number shown is attack + pounce, both
  normal physical damage reduced by armor. A 400-armor dummy took 15 from an attack
  and showed 38 for attack + pounce with Press the Attack's +8% (the model gives
  (15 + 20.5) × 1.08 = 38.3). Read measurements with that in mind: the pounce alone
  is the merged number minus the attack.
- The jungle pet's bite is true damage, and the +10% does not apply to it: that buff is
  on the champion, and the pet is its own unit, hence `byCompanion`. Its size follows
  the formula below; at level 3 with 75 AD and the health scaling rune shard (~30 bonus
  health) it gives 37.0, which is what red took in game.
- Our own champion's AD, AP, health, resists and ability haste come from the Live
  Client API (runes included). The simulation rebuilds stats from base + items and then
  adds the difference, so rune shards count; a level 3 Kindred measured 75 AD against
  69.8 rebuilt (5 from runes). Attack speed is left out because the snapshot can include a temporary
  Q buff.

### Jungle pets

The three jungle pets (Scorchclaw, Gustwalker, Mosstomper, two item ids each) carry
two effects, both `when` the target is a monster:

- True damage on every attack, sized by `PetDPS` on `SummonerSmite` in
  `shared.cdtb.bin.json` (the item tooltip names it): 20 to 150 across levels 1 to 18
  (`amount` 20, `perLevel` 7.6471) + 10% bonus AD + 16% AP + 4% bonus health + 25% bonus
  armor + 25% bonus magic resist. The stat numbers in that formula are read as 2 AD,
  0 AP, 12 health, 1 armor, 6 magic resist, with formula 2 meaning the bonus part.
  `area: true` and `byCompanion: true`. The companion's own `baseDamage` 35 in
  `sru_jungle_companions.bin.json` is not used. Firing on every one of our attacks and
  hitting the whole camp is a deliberate simplification.
- +10% damage (`damageAmp` 0.1), from `DamageAmp` 1.1 on `PuppyControllerBuff` in the
  same file.

`MonsterDamageTaken` 0.5 (monsters deal half damage to you) is left out, because nothing
simulates monsters hitting back yet.

## Item stacking

Items that grow over the game carry a `stacking` block. This is deliberately naive: it
does not simulate how stacks are earned, it only makes stacking items worth more the
earlier they are bought.

```json
"stacking": { "per": "minute owned", "gains": [ { "stat": "health", "amount": 10 },
  { "stat": "mana", "amount": 30 }, { "stat": "abilityPower", "amount": 3 } ],
  "max": 10, "stacksPerMinute": 1, "rateSource": "game data: SecondsPerStack 60" }
```

- Stacks = `stacksPerMinute` × minutes owned × `rangedMultiplier` (for ranged
  champions), capped at `max`.
- `gains` is what one stack gives, from the item's data values: `amount`, optionally
  `perMaxHealth` (Heartsteel: 10% of its proc, 7 + 0.6% max health). A `perMaxHealth` health gain
  compounds: each stack is worked out on the max health you have when it lands, earlier stacks
  included, so n stacks from health H add (H + amount/perMaxHealth) × ((1 + perMaxHealth)^n − 1).
- `stacksPerMinute` is game data only for Rod of Ages (one per minute). Every other rate
  is an estimate meant to be tuned: Heartsteel 1.2 (about 20 stacks and 600 health by 30:00 when
  finished around 13:00), Hubris 1 (18 stacks by 30:00 when finished around 12:00),
  Mejai's 0.5, Dark Seal 0.3, the omnivamp boots 0.5, Yun Tal 15 (half when ranged, max
  62.5 = 25% crit), The Collector 0.2 kills, Cull 7 minions.
- Rates are presets, never this game's trend: a stacking item is bought for what it grows
  into, so a few early takedowns or a dry spell do not decide it. A stacking item's takedown
  buff (Hubris) is up as often as its preset rate says.
- Hubris is a takedown buff, not a permanent stat: `OnTakedown` `StatBuff` 12 AD, `perStack` 3
  per takedown since you bought it, `duration` 90 seconds (game data: `BaseADBonus`,
  `ADPerStatue`, `BuffDuration`). Its value in a fight is weighted by how often it is up: carried
  over from a takedown in the last 90 seconds, `1 − e^(−1.5λ)` for λ takedowns per minute, and
  otherwise switched on by the first takedown inside the fight, which with k takedowns per fight
  (λ × `teamfightIntervalSeconds`) leaves `1 − (1 − e^(−k))/k` of the fight on average.
- Minutes owned come from when the app first saw the item on that player; an item that
  was already there when the app started counts from that moment.
- In the build path, an item not bought yet has no stacks when it is bought and gains them from
  then on (`stackLookaheadSeconds` 0): every later moment of the plan is scored with the stacks it
  has by then, so there is no head start to add. Enemy items count stacks from their
  own forecast purchase times, so a Heartsteel bought later is weaker at your next item.
- Your own champion's stats already include stacks through the Live Client API; the
  simulation adjusts to them instead of adding stacks on top.

## Model

`Model/` holds everything the build planner reads besides game data. None of it is game
data: it is judgement, kept in files so it can be tuned without touching code.

- `model.json`: every constant of the model (below).
- `baseline.json`: average level and gold earned by role over time. It only gives the
  *shape* of income over a game; how fast each player earns comes from the player. It is an
  estimate until the match-v5 aggregation in the spec replaces it.

Every champion file carries a `metaBuild`: its standard build in buy order, as item `id`s like
`buildPath`. Enemies and allies keep what they own and are assumed to buy the rest of it in
order, using owned components where the next item needs them. It is a forecast of other
players, never advice for you. A test requires every champion to have a legal six-item build.
The builds are hand-written estimates until the match-v5 aggregation in the spec replaces them.

### What an item is worth

The planner scores an inventory at a game time with

```
score = damage · ln(damage before death) + survival · ln(time alive) + clear · phase · ln(1 / clear time)
```

using the weights for your champion from `objectives` (Kindred: damage 1, survival 0.1).
Log terms make every weight read as "how many percent of one is worth a percent of the
other".

- **Damage**: your kit fights every enemy as forecast at that time (level, items, item and
  champion stacks), with their healing per second and shields, for a whole `teamfightSeconds`
  fight: when the target dies an identical one takes its place and cooldowns keep running, and
  the damage counts in kills, health and shields alike. So a burst that only opens a fight is
  not mistaken for damage over a fight. Grievous Wounds cuts the
  healing, Serpent's Fang the shields, The Collector executes, and a target that out-heals
  you scores near zero. Enemies are weighted by threat: forecast gold times how much damage
  they are built to deal.
- **Damage before death**: your damage times how much of a fight you are alive for. You are
  caught in `caughtShare` of fights, where survival decides how long you last; in the rest your
  positioning keeps you alive for the whole fight. So survivability is priced in the same
  currency as damage without every item being judged as if you were always the one dived.
- **Time alive**: each enemy's forecast damage streams, reduced by your armor, magic
  resist and mitigation against their penetration, times the share of their damage aimed
  at you (abilities also land `abilityAreaShare` of what was aimed at someone else). Shields,
  heals, life steal, omnivamp (both cut by enemy Grievous Wounds) and revives extend it;
  burst at the start of the fight shortens it. A champion's survival ability adds its undying
  time plus its heal, scaled by how often its cooldown lets it be up for a fight
  (`teamfightIntervalSeconds` / cooldown): for Kindred that is Lamb's Respite. A glass-cannon
  Kindred lasts about 2.5 seconds without it at 35 minutes, about 7 with it. The page shows both numbers.
- **Clear** (junglers): the full clear simulated camp by camp, weighted
  `clearWeightEarly` at the start and fading to `clearWeightLate` (0) between
  `fadeFromSeconds` and `fadeToSeconds` (8:00 to 16:40). It also fades out as you complete
  items: full below `clearFadeFromItems`, gone at `clearFadeToItems` (2.5). By then you are
  fighting, not farming camps.

### Heuristics, and where they live

Auto attacks and item effects are exact to the item data. What champion kits do is not
simulated for enemies, so these are estimated from tags (0–1), level and stats:

- `enemyDamage`: ability damage per cycle (`apBase + apPerLevel·level + apRatio·AP`, the same
  for AD and tanks), cycle length, how much of a fight melee and ranged champions spend
  attacking, how much of `trueDamage` is true, and how much of a combo lands as burst.
  Calibrated by symmetry: an enemy Kindred with your build, estimated this way, deals about
  60% of what your simulated kit deals (the rest is her own kit, which tags cannot know).
  `TagEstimatesAreInReachOfTheSimulatedKit` keeps it between half and 1.2 times.
- `sustain`: champion healing and shielding per tag, how much of a support's output goes to
  allies (`supportAllyShare`) and reaches the target you are hitting (`allyReceiveShare`),
  dragon healing and shields.
- `focus`: the share of each enemy's damage aimed at you. Allies draw a share by frontline
  (`tank` tag) and melee, squishy carries draw more (`carryFocusBias`), and burst champions
  dive past the frontline (`diveBias`). A ranged champion avoids `kiteReduction` of melee
  enemies' attacks by positioning. This is where positioning lives: the share is what reaches
  you in a fight you play reasonably, not what five enemies could do if you stood still.
- `allies`: how often an ally's Grievous Wounds, shield reduction or armor / magic resist
  shred (Black Cleaver, Bloodletter's Curse) is already on the target you are hitting. Anti-heal
  and shield reduction do not stack, so an ally's lowers what yours adds; shred lowers the
  target's resists in every fight.

### Movement speed

Movement speed is on the stat sheet: base plus flat bonuses, times one plus percent bonuses,
then the soft caps (above 415 each point counts 80%, above 490 it counts 50%), all from the
League wiki's Movement speed page. It is worth three things:

- **Tempo**: part of every game is walking between camps, lanes and fights (`walkShare` by
  role: jungle 45%, support 40%, mid 30%, top and bottom 25%). Being faster shrinks it, and the
  score adds `tempoWeight · ln(tempo)`. The weight follows `tempoWeightByMinute`, measured so one
  point of movement speed is worth about 12 gold of your other stats at that point in the game
  (the wiki's gold value for flat movement speed; Boots are 300 gold for 25).
  `OneBootsPointIsWorthAboutTwelveGoldOfStats` checks it.
- **Clear**: walking between camps (`walkSeconds`) is timed at base speed and shrinks with
  yours.
- **Fights**: incoming damage scales with (enemy team's average speed / yours) to the power
  `evasionExponent`, which stands in for dodging and repositioning, and the melee attacks you
  kite (`kiteReduction`) grow with (your speed / theirs) to the power `kiteSpeedExponent`.

Plain Boots are a candidate on their own and do not use up one of the planner's `depth`
items, so a plan can say "Boots now, finished boots later". Tier 3 boots (Gunmetal Greaves,
Swiftmarch, ...) are never suggested: they are a free Feats of Strength upgrade, not a shop
decision.

### Gold and levels

Your gold is exact. Everyone else's is the value of their items, the only gold the API
shows. Future gold extrapolates each player's own rate: their whole-game average blended with
a least-squares rate over `paceWindowSeconds` (`recentWeight`), relative to the baseline for
their role. Item value only moves when someone recalls, so every other player's pace is
pulled toward the lobby average by `trendWeight`. The first `baselineOnlyUntilSeconds` trust
the baseline. Levels follow the baseline plus today's lead, fading over
`levelReversionSeconds` (catch-up experience). Forecast times carry a spread of
`rateUncertainty` × horizon. Up to `baselineOnlyUntilSeconds` (8 minutes) the rate is entirely
the average game, from `observedOnlyFromSeconds` (15 minutes) entirely this one, with no cap on how far, and between
the two they are blended. All of it lives in `GameTrends`, which the forecaster, the enemy
build projection and the planner's replanning all read (see Model → Planner).

Enemies are simulated at a future minute holding what they own **now** plus the rest of their
standard build filled in from there, so armour, magic resist and health they have already
bought are in the fight: a team that has finished Zhonya's is a team you need penetration for.

### Planner and time budget

The planner runs while you play, on one background thread at below-normal priority, and
never uses more than one core.

**The trend layer decides when to think again.** `GameTrends` reads the history of states and
turns it into, per player, a gold rate (their own average blended with the recent slope over
`paceWindowSeconds`, pulled toward the lobby trend for enemies), a pace against the baseline
curve, a takedown rate and a confidence that rises from `baselineOnlyUntilSeconds` to
`observedOnlyFromSeconds`. Everything downstream — your income, enemy gold, the items they
are predicted to hold at each future moment — reads those trends and nothing else.

The trends also produce a **signature**: every player's champion, level, items, takedowns
bucketed by `takedownBucket` and pace bucketed by `paceBucket`, plus objectives. Gold ticking
up, a few CS or a second passing do not change it. This is what the planner watches:

- **The signature has not moved** → the build is not re-picked. It is replayed against the new
  state, so arrival times, gold needed and the forecast all move, and the plan does not.
- **The signature moved** → the ladder restarts at its first rung so the next item is
  re-decided straight away, keeping what it already found (below).

Planning climbs a **ladder** of stages, each deeper than the last, whenever no new state is
waiting. `openingStages` runs while the game loads (before `openingSeconds`): a whole-build
sketch, then a wider search, then the same at full fidelity — it keeps thinking for as long as
the loading screen lasts instead of answering once. `stages` runs in game: **quick** (a beam
`depth` 2 deep, under a second, so a change is answered immediately), **detailed**, then
**whole build** (`depth` 6, `horizonGold` 20000, full-fidelity scoring). The page shows which
rung is in hand and whether a deeper one is still running.

Two rules keep what was already worked out:

- **The build never shrinks.** When a shallow rung returns fewer items than the build in hand,
  the rest of that build is replayed onto the end of it. Going from the loading screen into
  the game, or reacting to an enemy item, changes the next step without throwing away the
  five steps behind it.
- **The next item only changes when keeping it is measurably worse.** Every rung, the deepest
  included, is told the item the page shows you saving for. A set without it has to be stronger
  by `keepMargin`, and an order that does not start with it has to be worth `keepMargin` more.
  A cheap or timed-out rung never moves it at all. Every `replanSeconds` the deepest stage
  re-runs on fresh state under the same rule.

Nothing is bought before `firstRecallSeconds`; after that an item is bought when its gold is
in, as a jungler backs once he can afford what he is saving for. A fixed grid of recalls made
anything cheap look free whenever the big item would land on the same recall anyway, and one
counted from now slid with every tick. Buy now spends your gold along the plan: after the first
item's components, leftover gold goes to the next item's.

Components no item on the plan uses will be sold at 70% one day; that 30% loss is charged at
the plan's own rate of score per gold, so finishing what you hold components for counts.

A plan's value is its score gain over your current items, integrated over time until you
have earned `horizonGold` more, discounted over `discountSeconds`. Buying something earlier
makes it count for longer. Saving for an item is not sitting on gold: its components are bought
on the way, so while you save its worth grows with the gold put in, up to
`componentValueShare` (60%) of what the finished item adds. Without that, a cheap item bought
whole always looked better first, only because it was the one thing that counted before a big
one landed.

### The core

The slider in the page header sets how big a core to aim at: one to five items, three by
default, always plus shoes. Shoes never use up one of its places.

The plan works toward that core as a package, as two separate questions.

**Which** items: sets of as many items as the part of the core you have not built yet, plus
shoes, grown one item at a time with the strongest few of each size kept. Every set is judged
on how well the finished inventory fights, all at one moment (when a core of that size is done),
so no set is preferred for being cheap, for finishing sooner, or for having a piece you can
afford right now. Gold in your pocket never changes which items make the core.

**In which order**: every order of that set is walked (all of them up to four entries; beyond
that, the best found by swapping neighbours and moving items to the front, `reorderPasses`),
each item bought as soon as the gold allows, and the order worth most over the game wins. Only
the order is about timing: a clear item earns its keep while clear still has weight, a
late-game item is not bought early, and shoes go first only when owning them early is worth
more than the delay they put on the first item.

Shoes belong to the core: once its items are built and the shoes are not, the shoes are the plan.
With a full inventory, where a purchase means selling, the older beam search over sequences
still decides.

Items that grow with time owned order themselves: stacks come from the minutes since the plan
buys them, so Hubris bought first carries four times the stacks of Hubris bought third by the
time the build is done, and scores accordingly. Once the core stands there is nothing left to save toward, so the
search is one item deep and the answer is simply the best item at the moment you buy it, each
time you buy. The jungle pet takes no slot in the plan: it leaves the inventory once grown, so a
core of five plus shoes fits the six slots. A plan that must make room sells something you own
now or bought along the way, but never a stacking item it bought itself (it has not stacked yet).

**Focus**, next to the slider, edits the objective weights the plan scores builds with, for
the entry of `objectives` it is reading now (`Kindred`, `Kayn/Darkin`, `Kayn/ShadowAssassin`):
constant damage (`damage`), `burst`, `uptime` and `survival`. Those are the constant,
champion-specific weights; `clear` and `movement` already change with game time and items, so
they stay the model's. The shape starts at the model's own values, drawn dashed behind it;
each corner runs from 0 to 3, and "Back to default" drops the override. Overrides are per entry,
so changing Rhaast leaves Shadow Assassin alone, and they are sent as
`POST /api/preferences` with `{ "weights": { "key", "damage", "burst", "uptime", "survival", "reset" } }`.
A set that leaves components you already hold unused is charged their 30% sell loss, so a
half-built item is only dropped for a build that is stronger even after paying for it.

The page reads the setting from `GET /api/preferences` and sets it with `POST /api/preferences`;
moving it is a reason to re-plan, exactly like an enemy finishing an item, and the page says it
is calculating again while that happens.

The page is only ever shown a build that was thought through to the end of the ladder. A build
half way up is never published, so it cannot flicker between rungs; while a new one is being
worked out the last finished one stays up, and before there is one the page says it is
calculating. That is `Display`, which is what the service publishes; `Compute` and `Current`
still hand back the working plan for tests and tooling.

### Enemy spikes

Value over time is not the whole story: it does not care *when* you are weak. Saving for a big
item leaves you holding boots and a component at the minute an enemy finishes theirs, and that
is when you lose a duel you would otherwise win.

From the enemy forecasts the planner knows the exact minute each enemy finishes each item —
those are their spikes. For `spikeWindowSeconds` after each one (windows merge when they
overlap), being strong counts for `1 + spikeWeight` times as much as it does the rest of the
time. Nothing is subtracted and no build is forbidden: the same value integral is simply
weighted toward the minutes when a fight is most likely to be lost. An item that lands before
their spike collects that weight; one that lands after it does not.

`spikeWeight` 0.5 means power during those windows is worth half as much again. Against the
demo lobby it moves buying boots first from 17 points ahead of finishing an item first to 3
points behind, and the planner's own opening flips from "Berserker's at 4m, first item at 12m"
to "Hubris at 8m". At 1 the gap widens to 23 points. At 0 the term is off and the planner buys
boots first on timing alone.

The same spikes are simulated exactly for the explanation. `SpikesBetween` lists them, and
`DuelAt` fights whatever you would be holding at that minute — finished items plus the
components your gold buys by then — one against one against the enemy who just spiked, and
reports how long each side needs to kill the other. Where that duel is lost, the plan says so:
*"At ~7:42 Veigar finishes Luden's Echo; you are still on Boots + Long Sword and lose that duel
(3.2s to kill you, 6.7s to kill them)."*

Gold items pay for later ones. A stacking item whose gains are `gold` (The Collector's 25 per
kill, Cull's 1 per minion) adds income from the moment the plan buys it, so every later item
arrives sooner. Per-kill gold uses your own kill rate this game, so it is worth more when you
are snowballing and nothing if you are not getting kills. Bought late, there is no time left
to earn it back, so such items drift to the front of a plan or out of it.

With a full inventory the planner sells the item that adds least (never boots and never the
jungle pet). Selling a finished item (2000 gold or more) has to beat keeping it by
`replaceFinishedMargin` (10%). Anything else needs `replaceMargin` (3%). A swap is marked
"sells X" on the build path.

Every completed item is a candidate, off-meta ones included; the page flags those.

Boots are required (`requireBoots`), and that is the only rule about them: where they belong in
a build is decided by what they are worth, like any other item. A build that never bought them
gets the pair that scores best appended at the end. Because a boots line scores low on its own
at the first layer, the best `bootsLines` of them are kept in the beam rather than pruned, so
"boots first, then the expensive item" can be compared properly against "expensive item first".

### Known gaps

- Enemy kits are tags, not simulations, so enemy damage, healing and shields are estimates.
- Kindred's marks are forecast at `initialStacksPerMinute` (0.35, about one every three
  minutes), leaned toward her own pace by at most 30%, and never forecast past 10. They matter more than a stack count usually does: each one adds 1% of the target's
  current health to every wolf bite and 5% attack speed to Q, so guessing them high quietly
  turns her into an ability champion and makes haste look better than attack speed.
- Her Q costs less while she stands in W's zone, which would otherwise keep its attack speed
  buff up permanently and make bought attack speed look redundant. `zoneUptime` (0.6) is the
  share of the zone she actually spends inside it; the rest of the time Q is on its full
  cooldown.
- `OnUltimate` effects (Malignance) fire when a simulated kit's ultimate hits enemies. Kindred's
  Lamb's Respite damages nobody, so such items are worth nothing to her, which is correct;
  for a champion without a simulated kit they are worth nothing either, which is not.
- Item passives that refund ability cooldowns (Axiom Arc) are not modelled, so such items are
  valued on their stats alone and are under-credited.
- Lamb's Respite also stops enemies inside from dying; that cost to your damage is not priced.

The board's per-enemy kill times come from the same model (current items, healing, shields,
allies' anti-heal and shred), worked out on the planner thread. The poll itself only computes
the E cast hints.
