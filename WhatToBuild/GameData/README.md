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
  "buildPath": ["<component guid>", "..."],
  "stats": { "attackDamage": 45, "attackSpeedPercent": 0.4 },
  "effects": [
    { "trigger": "OnAttack", "kind": "PhysicalDamage", "amount": 175, "cooldown": 2 }
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

## Effects

An effect is a **ballpark of what an item contributes**: how much, and how often.
That is all a buy decision needs.

Proc mechanics are deliberately not modelled. Kraken Slayer firing on every third
attack becomes "about 175 damage, no more than every 2 seconds". Whether it really
takes two attacks or three does not change whether you buy it, and pretending to
know would be false precision.

```json
{ "trigger": "OnAttack", "kind": "PhysicalDamage", "amount": 175, "cooldown": 2 }
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
- `stat` — for `StatBuff` and shreds, which stat. It is written as a name but parses
  into a `Stat` type, so an unknown name fails the load instead of reaching the
  simulation. Each type knows whether it is a fraction and whether it applies to the
  enemy rather than to you, which is how `enemyAttackSpeedPercent` and
  `enemyMagicDamageAmp` stay distinguishable from your own stats. `Stats.All` is the
  full list.
- `versus` — for `DamageReduction` and `Shield`, what it applies to: `All` (the
  default), `Attacks`, `Abilities`, `Crit`, `Physical` or `Magic`. This is the one
  place the ballpark is not allowed to round off, because it decides *who* an item
  is good against. Randuin's 30% is `Crit`, Plated Steelcaps' 10% is `Attacks`, and
  Maw's shield is `Magic`; calling any of them blanket mitigation would recommend
  them against comps they do nothing to.
- Scaling, any of which may be combined with `amount`:
  `perBaseAd`, `perTotalAd`, `perAp`, `perMaxHealth`, `perTargetMaxHealth`,
  `perTargetCurrentHealth`.

Anything absent is zero, so the simulation always reads a number and never has to
check whether a key exists.

### `when` — conditions

Some items are only worth their number against the right target. Lord Dominik's 15%
does nothing to a squishy, and pricing it as a flat amp would recommend it into
comps it barely touches. `when` holds requirements that **all** have to hold:

```json
{ "trigger": "Always", "kind": "StatBuff", "stat": "damageAmp", "amount": 0.15,
  "when": [ { "subject": "Target", "property": "BonusHealth", "op": "AtLeast", "value": 1000 } ] }
```

- `subject` — `Self` or `Target`
- `property` — `HealthPercent` (0–1), `BonusHealth`, `MaxHealth`, `Armor`,
  `MagicResist`, `Level`
- `op` — `AtLeast` or `AtMost`
- `value` — the threshold

An empty or absent `when` means the effect always counts.

The conditions are thresholds, not curves, on purpose. Lord Dominik's really ramps
up to its maximum at 1500 bonus health; the entry says "counts from 1000". That is
the same ballpark trade as the rest of the file — the evaluator needs to know *who
this is good against*, not to reproduce the ramp.

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
