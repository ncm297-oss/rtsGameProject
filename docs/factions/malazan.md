# Malazan Empire

**Faction id:** `malazan` · **Milestone:** vertical slice (M3-M6) · **Role:** balance reference

## Lore hook

A sprawling, pragmatic empire that conquered half a world with professional soldiers rather than
heroes. Its legions mix heavy infantry, crossbows, mages attached to every army (the cadre), and
marines and sappers armed with alchemical munitions bought from the Moranth. Malazan soldiers
are cynical, tired, and very good at their jobs. Their sappers are famous for blowing up the
enemy, and occasionally themselves.

## Playstyle

The "standard" faction: solid at everything, best at nothing. Disciplined combined arms. Holds a
line with Heavy Infantry, kills from behind it with Crossbowmen, flanks with Wickan Lancers, and
cracks fortifications or clumped armies with Sappers. Stats sit closest to the template so every
other faction can be balanced against it.

## Faction bonus: Imperial Drill

Infantry units (Heavy Infantry, Crossbowman, Sapper) **train 20% faster and cost 10% less Gold**.
Already applied in the table below.

## Faction upgrade: Moranth Supply

Age II, at the Armory, 200 G / 150 W, 45 s. Sapper **Cusser cooldown 45 → 30 s**; Catapult
**range +4 m**.

## Units

| Slot | Unit | HP | Armor | Class | Attack | Type | CD | Range | Targets | Speed | Sight | Cost (G/W) | Pop | Train | Trained at |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Worker | Laborer | 40 | 0 | Light | 4 | Melee | 1.5 | melee | all | 4.0 | 14 | 50 / 0 | 1 | 12 | Garrison Keep |
| Line | Heavy Infantry | 130 | 3 | Heavy | 10 | Melee | 1.5 | melee | all | 3.0 | 14 | 54 / 20 | 1 | 14 | Legion Barracks |
| Ranged | Crossbowman | 55 | 0 | Light | 9 | Pierce | 2.2 | 15 | all | 3.2 | 18 | 36 / 45 | 1 | 16 | Crossbow Range |
| Shock | Wickan Lancer | 150 | 1 | Mounted | 12 | Melee | 1.8 | melee | all | 6.2 | 16 | 90 / 30 | 2 | 26 | Wickan Corral |
| Caster | Cadre Mage | 60 | 0 | Light | 9 | Magic | 2.2 | 12 | all | 3.2 | 16 | 100 / 50 | 2 | 30 | Cadre Tower |
| Siege | Catapult | 220 | 4 | Heavy | 50 | Siege | 5.0 | 24 (min 6) | all | 2.2 | 18 | 200 / 150 | 3 | 40 | Engineers' Yard |
| Unique | Sapper | 70 | 1 | Light | 20 | Siege | 3.0 | 8 | all | 3.4 | 16 | 72 / 40 | 1 | 20 | Engineers' Yard |

### Unit notes

- **Heavy Infantry:** ×1.5 vs Mounted. Passive **Shield Wall**: +3 armor against pierce attacks
  while not moving. Slightly slower than the template line.
- **Crossbowman:** ×1.3 vs Heavy (bolts punch through armor). Slower rate of fire than a bow.
- **Wickan Lancer:** ×1.5 vs Light. Passive **Charge**: the first attack after moving at least
  8 m deals +10 damage.
- **Cadre Mage:** splash 1.5 m, detector 10 m. Signature ability **Telas Fire** (below).
- **Catapult:** splash 2.5 m, **friendly fire**. Can't fire at targets closer than 6 m.
- **Sapper (unique, Age II):** throws **Sharpers**, small munitions with 2.0 m splash and
  **friendly fire**. Ability **Cusser** (below). Fragile; wants an escort.

## Abilities

| Ability | Unit | Kind | Range | Radius | Cast | Cooldown | Effect |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Telas Fire | Cadre Mage | Target ground | 16 | 3 m | 0.8 s | 25 s | Enemy units in the area are Burning: 10 magic damage/s for 4 s. No effect on buildings |
| Cusser | Sapper | Target ground | 6 | 3.5 m | 1.0 s | 45 s | 120 siege damage in the area, full damage to buildings (≈355 to a Town Hall), friendly fire at 50% |

Telas Fire is the faction's signature caster ability: a short, decisive area denial that punishes
clumped Heavy units. Cusser is the unique unit's identity: a satchel charge that deletes
buildings and can wreck your own army if you're careless.

## Buildings

| Slot | Name | Id | HP | Armor | Cost (G/W) | Build (s) | Footprint | Sight | Provides | Requires |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Town Hall | Garrison Keep | `malazan_garrison_keep` | 2400 | 5 | 275 / 275 | 90 | 4×4 | 12 | +10 pop, drop-off, trains Laborer, researches Age II | — |
| House | Billet | `malazan_billet` | 500 | 3 | 0 / 50 | 20 | 2×2 | 12 | +8 pop | — |
| Camp | Quartermaster's Depot | `malazan_depot` | 600 | 3 | 0 / 75 | 25 | 2×2 | 12 | Drop-off | — |
| Infantry Hall | Legion Barracks | `malazan_barracks` | 1200 | 4 | 0 / 150 | 40 | 3×3 | 12 | Trains Heavy Infantry | — |
| Ranged Hall | Crossbow Range | `malazan_crossbow_range` | 1200 | 4 | 0 / 150 | 40 | 3×3 | 12 | Trains Crossbowman | — |
| Shock Hall | Wickan Corral | `malazan_wickan_corral` | 1200 | 4 | 75 / 150 | 45 | 3×3 | 12 | Trains Wickan Lancer | Legion Barracks |
| Forge | Armory | `malazan_armory` | 1000 | 4 | 100 / 100 | 40 | 3×3 | 12 | Upgrades, Moranth Supply | — |
| Caster Hall | Cadre Tower | `malazan_cadre_tower` | 1200 | 4 | 150 / 150 | 50 | 3×3 | 12 | Trains Cadre Mage | Age II |
| Siege Works | Engineers' Yard | `malazan_engineers_yard` | 1400 | 4 | 150 / 200 | 55 | 3×3 | 12 | Trains Catapult, Sapper | Age II |
| Watch Tower | Watchtower | `malazan_watchtower` | 800 | 5 | 50 / 125 | 35 | 2×2 | 24 | Attack 10 pierce / 2 s, range 18; sight 24; detector 16 m | Age II |

Building stats follow the template in [02-game-design.md](../02-game-design.md#buildings).

`Content/BuildingContentTests` and `UnitContentTests` pin this page to the data.

## Techs

| Id | Name | Researched at | Cost (G/W) | Time (s) | Requires | Effects |
| --- | --- | --- | --- | --- | --- | --- |
| `moranth_supply` | Moranth Supply | Armory | 200 / 150 | 45 | Age II | Sapper Cusser cooldown 45 → 30 s; Catapult range +4 m |

The faction upgrade is the only faction-specific tech. Age II and the six shared Forge upgrades are in
[02-game-design.md "Forge upgrades"](../02-game-design.md#forge-upgrades). `Content/TechContentTests` pins this
table and the "Faction upgrade" line above to the data.

## Strengths and weaknesses

- **Strong:** flexible, no hard counter. Cheap, fast infantry keeps up pressure. Sappers make
  sieges short.
- **Weak:** no standout mobility (Lancers are only template-fast). Friendly fire punishes sloppy
  play. Crossbows fire slowly, so swarms (Whirlwind) can close the gap.

## AI build order sketch (Normal)

```
Workers: train continuously to 20 in Age I, 30 in Age II.
Start:   3 workers on gold, 2 on wood.
pop 6    Billet
pop 8    Quartermaster's Depot at the nearest forest
pop 10   Legion Barracks (rally to base choke)
pop 12   Billet
pop 14   Crossbow Range
pop 16   Armory -> Melee Weapons I
pop 18+  a Billet every ~7 pop
~6:30    Age II
Age II   Cadre Tower, Engineers' Yard, Wickan Corral, Moranth Supply, Watchtower at the choke
Expand   2nd Garrison Keep when army >= 12 pop and no enemy army near base for 60 s
```

- **Composition target (by pop):** 35% Heavy Infantry, 30% Crossbowman, 10% Wickan Lancer,
  10% Cadre Mage, 10% Sapper, 5% Catapult.
- **Attack waves:** first at 20 army pop; then every 3 minutes or when army pop ≥ 1.3× the last
  wave. Sappers target buildings; Mages cast Telas Fire on the largest enemy clump.
- **Defense:** pull the army home when buildings take damage; Laborers flee to the Garrison Keep.

## Art notes

- **Palette:** dark steel grey, oxblood red cloth, dull bronze trim. Team color on shields and
  cloaks.
- **Silhouettes:** Heavy Infantry carry big rectangular shields; Crossbowmen are lighter with a
  visible crossbow; Wickan Lancers are lean riders with long lances, braids, and feathers; Cadre
  Mages wear hooded robes, no armor; Sappers wear leather aprons with munition satchels and a
  crouched stance.
- **Likely sources:** KayKit Adventurers (Knight for Heavy Infantry, Rogue for Crossbowman, Mage
  for Cadre Mage, Rogue_Hooded for Sapper, plus weapon accessories), a Quaternius horse for the
  Lancer's mount, Kenney Castle Kit siege weapons for the Catapult, Quaternius Ultimate Fantasy
  RTS for buildings. Details in [04-art-pipeline.md](../04-art-pipeline.md).

## Codename → release name

| Internal codename | Proposed release name |
| --- | --- |
| Malazan Empire | The Iron Legions |
| Wickan Lancer | Steppe Lancer |
| Wickan Corral | Steppe Corral |
| Cadre Mage | Battle Mage |
| Cadre Tower | Mage Tower |
| Telas Fire | Wildfire |
| Moranth munitions / Moranth Supply | Alchemist's munitions / Alchemist's Supply |
| Sharper | Fire-pot |
| Cusser | Satchel Charge |
| Heavy Infantry, Crossbowman, Catapult, Sapper, Laborer, all buildings | Keep (generic) |

## Balance baseline (M4-2b, 2026-10-08)

Measured at sim commit `dd5b5b9` by `Content/CounterTriangleMarginsTests` (D6), re-printed at `1a9925b` (D8) from the shared
`Scenario/CounterTriangleScene` harness, the scene of
`Scenario/CounterTriangleTests`: a flat map, 1,200 gold + wood a side (the whole number of units nearest to it), two
blocks 5 wide whose fronts start 24 m apart, every unit attack-moved into the other block, seed 7, run in both seats.
No abilities, upgrades, terrain or orders beyond the attack-move. Malazan is the balance reference (docs/02 "Template
baseline stats"); the Whirlwind page carries its own winners' rows. The test asserts only the winner as a rule (docs/02
"Faction template"); the margins have no target yet. Every cell of both tables is pinned to the harness's output, so a
sim change that moves a number fails the test naming the row, seat and column until the table is re-printed from it.
"Winner hp left" is the surviving winners' summed hit points.

| Rule | Winner v loser | Winner seat | Fielded (winner v loser) | Winner left | Winner hp left | Winner keeps (cost) | Time to last death |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Line beats Shock | Heavy Infantry v Horse Raider | 0 | 16 (1184) v 13 (1248) | 15 / 16 | 1462 | 1110 / 1184 (94 %) | 23.1 s |
| Line beats Shock | Heavy Infantry v Horse Raider | 1 | 16 (1184) v 13 (1248) | 14 / 16 | 1556 | 1036 / 1184 (88 %) | 20.5 s |
| Line beats Shock | Raider v Wickan Lancer | 0 | 19 (1216) v 10 (1200) | 19 / 19 | 1656 | 1216 / 1216 (100 %) | 17.4 s |
| Line beats Shock | Raider v Wickan Lancer | 1 | 19 (1216) v 10 (1200) | 18 / 19 | 1581 | 1152 / 1216 (95 %) | 18.3 s |
| Shock beats Ranged | Wickan Lancer v Desert Archer | 0 | 10 (1200) v 18 (1224) | 7 / 10 | 828 | 840 / 1200 (70 %) | 25.1 s |
| Shock beats Ranged | Wickan Lancer v Desert Archer | 1 | 10 (1200) v 18 (1224) | 8 / 10 | 888 | 960 / 1200 (80 %) | 22.0 s |
| Shock beats Ranged | Horse Raider v Crossbowman | 0 | 13 (1248) v 15 (1215) | 9 / 13 | 1152 | 864 / 1248 (69 %) | 20.9 s |
| Shock beats Ranged | Horse Raider v Crossbowman | 1 | 13 (1248) v 15 (1215) | 10 / 13 | 1197 | 960 / 1248 (77 %) | 18.2 s |
| Ranged beats casters | Crossbowman v Priest of the Whirlwind | 0 | 15 (1215) v 10 (1200) | 13 / 15 | 444 | 1053 / 1215 (87 %) | 11.6 s |
| Ranged beats casters | Crossbowman v Priest of the Whirlwind | 1 | 15 (1215) v 10 (1200) | 10 / 15 | 442 | 810 / 1215 (67 %) | 11.9 s |
| Ranged beats casters | Desert Archer v Cadre Mage | 0 | 18 (1224) v 8 (1200) | 16 / 18 | 602 | 1088 / 1224 (89 %) | 11.4 s |
| Ranged beats casters | Desert Archer v Cadre Mage | 1 | 18 (1224) v 8 (1200) | 13 / 18 | 525 | 884 / 1224 (72 %) | 13.0 s |

Siege beats buildings (one siege unit, ordered to attack, against the same cost of its faction's line infantry):

| Siege unit | Same cost of line infantry | Building | Siege time | Line time | Siege / line |
| --- | --- | --- | --- | --- | --- |
| 1 Catapult | 5 Heavy Infantry | Tent | 17.0 s | 192.2 s | 9 % |
| 1 Battering Ram | 4 Raider | Billet | 13.8 s | 254.5 s | 5 % |

**Proposed target (a proposal for the owner, not a decision; no number changes until he picks one):** in this scene
the counter's winner keeps **40-65 % of its cost**, in both seats, and the two seats differ by at most 15 points.
Below about 40 % one Forge level (+1 / +1) or a faction bonus on the loser's side can flip the fight, so the counter
stops being reliable; above about 65 % the loser trades away almost nothing, which makes the counter a "don't build
that" decided at build time rather than a fight where positioning matters (docs/01, "Counters matter"). Today every
row is above the band: Line v Shock at 88-100 % is the outlier; Shock v Ranged (69-80 %) and Ranged v casters
(67-89 %) are 2-24 points over. Casters fight without their signature abilities until M4-4, so the Ranged v casters
rows should be re-measured then before any tuning. Siege has no band: buildings are its job, and 5-9 % of the line
infantry's time is the point of the slot.

**Sapper self-splash (BUG-0182 item 2; `Content/SapperSplashReportTests`, same commit).** On a flat map, 4 Sappers
behind 4 Heavy Infantry against 8 Horse Raiders: shipped data, 15 Sharpers thrown, 0 friendly-fire deaths, 41 hit
points of own splash taken (9 of them by the Sapper that threw it); all four Sappers die to the riders, all four
Heavy Infantry live, the riders are wiped out (22.6 s). With `minRange` 2 m: 0 deaths, 61 own splash (0 on the
thrower), same outcome. With splash 1 m: 0 own splash, 2 Sappers and 1 Heavy Infantry left (29.4 s). 4 Sappers alone
lose to the 8 riders in every variant (12 / 3 / 0 own splash). The self-splash is small next to what the riders do;
the Sapper stays "Fragile; wants an escort".
