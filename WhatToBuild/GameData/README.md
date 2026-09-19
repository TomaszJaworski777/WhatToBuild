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
decision. Vision, gold generation, mana restoration, movement speed, ghosting,
stasis and cooldown refunds are all omitted: they are real, but nothing in the
evaluator can price them, and inventing a number for them would let hand-tuned
guesses outvote the parts we can actually compute.

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

`initialStacksPerMinute` is **only the prediction used at game start**, before there
is anything to measure. Once the game is running, the current stack count comes from
game state and the real rate is extrapolated from that, which replaces this number.
It is a starting prior, not a fact, and the values here are estimates.

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
`SupportedChampions/<Name>/`. For now that is only Kindred (`Kits/kindred.json`).

Every number is copied from the champion's CommunityDragon bins (`kindred.bin.json`
and `kindredwolf.bin.json`), and what each number means comes from the game's own
tooltip text in `lol.stringtable.json`. Per-rank lists are copied exactly as the game
stores them, so index 1 is rank 1 and index 0 is unused (E's cooldown is
`[14, 14, 12.5, 11, 9.5, 8, 8]`: rank 1 is 14s, rank 5 is 8s).

- `q` — `BaseDamage` plus 75% bonus AD (the ratio is in `mSpellCalculations`), attack
  speed `BaseBonusAS` + `ASPerMark` per mark for `BaseASDuration`, cooldown 9s or
  `CDNewValue` when cast inside W.
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

Left out: R (it stops deaths and heals, but deals no damage), the W passive heal, the
Q dash, and cast times. Monster-only modifiers (`MonsterBonusDmg` on W, `MonsterCap` on E) are not applied, so fights against camps are an
approximation, and Smite is not part of the simulation. Fights are one enemy standing still, opening with W and Q (E as above), and they end at the kill or after 30 seconds. The kill time is interpolated
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
  `perMaxHealth` (Heartsteel: 10% of its proc, 7 + 0.6% max health).
- `stacksPerMinute` is game data only for Rod of Ages (one per minute). Every other rate
  is an estimate (`rateSource: "estimate"`) meant to be tuned: Heartsteel 1, Hubris 0.2,
  Mejai's 0.5, Dark Seal 0.3, the omnivamp boots 0.5, Yun Tal 15 (half when ranged, max
  62.5 = 25% crit), The Collector 0.2 kills, Cull 7 minions.
- Minutes owned come from when the app first saw the item on that player; an item that
  was already there when the app started counts from that moment.
- In the build path, an item not bought yet is valued with the stacks it would have by
  30:00 if bought at its expected time, so the same item scores higher the earlier it
  fits in.
- Your own champion's stats already include stacks through the Live Client API; the
  simulation adjusts to them instead of adding stacks on top.
