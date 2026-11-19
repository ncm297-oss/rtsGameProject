# Teblor

**Faction id:** `teblor` · **Milestone:** M7 · **Role:** giants

This page was written ahead of the roster (D-R36, 2026-11-10) and the files under `game/data/factions/teblor/` were
copied from it in D28 (2026-11-11): the content pins compare every table, note and player-facing word here with the files,
both ways, as for the other factions. Where the design asks for something the data format can't hold yet, the page says
so next to it ("not in the data"); where the rule exists but the faction's row has not landed, it says when the row lands
("the rows land with D31"), and `Content/RuleRowClaimTests` holds such a note to the data both ways.

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

- Every unit but the War-dog is drawn **1.5× as tall** (`units.json` `scale` 1.5; the War-dog 1, normal-sized). A unit
  is drawn as wide as its room, its collision radius (0.75 m for the giants, still under the half-cell cap), and `scale`
  is its height (docs/03 "Implementation (M7-V3c)"). Visual only.
- Combat units cost **2-3 population** (the War-dog 1 a dog); workers cost 1.
- All units **regenerate 1 HP/s** after 6 s without taking damage (`faction.json` `regen`
  `{ "hpPerSecond": 1, "delay": 6 }`, docs/03 "Implementation (M7-1)").
- Units **train 25% slower** than the template (already in the table).
- The giants on foot (Warrior, Javelin Thrower, Shaman, Breaker) use the **Giant** armor class (`giant`; docs/02
  "Damage type × armor class": pierce and magic ×1.25, siege ×0.75). The Destrier Rider and the War-dog are Mounted, the
  Gatherer Light.

## Faction upgrade: Blood-oil Anointing

Age II, at the Bloodwood Carver, 200 G / 150 W, 45 s. Warriors and Destrier Riders gain **+2 armor**; Blood-oil Frenzy
**duration +4 s**.

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

- **Gatherer:** carries 15 instead of 10 (`units.json` `carry` 15, the worker's own load a trip; every other worker
  carries `rules.json` `workerCarry` 10). Expensive, tough enough to survive a light raid.
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
| Blood-oil Frenzy | Shaman | Self / aura | — | 8 m | 0.5 s | 40 s | — | Own units in the radius get Blood-oil Frenzy (+40% attack speed, +20% damage) for 10 s, then are Slowed 20% for 4 s. No effect on buildings |

In `abilities.json`: `kind` `selfAura` (no range), `radius` 8, `castTime` 0.5, `cooldown` 40, `affects` `own_units`, one
effect `{ "kind": "applyStatus", "status": "teblor_frenzied", "magnitude": 0.4, "duration": 10 }`. The 10 s is the status's,
so the Duration column (the ability's own, a zone's life) is "—", as for Telas Fire.

The status, `teblor_frenzied` in `common/statuses.json` ("Blood-oil Frenzy"), carries the rest: **+20% damage** while
it lasts (`damageBonus` 0.2), and the **aftermath**, Slowed 20% for 4 s when it ends (`onEnd`).
The aftermath slow is the trade-off: Frenzy wins fights, but a Frenzied army can't chase or escape right afterward.

## Ranged unit or pure melee? (decided 2026-10-02: Javelin Thrower)

Kept for the record, in case playtests reopen it.

| Option | Pros | Cons |
| --- | --- | --- |
| **Javelin Thrower, weak (chosen)** | Template stays complete (Ranged Hall, AI comps, UI); Teblor can hit the Andii Great Raven; some kiting answers | Less distinctive |
| No ranged unit at all | Strong asymmetry, very Teblor | Empty Ranged Hall slot needs special cases in UI/AI; only Shamans and towers can hit flying units; hard to answer kiting |

## Buildings

| Slot | Name | Id | HP | Armor | Cost (G/W) | Build (s) | Footprint | Sight | Provides | Requires | Scale |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Town Hall | Longhouse | `teblor_longhouse` | 2400 | 2 | 275 / 275 | 90 | 4×4 | 12 | +10 pop, drop-off, trains Gatherer, researches Age II | — | 1.25 |
| House | Hut | `teblor_hut` | 500 | 1 | 0 / 50 | 20 | 2×2 | 12 | +8 pop | — | 1.25 |
| Camp | Woodcutters' Lodge | `teblor_woodcutters_lodge` | 600 | 1 | 0 / 75 | 25 | 2×2 | 12 | Drop-off | — | 1.25 |
| Infantry Hall | War Lodge | `teblor_war_lodge` | 1200 | 2 | 0 / 150 | 40 | 3×3 | 12 | Trains Warrior | — | 1.25 |
| Ranged Hall | Javelin Ground | `teblor_javelin_ground` | 1200 | 2 | 0 / 150 | 40 | 3×3 | 12 | Trains Javelin Thrower | — | 1.25 |
| Shock Hall | Stables & Kennels | `teblor_stables` | 1200 | 2 | 75 / 150 | 45 | 3×3 | 12 | Trains Destrier Rider, War-dog | War Lodge | 1.25 |
| Forge | Bloodwood Carver | `teblor_bloodwood_carver` | 1000 | 2 | 100 / 100 | 40 | 3×3 | 12 | Upgrades, Blood-oil Anointing | — | 1.25 |
| Caster Hall | Shaman's Circle | `teblor_shamans_circle` | 1200 | 2 | 150 / 150 | 50 | 3×3 | 12 | Trains Shaman | Age II | 1.25 |
| Siege Works | Breaker's Hall | `teblor_breakers_hall` | 1400 | 2 | 150 / 200 | 55 | 3×3 | 12 | Trains Breaker | Age II | 1.25 |
| Watch Tower | Stone Cairn | `teblor_stone_cairn` | 800 | 2 | 50 / 125 | 35 | 2×2 | 24 | Attack 10 pierce / 2 s, range 18; sight 24; detector 16 m | Age II | 1.25 |

Building stats are the template's in [02-game-design.md](../02-game-design.md#buildings), unchanged: HP, armor, cost,
build time, footprint, sight and the Watch Tower's attack and detector (the rows above are the template's numbers with
the Teblor names, in the slot order of the file). Each building is drawn 1.25× taller on the same footprint, the
template's (`buildings.json` `scale`, the view's model height: the Scale column).

`Content/BuildingContentTests` and `UnitContentTests` pin this page to the data.

## Techs

| Id | Name | Researched at | Cost (G/W) | Time (s) | Requires | Effects |
| --- | --- | --- | --- | --- | --- | --- |
| `teblor_blood_oil_anointing` | Blood-oil Anointing | Bloodwood Carver | 200 / 150 | 45 | Age II | Warrior +2 armor; Destrier Rider +2 armor; Blood-oil Frenzy duration +4 s |

The faction upgrade is the only faction-specific tech: two `armor` effects, one per unit (`appliesTo` `units`
`["teblor_warrior"]`, then `["teblor_destrier_rider"]`). Its Frenzy duration +4 s is a third effect, a `statusDuration`
on the Frenzy's status (`statuses` `["teblor_frenzied"]`, no `appliesTo`). The
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
(M5-8a)") holds one **opening**, read the same way as malazan.md "AI build order (Normal)" (steps run side by side,
one command per step per think, in this order; **Count** is the `train` commands the step queues over its life). Easy,
Normal and Hard all play it, since there is one. The name and description below are what the player may read. The
Teblor keep few workers (14 in Age I, 22 from 10:00): each Gatherer costs half as much again as a template worker. The AI
starts with 5 Gatherers, 3 on gold and 2 on wood. `Content/AiContentTests` pins the openings table, the opening's step
table, composition table and wave line, and the retreat line below to the file, both ways.

| # | Id | Name (shown to the player) | Description (shown to the player) |
| --- | --- | --- | --- |
| 1 | `standard` | War band | War Lodge and Javelin Ground first, then Age II; four Warriors raid at 8 army pop, then Javelin Throwers and War-dogs join them; Shamans, Breakers, Destrier Riders and a second Longhouse may follow. |

Not in the file, because the schema has no field for it (requests to the sim track): the sketch's later waves "whenever
army pop ≥ 24" (an opening has one wave threshold, used for every wave), its expansion condition "when army ≥ 16 pop"
(the step fires at its time), "target the nearest enemy production building" for the first wave (a wave goes for the
known enemy building nearest the rally point), and "retreat to regenerate below 40% total HP" (the file's retreat is a
share of the wave's pop lost, and a unit's own hp line; see Retreat). The Javelin Ground step (5) is not in the
sketch: the composition lists Javelin Throwers, so a step must place their hall, and here it is Age II's second kind.

- **Retreat:** a wave that has lost 50% falls back; a unit under 40% hp counts as wounded (`retreat`; Hard pulls it
  back to regenerate). The 40% is the sketch's; the 50% is the other pages' loss share (the data track's proposal).
- **Later waves:** one wave at a time; a new one starts when none is out, at least `ai.waveCooldown` after the last
  began, at the opening's threshold, and never while the base is defended.
- **High starting resources** (docs/02 "Game flow" 2, docs/03 "Implementation (M6-S6)"): the AI
  plays the same opening as at Standard, but while it holds more gold + wood than the preset's spend-down threshold
  (`rules.json` `startingResources` `high` `aiSpendAbove`) it brings its next timed step forward, trains at every
  producer and leans its gatherers toward the kind it holds less of; Standard has no threshold, so it plays exactly as
  before. Not measured for the Teblor yet (not pinned): `standard` at High (the other pages' figures come from the sim
  track's M6-S6 QA report, which runs the two older factions).
- **Defense** (the shared rule in Teblor names; docs/02 "AI opponent", docs/03 "Implementation
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

"What answers what" is the shared rule applied to the Teblor units, so it is what the files play. The data track's
proposal for a Teblor-only answer (War-dogs against Ranged) is under "Owner review" at the end of this page.

### Opening 1: `standard` (War band)

D34's order Q2 (BUG-1612, BUG-1641) on D33's measured variant F3 (BUG-1420), on D32's X7: a War Lodge and the Javelin
Ground first, the two building kinds Age II needs, and Age II at 6:00; from 8:00 a second Longhouse, from 9:00 the
Breakers Hall, the Shamans' Circle and two Shamans, from 10:00 the Stables, the Bloodwood Carver, the faction upgrade
(10:00 since D34; was 11:30) and eight more Gatherers, then Armor (12:00); and one Breaker, one Destrier Rider and one
War-dog pack, each as soon as its hall stands. The sketch's Carver and Armor in Age I moved after Age II so the AI saves
for the age first; the 11 Gatherers at the start became 9. D32 put the expansion first among the Age II steps, since
nothing else may be due beside it if the bank is ever to hold a Longhouse's cost, and added the Shaman step, since the
mix's Shaman weight alone fielded a Shaman in 4 of 64 matches. D33 put the three train steps and the Breakers Hall ahead
of the expansion in the file: while the expansion waits for its 275 gold and 275 wood (in about half the matches it never
goes up) its reserve held back every unit step after it, so the halls stood and nothing trained in them ("The Age II
army (BUG-1420, D33)" at the end of this page). D34 put the Carver and the Anointing just after the Breaker's train step,
ahead of the Rider and War-dog steps: last but two in D33's file, the Anointing was researched in 1 of 64 Easy matches;
here it is 28 ("Easy's Anointing (BUG-1612, D34)" at the end of this page).

| # | At | Do | Id | Count | Why |
| --- | --- | --- | --- | --- | --- |
| 1 | time 0 | train | `teblor_gatherer` | 9 | Workers to 14 in Age I (5 at the start + 9) |
| 2 | pop 6 | build | `teblor_hut` | 1 | First Hut |
| 3 | pop 7 | build | `teblor_war_lodge` | 1 | War Lodge |
| 4 | pop 9 | build | `teblor_woodcutters_lodge` | 1 | Woodcutters' Lodge by the nearest known forest |
| 5 | pop 9 | build | `teblor_javelin_ground` | 1 | Age II's second building kind, and Javelin Throwers for the Age II mix (not in the sketch) |
| 6 | pop 11 | build | `teblor_hut` | 1 | Second Hut |
| 7 | pop 16 | build | `teblor_hut` | 1 | From pop 16 a Hut every 6 pop |
| 8 | pop 22 | build | `teblor_hut` | 1 | |
| 9 | pop 28 | build | `teblor_hut` | 1 | |
| 10 | pop 34 | build | `teblor_hut` | 1 | |
| 11 | pop 40 | build | `teblor_hut` | 1 | |
| 12 | pop 46 | build | `teblor_hut` | 1 | |
| 13 | pop 52 | build | `teblor_hut` | 1 | |
| 14 | pop 58 | build | `teblor_hut` | 1 | |
| 15 | pop 64 | build | `teblor_hut` | 1 | |
| 16 | pop 70 | build | `teblor_hut` | 1 | The 12th Hut: Longhouse 10 + 12 × 8 = 106, past the 100 cap |
| 17 | time 360 | research | `age_ii` | 1 | Age II at 6:00 (needs two kinds: the War Lodge and the Javelin Ground) |
| 18 | time 600 | train | `teblor_breaker` | 1 | One Breaker once the Breakers Hall stands (D33): ahead of the expansion and of the Anointing in the file, so neither's reserve holds it back |
| 19 | time 600 | build | `teblor_bloodwood_carver` | 1 | The Forge, at 10:00; after the Breaker's step and ahead of the Rider and War-dog steps (D34) |
| 20 | time 600 | research | `teblor_blood_oil_anointing` | 1 | The faction upgrade, at the Bloodwood Carver, at 10:00 (D34; was 11:30, last but two) |
| 21 | time 600 | train | `teblor_destrier_rider` | 1 | One Destrier Rider once the Stables stand (D33) |
| 22 | time 600 | train | `teblor_war_dog` | 1 | One War-dog pack (3 dogs) once the Stables stand (D33) |
| 23 | time 540 | build | `teblor_breakers_hall` | 1 | Breakers, at 9:00, ahead of the expansion in the file (D33) |
| 24 | time 480 | expand | — | 1 | 2nd Longhouse near the nearest free gold mine, at Normal's `expandAfter` (480 s); the first Age II building step after the Breakers Hall |
| 25 | time 600 | build | `teblor_stables` | 1 | Destrier Riders and War-dogs (needs the War Lodge), at 10:00, first of the 10:00 buildings after the expansion (D33) |
| 26 | time 540 | build | `teblor_shamans_circle` | 1 | Shamans, at 9:00 |
| 27 | time 540 | train | `teblor_shaman` | 2 | Two Shamans once the Circle stands (the mix's weight alone fielded almost none) |
| 28 | time 720 | research | `armor_1` | 1 | The Carver's first upgrade, Armor |
| 29 | time 600 | train | `teblor_gatherer` | 8 | Workers to 22 in Age II, at 10:00 |

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
- **Reserve:** the opening's own reserve share 0.25 (`attack.reserveArmyShare`; docs/02 "AI opponent"): the AI saves for
  a due step once its army is a quarter of the wave, not the whole of it (`rules.json` `ai.reserveArmyShare` 1, which
  every other opening plays). D31 measured it on the files that ship (below).

**The Age II army (D33, 2026-11-17, BUG-1420; D34, 2026-11-18, BUG-1612).** On the 64-match rows (the Teblor against the
Malazan and the Whirlwind, four maps, seeds 1-8, to 15:00) the Normal AI fields Javelin Throwers in 55 matches, Breakers
in 14, Destrier Riders in 22 and War-dogs in 37, a Shaman in 15 and a second Longhouse by 15:00 in 23, researches the
Anointing in 17, and is defeated in 9 (D34's order Q2). D33's order (F3, `main` until D34) gave Breakers 12, Destrier
Riders 22, War-dogs 39, a Shaman 25, a second Longhouse 23, the Anointing 11 and 10 defeats; before D33 (QA's row `main`
of D33, BUG-1613) they were 0, 4, 0, 26, 25, 8 and 12. The Easy and Hard rows are in the table under "Easy's Anointing
(BUG-1612, D34)" at the end of this page; D33's measurement and its 31 variants are under "The Age II army (BUG-1420,
D33)".

**The reserve share, measured (D31, 2026-11-14, on the files that ship; `Content/TeblorLandedShareReportTests`).**
Normal against Normal to 15:00, the Teblor on seat 0 against a Malazan and on seat 1 against a Whirlwind, on the
generated map, Raraku, Pale Hills and Vathar Crossing, seeds 1-8: 64 matches a row. The target (BUG-1241) is Age II and
a wave by 10:00 in 6 of a seed's 8 matches. Own share 0.25 with the Gatherer's carry 15 (shipped): the target on 8 of 8
seeds; of the 64, Age II by 10:00 in 55, a wave by 10:00 in 62, every building kind of the opening in 18, the Anointing
in 35, defeated by 15:00 in 14, no second Longhouse finished by 15:00, and an army of 42 pop at 15:00 summed over the
64 matches (0.7 pop a match: most seats had just lost their army). Own share 0.5 with the carry: 8 of 8; 55, 62, 1, 26,
14, 64. Before the carry (D-H8, 2026-11-13): 0.25 on 6 of 8 (46, 55, 8, 11, 17, 56) and 0.5 on 6 of 8 (44, 55, 0, 11,
16, 36). In those lists the last figure is the army pop at 15:00 summed over the 64 matches, not one match's army. The
rule fixed before the measurement was "0.25 if it meets 6 of 8, else 0.5", so 0.25 ships.

**The opening, measured (D32, 2026-11-14, on the files that ship; `Content/TeblorLandedShareReportTests` row `shipped`,
the variants in `Content/D32TeblorOpeningReportTests`).** A dated measurement: the figures in this paragraph and the next
were measured on 2026-11-14, before the sim's M6-S10 gather change (2026-11-15), and no test checks them; re-run the two
reports to bring them up to date. The same 64 matches (Normal against Normal to 15:00, four maps,
seeds 1-8). The target (Age II and a wave by 10:00 in 6 of a seed's 8) on 8 of 8 seeds; of the 64, Age II by 10:00 in 55,
a wave by 10:00 in 62, a second Longhouse finished by 15:00 in 18, every building kind of the opening in 18, the
Anointing in 20, defeated by 15:00 in 10; a Shaman fielded in 27, with 22 Blood-oil Frenzy casts landed in 18 of them;
Javelin Throwers fielded in 51 and no Breaker, Destrier Rider or War-dog in any by 15:00 (the description named only the
Javelin Throwers and Shamans for that reason until D33, BUG-1420); an army of 106 pop at 15:00 summed over the 64 matches (1.7 a match). D31's
opening on the same row: 8 of 8 seeds; 55, 62, no second Longhouse, 18, 35, 14 defeated; a Shaman in 4 (4 casts). The
brief's rule: at least 6 of 8 seeds, at most 14 defeats, more than 4 matches with a Shaman and more than 0 with a second
Longhouse; of the 19 variants measured beside D31's, six meet all four (E2, C3, X2, X7, X8, X9), and this one (X7) also
keeps D31's every-kind count: E2 and C3 (own share 0) lose the wave by 10:00 in 12 matches, and X2, X8 and X9 finish every
kind in 5, 6 and 6 and the Anointing in 15, 11 and 6.

**Why the expansion waited (D32, `Content/D32TeblorOpeningReportTests.Expansion_ThinkByThink_Report`).** Gold, not the
step's place in the list or a missing mine: at the Teblor thinks from 8:00 until the step was sent, on D31's opening (64
matches, 22,926 thinks), a free gold mine was known in 19,022, but the bank held a Longhouse's 275 gold and 275 wood in
36. The base was defended in 14,088 of them (no reserve at all then), and otherwise the reserve went to the earlier Age
II steps (the Breakers Hall 3,952 thinks, the Shamans' Circle 1,785) and to the expansion in 1,021. Moving the step first
alone gave it the reserve in 7,284 thinks, and the bank still held its cost in 34, since a building or research step after
the reserved one is not held back by the reserve (docs/02 "AI opponent"). So in D32's opening nothing else was due
beside it at 8:00: the Carver and the Breakers Hall waited until 10:00, the Anointing until 11:30 and Armor until 12:00
(since D33 the Breakers Hall is due at 9:00, and since D34 the Anointing at 10:00; nothing but the expansion is due at
8:00 still).
Where the step was sent before D32, on Raraku against the Whirlwind at 8:35, the site was razed and never placed again
(an expansion is not rebuilt, docs/02).

**Houses at the cap (D32, 2026-11-14, measured on the shipped files;
`QA/Content/AiHouseRuleReportTests.MenuMapNormal_HouseSources_Report` and `MenuMapNormal_TwelfthHouse_Report`).**
Since M5-4f the executor skips a house step once the population cap is the hard cap (100): the step is done with no
command sent and no Hut placed. On the menu's map (20 minutes, the other seat idle): Normal, seed 1: the cap reaches 100 at 1054 s; the last Hut step (step 16) is done at a cap of 100; skipped at the hard cap: step 16; 11 Huts stand at 20:00, 0 past the cap. As on the other pages, the 12th Hut step measured taken out: without the 12th Hut step (step 16) on the same seed, the cap reaches 100 at 1054 s, and 11 Huts stand at 20:00, 0 past the cap. The step stays: on this seed the
Longhouses and 11 Huts bring the cap to 100, so the 12th Hut step (16) is skipped at the hard cap (re-measured 2026-11-18
for D34's order: on D33's the cap reached 100 at 1043 s with 10 Huts, the second Longhouse (+10 pop, D32's expansion)
standing, and the 11th and 12th Hut steps (15 and 16) were skipped; re-measured 2026-11-17 for D33's opening: the cap was at 100 at 1059 s with 11 Huts, 1 past the cap, and only step 16 skipped; re-measured 2026-11-16 after the sim's M6-S12 AI rules; 2026-11-15, after the sim's M6-S10 gather change: the
cap at 100 at 1000 s with 10 Huts, both the 11th and the 12th Hut steps (15 and 16) skipped; before it step 15 was skipped
and step 16 never reached); a seat that loses or never builds the second Longhouse needs both,
and they cost nothing until they are reached.

**Spending on the menu's map (D32, 2026-11-14, gathering gold 0.85 / wood 0.75 a second; `Content/AiSpendingReportTests.GeneratedNormal_Spending_Report`).**
Against an idle seat, 12 forests and 8 mines (the menu's), 20 minutes. Normal, seed 1 (the smallest seed in 1-50 whose Normal pick is this opening): the idle base falls at 469 s; in hand at 5 min 245 gold / 120 wood; the first wave at 320 s; Age II in at 425 s (re-printed 2026-11-16 for the sim's M6-S12; was: the idle base falls at 470 s; in hand at 5 min 245 gold / 135 wood; the first wave at 320 s; Age II in at 425 s). Easy, seed 1 (Easy always plays the first opening): the idle base falls at 496 s; in hand at 5 min 215 gold / 75 wood; the first wave at 318 s; Age II in at 450 s (re-printed 2026-11-16 for the sim's M6-S12; was: the idle base falls at 502 s; in hand at 5 min 245 gold / 60 wood; the first wave at 326 s; Age II in at 450 s). Against an active opponent on the same map and seed
(`QA/Content/TeblorOpeningQaTests`, re-measured in session 2026-11-16-2036 after the sim's M6-S12 AI rules; its rows
pin the ticks and `TheOpponentSentence_QuotesTheReportRows` this sentence): a Normal Teblor seat sends its first wave at
320 s, is in Age II at 425 s and still stands at 20:00 against a Normal Malazan; against a Normal Whirlwind it sends its
first wave at 320 s, is in Age II at 425 s and still stands at 20:00 (M6-SH2's runs, 2026-11-15: 321 s and 425 s against
both; M6-S10's runs, 2026-11-15: 323 s and 448 s against
the Malazan; 316 s, 429 s and beaten at 1,022 s against the Whirlwind; D31's runs, 2026-11-14: 340 s and 466 s against
the Malazan; 362 s, 464 s and beaten at 910 s against the Whirlwind). BUG-1240's "beaten before Age II" is gone on this
seed, and since M6-SH2 the Teblor seat stands at 20:00 in both matches: the Whirlwind match is no longer a defeat.

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

## Owner review (D-R36; copied into the files in D28)

The words the Teblor files carry, for the owner to read (codenames, as the other factions' files; the release names
are the table above). D28 copied them as written, since no answer on the names had come; `Content/TeblorRosterPinTests`
pins every row to the files character for character, both ways. Each description is one or two short sentences of at
most 160 characters (an AI opening's at most 200, D21's rule for every faction's openings), names only Teblor things, and says "needs X" exactly for a building's requirements (the content
pins' rules for every faction).

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
| `techs.json` | `teblor_blood_oil_anointing` | Blood-oil Anointing | Blood-oil rubbed into the skin before battle: Warriors and Destrier Riders gain +2 armor, and Blood-oil Frenzy lasts 4 s longer. Needs Age II. |
| `abilities.json` | `blood_oil_frenzy` | Blood-oil Frenzy | Drives nearby warriors into a frenzy: own units within 8 m attack 40% faster and deal 20% more damage for 10 seconds, then are Slowed 20% for 4 seconds. |
| `statuses.json` | `teblor_frenzied` | Blood-oil Frenzy | Attacks faster and deals 20% more damage. When it ends, the unit is Slowed 20% for 4 seconds. |
| `ai.json` opening | `standard` | War band | War Lodge and Javelin Ground first, then Age II; four Warriors raid at 8 army pop, then Javelin Throwers and War-dogs join them; Shamans, Breakers, Destrier Riders and a second Longhouse may follow. |

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

## The Age II army (BUG-1420, D33, 2026-11-17)

Measured by `Content/D33BalanceReportTests.TeblorVariants_64SeatRow_Report` (2026-11-17; the shape of
`QA/Content/D32OpeningQaStressTests`, which re-runs the shipped opening's rows). The Teblor against the Malazan on seat 0 and against the Whirlwind on
seat 1, the same difficulty on both seats, the generated map, Raraku, Pale Hills and Vathar Crossing, seeds 1-8, to
15:00: 64 matches a row, and the columns count the matches in which at least one unit of the kind was alive at some
second before 15:00. On the files before D33 (with the Malazan and Whirlwind waves D33 ships) the Normal AI fielded no
Breaker or War-dog and a Destrier Rider in 4 of 64, though the Stables stood in 36 matches and the Breakers Hall in 17
(at a median 12:12 and 12:34). The cause is the reserve, not the halls: the Age II mix waits for its most under-weighted
unit, and every unit step after the expand step in the file spends only above the expansion's 275 gold and 275 wood
while the expansion waits, which on Normal is most of the match in about half of them. A Breaker (180 gold, 120 wood) is
the costliest unit, so it is the last to be paid for. The brief's target: on Normal and on Hard each of the three kinds
in at least 8 of 64, and on Normal a Shaman in 14 or more, a second Longhouse by 15:00 in 9 or more and at most 15
defeats. S0 and 31 variants on Normal, S0 and 14 of them on Hard too (`-`: not run). S0 and every variant play D33's
Malazan and Whirlwind waves, so S0 is not the data that shipped before D33: that is the row `main` (D32's opening on
D32's waves, data hash B74DC7AE61B4862D, QA's `QA/Content/D33TeblorRowQaStressTests` row `main`; BUG-1613), and the
"was" figures below are `main`'s:

| variant | change | Normal: defeated / 2nd Longhouse / Shamans | Normal: Breakers / Destrier Riders / War-dogs | Hard: defeated / 2nd Longhouse / Shamans | Hard: Breakers / Destrier Riders / War-dogs |
| --- | --- | --- | --- | --- | --- |
| `main` | before D33: the opening D32 shipped (X7) on D32's Malazan and Whirlwind waves (QA's row) | 12 / 25 / 26 | 0 / 4 / 0 | 11 / 28 / 35 | 6 / 12 / 5 |
| S0 | the opening D32 shipped (X7), on D33's waves | 5 / 28 / 30 | 0 / 4 / 0 | 9 / 28 / 36 | 11 / 18 / 8 |
| B1 | S0 with the Breakers Hall and the Stables first of the 10:00 steps (before the Carver) | 11 / 28 / 31 | 4 / 0 / 0 | 11 / 26 / 36 | 17 / 16 / 4 |
| B2 | B1 with the Carver at 11:00 | 8 / 28 / 19 | 0 / 0 / 0 | 11 / 33 / 41 | 20 / 14 / 5 |
| B3 | S0 with the Stables and the Breakers Hall at 9:00, after the Shaman step | 12 / 28 / 30 | 0 / 5 / 1 | 5 / 30 / 36 | 24 / 15 / 7 |
| B4 | S0 with the Stables at 7:00 before the expansion and the Breakers Hall first of the 10:00 steps | 13 / 21 / 23 | 3 / 29 / 6 | 8 / 32 / 40 | 30 / 60 / 51 |
| B5 | B1 with the eight Gatherers last in the file | 11 / 28 / 31 | 4 / 0 / 0 | 11 / 26 / 36 | 17 / 16 / 4 |
| B6 | B3 with the Carver at 11:00 | 9 / 28 / 34 | 0 / 5 / 1 | 5 / 30 / 42 | 32 / 25 / 7 |
| C1 | B4 plus train steps: 1 Breaker after the Breakers Hall, 1 Destrier Rider and 1 War-dog pack after the Stables | 11 / 0 / 17 | 10 / 57 / 53 | - | - |
| C2 | B3 plus C1's train steps | 7 / 28 / 29 | 0 / 21 / 38 | - | - |
| C3 | S0 plus C1's train steps | 5 / 28 / 30 | 0 / 4 / 15 | - | - |
| C4 | B1 plus C1's train steps | 11 / 28 / 18 | 6 / 11 / 29 | - | - |
| C5 | S0 with the Stables at 8:00 just after the expansion, the Breakers Hall first of the 10:00 steps, and C1's train steps | 10 / 17 / 16 | 5 / 34 / 43 | - | - |
| C6 | B6 plus C1's train steps | 5 / 28 / 29 | 0 / 26 / 33 | - | - |
| D1 | C4 with the Carver at 11:00 | 8 / 28 / 18 | 4 / 3 / 25 | - | - |
| D2 | S0 with the Breakers Hall at 9:00 before the Circle, the Stables first of the 10:00 steps, and C1's train steps | 11 / 33 / 12 | 1 / 28 / 31 | - | - |
| D3 | S0 with the Breakers Hall at 9:00 after the Shaman step, the Stables first of the 10:00 steps, and C1's train steps | 10 / 33 / 28 | 0 / 19 / 35 | - | - |
| D4 | D3 with the Carver at 11:00 | 8 / 28 / 31 | 0 / 18 / 38 | - | - |
| D5 | C4 with 2 Breakers in the train step | 11 / 28 / 18 | 6 / 11 / 29 | - | - |
| D6 | C4 with the Breaker listed before the Destrier Rider and the Shaman in the Age II mix (the tie order) | 11 / 28 / 18 | 6 / 11 / 29 | - | - |
| D7 | D3 with the Stables at 11:00 | 9 / 33 / 32 | 0 / 16 / 31 | - | - |
| E1 | C4 with its three train steps moved before the expand step | 11 / 28 / 17 | 7 / 23 / 38 | 11 / 26 / 22 | 22 / 44 / 50 |
| E2 | C4 with the Breaker train step moved before the expand step | 11 / 28 / 18 | 7 / 16 / 24 | 11 / 26 / 29 | 29 / 30 / 41 |
| E3 | C3 (S0 plus the train steps) with the three train steps before the expand step | 5 / 28 / 30 | 5 / 24 / 31 | 9 / 26 / 29 | 22 / 42 / 45 |
| E4 | D2 with the three train steps before the expand step | 11 / 28 / 17 | 7 / 29 / 40 | 11 / 23 / 23 | 35 / 42 / 48 |
| E5 | E1 with the Shaman step before the expand step too | 11 / 28 / 36 | 2 / 26 / 36 | 11 / 21 / 40 | 17 / 44 / 50 |
| E6 | E3 with the Shaman step before the expand step too | 5 / 28 / 38 | 5 / 24 / 31 | 9 / 21 / 41 | 22 / 41 / 45 |
| F1 | E1 with the Breakers Hall step moved before the expand step (still due at 10:00) | 11 / 28 / 21 | 7 / 19 / 38 | - | - |
| F2 | E1 with the Breakers Hall due at 9:00 | 10 / 28 / 21 | 7 / 31 / 44 | - | - |
| F3 | F1 with the Breakers Hall due at 9:00 | 10 / 23 / 25 | 12 / 22 / 39 | 11 / 23 / 25 | 35 / 43 / 48 |
| F4 | E1 with the Anointing at 13:00 and Armor at 13:30 | 11 / 28 / 25 | 8 / 23 / 37 | 11 / 31 / 41 | 24 / 41 / 50 |
| F5 | E1 with the Carver at 11:00, the Anointing at 12:30 and Armor at 13:00 | 8 / 28 / 27 | 5 / 30 / 41 | - | - |
| F6 | E4 with the Anointing at 13:00 and Armor at 13:30 | 11 / 28 / 32 | 6 / 26 / 40 | - | - |

F3 ships: the only variant that meets every figure on Normal with a margin on the Breakers (12; F4 meets them with 8).
Against `main` (QA's rows, BUG-1613; until D34 this paragraph printed S0's figures as "was"): on Normal it fields
Breakers in 12 of 64, Destrier Riders in 22 and War-dogs in 39 (were 0, 4 and 0), a Shaman in 25 (was 26), a second
Longhouse by 15:00 in 23 (was 25), the Anointing in 11 (was 8), and 10 defeats (was 12); Age II by 10:00 in 58 and a
wave by 10:00 in 63. On Hard: Breakers in 35 (was 6), Destrier Riders in 43 (was 12), War-dogs in 48 (was 5), a Shaman
in 25 (was 35), a second Longhouse in 23 (was 28), 11 defeats (as before), the Anointing in 18 (was 27). On Easy (both
seats Easy; Easy plays this opening too): Breakers 27, Destrier Riders 36, War-dogs 51 (all were 0), a Shaman in 34
(was 51), the Anointing in 1 (was 35), 1 defeat (as before), no second Longhouse (Easy's `expandAfter` is 900 s, as
before). What the Easy and Hard AIs lose is the Anointing and some Shamans: the gold that bought them now buys the
three kinds (BUG-1612; D34 won the Anointing back by order, the next section). D33's description named the three kinds
again (D32 had narrowed it to the kinds fielded then); each has its own train step, so a match whose halls stand fields
one of each. D34 softened it to name as joining only the kinds most Normal matches field (BUG-1613).

## Easy's Anointing (BUG-1612, D34, 2026-11-18)

Measured by `Content/D34TeblorStepOrderReportTests.Variants_Row_Report` (2026-11-18, on `main`'s AI; the shape of
`QA/Content/D33TeblorRowQaStressTests`: the Teblor against the Malazan on seat 0 and against the Whirlwind on seat 1, the
same difficulty on both seats, four maps, to 15:00; the columns count the matches in which the kind was alive at some
second before 15:00). M is D33's opening (F3, `main` before D34, `Fixtures/teblor_standard_d33.json`); each variant
changes only the order and the `at.time` of its steps. The brief's bar for shipping one, on the full 64: Easy's Anointing
in 20 or more, while on Normal and on Hard each of Breakers, Destrier Riders and War-dogs stays at 8 or more, and on
Normal a Shaman in 14 or more, a second Longhouse in 9 or more and at most 15 defeats. **Q2 ships** (QA's order,
BUG-1641; below); G1-G5 are the developer's first screen, Q1-Q3 QA's orders.

The reduced screen (seeds 1-4: 32 matches a row; Easy and Normal first, Hard run afterwards for the table):

| variant | change | Easy: defeated / 2nd Longhouse / Shaman / Breaker / Rider / War-dog / Anointing | Normal: defeated / 2nd Longhouse / Shaman / Breaker / Rider / War-dog / Anointing | Hard: defeated / 2nd Longhouse / Shaman / Breaker / Rider / War-dog / Anointing |
| --- | --- | --- | --- | --- |
| M | D33's opening (F3, `main` before D34) | 0 / 0 / 17 / 14 / 17 / 25 / 0 | 5 / 10 / 13 / 9 / 10 / 18 / 5 | 6 / 12 / 9 / 16 / 20 / 22 / 10 |
| G1 | M with the Shaman's Circle, the Shaman step and the Anointing moved ahead of the three train steps (times kept) | 0 / 0 / 27 / 0 / 12 / 16 / 18 | 5 / 10 / 19 / 0 / 5 / 15 / 10 | 6 / 8 / 21 / 9 / 15 / 21 / 12 |
| G2 | G1 with the Anointing due at 10:00 (was 11:30) | 0 / 0 / 26 / 0 / 8 / 15 / 18 | 5 / 10 / 19 / 0 / 6 / 15 / 10 | 6 / 8 / 21 / 9 / 14 / 21 / 12 |
| G3 | M with the Circle, the Shaman step, the Bloodwood Carver and the Anointing (at 10:00) ahead of the three train steps | 0 / 0 / 26 / 0 / 4 / 9 / 19 | 5 / 10 / 18 / 0 / 8 / 13 / 8 | 6 / 8 / 19 / 12 / 14 / 21 / 12 |
| G4 | G3 with the Carver at 9:00 and the Anointing at 9:30 | 0 / 0 / 26 / 0 / 5 / 23 / 24 | 6 / 8 / 11 / 0 / 12 / 12 / 15 | 5 / 8 / 17 / 12 / 13 / 14 / 14 |
| G5 | M with only the Carver and the Anointing (at 10:00) ahead of the three train steps | 0 / 0 / 13 / 1 / 0 / 22 / 17 | 5 / 10 / 9 / 4 / 9 / 21 / 8 | 6 / 12 / 7 / 15 / 20 / 22 / 12 |

The full rows (seeds 1-8: 64 matches). G5 was the first round's one candidate (the only screened variant whose Normal
Breakers stayed above 0). Q1-Q3 are QA's orders, measured in QA's own loop
(`QA/Content/D34OrderQaStressTests.QaOrders_Row_Report`, 2026-11-18): Q1 is the order the first round named as not
tried (the Carver and the Anointing, at 10:00, just after the three train steps), Q2 is G5 with the Breaker's train step
kept ahead of the Carver and the Anointing, and Q3 is Q1 with the Anointing kept at 11:30. Q2's rows were measured again
in this report's loop in the fix round, to the match the same. M's rows are QA's D33 rows to the match.

| variant | difficulty | defeated | 2nd Longhouse | Shaman | Breaker | Rider | War-dog | Anointing |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| M | Easy | 1 | 0 | 34 | 27 | 36 | 51 | 1 |
| G5 | Easy | 1 | 0 | 25 | 2 | 2 | 44 | 32 |
| Q1 | Easy | 1 | 0 | 17 | 29 | 28 | 47 | 18 |
| **Q2** | Easy | 1 | 0 | 16 | 30 | 4 | 27 | **28** |
| Q3 | Easy | 1 | 0 | 25 | 28 | 29 | 49 | 17 |
| M | Normal | 10 | 23 | 25 | 12 | 22 | 39 | 11 |
| G5 | Normal | 9 | 23 | 15 | 7 | 20 | 40 | 19 |
| Q1 | Normal | 9 | 23 | 22 | 13 | 25 | 38 | 12 |
| **Q2** | Normal | 9 | 23 | 15 | 14 | 22 | 37 | 17 |
| Q3 | Normal | 9 | 23 | 26 | 13 | 19 | 38 | 12 |
| M | Hard | 11 | 23 | 25 | 35 | 43 | 48 | 18 |
| G5 | Hard | 11 | 23 | 21 | 27 | 38 | 46 | 30 |
| Q1 | Hard | 11 | 23 | 23 | 39 | 42 | 48 | 25 |
| **Q2** | Hard | 11 | 23 | 22 | 39 | 33 | 46 | 23 |
| Q3 | Hard | 11 | 18 | 23 | 38 | 42 | 47 | 24 |

Q2's rows in this report's loop add the Bloodwood Carver / the Shaman's Circle finished and the Javelin Throwers
fielded: Easy 55 / 44 and 64, Normal 39 / 39 and 55, Hard 47 / 47 and 64.

**Q2 ships (D34 fix round 1, BUG-1641).** It is the only order measured that meets every figure of the bar: Easy's
Anointing 28 of 64 (was 1); on Normal Breakers 14, Destrier Riders 22, War-dogs 37, a Shaman 15 (the bar 14), a second
Longhouse 23 and 9 defeats; on Hard Breakers 39, Destrier Riders 33, War-dogs 46. G5 misses on Normal's Breakers (7), Q1
and Q3 on Easy's Anointing (18, 17). The first round's report took the fallback ("none ships, BUG-1612 waits for Request
55") on G1-G5; QA's Q2 showed an order alone does it. What Q2 costs, which the bar does not cover: on Easy Destrier
Riders 36 -> 4, War-dogs 51 -> 27 and Shamans 34 -> 16; on Normal Shamans 25 -> 15 and War-dogs 39 -> 37; on Hard
Destrier Riders 43 -> 33 and Shamans 25 -> 22. What it wins besides Easy's Anointing: the Anointing on Normal 11 -> 17 and
on Hard 18 -> 23, Breakers on every difficulty (Easy 27 -> 30, Normal 12 -> 14, Hard 35 -> 39), one defeat fewer on Normal.

**Why the order matters** (the measured rows, read against the executor's rule in docs/02 "AI opponent"; not traced
think by think). On Easy the Bloodwood Carver stands in 55 of 64 matches with M, yet the Anointing is researched in 1.
Due at 11:30 (12:00 with Easy's 30 s slack) and last but two in D33's file, it is paid only from what the steps ahead of
it (the three train steps, the Breakers Hall, the Stables, the Carver, the Circle) and the army mix leave, and the
reserve, which the mix and every later train step spend above, goes to the first due step in file order that only its
cost holds back, so rarely to it. Move the Anointing ahead of all three train steps (G1-G5) and, once the Carver stands,
it is that step: the train steps behind it then spend only above its 200 gold and 150 wood, and the Breaker (180 / 120,
the costliest unit) is the one that goes (G5's full row 27 -> 2 of 64 on Easy and 12 -> 7 on Normal). Q2 keeps the
Breaker's train step ahead of the Anointing, so the Breaker spends first, and the reserve falls on the cheaper Rider and
War-dog steps behind it: that is where Q2's costs are (Easy's Riders and War-dogs, Hard's Riders). Placed after all three
train steps (Q1, Q3), the Anointing waits behind them and Easy gets it back in only 17-18 matches. The trade is still the
reserve's (one step's cost held back for every train step after it), which Request 55 asks the sim to change.

**The step order is not in time order (BUG-1421 item 5, wontfix).** The Carver and the Anointing (10:00) sit before the
Circle (9:00), and they and the three 10:00 train steps before the 9:00 Breakers Hall and the 8:00 expansion. That is on purpose: file order is
priority (steps run side by side, earlier steps spend first and the first one held back by its cost takes the reserve),
and D33 and D34 measured that this order is what fields the Age II army. BUG-1050's pull-forward (the first time step not
yet due, in file order) acts only while the AI spends down at High starting resources; it then follows the same
priority, which is the intent. Sorting the file by time would change what the AI buys first.

## Questions for the Producer (D33, 2026-11-17)

Raised by D33 (BUG-1420), each with the developer's recommendation.

1. **Easy loses the Anointing.** With F3 the Easy AI researches Blood-oil Anointing in 1 of 64 matches (was 35) and
   fields a Shaman in 34 (was 51), while it now fields Breakers, Destrier Riders and War-dogs (27 / 36 / 51, all were 0).
   The brief's criteria are Normal and Hard; Easy plays the same opening. Recommendation: accept for now (an Easy AI with
   the whole roster and no upgrade is still an Easy AI); a per-difficulty opening is a schema question for the sim track.
2. **The opening's description is 195 characters.** The Owner review rows keep to 160; an opening may have 200 (D21's
   rule), and naming the five Age II kinds does not fit in 160. Recommendation: accept, or shorten to "... then Javelin
   Throwers, Shamans, Breakers, riders and dogs join; a second Longhouse goes up." (168) if the owner prefers.
3. **The reserve holds back every unit step after a waiting expansion.** D33 cures it in data by order (the three train
   steps and the Breakers Hall ahead of the expand step). Recommendation: a sim Request for the balance pass, so the
   order of the file need not carry it: an expansion that has waited `N` seconds for its cost stops holding the reserve
   (or a train step may name itself exempt).

## Questions for the Producer (D34, 2026-11-18)

Raised by D34 (BUG-1612, BUG-1613, BUG-1421, BUG-1641), each with the developer's recommendation. In the fix round
(BUG-1641) D34 shipped QA's order Q2 with the softened description: data hash 318E8C4256456C05 -> A732F387C2BB7E4D, so
saves and recordings made before it are refused.

1. **The tooltip, softened (BUG-1613 item 2, BUG-1421 item 2).** D33's `standard` description said "then Javelin
   Throwers, Shamans, Breakers, Destrier Riders and War-dogs join them and a second Longhouse goes up". On Q2's Normal row
   (64 matches, alive at some second before 15:00) Javelin Throwers are fielded in 55 and War-dogs in 37, but a Shaman in
   15, Breakers in 14, Destrier Riders in 22 and a second Longhouse finished in 23. D34 ships "War Lodge and Javelin Ground
   first, then Age II; four Warriors raid at 8 army pop, then Javelin Throwers and War-dogs join them; Shamans, Breakers,
   Destrier Riders and a second Longhouse may follow." (198 characters, D21's limit 200), which names as joining only the
   kinds most Normal matches field. Recommendation: accept; the owner may prefer the plan-like wording back (BUG-1421).
2. **BUG-1612: Q2 ships, at a cost the bar does not cover.** Q2 brings Easy's Anointing back to 28 of 64 (was 1) and
   meets every figure of the bar, Normal's Shamans by one (15, the bar 14). It costs Easy's Destrier Riders (36 -> 4) and
   War-dogs (51 -> 27) and Shamans (Easy 34 -> 16, Normal 25 -> 15), and Hard's Destrier Riders (43 -> 33).
   Recommendation: accept (the bar is met, and Request 55 can win the units back), or say "keep D33's order" and the
   file goes back to `Fixtures/teblor_standard_d33.json` at the next hash move.
3. **The expansion's reserve and the Shamans (BUG-1421 item 1) still wait for Request 55.** The variants that move the
   Shaman step ahead of the train steps (G1-G3) raise Normal's Shamans (13 -> 18-19 of 32 on the screen) but take the
   Breakers to 0, and Q2 itself lowers the Shamans: the same reserve trade as item 2.
