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
- `stat` — for `StatBuff` and shreds, which stat, by `ItemStats` field name, plus
  four that are not real stats: `damageAmp`, `abilityPowerAmp`,
  `enemyAttackSpeedPercent`, `enemyMagicDamageAmp`.
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

## Champions

Champion files carry **weighted tags** instead of a full ability model, so the
evaluator can reason about matchups it does not simulate:

```json
"tags": { "adDamageDealer": 0.9, "burst": 0.7, "ranged": 1.0, "healing": 0.3 }
```

Each weight is 0–1 strength, not a yes/no. A champion with `healing: 0.9` makes
Grievous Wounds urgent; `healing: 0.2` does not.

Vocabulary: `tank`, `adDamageDealer`, `apDamageDealer`, `burst`, `sustained`,
`splitPusher`, `healing`, `shielding`, `ranged`, `melee`, `mobile`, `crowdControl`,
`trueDamage`.

These answer questions the item data alone cannot. Whether anti-heal is worth buying
depends on enemy healing from **both** sides: champion tags cover abilities, while
item-granted healing and shielding fall out of the effect data automatically — any
enemy item with `kind: "Heal"`, `"Shield"`, or `lifeStealPercent` counts. The same
pairing decides `ShieldReduction`: Serpent's Fang is worth buying when enemy
`shielding` tags plus shield-granting items clear a threshold, and dead weight when
they do not.
