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

The AI opponent's `game/data/factions/whirlwind/ai.json` (docs/03 "For the data track (M5-1)"): one row per `buildOrder`
step, in file order, read the same way as malazan.md "AI build order (Normal)" (steps run side by side, one command per
step per think, in this order; **Count** is the `train` commands the step queues over its life). The Whirlwind keep
fewer workers (18 in Age I, 26 in Age II) and reach Age II later than the Malazan, spending on the army first. The AI
starts with 5 Camp Followers, 3 on gold and 2 on wood. `Content/AiContentTests` pins this table, the composition table
and the wave and retreat lines below to the file, both ways.

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
| 19 | pop 79 | build | `whirlwind_tent` | 1 | The 12th Tent: Holy Camp 10 + 12 × 8 = 106, past the 100 cap |
| 20 | time 450 | research | `age_ii` | 1 | Age II at 7:30, later than the Malazan (needs two of the camps, Horse Lines, Smithy) |
| 21 | time 450 | train | `whirlwind_camp_follower` | 8 | Workers to 26 in Age II |
| 22 | time 480 | expand | — | 1 | 2nd Holy Camp near the nearest free gold mine, at Normal's `expandAfter` (480 s), regardless of threat |
| 23 | time 510 | build | `whirlwind_shrine` | 1 | 8:30, when Age II (60 s) is in |
| 24 | time 540 | build | `whirlwind_raider_camp` | 1 | A second Raider Camp: Zealots from 2 Raider Camps |
| 25 | time 570 | build | `whirlwind_ram_yard` | 1 | Battering Rams |
| 26 | time 600 | research | `dryjhnas_prophecy` | 1 | The faction upgrade, at the Smithy |

Not in the file, because the schema has no field for it (requests to the sim track): the sketch's first raid at ~4:30
(6 Raiders + 2 Horse Raiders against the workers; the first wave goes at 14 army pop, below), and the 3 : 2 gold / wood
split (the economy manager's `goldShare`, a common rule). The sketch's expansion at ~9:00 is now the step at 8:00,
Normal's `expandAfter`.

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
  down); then continuous pressure, a new wave every 2 minutes. Priests cast Sandstorm on the enemy army's center as the
  melee engages.
- **Retreat:** a wave that has lost 50% falls back; a unit under 30% hp counts as wounded (`retreat`; Hard pulls it back).
- **Defense** (docs/03 "Implementation (M5-4b)"): the army comes home when an own building (a site too) within the
  defend radius of the Holy Camp is hit, or an enemy soldier (not a worker) is seen within it (`rules.json`
  `ai.defendRadius` 48 m; the threat holds `ai.defendFor` 20 s after the last sign); a wave out ends like a retreat.
  Camp Followers within `ai.workerFleeRadius` (16 m) of the threat flee to the nearest finished drop-off away from the
  threat (the Holy Camp or a Supply Cache, whichever is nearest among those more than 16 m from the threat; else the
  nearest one).
- **Later idea (not in M5):** counter-attack the enemy base with Horse Raiders when its army is away. Not built: no
  AI manager raids today.

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

Measured at sim commit `dd5b5b9` by `Content/CounterTriangleMarginsTests` (D6), re-printed at `1a9925b` (D8) and, for the two Line v Shock rows, in D13 (2026-10-11), and for those two rows and the siege row in D19 (2026-10-15), the siege row again on the merged tree with the sim's surround rule (M5-P1, 2026-10-16, BUG-0734), from the shared
`Scenario/CounterTriangleScene` harness, the scene of
`Scenario/CounterTriangleTests`: a flat map, 1,200 gold + wood a side, two blocks 5 wide whose fronts start 24 m apart,
every unit attack-moved into the other block, seed 7, both seats; no abilities, upgrades or terrain. These are the
rows a Whirlwind unit wins; the full table, the scene and the proposed target margins are in
[malazan.md "Balance baseline"](malazan.md#balance-baseline-m4-2b-2026-10-08) (Malazan is the balance reference). The test
asserts only the winner as a rule; the margins are not asserted, but every cell here is pinned to the harness's output.

| Rule | Winner v loser | Winner seat | Fielded (winner v loser) | Winner left | Winner hp left | Winner keeps (cost) | Time to last death |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Line beats Shock | Raider v Wickan Lancer | 0 | 19 (1216) v 10 (1200) | 19 / 19 | 1667 | 1216 / 1216 (100 %) | 16.6 s |
| Line beats Shock | Raider v Wickan Lancer | 1 | 19 (1216) v 10 (1200) | 18 / 19 | 1581 | 1152 / 1216 (95 %) | 18.3 s |
| Shock beats Ranged | Horse Raider v Crossbowman | 0 | 13 (1248) v 15 (1215) | 9 / 13 | 1152 | 864 / 1248 (69 %) | 20.9 s |
| Shock beats Ranged | Horse Raider v Crossbowman | 1 | 13 (1248) v 15 (1215) | 10 / 13 | 1197 | 960 / 1248 (77 %) | 18.2 s |
| Ranged beats casters | Desert Archer v Cadre Mage | 0 | 18 (1224) v 8 (1200) | 16 / 18 | 602 | 1088 / 1224 (89 %) | 11.4 s |
| Ranged beats casters | Desert Archer v Cadre Mage | 1 | 18 (1224) v 8 (1200) | 13 / 18 | 525 | 884 / 1224 (72 %) | 13.0 s |

Siege beats buildings (one siege unit, ordered to attack, against the same cost of its faction's line infantry):

| Siege unit | Same cost of line infantry | Building | Siege time | Line time | Siege / line |
| --- | --- | --- | --- | --- | --- |
| 1 Battering Ram | 4 Raider | Billet | 13.7 s | 31.2 s | 44 % |

The target band (winner keeps 40-65 % of its cost, both seats within 15 points; proposed in D6, the Producer's default
since 2026-10-11, owner may revisit): every Whirlwind row is still above it, Raider v Lancer (95-100 %) most. D13 cut
the line units' bonus vs Mounted from ×1.5 to ×1.1, the nearest value to the band that kept the test suite green (1.0 came
nearer but broke a stress floor; the sweep and the reason are in malazan.md); Raider v Lancer moved from 95-100 % to 89-100 %. D19 (2026-10-15)
put it back to ×1.5 (the owner's decision): Raider v Lancer is 95-100 % again. The Battering Ram's Siege / line share went
from 5 % to 34 % in D19: the building damage set (C) (docs/02 "Combat") speeds the Raiders up far more than the Ram.
The sim's surround rule (M5-P1, 2026-10-16: more Raiders strike the Billet at once) moved it to 44 % (Raiders 40.0 s
to 31.2 s, the Ram 13.8 s to 13.7 s); the Ram still razes the Billet about 2.3× faster.
