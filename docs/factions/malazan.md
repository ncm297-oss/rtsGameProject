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

- **Heavy Infantry:** ×1.5 vs Mounted (×1.1 from D13 until D19, 2026-10-15).
  Passive **Shield Wall**: +3 armor against pierce attacks while not moving. Slightly slower than the template line.
- **Crossbowman:** ×1.3 vs Heavy (bolts punch through armor). Slower rate of fire than a bow.
- **Wickan Lancer:** ×1.5 vs Light. Passive **Charge**: the first attack after moving at least
  8 m deals +10 damage.
- **Cadre Mage:** splash 1.5 m, detector 10 m. Signature ability **Telas Fire** (below).
- **Catapult:** splash 2.5 m, **friendly fire**. Can't fire at targets closer than 6 m.
- **Sapper (unique, Age II):** throws **Sharpers**, small munitions with 2.0 m splash and
  **friendly fire**. Ability **Cusser** (below). Fragile; wants an escort.

`Content/UnitContentTests` pins each note's "detector N m" to the unit's `detector` in `units.json`, both ways.

## Abilities

| Ability | Unit | Kind | Range | Radius | Cast | Cooldown | Duration | Effect |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Telas Fire | Cadre Mage | Target ground | 16 | 3 m | 0.8 s | 25 s | — | Enemy units in the area are Burning: 10 magic damage/s for 4 s. No effect on buildings |
| Cusser | Sapper | Target ground | 6 | 3.5 m | 1.0 s | 45 s | — | Enemy units in the area take 120 siege damage, full damage to buildings (≈358 to a Town Hall), friendly fire at 50% |

Telas Fire is the faction's signature caster ability: a short, decisive area denial that punishes
clumped Heavy units. Cusser is the unique unit's identity: a satchel charge that deletes
buildings and can wreck your own army if you're careless.

## Buildings

| Slot | Name | Id | HP | Armor | Cost (G/W) | Build (s) | Footprint | Sight | Provides | Requires |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Town Hall | Garrison Keep | `malazan_garrison_keep` | 2400 | 2 | 275 / 275 | 90 | 4×4 | 12 | +10 pop, drop-off, trains Laborer, researches Age II | — |
| House | Billet | `malazan_billet` | 500 | 1 | 0 / 50 | 20 | 2×2 | 12 | +8 pop | — |
| Camp | Quartermaster's Depot | `malazan_depot` | 600 | 1 | 0 / 75 | 25 | 2×2 | 12 | Drop-off | — |
| Infantry Hall | Legion Barracks | `malazan_barracks` | 1200 | 2 | 0 / 150 | 40 | 3×3 | 12 | Trains Heavy Infantry | — |
| Ranged Hall | Crossbow Range | `malazan_crossbow_range` | 1200 | 2 | 0 / 150 | 40 | 3×3 | 12 | Trains Crossbowman | — |
| Shock Hall | Wickan Corral | `malazan_wickan_corral` | 1200 | 2 | 75 / 150 | 45 | 3×3 | 12 | Trains Wickan Lancer | Legion Barracks |
| Forge | Armory | `malazan_armory` | 1000 | 2 | 100 / 100 | 40 | 3×3 | 12 | Upgrades, Moranth Supply | — |
| Caster Hall | Cadre Tower | `malazan_cadre_tower` | 1200 | 2 | 150 / 150 | 50 | 3×3 | 12 | Trains Cadre Mage | Age II |
| Siege Works | Engineers' Yard | `malazan_engineers_yard` | 1400 | 2 | 150 / 200 | 55 | 3×3 | 12 | Trains Catapult, Sapper | Age II |
| Watch Tower | Watchtower | `malazan_watchtower` | 800 | 2 | 50 / 125 | 35 | 2×2 | 24 | Attack 10 pierce / 2 s, range 18; sight 24; detector 16 m | Age II |

Building stats follow the template in [02-game-design.md](../02-game-design.md#buildings). Every building's armor was halved, rounded down, in D19 (2026-10-15, the owner's building damage set (C)): Town Hall and Watch Tower 5 → 2, House and Camp 3 → 1, the rest 4 → 2.

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

## AI build order (Normal)

The AI opponent's `game/data/factions/malazan/ai.json` (docs/03 "For the data track (M5-1)" and "Implementation (M5-8a)")
holds three **openings**, each a build order, an army mix per age and an attack threshold. **An Easy AI always plays
opening 1, `standard`**; a Normal or Hard AI picks one of the three from the match seed at the start (docs/02 "AI
opponent"), so two matches against the Malazans can open differently, and a replay or a save plays the same one again.
The name and description below are what the player may read (a stats line "the AI opened with …"). Each step table has
one row per `buildOrder` step, in file order. The steps run side by side, not one after another: each think the executor
works every step whose trigger is reached and which is not done, in this order, one command per step (a step earlier in
the list gets the gold first). **At** is the trigger: `time` in seconds of match time, `pop` the AI's population
(workers and army). **Count** is how many `train` commands the step queues over its life; a `build`, `research` or
`expand` is done once its command is accepted (1). The AI starts with 5 Laborers, 3 on gold and 2 on wood (the economy
manager's split, not a step). On Easy each step waits `buildOrderSlack` (30 s) after it is due, on Hard none.
`Content/AiContentTests` pins the openings table, every opening's step table, composition table and wave line, and the
retreat line below to the file, both ways; `Content/AiOpeningPairsReportTests` plays every Malazan opening against every
Whirlwind opening (9 Normal AI-v-AI matches, 20 minutes each, the menu's map) and fails unless both sides send a wave and
no step is a dead end.

| # | Id | Name (shown to the player) | Description (shown to the player) |
| --- | --- | --- | --- |
| 1 | `standard` | Combined arms | Barracks, Crossbow Range and Armory first; waves of shields and crossbows from 16 army pop, then mages, siege and Wickan cavalry in Age II. |
| 2 | `crossbow_push` | Crossbow push | A Crossbow Range before the Barracks, then a second: an early wave of crossbows behind a thin shield line at 14 army pop. |
| 3 | `cadre_rush` | Fast Age II | Only a Barracks and an Armory before racing to Age II, then two Cadre Towers: few soldiers early, many mages later. The first wave waits for 18 army pop. |

Not in the file, because the schema has no field for it (requests to the sim track): the sketch's expansion condition
(army ≥ 12 pop and no enemy army near the base for 60 s; the step fires at its time), the Barracks rally to the base
choke and the Watchtower's spot at the choke (the executor places every building near the Keep), the 3 : 2 gold /
wood split (the economy manager's `goldShare`, a common rule), and a reserve for a due step: the AI saves nothing for
Age II (400 gold / 200 wood), so production spends first and Age II comes in minutes after its step is due (each
opening's spending note gives the time; docs/03 "Implementation (M5-2)", the production manager).

- **Retreat:** a wave that has lost 50% falls back; a unit under 30% hp counts as wounded (`retreat`, shared by the
  openings; Hard pulls it back).
- **Later waves** (every opening): one wave at a time; a new one starts when none is out, at least `ai.waveCooldown`
  after the last began, at the opening's threshold, and never while the base is defended.
- **Defense** (docs/02 "AI opponent", docs/03 "Implementation (M5-4b)" and "(M5-4f)"; re-worded in D20, 2026-10-17, for
  M5-4f's sized answer): a threat starts when an own building (a site too) within the defend radius of the Garrison Keep
  is hit, or an enemy soldier (not a worker) is seen within it (`rules.json` `ai.defendRadius` 48 m; the threat holds
  `ai.defendFor` 20 s after the last sign). The AI answers with defenders worth `ai.defendRatio` 1.5 times the
  intruders' gold + wood cost, at least `ai.defendMinValue` 150 (a building hit with nobody seen gets 150), taken from
  the army at home first (nearest the defense point first), then from the members of a wave out (nearest the Garrison
  Keep first), each only while the members left keep the wave's start threshold (`attack.minArmyPop` and `minArmyCost`
  times the difficulty's wave size ×, so a wave sent above it gives its surplus). One Raider (64 gold + wood) is answered with 150 worth, e.g. two Heavy Infantry from home (148), and a wave out keeps
  going. A raid worth more than the home army and the wave's spare units together calls the whole wave home (it ends
  like a retreat) and every free army unit answers. A hit that leaves the Garrison Keep below `ai.recallHallHp` 0.5 of
  its max hp does the same, however small the raid. Laborers within `ai.workerFleeRadius` (16 m) of the threat flee to the nearest finished drop-off away from the
  threat (the Garrison Keep or a Quartermaster's Depot, whichever is nearest among those more than 16 m from the threat;
  else the nearest one).

### Opening 1: `standard` (Combined arms)

The order the AI has played since D14, step for step; Easy plays only this one. D21 (2026-10-18) gave it its name and
description and lowered the wave from 20 army pop / 1,544 to 16 / 1,235: at 20 the Malazans never sent a wave against the
Whirlwind's raider rush (seed 2: the base was defended at 779 of its 1,200 thinks and the army peaked at 15 pop).

| # | At | Do | Id | Count | Why |
| --- | --- | --- | --- | --- | --- |
| 1 | time 0 | train | `malazan_laborer` | 15 | Workers to 20 in Age I (5 at the start + 15) |
| 2 | pop 6 | build | `malazan_billet` | 1 | First Billet |
| 3 | pop 8 | build | `malazan_depot` | 1 | Quartermaster's Depot at the nearest forest |
| 4 | pop 10 | build | `malazan_barracks` | 1 | Legion Barracks |
| 5 | pop 12 | build | `malazan_billet` | 1 | Second Billet |
| 6 | pop 14 | build | `malazan_crossbow_range` | 1 | Crossbow Range |
| 7 | pop 16 | build | `malazan_armory` | 1 | The Forge |
| 8 | pop 16 | research | `melee_weapons_1` | 1 | Its first upgrade, Melee Weapons (refused, and retried, until the Armory stands) |
| 9 | pop 18 | build | `malazan_billet` | 1 | From pop 18 a Billet every 7 pop |
| 10 | pop 25 | build | `malazan_billet` | 1 | |
| 11 | pop 32 | build | `malazan_billet` | 1 | |
| 12 | pop 39 | build | `malazan_billet` | 1 | |
| 13 | pop 46 | build | `malazan_billet` | 1 | |
| 14 | pop 53 | build | `malazan_billet` | 1 | |
| 15 | pop 60 | build | `malazan_billet` | 1 | |
| 16 | pop 67 | build | `malazan_billet` | 1 | |
| 17 | pop 74 | build | `malazan_billet` | 1 | |
| 18 | pop 81 | build | `malazan_billet` | 1 | The 12th Billet: Keep 10 + 12 × 8 = 106, past the 100 cap, kept on the measure below ("Houses at the cap") |
| 19 | time 390 | research | `age_ii` | 1 | Age II at 6:30 (needs two of Barracks, Range, Armory: all three stand) |
| 20 | time 390 | train | `malazan_laborer` | 10 | Workers to 30 in Age II |
| 21 | time 450 | build | `malazan_cadre_tower` | 1 | 7:30, when Age II (60 s) is in |
| 22 | time 480 | build | `malazan_engineers_yard` | 1 | Catapults and Sappers |
| 23 | time 480 | expand | — | 1 | 2nd Garrison Keep near the nearest free gold mine, at Normal's `expandAfter` (480 s) |
| 24 | time 510 | build | `malazan_wickan_corral` | 1 | The Corral needs only the Barracks; the AI waits for Age II so Age I spends on the line |
| 25 | time 540 | research | `moranth_supply` | 1 | The faction upgrade, at the Armory |
| 26 | time 570 | build | `malazan_watchtower` | 1 | Watchtower |

**Houses at the cap (D21, 2026-10-18, D15 C's parked finding measured; `QA/Content/AiHouseRuleReportTests.MenuMapNormal_HouseSources_Report` and `MenuMapNormal_TwelfthHouse_Report`).**
Since M5-4f the executor skips a house step once the population cap is the hard cap (100): the step is done with no
command sent and no Billet placed. On the menu's map (20 minutes, the other seat idle): Normal, seed 1: the cap reaches
100 at 727 s; the last Billet step (step 18) is done at a cap of 90; skipped at the hard cap: none; 12 Billets stand at
20:00, 0 past the cap. The 12th Billet is due at a cap of 90 and leaves 6 of its 8 pop unused, so D21 measured taking it
out: without the 12th Billet step (step 18) on the same seed, the cap reaches 100 at 985 s, and 12 Billets stand at
20:00, 0 past the cap. The economy manager's house rule places the 12th Billet anyway (the expansion's Keep rarely
stands: it waits for a known free mine), only later, so the step stays, in every opening.

**Spending on the menu's map (D21, 2026-10-18, gathering gold 0.85 / wood 0.75 a second; `Content/AiSpendingReportTests.GeneratedNormal_Spending_Report`).**
Against an idle seat, 12 forests and 8 mines (the menu's), 20 minutes. Normal, seed 1 (the smallest seed in 1-50 whose
Normal pick is this opening): the idle base falls at 498 s; in hand at 5 min 156 gold / 25 wood; the first wave at 338
s; Age II in at 938 s. Easy, seed 1 (Easy always plays the first opening): the idle base falls at 497 s; in hand at 5
min 72 gold / 0 wood; the first wave at 332 s; Age II in at 844 s. Age II comes in about 8 minutes after its step is due
at 6:30: the production manager spends the gold first (no reserve, see above). D20's two-seed note (512 s / 536 s,
seeds 1 and 7) measured the old 20-pop wave and both seeds; the per-seed runs are now one per opening.

**Army composition (`compositions`), weights by pop.** Age I lists only the units the Age I steps can train (the Corral
waits for Age II), so its weights sum to 65.

| Unit | Age I | Age II |
| --- | --- | --- |
| `malazan_heavy_infantry` | 35 | 35 |
| `malazan_crossbowman` | 30 | 30 |
| `malazan_wickan_lancer` | — | 10 |
| `malazan_cadre_mage` | — | 10 |
| `malazan_sapper` | — | 10 |
| `malazan_catapult` | — | 5 |

- **Composition target (by pop):** 35% Heavy Infantry, 30% Crossbowman, 10% Wickan Lancer,
  10% Cadre Mage, 10% Sapper, 5% Catapult.
- **Attack waves:** first at 16 army pop and 1,235 gold + wood (`attack`; the cost of 16 pop of the Age I mix, rounded
  down). Sappers target buildings; Mages cast Telas Fire on the largest enemy clump.

### Opening 2: `crossbow_push` (Crossbow push)

Crossbows first: a Crossbow Range before the Barracks and a second Range at pop 20, Ranged Weapons at the Armory, and
an Age I mix of 60% Crossbowmen to 40% Heavy Infantry (36 : 24) that waves at 14 army pop. More Laborers than the
standard (23 in Age I): two Ranges and a crossbow army eat the wood.

| # | At | Do | Id | Count | Why |
| --- | --- | --- | --- | --- | --- |
| 1 | time 0 | train | `malazan_laborer` | 18 | Workers to 23 in Age I (5 at the start + 18): the Ranges and the crossbows need the wood |
| 2 | pop 6 | build | `malazan_billet` | 1 | First Billet |
| 3 | pop 8 | build | `malazan_depot` | 1 | Quartermaster's Depot at the nearest forest |
| 4 | pop 9 | build | `malazan_crossbow_range` | 1 | The first Crossbow Range, before the Barracks |
| 5 | pop 11 | build | `malazan_billet` | 1 | Second Billet |
| 6 | pop 13 | build | `malazan_barracks` | 1 | Legion Barracks: the shield line |
| 7 | pop 18 | build | `malazan_billet` | 1 | From pop 18 a Billet every 7 pop |
| 8 | pop 20 | build | `malazan_crossbow_range` | 1 | The second Crossbow Range |
| 9 | pop 22 | build | `malazan_armory` | 1 | The Forge |
| 10 | pop 22 | research | `ranged_weapons_1` | 1 | Its first upgrade, Ranged Weapons: most of the army shoots (refused, and retried, until the Armory stands) |
| 11 | pop 25 | build | `malazan_billet` | 1 | |
| 12 | pop 32 | build | `malazan_billet` | 1 | |
| 13 | pop 39 | build | `malazan_billet` | 1 | |
| 14 | pop 46 | build | `malazan_billet` | 1 | |
| 15 | pop 53 | build | `malazan_billet` | 1 | |
| 16 | pop 60 | build | `malazan_billet` | 1 | |
| 17 | pop 67 | build | `malazan_billet` | 1 | |
| 18 | pop 74 | build | `malazan_billet` | 1 | |
| 19 | pop 81 | build | `malazan_billet` | 1 | The 12th Billet, as in the standard opening |
| 20 | time 420 | research | `age_ii` | 1 | Age II at 7:00, after the push (needs two of Range, Barracks, Armory) |
| 21 | time 420 | train | `malazan_laborer` | 7 | Workers to 30 in Age II |
| 22 | time 480 | build | `malazan_engineers_yard` | 1 | Catapults and Sappers to break what the crossbows can't |
| 23 | time 480 | expand | — | 1 | 2nd Garrison Keep near the nearest free gold mine, at Normal's `expandAfter` (480 s) |
| 24 | time 510 | build | `malazan_cadre_tower` | 1 | Cadre Mages |
| 25 | time 540 | build | `malazan_wickan_corral` | 1 | Wickan Lancers |
| 26 | time 570 | research | `moranth_supply` | 1 | The faction upgrade (Catapult range + 4 m) |

**Houses at the cap (D21, 2026-10-18; `QA/Content/AiHouseRuleReportTests.MenuMapNormal_HouseSources_Report`).** On the
menu's map (20 minutes, the other seat idle): Normal, seed 5: the cap reaches 100 at 650 s; the last Billet step (step
19) is done at a cap of 98; skipped at the hard cap: none; 12 Billets stand at 20:00, 0 past the cap.

**Spending on the menu's map (D21, 2026-10-18, gathering gold 0.85 / wood 0.75 a second; `Content/AiSpendingReportTests.GeneratedNormal_Spending_Report`).**
Against an idle seat, the menu's map, 20 minutes. Normal, seed 5 (the smallest seed in 1-50 whose Normal pick is this
opening): the idle base falls at 474 s; in hand at 5 min 282 gold / 30 wood; the first wave at 341 s; Age II in at 943 s.

**Army composition (`compositions`), weights by pop.** Age I is the two units the Age I steps train, 36 : 24 (60% /
40%), so its weights sum to 60.

| Unit | Age I | Age II |
| --- | --- | --- |
| `malazan_crossbowman` | 36 | 36 |
| `malazan_heavy_infantry` | 24 | 24 |
| `malazan_wickan_lancer` | — | 10 |
| `malazan_cadre_mage` | — | 10 |
| `malazan_sapper` | — | 10 |
| `malazan_catapult` | — | 10 |

- **Composition target (by pop):** 36% Crossbowman, 24% Heavy Infantry, 10% Wickan Lancer,
  10% Cadre Mage, 10% Sapper, 10% Catapult.
- **Attack waves:** first at 14 army pop and 1,094 gold + wood (`attack`; the cost of 14 pop of the Age I mix, rounded
  down: the lowest Malazan threshold).

### Opening 3: `cadre_rush` (Fast Age II)

Age II first: only the Barracks and the Armory (Age II's two kinds of building) before the Age II step at 5:00, so one
hall's queue spends while the gold piles up; then two Cadre Towers, and an Age II mix of 30% Cadre Mages. The Crossbow
Range waits for Age II, so Age I trains Heavy Infantry only.

| # | At | Do | Id | Count | Why |
| --- | --- | --- | --- | --- | --- |
| 1 | time 0 | train | `malazan_laborer` | 15 | Workers to 20 in Age I (5 at the start + 15) |
| 2 | pop 6 | build | `malazan_billet` | 1 | First Billet |
| 3 | pop 8 | build | `malazan_depot` | 1 | Quartermaster's Depot at the nearest forest |
| 4 | pop 10 | build | `malazan_barracks` | 1 | Legion Barracks: the one hall of Age I |
| 5 | pop 12 | build | `malazan_billet` | 1 | Second Billet |
| 6 | pop 14 | build | `malazan_armory` | 1 | The Forge: Age II's second kind of building, and no queue to spend the gold |
| 7 | pop 18 | build | `malazan_billet` | 1 | From pop 18 a Billet every 7 pop |
| 8 | pop 25 | build | `malazan_billet` | 1 | |
| 9 | pop 32 | build | `malazan_billet` | 1 | |
| 10 | pop 39 | build | `malazan_billet` | 1 | |
| 11 | pop 46 | build | `malazan_billet` | 1 | |
| 12 | pop 53 | build | `malazan_billet` | 1 | |
| 13 | pop 60 | build | `malazan_billet` | 1 | |
| 14 | pop 67 | build | `malazan_billet` | 1 | |
| 15 | pop 74 | build | `malazan_billet` | 1 | |
| 16 | pop 81 | build | `malazan_billet` | 1 | The 12th Billet, as in the standard opening |
| 17 | time 300 | research | `age_ii` | 1 | Age II at 5:00, 90 s before the standard's step (needs two kinds: the Barracks and the Armory) |
| 18 | time 300 | train | `malazan_laborer` | 10 | Workers to 30 in Age II |
| 19 | time 360 | build | `malazan_cadre_tower` | 1 | The first Cadre Tower, as soon as Age II (60 s) can be in |
| 20 | time 390 | build | `malazan_crossbow_range` | 1 | Crossbowmen for Age II |
| 21 | time 420 | build | `malazan_cadre_tower` | 1 | The second Cadre Tower: mages from two towers |
| 22 | time 480 | build | `malazan_engineers_yard` | 1 | Catapults and Sappers |
| 23 | time 480 | expand | — | 1 | 2nd Garrison Keep near the nearest free gold mine, at Normal's `expandAfter` (480 s) |
| 24 | time 540 | build | `malazan_wickan_corral` | 1 | Wickan Lancers |
| 25 | time 570 | research | `moranth_supply` | 1 | The faction upgrade, at the Armory |

**Houses at the cap (D21, 2026-10-18; `QA/Content/AiHouseRuleReportTests.MenuMapNormal_HouseSources_Report`).** On the
menu's map (20 minutes, the other seat idle): Normal, seed 7: the cap reaches 100 at 835 s; the last Billet step (step
16) is done at a cap of 98; skipped at the hard cap: none; 12 Billets stand at 20:00, 0 past the cap.

**Spending on the menu's map (D21, 2026-10-18, gathering gold 0.85 / wood 0.75 a second; `Content/AiSpendingReportTests.GeneratedNormal_Spending_Report`).**
Against an idle seat, the menu's map, 20 minutes. Normal, seed 7 (the smallest seed in 1-50 whose Normal pick is this
opening): the idle base falls at 619 s; in hand at 5 min 198 gold / 455 wood, at 10 min 99 / 490; the first wave at 487
s; Age II in at 509 s. The fastest Malazan Age II by far (the others: 938 s and 943 s), though not the step's 6:00: the
one Barracks still spends gold before 400 piles up.

**Army composition (`compositions`), weights by pop.** Age I lists only Heavy Infantry (the one unit the Age I steps can
train), so its weights sum to 30.

| Unit | Age I | Age II |
| --- | --- | --- |
| `malazan_heavy_infantry` | 30 | 30 |
| `malazan_crossbowman` | — | 20 |
| `malazan_cadre_mage` | — | 30 |
| `malazan_wickan_lancer` | — | 10 |
| `malazan_sapper` | — | 5 |
| `malazan_catapult` | — | 5 |

- **Composition target (by pop):** 30% Heavy Infantry, 20% Crossbowman, 30% Cadre Mage,
  10% Wickan Lancer, 5% Sapper, 5% Catapult.
- **Attack waves:** first at 18 army pop and 1,332 gold + wood (`attack`; the cost of 18 pop of the Age I mix, Heavy
  Infantry at 74 a pop). Mages cast Telas Fire on the largest enemy clump.

## Difficulty

The `difficulties` block of the same `game/data/factions/malazan/ai.json` (docs/02 "AI opponent", docs/03 "For the data
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

Measured at sim commit `dd5b5b9` by `Content/CounterTriangleMarginsTests` (D6), re-printed at `1a9925b` (D8) and, for the four Line v Shock rows, in D13 (2026-10-11), and for those four rows and the siege rows in D19 (2026-10-15), the siege rows again on the merged tree with the sim's surround rule (M5-P1, 2026-10-16, BUG-0734), and the group table re-printed with the sim's flank rule (M5-P2, 2026-10-17, BUG-0900), and its Desert Archer v Cadre Mage seat-1 "Winner hp left" cell with the sim's stand-point change (M5-H3, 2026-10-17, BUG-0790: 522 to 523, nothing else moved), from the shared
`Scenario/CounterTriangleScene` harness, the scene of
`Scenario/CounterTriangleTests`: a flat map, 1,200 gold + wood a side (the whole number of units nearest to it), two
blocks 5 wide whose fronts start 24 m apart, every unit attack-moved into the other block, seed 7, run in both seats.
No abilities, upgrades, terrain or orders beyond the attack-move. Malazan is the balance reference (docs/02 "Template
baseline stats"); the Whirlwind page carries its own winners' rows. The test asserts only the winner as a rule (docs/02
"Faction template"); the margins are not asserted (the target band below is a Producer default). Every cell of both tables is pinned to the harness's output, so a
sim change that moves a number fails the test naming the row, seat and column until the table is re-printed from it.
"Winner hp left" is the surviving winners' summed hit points.

| Rule | Winner v loser | Winner seat | Fielded (winner v loser) | Winner left | Winner hp left | Winner keeps (cost) | Time to last death |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Line beats Shock | Heavy Infantry v Horse Raider | 0 | 16 (1184) v 13 (1248) | 16 / 16 | 1544 | 1184 / 1184 (100 %) | 17.9 s |
| Line beats Shock | Heavy Infantry v Horse Raider | 1 | 16 (1184) v 13 (1248) | 16 / 16 | 1584 | 1184 / 1184 (100 %) | 16.3 s |
| Line beats Shock | Raider v Wickan Lancer | 0 | 19 (1216) v 10 (1200) | 18 / 19 | 1592 | 1152 / 1216 (95 %) | 15.6 s |
| Line beats Shock | Raider v Wickan Lancer | 1 | 19 (1216) v 10 (1200) | 18 / 19 | 1570 | 1152 / 1216 (95 %) | 15.9 s |
| Shock beats Ranged | Wickan Lancer v Desert Archer | 0 | 10 (1200) v 18 (1224) | 8 / 10 | 1044 | 960 / 1200 (80 %) | 15.9 s |
| Shock beats Ranged | Wickan Lancer v Desert Archer | 1 | 10 (1200) v 18 (1224) | 8 / 10 | 1050 | 960 / 1200 (80 %) | 16.4 s |
| Shock beats Ranged | Horse Raider v Crossbowman | 0 | 13 (1248) v 15 (1215) | 12 / 13 | 1332 | 1152 / 1248 (92 %) | 14.8 s |
| Shock beats Ranged | Horse Raider v Crossbowman | 1 | 13 (1248) v 15 (1215) | 11 / 13 | 1332 | 1056 / 1248 (85 %) | 14.6 s |
| Ranged beats casters | Crossbowman v Priest of the Whirlwind | 0 | 15 (1215) v 10 (1200) | 13 / 15 | 380 | 1053 / 1215 (87 %) | 13.4 s |
| Ranged beats casters | Crossbowman v Priest of the Whirlwind | 1 | 15 (1215) v 10 (1200) | 9 / 15 | 325 | 729 / 1215 (60 %) | 13.6 s |
| Ranged beats casters | Desert Archer v Cadre Mage | 0 | 18 (1224) v 8 (1200) | 16 / 18 | 618 | 1088 / 1224 (89 %) | 9.6 s |
| Ranged beats casters | Desert Archer v Cadre Mage | 1 | 18 (1224) v 8 (1200) | 16 / 18 | 523 | 1088 / 1224 (89 %) | 11.4 s |

Siege beats buildings (one siege unit, ordered to attack, against the same cost of its faction's line infantry):

| Siege unit | Same cost of line infantry | Building | Siege time | Line time | Siege / line |
| --- | --- | --- | --- | --- | --- |
| 1 Catapult | 5 Heavy Infantry | Tent | 17.0 s | 30.2 s | 56 % |
| 1 Battering Ram | 4 Raider | Billet | 13.7 s | 31.2 s | 44 % |

**Target band (proposed in D6; adopted as the Producer's default on 2026-10-11, owner may revisit):** in this scene
the counter's winner keeps **40-65 % of its cost**, in both seats, and the two seats differ by at most 15 points.
Below about 40 % one Forge level (+1 / +1) or a faction bonus on the loser's side can flip the fight, so the counter
stops being reliable; above about 65 % the loser trades away almost nothing, which makes the counter a "don't build
that" decided at build time rather than a fight where positioning matters (docs/01, "Counters matter"). Every row
but one is still above the band: Crossbowman v Priest in seat 1 (60 %) is inside it, but its seats are 27 points apart
(87 / 60 %), over the 15-point limit. **The sim's flank rule (M5-P2, 2026-10-17, BUG-0900):** melee units blocked by
their own ranks now walk round them, so melee blocks engage sooner and more of them strike at once; every fight with a
melee winner ends 1.0-9.2 s sooner and most winners keep more (Heavy Infantry v Horse Raider 94 / 88 % to
100 / 100 %, Wickan Lancer v Desert Archer 70 / 80 % to 80 / 80 %, Horse Raider v Crossbowman 69 / 77 % to 92 / 85 %;
Raider v Wickan Lancer seat 0 loses one Raider, 100 to 95 %). Desert Archer v Cadre Mage ends 1.6-1.8 s sooner (seat 1
72 to 89 %). Crossbowman v Priest moves the other way (87 / 67 % to 87 / 60 %, 1.7-1.8 s longer: Crossbowmen flanking
their own front spread out under the storms). Every winner is unchanged. **Line v Shock (D13):** the one lever tried was the line units' bonus vs Mounted, the same
value on both factions, swept 1.5 / 1.4 / 1.3 / 1.2 / 1.1 / 1.0 (the four Line v Shock cells, in order, D13, measured
on the movement before pathing slice 2: 94 / 88 / 100 / 95 % at 1.5, 88 / 94 / 100 / 89 % at 1.4, 88 / 94 / 95 / 89 % at 1.3, 94 / 88 / 100 / 89 % at 1.2, 88 / 88 / 100 / 89 % at
1.1, 88 / 94 / 84 / 79 % at 1.0). No value reaches the band and Line wins every cell at every value. 1.0 came nearest,
but it broke a sim stress floor (seed 11 of the Sandstorm fuzz cast 4 storms, its floor is 5), so D13 shipped
one step back, ×1.1. "Nearest" here means the next step of the sweep, not the best score among the passing values:
1.3 is a near tie (points over 65 % summed across the four cells, 106 at 1.3 v 105 at 1.1) with the smaller worst
cell (95 v 100 %) and the closer seats (6 / 6 points apart v 0 / 11). **D19 (2026-10-15):** the owner set the bonus
back to ×1.5, so the four cells were the ×1.5 ones (94 / 88 / 100 / 95 %, D19's movement); with the sim's flank rule
(M5-P2) they are 100 / 100 / 95 / 95 %. D18's sweep of the line units' hp (100-140), armor (1-3) and gold (50-80) at
×1.5 found no set in the band either (measured on the movement before pathing slice 2; nearest: Heavy Infantry at hp
100 and armor 1, 63 / 75 %; Raider at gold 80, 85 / 85 %; `Content/LineShockSweepReportTests`), so the Shock side is
the next lever, the owner's call. Shock v Ranged (80-92 %) is 15-27 points over; Ranged v casters (60-89 %) is up to
24 points over, its one in-band cell (60 %) 27 points from its other seat. Casters fight without their signature abilities until M4-4, so the Ranged v casters
rows should be re-measured then before any tuning. Siege has no band: buildings are its job. Until D19 it razed in
5-9 % of the same cost of line infantry's time; D19's building damage (the Structure column and the halved building
armor, the owner's set (C)) speeds the line infantry up far more than siege (34-47 % of their time on D19's movement),
and the sim's surround rule (M5-P1, 2026-10-16: more of a group strikes a building at once) speeds them up again, so
siege now takes 44-56 % of their time, still about 1.8-2.3× faster (and at least 2× their damage per cost against a
House, a hall and a Town Hall: `Content/BuildingKillTimeReportTests`). Checked against both line rows (D-H5,
2026-10-16, BUG-0721): 2 Catapults do 3.9 / 2.3 / 2.3× the 20 Heavy Infantry's damage per cost (House / hall / Town
Hall) but only 2.8 / 1.7 / 1.6× the 20 Raiders' (the cheaper, faster line), so on a hall and a Town Hall the Catapult is
under 2× the Raider's; 2 Sappers do 4.8-5.7× and 3.3-4.1×. Pinned as measured, the owner's call (BUG-0780).

**Sapper self-splash (BUG-0182 item 2; `Content/SapperSplashReportTests`, same commit).** On a flat map, 4 Sappers
behind 4 Heavy Infantry against 8 Horse Raiders: shipped data, 15 Sharpers thrown, 0 friendly-fire deaths, 41 hit
points of own splash taken (9 of them by the Sapper that threw it); all four Sappers die to the riders, all four
Heavy Infantry live, the riders are wiped out (22.6 s). With `minRange` 2 m: 0 deaths, 61 own splash (0 on the
thrower), same outcome. With splash 1 m: 0 own splash, 2 Sappers and 1 Heavy Infantry left (29.4 s). 4 Sappers alone
lose to the 8 riders in every variant (12 / 3 / 0 own splash). The self-splash is small next to what the riders do;
the Sapper stays "Fragile; wants an escort".
