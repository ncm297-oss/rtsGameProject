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
against the Malazan openings (malazan.md).

| # | Id | Name (shown to the player) | Description (shown to the player) |
| --- | --- | --- | --- |
| 1 | `standard` | Swarm | Raider, Archer and Horse camps first and a raid at 14 army pop; Zealots, Priests and Battering Rams join in Age II. |
| 2 | `raider_rush` | Raider rush | Two Raider Camps and Horse Lines, no archers: Raiders and Horse Raiders strike at 10 army pop and keep coming. |
| 3 | `holy_war` | Holy war | An early Age II, the Shrine at once and Dryjhna's Prophecy for Zealots and Priests. No Horse Lines; the first wave waits for 20 army pop. |

Not in the file, because the schema has no field for it (requests to the sim track): the sketch's first raid at ~4:30
(6 Raiders + 2 Horse Raiders against the workers; the standard opening's first wave goes at 14 army pop, below), the 3 :
2 gold / wood split (the economy manager's `goldShare`, a common rule), and a reserve for a due step (the AI saves
nothing for Age II, so it comes in minutes after its step is due: each opening's spending note). The sketch's
expansion at ~9:00 is now the step at 8:00, Normal's `expandAfter`.

- **Retreat:** a wave that has lost 50% falls back; a unit under 30% hp counts as wounded (`retreat`, shared by the
  openings; Hard pulls it back).
- **Later waves** (every opening): one wave at a time; a new one starts when none is out, at least `ai.waveCooldown`
  after the last began, at the opening's threshold, and never while the base is defended. Priests cast Sandstorm on
  the enemy army's center as the melee engages.
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

### Opening 1: `standard` (Swarm)

The order the AI has played since D14, step for step, and its wave; Easy plays only this one. D21 (2026-10-18) gave it
its name and description.

| # | At | Do | Id | Count | Why |
| --- | --- | --- | --- | --- | --- |
| 1 | time 0 | train | `whirlwind_camp_follower` | 13 | Workers to 18 in Age I (5 at the start + 13) |
| 2 | pop 6 | build | `whirlwind_tent` | 1 | First Tent |
| 3 | pop 8 | build | `whirlwind_raider_camp` | 1 | Raider Camp |
| 4 | pop 9 | build | `whirlwind_supply_cache` | 1 | Supply Cache at the nearest forest |
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
command sent and no Tent placed. On the menu's map (20 minutes, the other seat idle): Normal, seed 1: the cap reaches 100
at 683 s; the last Tent step (step 19) is done at a cap of 98; skipped at the hard cap: none; 12 Tents stand at 20:00, 0
past the cap. The 12th Tent leaves 6 of its 8 pop unused, so D21 measured taking it out: without the 12th Tent step
(step 19) on the same seed, the cap reaches 100 at 827 s, and 12 Tents stand at 20:00, 0 past the cap. The economy
manager's house rule places the 12th Tent anyway, only later, so the step stays, in every opening.

**Spending on the menu's map (D21, 2026-10-18, gathering gold 0.85 / wood 0.75 a second; `Content/AiSpendingReportTests.GeneratedNormal_Spending_Report`).**
Against an idle seat, 12 forests and 8 mines (the menu's), 20 minutes. Normal, seed 1 (the smallest seed in 1-50 whose
Normal pick is this opening): the idle base falls at 495 s; in hand at 5 min 336 gold / 97 wood; the first wave at 372
s; Age II in at 1098 s. Easy, seed 1 (Easy always plays the first opening): the idle base falls at 518 s; in hand at 5
min 458 gold / 29 wood; the first wave at 356 s; no Age II in 20 minutes. D20's two-seed note (495 s / 518 s, seeds 1
and 7) measured both seeds; the runs are now one per opening.

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
| 4 | pop 9 | build | `whirlwind_supply_cache` | 1 | Supply Cache at the nearest forest |
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
menu's map (20 minutes, the other seat idle): Normal, seed 2: the cap reaches 100 at 928 s; the last Tent step (step 19)
is done at a cap of 90; skipped at the hard cap: none; 12 Tents stand at 20:00, 2 past the cap (the expansion's Holy
Camp stands on this seed).

**Spending on the menu's map (D21, 2026-10-18, gathering gold 0.85 / wood 0.75 a second; `Content/AiSpendingReportTests.GeneratedNormal_Spending_Report`).**
Against an idle seat, the menu's map, 20 minutes. Normal, seed 2 (the smallest seed in 1-50 whose Normal pick is this
opening): the idle base falls at 416 s; in hand at 5 min 36 gold / 37 wood; the first wave at 287 s; Age II in at 957 s.
The quickest wave and the quickest win of the six openings.

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
wave waits for 20 army pop, the latest Whirlwind wave.

| # | At | Do | Id | Count | Why |
| --- | --- | --- | --- | --- | --- |
| 1 | time 0 | train | `whirlwind_camp_follower` | 13 | Workers to 18 in Age I (5 at the start + 13) |
| 2 | pop 6 | build | `whirlwind_tent` | 1 | First Tent |
| 3 | pop 8 | build | `whirlwind_raider_camp` | 1 | Raider Camp |
| 4 | pop 9 | build | `whirlwind_supply_cache` | 1 | Supply Cache at the nearest forest |
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
menu's map (20 minutes, the other seat idle): Normal, seed 3: the cap reaches 100 at 1184 s; the last Tent step (step 18)
is done at a cap of 90; skipped at the hard cap: none; 12 Tents stand at 20:00, 1 past the cap.

**Spending on the menu's map (D21, 2026-10-18, gathering gold 0.85 / wood 0.75 a second; `Content/AiSpendingReportTests.GeneratedNormal_Spending_Report`).**
Against an idle seat, the menu's map, 20 minutes. Normal, seed 3 (the smallest seed in 1-50 whose Normal pick is this
opening): the idle base falls at 573 s; in hand at 5 min 478 gold / 101 wood; the first wave at 485 s; Age II in at 791
s. The earliest Whirlwind Age II (the others: 1098 s and 957 s).

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
- **Attack waves:** first at 20 army pop and 1,312 gold + wood (`attack`; the cost of 20 pop of the Age I mix, rounded
  down).

## Difficulty

The `difficulties` block of the same `game/data/factions/whirlwind/ai.json` (docs/02 "AI opponent", docs/03 "For the data
track (M5-1)"): one row per difficulty, one column per field. Times are in seconds as the file writes them (the loader
turns them into ticks). **Build-order slack** is how long a step waits after it is due; **wave size ×** scales the
attack wave's thresholds above; **gather bonus** is extra gather rate (docs/02: Hard's 25%, shown in the skirmish setup);
**expands after** is the earliest match time the AI expands (the order's `expand` step, due at Normal's 480 s, also
waits for it). The rows are the same for every faction today. `Content/AiContentTests` pins this table to the file,
both ways (J), and docs/02's stated numbers to the file (K).

| Difficulty | Thinks every (s) | Build-order slack (s) | Wave size × | Gather bonus | Focus fire | Pulls back wounded | Uses abilities | Expands after (s) |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Easy | 2 | 30 | 0.6 | 0% | no | no | yes | 900 |
| Normal | 1 | 5 | 1 | 0% | no | no | yes | 480 |
| Hard | 0.5 | 0 | 1.25 | 25% | yes | yes | yes | 300 |

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
