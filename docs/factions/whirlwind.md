# The Whirlwind (Seven Cities / Army of the Apocalypse)

**Faction id:** `whirlwind` · **Milestone:** vertical slice (M3-M6) · **Role:** swarm

## Lore hook

In the deserts of Seven Cities, a prophesied uprising against the Malazan occupiers rises around
the goddess of the Whirlwind, whose storm wraps the holy desert of Raraku. Tribes, bandits,
priests, and fanatics gather into the Army of the Apocalypse: huge, fast, poorly armored, and
burning with faith. The vertical slice pits them against the Malazans, the clash at the heart of
the uprising.

## Playstyle

Numbers and speed. Cheap, slightly fragile units that arrive early and keep coming. Raids worker
lines with Horse Raiders, overwhelms with Raiders and Zealots, and blinds the enemy with
Sandstorms so they can't focus fire. No ranged siege: buildings fall to Battering Rams under the
cover of the swarm.

## Faction bonus: Army of the Apocalypse

- All units **cost 20% less** (Gold and Wood).
- All units have **10% less HP** than their template equivalent.
- Workers **gather 15% faster**.

Cost and HP changes are already applied in the table below. The gather bonus is a faction
modifier in `faction.json`.

## Faction upgrade: Dryjhna's Prophecy

Age II, at the Smithy, 200 G / 150 W, 45 s. Zealots **+20 HP**; Sandstorm **cooldown 45 → 30 s**.

## Units

| Slot | Unit | HP | Armor | Class | Attack | Type | CD | Range | Targets | Speed | Sight | Cost (G/W) | Pop | Train | Trained at |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Worker | Camp Follower | 36 | 0 | Light | 4 | Melee | 1.5 | melee | all | 4.0 | 14 | 40 / 0 | 1 | 12 | Holy Camp |
| Line | Raider | 108 | 1 | Heavy | 11 | Melee | 1.5 | melee | all | 3.4 | 14 | 48 / 16 | 1 | 18 | Raider Camp |
| Ranged | Desert Archer | 50 | 0 | Light | 7 | Pierce | 1.8 | 14 | all | 3.6 | 18 | 32 / 36 | 1 | 20 | Archer Camp |
| Shock | Horse Raider | 135 | 0 | Mounted | 11 | Melee | 1.6 | melee | all | 6.6 | 18 | 72 / 24 | 2 | 26 | Horse Lines |
| Caster | Priest of the Whirlwind | 54 | 0 | Light | 9 | Magic | 2.2 | 12 | all | 3.2 | 16 | 80 / 40 | 2 | 30 | Shrine of the Whirlwind |
| Siege | Battering Ram | 240 | 6 | Heavy | 60 | Siege | 3.0 | melee | buildings | 2.4 | 10 | 160 / 120 | 3 | 36 | Ram Yard |
| Unique | Zealot | 63 | 0 | Light | 9 | Melee | 1.0 | melee | all | 4.4 | 14 | 30 / 10 | 1 | 10 | Raider Camp |

### Unit notes

- **Raider:** ×1.5 vs Mounted (×1.1 from D13 until D19, 2026-10-15), ×1.2 vs Heavy (flails wrap around
  shields). Lighter armor and a bit faster than template line infantry.
- **Desert Archer:** fires faster but hits softer than the template. Fast on foot.
- **Horse Raider:** ×1.5 vs Light. The fastest template unit in the vertical slice; built to raid
  worker lines and run.
- **Priest of the Whirlwind:** splash 1.5 m, detector 10 m. Signature ability **Sandstorm**.
- **Battering Ram:** attacks buildings only. Very high armor shrugs off arrows (pierce does
  1 damage); melee units kill it.
- **Zealot (unique, Age II):** cheap, fast, and frenzied. Passive **Frenzy of the Apocalypse**:
  below 50% HP gains Frenzied (+50% attack speed). Passive **Martyrdom**: when a Zealot dies,
  Zealots within 6 m gain Frenzied (+50% attack speed) for 5 s.

`Content/PassiveContentTests` pins the two Zealot passives above to the Zealot's `passives` in `units.json`, both ways.

`Content/UnitContentTests` pins each note's "detector N m" to the unit's `detector` in `units.json`, both ways.

## Abilities

| Ability | Unit | Kind | Range | Radius | Cast | Cooldown | Duration | Effect |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Sandstorm | Priest of the Whirlwind | Target ground (zone) | 18 | 6 m | 1.2 s | 45 s | 12 s | Enemy units inside are Blinded and Slowed 30%. Enemies outside can't see into the storm. Your own units are unaffected. No effect on buildings |

Sandstorm is the signature: drop it on the enemy army as the swarm engages, so enemy ranged units
can't shoot beyond 3 m and the army can't retreat quickly.

## Buildings

| Slot | Name | Id | HP | Armor | Cost (G/W) | Build (s) | Footprint | Sight | Provides | Requires |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Town Hall | Holy Camp | `whirlwind_holy_camp` | 2400 | 2 | 275 / 275 | 90 | 4×4 | 12 | +10 pop, drop-off, trains Camp Follower, researches Age II | — |
| House | Tent | `whirlwind_tent` | 500 | 1 | 0 / 50 | 20 | 2×2 | 12 | +8 pop | — |
| Camp | Supply Cache | `whirlwind_supply_cache` | 600 | 1 | 0 / 75 | 25 | 2×2 | 12 | Drop-off | — |
| Infantry Hall | Raider Camp | `whirlwind_raider_camp` | 1200 | 2 | 0 / 150 | 40 | 3×3 | 12 | Trains Raider, Zealot | — |
| Ranged Hall | Archer Camp | `whirlwind_archer_camp` | 1200 | 2 | 0 / 150 | 40 | 3×3 | 12 | Trains Desert Archer | — |
| Shock Hall | Horse Lines | `whirlwind_horse_lines` | 1200 | 2 | 75 / 150 | 45 | 3×3 | 12 | Trains Horse Raider | Raider Camp |
| Forge | Smithy | `whirlwind_smithy` | 1000 | 2 | 100 / 100 | 40 | 3×3 | 12 | Upgrades, Dryjhna's Prophecy | — |
| Caster Hall | Shrine of the Whirlwind | `whirlwind_shrine` | 1200 | 2 | 150 / 150 | 50 | 3×3 | 12 | Trains Priest of the Whirlwind | Age II |
| Siege Works | Ram Yard | `whirlwind_ram_yard` | 1400 | 2 | 150 / 200 | 55 | 3×3 | 12 | Trains Battering Ram | Age II |
| Watch Tower | Lookout Tower | `whirlwind_lookout_tower` | 800 | 2 | 50 / 125 | 35 | 2×2 | 24 | Attack 10 pierce / 2 s, range 18; sight 24; detector 16 m | Age II |

Building stats follow the template in [02-game-design.md](../02-game-design.md#buildings). Every building's armor was halved, rounded down, in D19 (2026-10-15, the owner's building damage set (C)): Town Hall and Watch Tower 5 → 2, House and Camp 3 → 1, the rest 4 → 2.

`Content/BuildingContentTests` and `UnitContentTests` pin this page to the data.

## Techs

| Id | Name | Researched at | Cost (G/W) | Time (s) | Requires | Effects |
| --- | --- | --- | --- | --- | --- | --- |
| `dryjhnas_prophecy` | Dryjhna's Prophecy | Smithy | 200 / 150 | 45 | Age II | Zealot +20 HP; Priest of the Whirlwind Sandstorm cooldown 45 → 30 s |

The faction upgrade is the only faction-specific tech. Age II and the six shared Forge upgrades are in
[02-game-design.md "Forge upgrades"](../02-game-design.md#forge-upgrades). `Content/TechContentTests` pins this
table and the "Faction upgrade" line above to the data.

## Strengths and weaknesses

- **Strong:** early pressure, map-wide speed, cheap reinforcements, Sandstorm neutralizes ranged
  armies and casters.
- **Weak:** splash damage (Telas Fire, Catapults, Sappers) shreds clumped low-HP units. No ranged
  siege, so a well-defended choke with towers is hard to crack. Loses long, even trades.

## AI build order (Normal)

The AI opponent's `game/data/factions/whirlwind/ai.json` (docs/03 "For the data track (M5-1)" and "Implementation
(M5-8a)") holds three **openings**, read the same way as malazan.md "AI build order (Normal)" (steps run side by side, one
command per step per think, in this order; **Count** is the `train` commands the step queues over its life). **An Easy
AI always plays opening 1, `standard`**; a Normal or Hard AI picks one of the three from the match seed at the start
(docs/02 "AI opponent"). The name and description below are what the player may read. The Whirlwind keep fewer workers
(18 in Age I, 26 in Age II) than the Malazan, spending on the army first. The AI starts with 5 Camp Followers, 3 on gold
and 2 on wood. `Content/AiContentTests` pins the openings table, every opening's step table, composition table and wave
line, and the retreat line below to the file, both ways; `Content/AiOpeningPairsReportTests` plays the 9 pairings
against the Malazan openings (malazan.md; "the menu's map" on this page is, as there, the setup's default Generated
map), and with `QA/Content/D21OpeningsQaTests` (each pairing's 2nd and 3rd seeds)
pins every side's Age II to "Age II against the Malazans" at the end of this page.

| # | Id | Name (shown to the player) | Description (shown to the player) |
| --- | --- | --- | --- |
| 1 | `standard` | Swarm | Raider, Archer and Horse camps first and a raid at 14 army pop; Zealots, Priests and Battering Rams join in Age II. |
| 2 | `raider_rush` | Raider rush | Two Raider Camps and Horse Lines, no archers: Raiders and Horse Raiders strike at 10 army pop and keep coming. |
| 3 | `holy_war` | Holy war | An early Age II, the Shrine at once and Dryjhna's Prophecy for Zealots and Priests. No Horse Lines; the first wave goes at 14 army pop. |

Not in the file, because the schema has no field for it (requests to the sim track): the sketch's first raid at ~4:30
(6 Raiders + 2 Horse Raiders against the workers; the standard opening's first wave goes at 14 army pop, below) and the
3 : 2 gold / wood split (the economy manager's `goldShare`, a common rule). The sketch's expansion at ~9:00 is now the
step at 8:00, Normal's `expandAfter`.

**The reserve (BUG-0981, fixed by the sim's M5-4g; re-printed 2026-10-26).** Until M5-4g the AI saved nothing for a
due step, so production spent first and Age II came in minutes after its step was due (791-1,098 s on the menu's map
against an idle seat, never on Easy). Now a due step's cost is kept in reserve while the army is at least the opening's
wave and the base is not being defended (malazan.md "The reserve", docs/02 "AI opponent"): against an idle seat every
opening's Age II is in at 561-616 s, Easy's at 599 s (each opening's spending note). Against the Malazans it comes at
7:35-17:39 (the table at the end of this page); 2 of the 27 Whirlwind sides never get it (`standard` seeds 9 and 13,
both standing at 20:00, knowing no gold node, so their Age II is not reservable: docs/03 "Implementation (M5-4g)"). No
Whirlwind side was defeated in the 27 runs. The residue on the Malazan side is BUG-1010 (malazan.md "Fast Age II
against the raider rush").

- **Retreat:** a wave that has lost 50% falls back; a unit under 30% hp counts as wounded (`retreat`, shared by the
  openings; Hard pulls it back).
- **Later waves** (every opening): one wave at a time; a new one starts when none is out, at least `ai.waveCooldown`
  after the last began, at the opening's threshold, and never while the base is defended. Priests cast Sandstorm on
  the enemy army's center as the melee engages.
- **High starting resources** (docs/02 "Game flow" 2, docs/03 "Implementation (M6-S6)"): the AI plays the same openings
  as at Standard, but while it holds more gold + wood than the preset's spend-down threshold (`rules.json`
  `startingResources` `high` `aiSpendAbove`) it brings its next timed step forward, trains at every producer and leans
  its gatherers toward the kind it holds less of; Standard has no threshold, so it plays exactly as before. Measured (QA,
  M6-S6, 2026-11-05; not pinned): `holy_war` held 1,025-1,225 gold + wood at 5:00 at High before the rule (3 seeds) and
  85-545 with it (6 seeds nobody tuned on).
- **Defense** (docs/02 "AI opponent", docs/03 "Implementation (M5-4b)" and "(M5-4f)"; re-worded in D20, 2026-10-17, for
  M5-4f's sized answer): a threat starts when an own building (a site too) within the defend radius of the Holy Camp is
  hit, or an enemy soldier (not a worker) is seen within it (`rules.json` `ai.defendRadius` 48 m; the threat holds
  `ai.defendFor` 20 s after the last sign). The AI answers with defenders worth `ai.defendRatio` 1.5 times the
  intruders' gold + wood cost, at least `ai.defendMinValue` 150 (a building hit with nobody seen gets 150), taken from
  the army at home first (nearest the defense point first), then from the members of a wave out (nearest the Holy Camp
  first), each only while the members left keep the wave's start threshold (`attack.minArmyPop` and `minArmyCost` times
  the difficulty's wave size ×, so a wave sent above it gives its surplus). One Heavy Infantry (74 gold + wood) is answered with 150 worth, e.g. two Raiders from home (128), and a wave out keeps
  going. A raid worth more than the home army and the wave's spare units together calls the whole wave home (it ends
  like a retreat) and every free army unit answers. A hit that leaves the Holy Camp below `ai.recallHallHp` 0.5 of its
  max hp does the same, however small the raid. Camp Followers within `ai.workerFleeRadius` (16 m) of the threat flee to the nearest finished drop-off away from the
  threat (the Holy Camp or a Supply Cache, whichever is nearest among those more than 16 m from the threat; else the
  nearest one).
- **Later idea (not in M5):** counter-attack the enemy base with Horse Raiders when its army is away. Not built: no
  AI manager raids today.

#### How the AI adapts

Three rules sit on top of every opening above, the counter shift, the starved charge and the far expansion, and two
notes go with the first: what answers what, and where the shift doesn't show. Read them as in malazan.md "How the AI
adapts" (docs/02 "AI opponent"; docs/03 "Implementation (M5-9a)" and "(M5-9b)"). `Content/AiAdaptsContentTests` pins
every number and name in the five bullets to `ai.json`, `rules.json` and `buildings.json`, both ways.

- **Counter shift:** the AI remembers how many enemy soldiers of each slot it has seen at once (workers never count, and
  it sees only what its own fog of war shows), and that memory fades to nothing over `rules.json` `ai.counterMemory`
  180 s unless a sighting at least as big renews it. When it picks its next unit, each unit of the opening's army mix
  weighs more by the share of that memory its slot answers, times the difficulty's `counterStrength` in `ai.json`: Easy
  0 (an Easy AI never adapts), Normal 0.5, Hard 1.0. A Whirlwind AI that has seen only cavalry weighs its Raiders ×1.5
  on Normal and ×2 on Hard. The shift tilts the army, it doesn't replace it: no unit is weighted below the opening's
  mix, and a unit the mix doesn't list is never added.
- **What answers what** (`rules.json` `ai.counters`, the "Countered by" column of docs/02 "Faction template", in this
  faction's units): enemy Line → Priest of the Whirlwind, Battering Ram; enemy Ranged → Horse Raider; enemy Shock →
  Raider; enemy Caster → Horse Raider, Desert Archer; enemy Siege → Raider, Horse Raider; enemy Unique → none.
- **Starved charge:** when the AI cannot pay for the unit it would train next and no income can close the gap, and it
  has at least `ai.starvedMinArmy` 1 combat unit free, then after `ai.starvedWaveAfter` 60 s in that state it sends
  every free combat unit at you whatever its wave threshold (or hunts, if it knows none of your buildings), again after
  each wave's cooldown, at every difficulty. A raid on its base resets the clock.
- **Far expansion:** when its own gold is spent (short of gold for its next unit, no Camp Follower on gold and no gold
  mine it knows), it sends a Camp Follower to look at the start locations nobody took. Once it has seen a free mine (at
  one of those starts or anywhere else) within `ai.farExpandReach` 384 m of its Holy Camp, with no enemy drop-off by it,
  it builds a Supply Cache (`whirlwind_supply_cache`, 0 gold / 75 wood) beside it and moves gatherers there; a Holy Camp
  only if it can pay for one. One at a time. The reach holds for every mine, a start location's included: a free start
  farther than that from the camp is not walked to, and a mine beyond it is never taken.
- **Where the shift doesn't show (BUG-1156):** a Raider Camp that already takes a Raider every time its queue empties
  cannot train more of them for the heavier weight (measured on a Hard Malazan seat's Legion Barracks), so against
  cavalry the shift shows only when the bank, not the camp, is the limit. An enemy Line army seen in Age I shifts
  nothing, since no opening's Age I mix lists a Priest of the Whirlwind or a Battering Ram.

### Opening 1: `standard` (Swarm)

The order the AI has played since D14, step for step, and its wave; Easy plays only this one. D21 (2026-10-18) gave it
its name and description.

| # | At | Do | Id | Count | Why |
| --- | --- | --- | --- | --- | --- |
| 1 | time 0 | train | `whirlwind_camp_follower` | 13 | Workers to 18 in Age I (5 at the start + 13) |
| 2 | pop 6 | build | `whirlwind_tent` | 1 | First Tent |
| 3 | pop 8 | build | `whirlwind_raider_camp` | 1 | Raider Camp |
| 4 | pop 9 | build | `whirlwind_supply_cache` | 1 | Supply Cache by the nearest known forest |
| 5 | pop 11 | build | `whirlwind_tent` | 1 | Second Tent |
| 6 | pop 12 | build | `whirlwind_archer_camp` | 1 | Archer Camp |
| 7 | pop 14 | build | `whirlwind_horse_lines` | 1 | Horse Lines |
| 8 | pop 16 | build | `whirlwind_tent` | 1 | From pop 16 a Tent every 7 pop |
| 9 | pop 20 | build | `whirlwind_smithy` | 1 | The Forge, after the first raiding army |
| 10 | pop 20 | research | `melee_weapons_1` | 1 | Its first upgrade, Melee Weapons: most of the army is melee (refused, and retried, until the Smithy stands) |
| 11 | pop 23 | build | `whirlwind_tent` | 1 | |
| 12 | pop 30 | build | `whirlwind_tent` | 1 | |
| 13 | pop 37 | build | `whirlwind_tent` | 1 | |
| 14 | pop 44 | build | `whirlwind_tent` | 1 | |
| 15 | pop 51 | build | `whirlwind_tent` | 1 | |
| 16 | pop 58 | build | `whirlwind_tent` | 1 | |
| 17 | pop 65 | build | `whirlwind_tent` | 1 | |
| 18 | pop 72 | build | `whirlwind_tent` | 1 | |
| 19 | pop 79 | build | `whirlwind_tent` | 1 | The 12th Tent: Holy Camp 10 + 12 × 8 = 106, past the 100 cap, kept on the measure below ("Houses at the cap") |
| 20 | time 450 | research | `age_ii` | 1 | Age II at 7:30, later than the Malazan (needs two of the camps, Horse Lines, Smithy) |
| 21 | time 450 | train | `whirlwind_camp_follower` | 8 | Workers to 26 in Age II |
| 22 | time 480 | expand | — | 1 | 2nd Holy Camp near the nearest free gold mine, at Normal's `expandAfter` (480 s), regardless of threat |
| 23 | time 510 | build | `whirlwind_shrine` | 1 | 8:30, when Age II (60 s) is in |
| 24 | time 540 | build | `whirlwind_raider_camp` | 1 | A second Raider Camp: Zealots from 2 Raider Camps |
| 25 | time 570 | build | `whirlwind_ram_yard` | 1 | Battering Rams |
| 26 | time 600 | research | `dryjhnas_prophecy` | 1 | The faction upgrade, at the Smithy |

**Houses at the cap (D21, 2026-10-18, D15 C's parked finding measured; `QA/Content/AiHouseRuleReportTests.MenuMapNormal_HouseSources_Report` and `MenuMapNormal_TwelfthHouse_Report`).**
Since M5-4f the executor skips a house step once the population cap is the hard cap (100): the step is done with no
command sent and no Tent placed. On the menu's map (20 minutes, the other seat idle): Normal, seed 1: the cap reaches 100 at 865 s; the last Tent step (step 19) is done at a cap of 100; skipped at the hard cap: steps 18, 19; 10 Tents stand at 20:00, 0 past the cap. Re-printed 2026-11-26 for the sim's M6-S14: the cap reached 100 at 892 s, the rest as now. The 12th Tent can leave 6 of its 8 pop unused, so D21 measured taking it out: without the 12th Tent step (step 19) on the same seed, the cap reaches 100 at 865 s, and 10 Tents stand at 20:00, 0 past the cap (re-printed 2026-11-26 for the sim's M6-S14: 892 s with and without the step; 2026-11-16 for the sim's M6-S12: 872 s with and without the step).
Re-printed 2026-10-26 for M5-4g (the reserve): three Tents are still sites at 20:00, so the cap is 92 with the 12th Tent step and 84 without it, where
the population sits from 17:00. Before M5-4g the cap reached 100 at 683 s with the step and 827 s without it (12 Tents
both times, the house rule placing the 12th anyway). The step stays in every opening.

**Spending on the menu's map (D21, 2026-10-18, gathering gold 0.85 / wood 0.75 a second; `Content/AiSpendingReportTests.GeneratedNormal_Spending_Report`).**
Against an idle seat, 12 forests and 8 mines (the menu's), 20 minutes. Normal, seed 1 (the smallest seed in 1-50 whose Normal pick is this opening): the idle base falls at 496 s; in hand at 5 min 356 gold / 137 wood; the first wave at 375 s; Age II in at 568 s (re-printed 2026-11-26 for the sim's M6-S14; was: the idle base falls at 502 s; in hand at 5 min 346 gold / 127 wood; the first wave at 380 s; Age II in at 568 s) (re-printed 2026-11-16 for the sim's M6-S12; was: the idle base falls at 501 s; in hand at 5 min 356 gold / 117 wood; the first wave at 380 s; Age II in at 563 s). Easy, seed 1 (Easy always plays the first opening): the idle base falls at 519 s; in hand at 5 min 508 gold / 29 wood; the first wave at 372 s; Age II in at 635 s (re-printed 2026-11-26 for the sim's M6-S14; was: the idle base falls at 521 s; in hand at 5 min 508 gold / 29 wood; the first wave at 372 s; Age II in at 635 s) (re-printed 2026-11-16 for the sim's M6-S12; was: the idle base falls at 525 s; in hand at 5 min 508 gold / 39 wood; the first wave at 362 s; Age II in at 623 s). Re-printed 2026-10-26 for M5-4g (the reserve,
above): before it Age II came in at 1098 s on Normal and never in 20 minutes on Easy, and the idle base fell at 495 s
and 518 s; the 5-minute banks and the first waves did not move. D20's two-seed note (495 s / 518 s, seeds 1 and 7)
measured both seeds; the runs are now one per opening.

**Army composition (`compositions`), weights by pop.** All three Age I units can be trained in Age I; their weights sum
to 65.

| Unit | Age I | Age II |
| --- | --- | --- |
| `whirlwind_raider` | 30 | 30 |
| `whirlwind_desert_archer` | 20 | 20 |
| `whirlwind_horse_raider` | 15 | 15 |
| `whirlwind_zealot` | — | 20 |
| `whirlwind_priest` | — | 10 |
| `whirlwind_battering_ram` | — | 5 |

- **Composition target (by pop):** 30% Raider, 20% Desert Archer, 15% Horse Raider, 20% Zealot,
  10% Priest of the Whirlwind, 5% Battering Ram.
- **Attack waves:** first at 14 army pop and 861 gold + wood (`attack`; the cost of 14 pop of the Age I mix, rounded
  down).

### Opening 2: `raider_rush` (Raider rush)

Two Raider Camps by pop 10 and Horse Lines at pop 13, no Archer Camp: an Age I mix of Raiders and Horse Raiders (45 :
25) that strikes at 10 army pop and, with a wave at most `ai.waveCooldown` apart, keeps the enemy base defending. The
Smithy waits for pop 24 and Age II for 8:00.

| # | At | Do | Id | Count | Why |
| --- | --- | --- | --- | --- | --- |
| 1 | time 0 | train | `whirlwind_camp_follower` | 13 | Workers to 18 in Age I (5 at the start + 13) |
| 2 | pop 6 | build | `whirlwind_tent` | 1 | First Tent |
| 3 | pop 7 | build | `whirlwind_raider_camp` | 1 | The first Raider Camp |
| 4 | pop 9 | build | `whirlwind_supply_cache` | 1 | Supply Cache by the nearest known forest |
| 5 | pop 10 | build | `whirlwind_raider_camp` | 1 | The second Raider Camp |
| 6 | pop 11 | build | `whirlwind_tent` | 1 | Second Tent |
| 7 | pop 13 | build | `whirlwind_horse_lines` | 1 | Horse Lines (needs the Raider Camp) |
| 8 | pop 16 | build | `whirlwind_tent` | 1 | From pop 16 a Tent every 7 pop |
| 9 | pop 23 | build | `whirlwind_tent` | 1 | |
| 10 | pop 24 | build | `whirlwind_smithy` | 1 | The Forge, once the rush is out |
| 11 | pop 24 | research | `melee_weapons_1` | 1 | Melee Weapons: the whole army is melee (refused, and retried, until the Smithy stands) |
| 12 | pop 30 | build | `whirlwind_tent` | 1 | |
| 13 | pop 37 | build | `whirlwind_tent` | 1 | |
| 14 | pop 44 | build | `whirlwind_tent` | 1 | |
| 15 | pop 51 | build | `whirlwind_tent` | 1 | |
| 16 | pop 58 | build | `whirlwind_tent` | 1 | |
| 17 | pop 65 | build | `whirlwind_tent` | 1 | |
| 18 | pop 72 | build | `whirlwind_tent` | 1 | |
| 19 | pop 79 | build | `whirlwind_tent` | 1 | The 12th Tent, as in the standard opening |
| 20 | time 480 | research | `age_ii` | 1 | Age II at 8:00, the latest (needs two kinds: the Raider Camps and the Horse Lines) |
| 21 | time 480 | train | `whirlwind_camp_follower` | 8 | Workers to 26 in Age II |
| 22 | time 480 | expand | — | 1 | 2nd Holy Camp near the nearest free gold mine, at Normal's `expandAfter` (480 s) |
| 23 | time 540 | build | `whirlwind_shrine` | 1 | Priests, once Age II (60 s) is in |
| 24 | time 570 | build | `whirlwind_ram_yard` | 1 | Battering Rams |
| 25 | time 600 | research | `dryjhnas_prophecy` | 1 | The faction upgrade, at the Smithy |

**Houses at the cap (D21, 2026-10-18; `QA/Content/AiHouseRuleReportTests.MenuMapNormal_HouseSources_Report`).** On the
menu's map (20 minutes, the other seat idle): Normal, seed 2: the cap reaches 100 at 723 s; the last Tent step (step 19) is done at a cap of 100; skipped at the hard cap: steps 18, 19; 10 Tents stand at 20:00, 0 past the cap (the expansion's Holy
Camp stands on this seed). Re-printed 2026-11-16 for the sim's M6-S12: 827 s. Re-printed 2026-10-26 for M5-4g: before it the cap reached 100 at 928 s and step 19 was done
at a cap of 90.

**Spending on the menu's map (D21, 2026-10-18, gathering gold 0.85 / wood 0.75 a second; `Content/AiSpendingReportTests.GeneratedNormal_Spending_Report`).**
Against an idle seat, the menu's map, 20 minutes. Normal, seed 2 (the smallest seed in 1-50 whose Normal pick is this opening): the idle base falls at 412 s; in hand at 5 min 187 gold / 9 wood; the first wave at 288 s; Age II in at 661 s (re-printed 2026-11-26 for the sim's M6-S14; was: the idle base falls at 409 s; in hand at 5 min 187 gold / 9 wood; the first wave at 288 s; Age II in at 674 s) (re-printed 2026-11-16 for the sim's M6-S12; was: the idle base falls at 416 s; in hand at 5 min 162 gold / 99 wood; the first wave at 288 s; Age II in at 682 s).
The quickest wave and the quickest win of the six openings. Re-printed 2026-10-26 for M5-4g: before it the idle base
fell at 416 s, the 5-minute bank was 36 gold / 37 wood and Age II came in at 957 s.

**Army composition (`compositions`), weights by pop.** Age I is the two units the Age I steps train, so its weights sum
to 70.

| Unit | Age I | Age II |
| --- | --- | --- |
| `whirlwind_raider` | 45 | 45 |
| `whirlwind_horse_raider` | 25 | 25 |
| `whirlwind_zealot` | — | 20 |
| `whirlwind_priest` | — | 5 |
| `whirlwind_battering_ram` | — | 5 |

- **Composition target (by pop):** 45% Raider, 25% Horse Raider, 20% Zealot, 5% Priest of the Whirlwind,
  5% Battering Ram.
- **Attack waves:** first at 10 army pop and 582 gold + wood (`attack`; the cost of 10 pop of the Age I mix, rounded
  down: the lowest threshold of either faction).

### Opening 3: `holy_war` (Holy war)

The faith first: Raider and Archer Camps only (no Horse Lines), the Smithy at pop 18, Age II at 6:30, the Shrine as
soon as it can stand and Dryjhna's Prophecy (Zealots +20 HP) at 8:00, then a second Raider Camp for Zealots. The first
wave goes at 14 army pop, as the standard opening's (20 until D33, 2026-11-17: at 20 the army never got there on one
measured seed, BUG-1553; "The holy war's first wave" at the end of this page).

| # | At | Do | Id | Count | Why |
| --- | --- | --- | --- | --- | --- |
| 1 | time 0 | train | `whirlwind_camp_follower` | 13 | Workers to 18 in Age I (5 at the start + 13) |
| 2 | pop 6 | build | `whirlwind_tent` | 1 | First Tent |
| 3 | pop 8 | build | `whirlwind_raider_camp` | 1 | Raider Camp |
| 4 | pop 9 | build | `whirlwind_supply_cache` | 1 | Supply Cache by the nearest known forest |
| 5 | pop 11 | build | `whirlwind_tent` | 1 | Second Tent |
| 6 | pop 12 | build | `whirlwind_archer_camp` | 1 | Archer Camp |
| 7 | pop 16 | build | `whirlwind_tent` | 1 | From pop 16 a Tent every 7 pop |
| 8 | pop 18 | build | `whirlwind_smithy` | 1 | The Forge, early: it researches the Prophecy |
| 9 | pop 18 | research | `melee_weapons_1` | 1 | Melee Weapons (refused, and retried, until the Smithy stands) |
| 10 | pop 23 | build | `whirlwind_tent` | 1 | |
| 11 | pop 30 | build | `whirlwind_tent` | 1 | |
| 12 | pop 37 | build | `whirlwind_tent` | 1 | |
| 13 | pop 44 | build | `whirlwind_tent` | 1 | |
| 14 | pop 51 | build | `whirlwind_tent` | 1 | |
| 15 | pop 58 | build | `whirlwind_tent` | 1 | |
| 16 | pop 65 | build | `whirlwind_tent` | 1 | |
| 17 | pop 72 | build | `whirlwind_tent` | 1 | |
| 18 | pop 79 | build | `whirlwind_tent` | 1 | The 12th Tent, as in the standard opening |
| 19 | time 390 | research | `age_ii` | 1 | Age II at 6:30, 60 s before the standard's step (needs two kinds: the camps, or a camp and the Smithy) |
| 20 | time 390 | train | `whirlwind_camp_follower` | 8 | Workers to 26 in Age II |
| 21 | time 450 | build | `whirlwind_shrine` | 1 | The Shrine at 7:30, when Age II (60 s) can be in |
| 22 | time 480 | expand | — | 1 | 2nd Holy Camp near the nearest free gold mine, at Normal's `expandAfter` (480 s) |
| 23 | time 480 | research | `dryjhnas_prophecy` | 1 | The faction upgrade early: Zealots + 20 HP |
| 24 | time 510 | build | `whirlwind_raider_camp` | 1 | A second Raider Camp: Zealots from two camps |
| 25 | time 570 | build | `whirlwind_ram_yard` | 1 | Battering Rams |

**Houses at the cap (D21, 2026-10-18; `QA/Content/AiHouseRuleReportTests.MenuMapNormal_HouseSources_Report`).** On the
menu's map (20 minutes, the other seat idle): Normal, seed 3: the cap reaches 100 at 750 s; the last Tent step (step 18) is done at a cap of 100; skipped at the hard cap: steps 17, 18; 10 Tents stand at 20:00, 0 past the cap (re-printed 2026-11-17 for D33's 14-pop wave: 767 s; re-printed 2026-11-16 for the sim's M6-S12: 783 s, step 18 skipped, 11 Tents, 1 past the cap; re-printed 2026-11-15 for the sim's M6-SH2: 0 past the cap). Re-printed 2026-10-26
for M5-4g: before it the cap reached 100 at 1184 s, step 18 was done at a cap of 90 and 1 Tent stood past the cap.

**Spending on the menu's map (D21, 2026-10-18, gathering gold 0.85 / wood 0.75 a second; `Content/AiSpendingReportTests.GeneratedNormal_Spending_Report`).**
Against an idle seat, the menu's map, 20 minutes. Normal, seed 3 (the smallest seed in 1-50 whose Normal pick is this opening): the idle base falls at 512 s; in hand at 5 min 428 gold / 101 wood; the first wave at 419 s; Age II in at 550 s (re-printed 2026-11-17 for D33's 14-pop wave; was: the idle base falls at 567 s; the first wave at 481 s; Age II in at 552 s; and before the sim's M6-S12: 583 s, 428 / 91, 497 s, 563 s). Its Age II step is the earliest (6:30), and its Age II comes in first again (the standard opening's at 568 s, the
raider rush's at 674 s; from M5-4g until D33 the standard opening's came first). Re-printed 2026-10-26 for M5-4g: before it the idle base fell at 573 s and Age II came in at 791 s, the
earliest Whirlwind Age II then (the others: 1098 s and 957 s).

**Army composition (`compositions`), weights by pop.** Age I is the two units the Age I steps train (no Horse Lines), so
its weights sum to 50.

| Unit | Age I | Age II |
| --- | --- | --- |
| `whirlwind_raider` | 30 | 30 |
| `whirlwind_desert_archer` | 20 | 20 |
| `whirlwind_zealot` | — | 30 |
| `whirlwind_priest` | — | 15 |
| `whirlwind_battering_ram` | — | 5 |

- **Composition target (by pop):** 30% Raider, 20% Desert Archer, 30% Zealot, 15% Priest of the Whirlwind,
  5% Battering Ram.
- **Attack waves:** first at 14 army pop and 918 gold + wood (`attack`; the cost of 14 pop of the Age I mix, rounded
  down; 20 and 1,312 until D33, 2026-11-17).

## Difficulty

The `difficulties` block of the same `game/data/factions/whirlwind/ai.json` (docs/02 "AI opponent", docs/03 "For the data
track (M5-1)"): one row per difficulty, one column per field. Times are in seconds as the file writes them (the loader
turns them into ticks). **Build-order slack** is how long a step waits after it is due; **wave size ×** scales the
attack wave's thresholds above; **gather bonus** is extra gather rate (docs/02: Hard's 40%, shown in the skirmish setup;
25% before D27, 2026-11-10, raised so Hard beats Normal on Vathar Crossing too, BUG-1163);
**expands after** is the earliest match time the AI expands (the order's `expand` step, due at Normal's 480 s, also
waits for it). The rows are the same for every faction today. `Content/AiContentTests` pins this table to the file,
both ways (J), and docs/02's stated numbers to the file (K).

| Difficulty | Thinks every (s) | Build-order slack (s) | Wave size × | Gather bonus | Focus fire | Pulls back wounded | Uses abilities | Expands after (s) |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Easy | 2 | 30 | 0.6 | 0% | no | no | yes | 900 |
| Normal | 1 | 5 | 1 | 0% | no | no | yes | 480 |
| Hard | 0.5 | 0 | 1.25 | 40% | yes | yes | yes | 300 |

## Art notes

- **Palette:** ochre, sand, sun-bleached white robes, crimson sashes and paint. Team color on
  sashes, banners, and shields.
- **Silhouettes:** lean and loose: veils, head-wraps, curved blades and flails. Horse Raiders on
  small, light horses. Priests in tall headdresses with a staff trailing ribbons. Zealots
  bare-chested, red-painted, twin knives, so they read differently from Raiders at a glance.
  Battering Ram as a hide-covered ram on wheels.
- **Likely sources:** KayKit Adventurers (Barbarian and Rogue bases recolored), Quaternius horse,
  Kenney Castle Kit / Quaternius Ultimate Fantasy RTS for buildings (tent and mudbrick variants
  may need AI generation or Blender kit-bashing). Details in [04-art-pipeline.md](../04-art-pipeline.md).
- **As built (M6-V25, stand-ins, re-tinted in M6-VH9; BUG-1602):** the Priest and the Zealot on `main` differ from the
  silhouettes and the palette above. The Priest wears a team-coloured hood and jerkin, a long weathered-sand robe
  (`#a39577`; bleached white until M6-VH9, which hid a Bone player's colour) and a dark maroon veil over the lower face
  (`#5e1a20`; crimson until M6-VH9), with no headdress and no staff. The Zealot is clothed, not bare-chested: a
  team-coloured tunic over rust trousers (`#6e3a26`; crimson until M6-VH9) and a maroon head-wrap with a tail (`#5a1c1e`),
  and it carries two curved blades (the Raider's 0.7 m blade, one in each hand), not knives. The reds went darker, not
  away from red, so no non-team part sits within 60 of the Crimson player's colour (docs/04 "Re-tint in the setup's
  colours (M6-VH9, BUG-1600)"). Each still reads apart from the Raider (hooded, one blade). Both are the outfits on disk
  recoloured, with box props (docs/04 "Fourth slice (M6-V25)", the team parts, props and tints "developer's pick, owner
  may revisit"); the Priest's veil and robe colour and the Zealot's idle blade are with the owner. The notes above stay
  the target for a later art pass.

## Codename → release name

| Internal codename | Proposed release name |
| --- | --- |
| The Whirlwind / Army of the Apocalypse | The Storm Faithful |
| Seven Cities | The Sun Coast |
| Priest of the Whirlwind | Storm Priest |
| Shrine of the Whirlwind | Storm Shrine |
| Dryjhna's Prophecy | The Prophecy |
| Raraku | The Holy Desert |
| Raider, Desert Archer, Horse Raider, Battering Ram, Zealot, Camp Follower, other buildings | Keep (generic) |

## Balance baseline (M4-2b, 2026-10-08)

Measured at sim commit `dd5b5b9` by `Content/CounterTriangleMarginsTests` (D6), re-printed at `1a9925b` (D8) and, for the two Line v Shock rows, in D13 (2026-10-11), and for those two rows and the siege row in D19 (2026-10-15), the siege row again on the merged tree with the sim's surround rule (M5-P1, 2026-10-16, BUG-0734), and the group table re-printed with the sim's flank rule (M5-P2, 2026-10-17, BUG-0900), and its Desert Archer v Cadre Mage seat-1 "Winner hp left" cell with the sim's stand-point change (M5-H3, 2026-10-17, BUG-0790: 522 to 523, nothing else moved), from the shared
`Scenario/CounterTriangleScene` harness, the scene of
`Scenario/CounterTriangleTests`: a flat map, 1,200 gold + wood a side, two blocks 5 wide whose fronts start 24 m apart,
every unit attack-moved into the other block, seed 7, both seats; no abilities, upgrades or terrain. These are the
rows a Whirlwind unit wins; the full table, the scene and the proposed target margins are in
[malazan.md "Balance baseline"](malazan.md#balance-baseline-m4-2b-2026-10-08) (Malazan is the balance reference). The test
asserts only the winner as a rule; the margins are not asserted, but every cell here is pinned to the harness's output.

| Rule | Winner v loser | Winner seat | Fielded (winner v loser) | Winner left | Winner hp left | Winner keeps (cost) | Time to last death |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Line beats Shock | Raider v Wickan Lancer | 0 | 19 (1216) v 10 (1200) | 18 / 19 | 1592 | 1152 / 1216 (95 %) | 15.6 s |
| Line beats Shock | Raider v Wickan Lancer | 1 | 19 (1216) v 10 (1200) | 18 / 19 | 1570 | 1152 / 1216 (95 %) | 15.9 s |
| Shock beats Ranged | Horse Raider v Crossbowman | 0 | 13 (1248) v 15 (1215) | 12 / 13 | 1332 | 1152 / 1248 (92 %) | 14.8 s |
| Shock beats Ranged | Horse Raider v Crossbowman | 1 | 13 (1248) v 15 (1215) | 11 / 13 | 1332 | 1056 / 1248 (85 %) | 14.6 s |
| Ranged beats casters | Desert Archer v Cadre Mage | 0 | 18 (1224) v 8 (1200) | 16 / 18 | 618 | 1088 / 1224 (89 %) | 9.6 s |
| Ranged beats casters | Desert Archer v Cadre Mage | 1 | 18 (1224) v 8 (1200) | 16 / 18 | 523 | 1088 / 1224 (89 %) | 11.4 s |

Siege beats buildings (one siege unit, ordered to attack, against the same cost of its faction's line infantry):

| Siege unit | Same cost of line infantry | Building | Siege time | Line time | Siege / line |
| --- | --- | --- | --- | --- | --- |
| 1 Battering Ram | 4 Raider | Billet | 13.7 s | 31.2 s | 44 % |

The target band (winner keeps 40-65 % of its cost, both seats within 15 points; proposed in D6, the Producer's default
since 2026-10-11, owner may revisit): every Whirlwind row is still above it, Raider v Lancer (95 / 95 %) and Horse Raider
v Crossbowman (92 / 85 %) most. D13 cut
the line units' bonus vs Mounted from ×1.5 to ×1.1, the nearest value to the band that kept the test suite green (1.0 came
nearer but broke a stress floor; the sweep and the reason are in malazan.md); Raider v Lancer moved from 95-100 % to 89-100 %. D19 (2026-10-15)
put it back to ×1.5 (the owner's decision): Raider v Lancer was 95-100 % again (D13 and D19 measured on the movement
before pathing slice 2). The sim's flank rule (M5-P2, 2026-10-17, BUG-0900: melee units blocked by their own ranks walk
round them) ends every Whirlwind fight sooner (Raider v Lancer 16.6 / 18.3 s to 15.6 / 15.9 s, Horse Raider v
Crossbowman 20.9 / 18.2 s to 14.8 / 14.6 s, Desert Archer v Cadre Mage 11.4 / 13.0 s to 9.6 / 11.4 s); the Horse
Raiders keep 92 / 85 % (was 69 / 77 %), the Archers 89 / 89 % (was 89 / 72 %), and the Raiders 95 / 95 % (was
100 / 95 %). Every winner is unchanged. The Battering Ram's Siege / line share went
from 5 % to 34 % in D19: the building damage set (C) (docs/02 "Combat") speeds the Raiders up far more than the Ram.
The sim's surround rule (M5-P1, 2026-10-16: more Raiders strike the Billet at once) moved it to 44 % (Raiders 40.0 s
to 31.2 s, the Ram 13.8 s to 13.7 s); the Ram still razes the Billet about 2.3× faster. Kill times (D-H5, 2026-10-16,
`Content/BuildingKillTimeReportTests`, re-run after the sim's M5-H2 merge): 20 Raiders raze a lone Malazan House,
infantry hall and Town Hall in 9.8 / 19.5 / 35.8 s (the band is 20 / 60 / 120 s; the hall was 19.4 s before M5-H2's
movement change), 10 Raiders in 13.8 / 33.0 / 62.6 s.

## Age II against the Malazans (M5-4g, 2026-10-26)

When each Whirlwind side's Age II was in, Normal against Normal on the menu's map (12 forests, 8 mines), 20 minutes, for
every Whirlwind opening against every Malazan opening on the pairing's three smallest seeds in 1-500, "never" if not in
20 minutes, and the defeat if the side fell. The 1st seed is `Content/AiOpeningPairsReportTests`' run, the 2nd
and 3rd `QA/Content/D21OpeningsQaTests`'; each run pins its cell here and on malazan.md's twin table ("Age II against
the Whirlwind"). Under M5-4g no Whirlwind side fell, and the two sides without Age II (`standard` against
`crossbow_push` seed 9 and against `cadre_rush` seed 13) stood at 20:00 knowing no gold node, so their Age II can't be
reserved (docs/03 "Implementation (M5-4g)"). Since the sim's M6-S10 (2026-11-15, the gather change) seed 9's side
reaches Age II at 10:16 (since the sim's M6-S14; 17:16 under M6-S12 and D33; 11:47 under M6-S10 and M6-SH2; from the
sim's M5-9a until M6-S10 it was defeated at 19:36 without it); seed 13's side never reaches Age II and is defeated at
17:03 (since the sim's M6-S14; under M6-S12 and D33 it stood at 20:00 without it; under M6-SH2 it was defeated at
15:19; under M6-S10 it still stood at 20:00 without Age II, beaten down: its Town Hall razed, 4 workers left). Under M6-S10 three Whirlwind sides fell: `holy_war` against `standard` seed 3 at
12:29 (before its first wave was due, see "The holy war's first wave" below), `raider_rush` against `crossbow_push`
seed 11 at 18:34, without Age II, and `standard` against `cadre_rush` seed 17 at 16:27.

| Whirlwind opening | Malazan opening | 1st seed | 2nd seed | 3rd seed |
| --- | --- | --- | --- | --- |
| `standard` | `standard` | seed 1: 9:08 | seed 22: 11:06 | seed 26: 9:28 (defeated 18:50) |
| `standard` | `crossbow_push` | seed 9: 10:16 | seed 32: 10:10 | seed 37: 10:46 |
| `standard` | `cadre_rush` | seed 13: never (defeated 17:03) | seed 16: 10:10 | seed 17: 9:34 (defeated 16:52) |
| `raider_rush` | `standard` | seed 2: 9:52 | seed 10: 9:32 | seed 21: 11:07 |
| `raider_rush` | `crossbow_push` | seed 5: 13:49 | seed 11: 16:16 | seed 19: 11:18 |
| `raider_rush` | `cadre_rush` | seed 7: 9:52 | seed 8: 10:01 | seed 14: 9:48 |
| `holy_war` | `standard` | seed 3: 9:10 | seed 4: 10:05 | seed 6: 7:35 |
| `holy_war` | `crossbow_push` | seed 12: 8:46 (defeated 18:45) | seed 45: 7:35 | seed 71: 8:58 |
| `holy_war` | `cadre_rush` | seed 20: 8:34 | seed 24: 9:10 | seed 27: 7:48 |

Re-printed 2026-11-15 for the sim's M6-S10 (docs/03 "Implementation (M6-S10)"): `raider_rush` against `crossbow_push`
seed 19 now reaches Age II at 11:18 (was 10:34 from the sim's M5-H6, and 10:21 before it; 10:04 under M6-S10 and
M6-SH2, 16:10 under the sim's M6-S12 and D33, 11:18 since the sim's M6-S14).

Re-printed again 2026-11-15 for the sim's M6-SH2 (a construction site is no longer a drop-off; an AI with no worker trains
one; docs/03 "Implementation (M6-SH2)"): 25 of the 27 Whirlwind sides reach Age II; the two that don't are both defeated
(`standard` against `cadre_rush` seed 13 at 15:19, `raider_rush` against `crossbow_push` seed 11 at 18:04). Four sides
fall: those two, `standard` against `cadre_rush` seed 16 at 18:23 (after its Age II) and `raider_rush` against
`cadre_rush` seed 14 at 19:53. `holy_war` against `standard` seed 3 no longer falls (Age II at 9:10). The paragraph above
the table describes the M6-S10 runs, except seed 13's sentence, which reads the M6-SH2 cell.

Re-printed 2026-11-16 for the sim's M6-S12 (the AI's rally point off one-cell passages, a rebalanced gatherer's node
measured from its drop-off, a blind seat's re-scout; docs/03 "Implementation (M6-S12)"): 26 of the 27 Whirlwind sides
reach Age II; the one that doesn't is `standard` against `cadre_rush` seed 13, which now stands at 20:00 without it (it
was defeated at 15:19 under M6-SH2). One side falls: `raider_rush` against `cadre_rush` seed 7 at 19:59, after its Age II
(10:04). `raider_rush` against `crossbow_push` seed 11 reaches Age II at 13:09 and stands.

Re-printed 2026-11-17 for D33 (the data track's balance pass: `holy_war`'s wave at 14 army pop, was 20; the Malazan
`cadre_rush`'s at 16, was 18): 26 of the 27 Whirlwind sides reach Age II; the one that doesn't is still `standard` against
`cadre_rush` seed 13, which stands at 20:00. Two sides fall, both after their Age II: `raider_rush` against `cadre_rush`
seed 7 at 19:54 (was 19:59) and `standard` against `cadre_rush` seed 16 at 18:47 (it stood under M6-S12). `holy_war`
reaches Age II sooner against `crossbow_push` (8:58, 7:35 and 8:58, was 9:10 on all three seeds) and against `cadre_rush`
seeds 20 and 27 (8:34 and 7:53, was 9:19 and 8:46); `raider_rush` against `cadre_rush` seed 8 later (12:03, was 10:02).


Re-printed 2026-11-26 for the sim's M6-S14 (the AI-play session: the hunt's resume, the worker floor, the rally search's memory, the blind re-scout's count, the rally point's memory; docs/03 "Implementation (M6-S14)"; cells moved on every seed): 26 of the 27 Whirlwind sides reach Age II; the one that doesn't: `standard` against `cadre_rush` seed 13 (defeated 17:03). 4 sides fall: `standard` against `standard` seed 26 (18:50, after its Age II at 9:28); `standard` against `cadre_rush` seed 13 (17:03); `standard` against `cadre_rush` seed 17 (16:52, after its Age II at 9:34); `holy_war` against `crossbow_push` seed 12 (18:45, after its Age II at 8:46). The paragraphs above describe the M6-S10, M6-SH2, M6-S12 and D33 runs. The old cells (D33): `standard` v `standard` seed 1 9:10, `standard` v `crossbow_push` seed 9 17:16, `standard` v `cadre_rush` seed 13 never, `raider_rush` v `standard` seed 2 9:57, `raider_rush` v `crossbow_push` seed 5 13:28, `raider_rush` v `cadre_rush` seed 7 9:52 (defeated 19:54), `holy_war` v `crossbow_push` seed 12 8:58, `raider_rush` v `standard` seed 21 16:14, `raider_rush` v `crossbow_push` seeds 11 / 19 13:09 / 16:10, `raider_rush` v `cadre_rush` seeds 8 / 14 12:03 / 10:00, `standard` v `standard` seed 26 9:30, `standard` v `crossbow_push` seeds 32 / 37 9:58 / 9:18, `standard` v `cadre_rush` seeds 16 / 17 10:10 (defeated 18:47) / 9:58, `holy_war` v `standard` seed 4 10:03, `holy_war` v `cadre_rush` seed 27 7:53.

## The holy war's first wave (D-R39, 2026-11-15)

Measured by `Content/AiOpeningPairsReportTests`' companion `Content/HolyWarFirstWaveReportTests`, which pins this table to
its output cell for cell. `holy_war` against each Malazan opening, two Normal AI seats on the menu's map (12 forests, 8
mines), 20 minutes, on each pairing's three smallest seeds in 1-500 (the Age II table's seeds). The `holy_war` columns:
when its first wave was due (its army at the opening's 14 army pop and their cost, or the starved charge), its first
wave and the waves sent, its defeat and its largest army (read once a second).

| Malazan opening | seed | Malazan first wave | `holy_war` wave due | `holy_war` first wave (waves) | `holy_war` defeated at | `holy_war` peak army pop |
| --- | --- | --- | --- | --- | --- | --- |
| `standard` | 3 | 5:50 | 6:21 | 6:21 (2) | never | 16 |
| `standard` | 4 | 6:02 | 6:34 | 6:34 (4) | never | 21 |
| `standard` | 6 | 5:55 | 6:17 | 6:17 (5) | never | 25 |
| `crossbow_push` | 12 | 5:45 | 5:58 | 5:58 (3) | 18:45 | 19 |
| `crossbow_push` | 45 | 5:40 | 6:10 | 6:10 (7) | never | 41 |
| `crossbow_push` | 71 | 7:03 | 6:26 | 6:26 (4) | never | 46 |
| `cadre_rush` | 20 | 8:56 | 6:38 | 6:38 (7) | never | 37 |
| `cadre_rush` | 24 | 10:25 | 6:49 | 6:49 (4) | never | 21 |
| `cadre_rush` | 27 | 10:09 | 5:56 | 5:56 (4) | never | 32 |

Re-printed 2026-11-26 for M6-S14 (the sim's AI-play session: the hunt's resume, the worker floor, the rally point's memory,
the blind re-scout's count; no data changed but `ai.minWorkers`). The first waves (Malazan's and `holy_war`'s) and the due
times are unchanged but for the `cadre_rush` rows' Malazan first waves (8:56, 10:25, 10:09; were 8:54, 9:02, 10:17); the
waves sent, the peaks and one defeat moved: `holy_war` falls on `crossbow_push` seed 12 at 18:45 (it fell on none). The D33
table read: waves 5 / 4 / 6 / 4 / 4 / 4 / 5 / 5 / 5, peaks 25 / 17 / 19 / 20 / 66 / 40 / 20 / 22 / 22, no defeat.

Re-printed 2026-11-17 for D33 (the data track's balance pass, BUG-1553): `holy_war`'s wave is 14 army pop and 918 gold +
wood (was 20 and 1,312), and `cadre_rush`'s 16 (was 18; malazan.md "Fast Age II against the raider rush"). `holy_war` now
sends 4 to 6 waves on every one of the nine runs, the first between 5:56 and 6:49, and falls on none. On seed 3, where
at 20 pop its army never got there (peak 16) and it sent no wave in 20 minutes against the Malazan's five, it waves at
6:21. The `cadre_rush` rows' Malazan first waves are earlier (8:54, 9:02, 10:17; were 8:08, 10:07, 9:41 at 18 pop).

How D33 chose 14 (`Content/D33BalanceReportTests.HolyWar_WaveThreshold_NineRuns_Report`, the same nine runs with
`cadre_rush` still at 18, the wave cost the floor of the Age I mix at that pop as D21's rule has it): at 20, 18, 16 and
15 army pop seed 3's army peaks at 16 and it sends no wave; at 14 it waves on all nine. 14 is the standard opening's wave
too (the raider rush's is 10), so the holy war's first wave no longer waits longer than the other openings'; its early
Age II is still its own.

The M6-S12 table (2026-11-16) read: seed 3 no wave (peak 16, not defeated); the other eight waved first between 6:53 and
8:33 (3 to 5 waves); `holy_war` fell on none. The M6-SH2 table read, for seed 3: Malazan first wave 5:46, wave due 12:57,
one wave at 14:45, peak 49. D-R39's reading of the M6-S10 runs, where seed 3 fell at 12:29 with a 14-pop army before its
wave was due, is history.

**What it says (D33; M6-S14).** `holy_war` sends its first wave before the Malazan's on four of the nine runs (the three
`cadre_rush` runs and `crossbow_push` seed 71) and 13 to 32 s after it on the other five. It falls on one, `crossbow_push`
seed 12 at 18:45, long after its waves (it fell on none before M6-S14). `Content/AiOpeningPairsReportTests` names no side in `AliveWithoutAWave` since D33
(BUG-1553: (0, 2) seed 3 was named there from M6-S12) and fails any side that sends no wave.

## Questions for the Producer (D33, 2026-11-17)

Raised by D33 (BUG-1553), with the developer's recommendation.

1. **`holy_war` waves at 14 army pop.** Of 20, 18, 16, 15 and 14 (each with D21's cost rule), only 14 makes seed 3's side
   wave; at 15 and above its army peaks at 16 pop and waits. 14 is the standard opening's number, so the opening's
   "waits for 20" identity is gone (its description now reads "the first wave goes at 14 army pop"). Recommendation:
   accept; a lower `minArmyCost` at 16 pop, or a sim rule ("wave with what you have when attacked every two minutes",
   BUG-1010's family) would keep a later wave, and both are for the owner to choose.

