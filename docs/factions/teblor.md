# Teblor

**Faction id:** `teblor` · **Milestone:** M7 · **Role:** giants

This page is written ahead of the roster (D-R36, 2026-11-10): every section and column the content pins read on
malazan.md is here, with the ids, numbers and player-facing words the Teblor files will carry (D28 copies them). Until
the files land no test compares it to data; `Content/PagePinReadinessTests` checks that the sections are here and parse.
Where the design asks for something the data format can't hold yet, the page says so next to it ("not in the data").

## Lore hook

Mountain clans of giants, twice the height of lowlanders, who live by raiding, feuds, and an
honor code that outsiders find terrifying. Their warriors carry huge swords carved from
bloodwood and rub blood-oil into their skin before battle. They count their kills, prize their
war-dogs and their massive horses, and consider the "children" of the lowlands to be weak,
numerous, and contemptible.

## Playstyle

Few, huge, slow to build, very hard to kill. A Teblor army is a handful of giants that regenerate
between fights and crush anything that meets them in melee. Each loss hurts. They win by making
every engagement a brawl on their terms and by using Blood-oil Frenzy at the decisive moment.
They lose to kiting, focus fire, and magic.

## Faction bonus: Children of the Mountains

- Every unit but the War-dog is drawn at **1.5× scale** (`units.json` `scale` 1.5; the War-dog 1, normal-sized). Visual
  only: collision radius 0.75 m, still under the half-cell cap.
- Combat units cost **2-3 population** (the War-dog 1 a dog); workers cost 1.
- All units **regenerate 1 HP/s** after 6 s without taking damage (`faction.json` `regen`
  `{ "hpPerSecond": 1, "delay": 6 }`, docs/03 "Implementation (M7-1)").
- Units **train 25% slower** than the template (already in the table).
- The giants on foot (Warrior, Javelin Thrower, Shaman, Breaker) use the **Giant** armor class (`giant`; docs/02
  "Damage type × armor class": pierce and magic ×1.25, siege ×0.75). The Destrier Rider and the War-dog are Mounted, the
  Gatherer Light.

## Faction upgrade: Blood-oil Anointing

Age II, at the Bloodwood Carver, 200 G / 150 W, 45 s. Warriors and Destrier Riders gain **+2 armor**; Blood-oil Frenzy
**duration +4 s** (not in the data: no tech stat lengthens a status, a request to the sim track).

## Units

| Slot | Unit | Id | HP | Armor | Class | Attack | Type | CD | Range | Targets | Speed | Sight | Cost (G/W) | Pop | Train | Spawn | Scale | Trained at |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Worker | Gatherer | `teblor_gatherer` | 70 | 0 | Light | 6 | Melee | 1.5 | melee | all | 4.0 | 14 | 75 / 0 | 1 | 16 | 1 | 1.5 | Longhouse |
| Line | Warrior | `teblor_warrior` | 320 | 2 | Giant | 22 | Melee | 1.8 | melee | all | 3.4 | 16 | 120 / 30 | 2 | 28 | 1 | 1.5 | War Lodge |
| Ranged | Javelin Thrower | `teblor_javelin_thrower` | 140 | 1 | Giant | 12 | Pierce | 2.6 | 11 | all | 3.4 | 18 | 70 / 50 | 2 | 26 | 1 | 1.5 | Javelin Ground |
| Shock | Destrier Rider | `teblor_destrier_rider` | 360 | 2 | Mounted | 24 | Melee | 2.0 | melee | all | 5.6 | 16 | 160 / 60 | 3 | 36 | 1 | 1.5 | Stables & Kennels |
| Caster | Shaman | `teblor_shaman` | 130 | 0 | Giant | 10 | Magic | 2.4 | 10 | all | 3.4 | 16 | 130 / 60 | 2 | 36 | 1 | 1.5 | Shaman's Circle |
| Siege | Breaker | `teblor_breaker` | 380 | 3 | Giant | 40 | Siege | 3.0 | melee | all | 3.0 | 14 | 180 / 120 | 3 | 45 | 1 | 1.5 | Breaker's Hall |
| Unique | War-dog | `teblor_war_dog` | 70 | 0 | Mounted | 7 | Melee | 1.0 | melee | all | 6.4 | 16 | 120 / 0 | 1 | 24 | 3 | 1 | Stables & Kennels |

The War-dog row is one queue item, a pack (docs/03 "For the data track (M7-2)"): **Spawn** is `spawnCount` 3, the dogs
one item trains; its **Cost** (120 / 0) and **Train** (24 s) are the item's, paid and trained once; its HP, attack and
**Pop** are each dog's (1 each, 3 for the pack). **Scale** is `scale`, the view's model size; **Targets** is
`attack.targets` ("all" everywhere: the Breaker hits units too).

### Unit notes

- **Gatherer:** carries 15 instead of 10 (not in the data: `rules.json` `workerCarry` is one number for every
  faction). Expensive, tough enough to survive a light raid.
- **Warrior:** ×1.5 vs Mounted (×1.25 on this page until 2026-11-10: docs/02's Line rule applies, docs/01 2026-11-10
  (d); the M7 balance pass may lower it). The core of the army: long strides make it faster than human infantry, and it
  out-trades two or three template line units.
- **Javelin Thrower:** weak, short-ranged, but lets Teblor hit flying units and kite a little.
- **Destrier Rider:** ×1.5 vs Light. A giant on a huge horse; armor class is Mounted, so line
  infantry counters it.
- **Shaman:** splash 1.5 m, detector 10 m. Signature ability **Blood-oil Frenzy** (below).
- **Breaker:** a giant with a bloodwood maul. Siege damage; can also hit units (at siege
  multipliers, so poorly). No siege engine needed.
- **War-dog (unique, Age II):** one queue item spawns a pack of 3 normal-sized dogs (1 pop each).
  Passive **Hamstring**: each bite on a target that survives it leaves it Slowed 20% for 2 s, so the pack pins kiting
  units for the giants to catch.

`Content/UnitContentTests` pins each note's "detector N m" to the unit's `detector` in `units.json`, both ways, and
`Content/PassiveContentTests` the Hamstring sentence to the War-dog's `passives` entry (`{ "trigger": "onHit",
"status": "slowed", "magnitude": 0.2, "duration": 2 }`).

## Abilities

| Ability | Unit | Kind | Range | Radius | Cast | Cooldown | Duration | Effect |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Blood-oil Frenzy | Shaman | Self / aura | — | 8 m | 0.5 s | 40 s | — | Own units in the radius are Frenzied (+40% attack speed) for 10 s. No effect on buildings |

In `abilities.json`: `kind` `selfAura` (no range), `radius` 8, `castTime` 0.5, `cooldown` 40, `affects` `own_units`, one
effect `{ "kind": "applyStatus", "status": "frenzied", "magnitude": 0.4, "duration": 10 }`. The 10 s is the status's,
so the Duration column (the ability's own, a zone's life) is "—", as for Telas Fire.

Two parts of the design are **not in the data** until the sim has them: **+20% damage** while Frenzied (the `frenzied`
status has one magnitude, attack speed; a damage share is a request to the sim track), and the **aftermath**: when the
Frenzy ends the units are Slowed 20% for 4 s (M7-4, not in the data until then). The aftermath slow is the trade-off:
Frenzy wins fights, but a Frenzied army can't chase or escape right afterward.

## Ranged unit or pure melee? (decided 2026-10-02: Javelin Thrower)

Kept for the record, in case playtests reopen it.

| Option | Pros | Cons |
| --- | --- | --- |
| **Javelin Thrower, weak (chosen)** | Template stays complete (Ranged Hall, AI comps, UI); Teblor can hit the Andii Great Raven; some kiting answers | Less distinctive |
| No ranged unit at all | Strong asymmetry, very Teblor | Empty Ranged Hall slot needs special cases in UI/AI; only Shamans and towers can hit flying units; hard to answer kiting |

## Buildings

| Slot | Name | Id | HP | Armor | Cost (G/W) | Build (s) | Footprint | Sight | Provides | Requires |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Town Hall | Longhouse | `teblor_longhouse` | 2400 | 2 | 275 / 275 | 90 | 4×4 | 12 | +10 pop, drop-off, trains Gatherer, researches Age II | — |
| House | Hut | `teblor_hut` | 500 | 1 | 0 / 50 | 20 | 2×2 | 12 | +8 pop | — |
| Camp | Woodcutters' Lodge | `teblor_woodcutters_lodge` | 600 | 1 | 0 / 75 | 25 | 2×2 | 12 | Drop-off | — |
| Infantry Hall | War Lodge | `teblor_war_lodge` | 1200 | 2 | 0 / 150 | 40 | 3×3 | 12 | Trains Warrior | — |
| Ranged Hall | Javelin Ground | `teblor_javelin_ground` | 1200 | 2 | 0 / 150 | 40 | 3×3 | 12 | Trains Javelin Thrower | — |
| Shock Hall | Stables & Kennels | `teblor_stables` | 1200 | 2 | 75 / 150 | 45 | 3×3 | 12 | Trains Destrier Rider, War-dog | War Lodge |
| Forge | Bloodwood Carver | `teblor_bloodwood_carver` | 1000 | 2 | 100 / 100 | 40 | 3×3 | 12 | Upgrades, Blood-oil Anointing | — |
| Caster Hall | Shaman's Circle | `teblor_shamans_circle` | 1200 | 2 | 150 / 150 | 50 | 3×3 | 12 | Trains Shaman | Age II |
| Siege Works | Breaker's Hall | `teblor_breakers_hall` | 1400 | 2 | 150 / 200 | 55 | 3×3 | 12 | Trains Breaker | Age II |
| Watch Tower | Stone Cairn | `teblor_stone_cairn` | 800 | 2 | 50 / 125 | 35 | 2×2 | 24 | Attack 10 pierce / 2 s, range 18; sight 24; detector 16 m | Age II |

Building stats are the template's in [02-game-design.md](../02-game-design.md#buildings), unchanged: HP, armor, cost,
build time, footprint, sight and the Watch Tower's attack and detector (the rows above are the template's numbers with
the Teblor names, in the slot order of the file). Models are 1.25× scale visually but keep template footprints (a
building has no `scale` field; the view's call).

`Content/BuildingContentTests` and `UnitContentTests` pin this page to the data once the files land.

## Techs

| Id | Name | Researched at | Cost (G/W) | Time (s) | Requires | Effects |
| --- | --- | --- | --- | --- | --- | --- |
| `teblor_blood_oil_anointing` | Blood-oil Anointing | Bloodwood Carver | 200 / 150 | 45 | Age II | Warrior +2 armor; Destrier Rider +2 armor |

The faction upgrade is the only faction-specific tech: two `armor` effects, one per unit (`appliesTo` `units`
`["teblor_warrior"]`, then `["teblor_destrier_rider"]`). Its Frenzy duration +4 s is not in the data (above). The
common techs are the same as for every faction: Age II at the Longhouse, and the six shared Forge upgrades (Melee
Weapons, Ranged Weapons, Armor, each with a level II in Age II) at the Bloodwood Carver, in
[02-game-design.md "Forge upgrades"](../02-game-design.md#forge-upgrades). `Content/TechContentTests` pins this table
and the "Faction upgrade" line above to the data.

## Strengths and weaknesses

- **Strong:** wins any even melee fight; regenerates between fights, so harassment doesn't stick;
  Frenzy turns close fights into routs.
- **Weak:** few units, so losing one is expensive; kiting ranged armies, magic, and focus fire;
  slow to rebuild after a lost fight; big units are easy to see coming.

## AI build order (Normal)

The AI opponent's `game/data/factions/teblor/ai.json` (docs/03 "For the data track (M5-1)" and "Implementation
(M5-8a)") will hold one **opening**, read the same way as malazan.md "AI build order (Normal)" (steps run side by side,
one command per step per think, in this order; **Count** is the `train` commands the step queues over its life). Easy,
Normal and Hard all play it, since there is one. The name and description below are what the player may read. The
Teblor keep few workers (16 in Age I, 24 in Age II): each Gatherer costs half as much again as a template worker. The AI
starts with 5 Gatherers, 3 on gold and 2 on wood. `Content/AiContentTests` pins the openings table, the opening's step
table, composition table and wave line, and the retreat line below to the file, both ways.

| # | Id | Name (shown to the player) | Description (shown to the player) |
| --- | --- | --- | --- |
| 1 | `standard` | War band | War Lodge and Bloodwood Carver first; four Warriors raid at 8 army pop, then Javelin Throwers, Destrier Riders, Shamans, Breakers and War-dogs join in Age II. |

Not in the file, because the schema has no field for it (requests to the sim track): the sketch's later waves "whenever
army pop ≥ 24" (an opening has one wave threshold, used for every wave), its expansion condition "when army ≥ 16 pop"
(the step fires at its time), "target the nearest enemy production building" for the first wave (a wave goes for the
known enemy building nearest the rally point), and "retreat to regenerate below 40% total HP" (the file's retreat is a
share of the wave's pop lost, and a unit's own hp line; see Retreat). The Javelin Ground step (23) is not in the
sketch: the composition lists Javelin Throwers, so a step must place their hall.

- **Retreat:** a wave that has lost 50% falls back; a unit under 40% hp counts as wounded (`retreat`; Hard pulls it
  back to regenerate). The 40% is the sketch's; the 50% is the other pages' loss share (the data track's proposal).
- **Later waves:** one wave at a time; a new one starts when none is out, at least `ai.waveCooldown` after the last
  began, at the opening's threshold, and never while the base is defended.
- **High starting resources** (proposed for D28; docs/02 "Game flow" 2, docs/03 "Implementation (M6-S6)"): the AI
  plays the same opening as at Standard, but while it holds more gold + wood than the preset's spend-down threshold
  (`rules.json` `startingResources` `high` `aiSpendAbove`) it brings its next timed step forward, trains at every
  producer and leans its gatherers toward the kind it holds less of; Standard has no threshold, so it plays exactly as
  before. Not measured for the Teblor yet (not pinned): `standard` at High is measured once the files land (D28).
- **Defense** (proposed for D28, the shared rule in Teblor names; docs/02 "AI opponent", docs/03 "Implementation
  (M5-4b)" and "(M5-4f)", as re-worded in D20, 2026-10-17): a threat starts when an own building (a site too) within
  the defend radius of the Longhouse is hit, or an enemy soldier (not a worker) is seen within it (`rules.json`
  `ai.defendRadius` 48 m; the threat holds `ai.defendFor` 20 s after the last sign). The AI answers with defenders
  worth `ai.defendRatio` 1.5 times the intruders' gold + wood cost, at least `ai.defendMinValue` 150 (a building hit
  with nobody seen gets 150), taken from the army at home first (nearest the defense point first), then from the
  members of a wave out (nearest the Longhouse first), each only while the members left keep the wave's start
  threshold (`attack.minArmyPop` and `minArmyCost` times the difficulty's wave size ×, so a wave sent above it gives
  its surplus). One Cadre Mage (150 gold + wood) is answered with 225 worth, e.g. two Javelin Throwers from home
  (240), and a wave out keeps going. A raid worth more than the home army and the wave's spare units together calls
  the whole wave home (it ends like a retreat) and every free army unit answers. A hit that leaves the Longhouse below
  `ai.recallHallHp` 0.5 of its max hp does the same, however small the raid. Gatherers within `ai.workerFleeRadius`
  (16 m) of the threat flee to the nearest finished drop-off away from the threat (the Longhouse or a Woodcutters'
  Lodge, whichever is nearest among those more than 16 m from the threat; else the nearest one).

#### How the AI adapts

Three rules sit on top of every opening above, the counter shift, the starved charge and the far expansion, and two
notes go with the first: what answers what, and where the shift doesn't show (docs/02 "AI opponent"; docs/03
"Implementation (M5-9a)" and "(M5-9b)"). `Content/AiAdaptsContentTests` pins every number and name in the five
bullets to `ai.json`, `rules.json` and `buildings.json`, both ways.

- **Counter shift:** the AI remembers how many enemy soldiers of each slot it has seen at once (workers never count, and
  it sees only what its own fog of war shows), and that memory fades to nothing over `rules.json` `ai.counterMemory`
  180 s unless a sighting at least as big renews it. When it picks its next unit, each unit of the opening's army mix
  weighs more by the share of that memory its slot answers, times the difficulty's `counterStrength` in `ai.json`: Easy
  0 (an Easy AI never adapts), Normal 0.5, Hard 1.0. A Teblor AI that has seen only cavalry weighs its Warriors
  ×1.5 on Normal and ×2 on Hard. The shift tilts the army, it doesn't replace it: no unit is weighted below the
  opening's mix, and a unit the mix doesn't list is never added.
- **What answers what** (`rules.json` `ai.counters`, the "Countered by" column of docs/02 "Faction template", in this
  faction's units): enemy Line → Shaman, Breaker; enemy Ranged → Destrier Rider; enemy Shock → Warrior; enemy Caster →
  Destrier Rider, Javelin Thrower; enemy Siege → Warrior, Destrier Rider; enemy Unique → none.
- **Starved charge:** when the AI cannot pay for the unit it would train next and no income can close the gap, and it
  has at least `ai.starvedMinArmy` 1 combat unit free, then after `ai.starvedWaveAfter` 60 s in that state it sends
  every free combat unit at you whatever its wave threshold (or hunts, if it knows none of your buildings), again after
  each wave's cooldown, at every difficulty. A raid on its base resets the clock.
- **Far expansion:** when its own gold is spent (short of gold for its next unit, no Gatherer on gold and no gold mine it
  knows), it sends a Gatherer to look at the start locations nobody took. Once it has seen a free mine (at one of those
  starts or anywhere else) within `ai.farExpandReach` 384 m of its Longhouse, with no enemy drop-off by it, it
  builds a Woodcutters' Lodge (`teblor_woodcutters_lodge`, 0 gold / 75 wood) beside it and moves gatherers there;
  a Longhouse only if it can pay for one. One at a time. The reach holds for every mine, a start location's
  included: a free start farther than that from the hall is not walked to, and a mine beyond it is never taken.
- **Where the shift doesn't show (BUG-1156):** a War Lodge that already takes a Warrior every time its queue empties
  cannot train more of them for the heavier weight, so against cavalry the shift shows only when the bank, not the
  lodge, is the limit (the Malazan pattern, not yet measured for the Teblor). An enemy Line army seen in Age I shifts
  nothing, since no opening's Age I mix lists a Shaman or a Breaker.

"What answers what" is the shared rule applied to the Teblor units, so it is what the files will play. The data track's
proposal for a Teblor-only answer (War-dogs against Ranged) is under "Owner review" at the end of this page.

### Opening 1: `standard` (War band)

The sketch's order, step for step: a War Lodge first, a Woodcutters' Lodge, the Bloodwood Carver and Armor in Age I,
Age II at 7:00, then the Age II halls and the faction upgrade.

| # | At | Do | Id | Count | Why |
| --- | --- | --- | --- | --- | --- |
| 1 | time 0 | train | `teblor_gatherer` | 11 | Workers to 16 in Age I (5 at the start + 11) |
| 2 | pop 6 | build | `teblor_hut` | 1 | First Hut |
| 3 | pop 7 | build | `teblor_war_lodge` | 1 | War Lodge |
| 4 | pop 9 | build | `teblor_woodcutters_lodge` | 1 | Woodcutters' Lodge by the nearest known forest |
| 5 | pop 11 | build | `teblor_hut` | 1 | Second Hut |
| 6 | pop 15 | build | `teblor_bloodwood_carver` | 1 | The Forge |
| 7 | pop 15 | research | `armor_1` | 1 | Its first upgrade, Armor (refused, and retried, until the Carver stands) |
| 8 | pop 16 | build | `teblor_hut` | 1 | From pop 16 a Hut every 6 pop |
| 9 | pop 22 | build | `teblor_hut` | 1 | |
| 10 | pop 28 | build | `teblor_hut` | 1 | |
| 11 | pop 34 | build | `teblor_hut` | 1 | |
| 12 | pop 40 | build | `teblor_hut` | 1 | |
| 13 | pop 46 | build | `teblor_hut` | 1 | |
| 14 | pop 52 | build | `teblor_hut` | 1 | |
| 15 | pop 58 | build | `teblor_hut` | 1 | |
| 16 | pop 64 | build | `teblor_hut` | 1 | |
| 17 | pop 70 | build | `teblor_hut` | 1 | The 12th Hut: Longhouse 10 + 12 × 8 = 106, past the 100 cap |
| 18 | time 420 | research | `age_ii` | 1 | Age II at 7:00 (needs two kinds: the War Lodge and the Bloodwood Carver) |
| 19 | time 420 | train | `teblor_gatherer` | 8 | Workers to 24 in Age II |
| 20 | time 480 | build | `teblor_shamans_circle` | 1 | 8:00, when Age II (60 s) is in |
| 21 | time 480 | expand | — | 1 | 2nd Longhouse near the nearest free gold mine, at Normal's `expandAfter` (480 s) |
| 22 | time 495 | build | `teblor_stables` | 1 | Destrier Riders and War-dogs (needs the War Lodge) |
| 23 | time 510 | build | `teblor_javelin_ground` | 1 | Javelin Throwers for the Age II mix (not in the sketch) |
| 24 | time 540 | build | `teblor_breakers_hall` | 1 | Breakers |
| 25 | time 570 | research | `teblor_blood_oil_anointing` | 1 | The faction upgrade, at the Bloodwood Carver |

**Army composition (`compositions`), weights by pop.** Age I lists only the Warrior (the one unit the Age I steps can
train), so its weights sum to 45. The War-dog's weight is by pop like the rest (1 a dog); how the AI counts a pack item
against its share is the sim's (docs/03 "Implementation (M7-2)", BUG-1168).

| Unit | Age I | Age II |
| --- | --- | --- |
| `teblor_warrior` | 45 | 45 |
| `teblor_javelin_thrower` | — | 15 |
| `teblor_destrier_rider` | — | 15 |
| `teblor_shaman` | — | 10 |
| `teblor_breaker` | — | 10 |
| `teblor_war_dog` | — | 5 |

- **Composition target (by pop):** 45% Warrior, 15% Javelin Thrower, 15% Destrier Rider,
  10% Shaman, 10% Breaker, 5% War-dog.
- **Attack waves:** first at 8 army pop and 600 gold + wood (`attack`; the cost of 8 pop of the Age I mix: four
  Warriors at 75 a pop). Shamans cast Blood-oil Frenzy at engagement.

## Difficulty

The `difficulties` block of the same `game/data/factions/teblor/ai.json` (docs/02 "AI opponent", docs/03 "For the data
track (M5-1)"): one row per difficulty, one column per field, the same rows as every other faction's. Times are in
seconds as the file writes them (the loader turns them into ticks). **Build-order slack** is how long a step waits after
it is due; **wave size ×** scales the attack wave's thresholds above; **gather bonus** is extra gather rate (docs/02:
Hard's 40%, shown in the skirmish setup); **expands after** is the earliest match time the AI expands (the order's
`expand` step, due at Normal's 480 s, also waits for it). The `counterStrength` of each row (Easy 0, Normal 0.5, Hard 1)
is in "How the AI adapts" above. `Content/AiContentTests` pins this table to the file, both ways (J), and docs/02's
stated numbers to the file (K).

| Difficulty | Thinks every (s) | Build-order slack (s) | Wave size × | Gather bonus | Focus fire | Pulls back wounded | Uses abilities | Expands after (s) |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Easy | 2 | 30 | 0.6 | 0% | no | no | yes | 900 |
| Normal | 1 | 5 | 1 | 0% | no | no | yes | 480 |
| Hard | 0.5 | 0 | 1.25 | 40% | yes | yes | yes | 300 |

## Art notes

- **Palette:** earth brown, bloodwood red-brown, bone white, slate grey, furs. Team color on
  painted skin patterns and hair bindings.
- **Silhouettes:** 1.5× scale is the identity. Long hair, bare arms, huge single-edged wooden
  swords. Destrier Riders on oversized horses. Shamans with antlers or bone headdresses.
  Breakers carry a log-sized maul. War-dogs are big, shaggy, normal-scale dogs.
- **Likely sources:** KayKit Adventurers Barbarian scaled 1.5× and recolored, Quaternius horse
  scaled up, war-dogs from the Quaternius Ultimate Animated Animal Pack if it has a suitable dog
  or wolf, otherwise AI-generated. Details in [04-art-pipeline.md](../04-art-pipeline.md).

## Codename → release name

| Internal codename | Proposed release name |
| --- | --- |
| Teblor | The Bloodwood Clans |
| Warrior | Clan Warrior |
| Blood-oil Frenzy / Blood-oil Anointing | Rage-oil Frenzy / Rage-oil Anointing |
| Gatherer, Javelin Thrower, Destrier Rider, Shaman, Breaker, War-dog, all buildings | Keep (generic) |

## Owner review (D-R36, proposed)

The words the Teblor files will carry, for the owner to read before D28 copies them (codenames, as the other
factions' files; the release names are the table above). Each description is one or two short sentences of at most
160 characters, names only Teblor things, and says "needs X" exactly for a building's requirements (the content pins'
rules for the shipped factions).

| File | Id | Name (`displayName`) | Description (`description`) |
| --- | --- | --- | --- |
| `faction.json` | `teblor` | Teblor | Mountain clans of giants who live by raiding and feud. Few, huge and hard to kill, they heal between fights. |
| `faction.json` bonus | — | Children of the Mountains | Units heal 1 HP a second once they have gone 6 seconds without taking damage. Giants train slowly. |
| `units.json` | `teblor_gatherer` | Gatherer | Gathers gold and wood, raises buildings, and repairs them. Tough enough to shrug off a light raid. |
| `units.json` | `teblor_warrior` | Warrior | A giant with a bloodwood sword who out-trades two or three lowland soldiers and cuts down cavalry. |
| `units.json` | `teblor_javelin_thrower` | Javelin Thrower | Hurls heavy javelins at short range, so the clans can hit fliers and enemies who keep their distance. |
| `units.json` | `teblor_destrier_rider` | Destrier Rider | A giant on a huge horse who rides down archers, mages, and workers. |
| `units.json` | `teblor_shaman` | Shaman | Rubs blood-oil into nearby warriors to drive them into a frenzy. Detects hidden units. |
| `units.json` | `teblor_breaker` | Breaker | A giant with a log-sized maul that smashes buildings; it can hit soldiers too, but poorly. |
| `units.json` | `teblor_war_dog` | War-dog | Big shaggy dogs trained three at a time. Each bite slows its victim so the giants can catch it. |
| `buildings.json` | `teblor_longhouse` | Longhouse | The heart of a Teblor hold. Gatherers bring gold and wood here, and it trains new Gatherers. |
| `buildings.json` | `teblor_hut` | Hut | A low hut of hide and timber for another giant's family, raising your population cap by 8. |
| `buildings.json` | `teblor_woodcutters_lodge` | Woodcutters' Lodge | A forward store where Gatherers drop off gold and wood, so they spend less time walking. |
| `buildings.json` | `teblor_war_lodge` | War Lodge | Trains the Warrior, the giant swordsman every war party is built around. |
| `buildings.json` | `teblor_javelin_ground` | Javelin Ground | Trains the Javelin Thrower, who hits fliers and keeps the clans from being kited. |
| `buildings.json` | `teblor_stables` | Stables & Kennels | Trains the Destrier Rider and packs of War-dogs; needs a War Lodge. |
| `buildings.json` | `teblor_bloodwood_carver` | Bloodwood Carver | Carves bloodwood weapons and armor for the clans, and Blood-oil Anointing once you reach Age II. |
| `buildings.json` | `teblor_shamans_circle` | Shaman's Circle | Trains the Shaman, who drives the warriors into a blood-oil frenzy; needs Age II. |
| `buildings.json` | `teblor_breakers_hall` | Breaker's Hall | Trains the Breaker, a giant who smashes walls with a bloodwood maul; needs Age II. |
| `buildings.json` | `teblor_stone_cairn` | Stone Cairn | A lookout of piled stone that watches the passes, spots hidden enemies and shoots at intruders; needs Age II. |
| `techs.json` | `teblor_blood_oil_anointing` | Blood-oil Anointing | Blood-oil rubbed into the skin before battle: Warriors and Destrier Riders gain +2 armor. Needs Age II. |
| `abilities.json` | `blood_oil_frenzy` | Blood-oil Frenzy | Drives nearby warriors into a frenzy: own units within 8 m attack 40% faster for 10 seconds. |
| `ai.json` opening | `standard` | War band | War Lodge and Bloodwood Carver first; four Warriors raid at 8 army pop, then Javelin Throwers, Destrier Riders, Shamans, Breakers and War-dogs join in Age II. |

**Counter answers (the data track's proposal, the owner's call).** `rules.json` `ai.counters` is one rule by slot for
every faction, so the Teblor answers in "How the AI adapts" are that rule in Teblor units. The proposal differs in one
row: War-dogs (the Unique slot) against enemy Ranged, the design's "the pack pins kiting units". The shared rule gives
the Unique slot no answers, so the row needs a per-faction override or a Unique entry in the rule (a request to the sim
track; it would move the other factions' answers too).

| Enemy slot | The shared rule gives the Teblor | Proposed |
| --- | --- | --- |
| Line | Shaman, Breaker | Shaman, Breaker |
| Ranged | Destrier Rider | Destrier Rider, War-dog |
| Shock | Warrior | Warrior |
| Caster | Destrier Rider, Javelin Thrower | Destrier Rider, Javelin Thrower |
| Siege | Warrior, Destrier Rider | Warrior, Destrier Rider |
| Unique | none | none |
