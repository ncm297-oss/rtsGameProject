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
Whirlwind opening (9 Normal AI-v-AI matches, 20 minutes each, the menu's map: on this page the skirmish setup's default
Generated map, 12 forests and 8 mines; no hand-made map was measured) and fails unless both sides send a wave and
no step is a dead end, and with `QA/Content/D21OpeningsQaTests` (each pairing's 2nd and 3rd seeds) pins every side's
Age II to "Age II against the Whirlwind" at the end of this page. A step still short of gold or wood at 20:00 prints
how long it has been due and the banks; a wait over 5 minutes is marked as BUG-0981's pattern (see "The reserve" below
the openings table), printed but not failed.

| # | Id | Name (shown to the player) | Description (shown to the player) |
| --- | --- | --- | --- |
| 1 | `standard` | Combined arms | Barracks, Crossbow Range and Armory first; waves of shields and crossbows from 16 army pop, then mages, siege and Wickan cavalry in Age II. |
| 2 | `crossbow_push` | Crossbow push | A Crossbow Range before the Barracks, then a second: an early wave of crossbows behind a thin shield line at 14 army pop. |
| 3 | `cadre_rush` | Fast Age II | Only a Barracks and an Armory before racing to Age II, then two Cadre Towers: few soldiers early, many mages later. The first wave waits for 16 army pop. |

Not in the file, because the schema has no field for it (requests to the sim track): the sketch's expansion condition
(army ≥ 12 pop and no enemy army near the base for 60 s; the step fires at its time), the Barracks rally to the base
choke and the Watchtower's spot at the choke (the executor places every building but the Depot near the Keep), and the 3 : 2 gold /
wood split (the economy manager's `goldShare`, a common rule).

**The reserve (BUG-0981, fixed by the sim's M5-4g; re-printed 2026-10-26).** Until M5-4g the AI saved nothing for a
due step: production spent first, and Age II (400 gold / 200 wood) came in about 8 minutes after its step was due
(938 s and 943 s on the menu's map against an idle seat). Now a due building, research or expansion step's cost is
kept in reserve while the army, at home or out on a wave, is at least the opening's wave in pop and cost and the base
is not being defended (docs/02 "AI opponent"; `rules.json` `ai.reserveShift` 0.25, `ai.reserveArmyShare` 1). Against
an idle seat every opening's Age II is now in at 509-568 s (each opening's spending note). Against the Whirlwind it
comes later (the table at the end of this page) and 4 of the 27 measured Malazan sides never get it, 2 of them
defeated first. What is left is BUG-1010: an army that never reaches its wave's size never opens the reserve, and
against `raider_rush` those are the Malazan sides that fall, or are beaten down, without ever attacking ("Fast Age II
against the raider rush" at the end of this page). The long waits the pairings report marks are still there (docs/03
"Implementation (M5-4g)": mostly expansions with no free mine known and Age II buildings).

- **Retreat:** a wave that has lost 50% falls back; a unit under 30% hp counts as wounded (`retreat`, shared by the
  openings; Hard pulls it back).
- **Later waves** (every opening): one wave at a time; a new one starts when none is out, at least `ai.waveCooldown`
  after the last began, at the opening's threshold, and never while the base is defended.
- **High starting resources** (docs/02 "Game flow" 2, docs/03 "Implementation (M6-S6)"): the AI plays the same openings
  as at Standard, but while it holds more gold + wood than the preset's spend-down threshold (`rules.json`
  `startingResources` `high` `aiSpendAbove`) it brings its next timed step forward, trains at every producer and leans
  its gatherers toward the kind it holds less of; Standard has no threshold, so it plays exactly as before. Measured (QA,
  M6-S6, 2026-11-05; not pinned): `cadre_rush` held 1,539-1,769 gold + wood at 5:00 at High before the rule (3 seeds) and
  70-466 with it (6 seeds nobody tuned on).
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

#### How the AI adapts

Three rules sit on top of every opening above, the counter shift, the starved charge and the far expansion, and two
notes go with the first: what answers what, and where the shift doesn't show (docs/02 "AI opponent"; docs/03
"Implementation (M5-9a)" and "(M5-9b)"). `Content/AiAdaptsContentTests` pins every number and name in the five bullets
to `ai.json`, `rules.json` and `buildings.json`, both ways.

- **Counter shift:** the AI remembers how many enemy soldiers of each slot it has seen at once (workers never count, and
  it sees only what its own fog of war shows), and that memory fades to nothing over `rules.json` `ai.counterMemory`
  180 s unless a sighting at least as big renews it. When it picks its next unit, each unit of the opening's army mix
  weighs more by the share of that memory its slot answers, times the difficulty's `counterStrength` in `ai.json`: Easy
  0 (an Easy AI never adapts), Normal 0.5, Hard 1.0. A Malazan AI that has seen only cavalry weighs its Heavy Infantry
  ×1.5 on Normal and ×2 on Hard. The shift tilts the army, it doesn't replace it: no unit is weighted below the
  opening's mix, and a unit the mix doesn't list is never added.
- **What answers what** (`rules.json` `ai.counters`, the "Countered by" column of docs/02 "Faction template", in this
  faction's units): enemy Line → Cadre Mage, Catapult; enemy Ranged → Wickan Lancer; enemy Shock → Heavy Infantry;
  enemy Caster → Wickan Lancer, Crossbowman; enemy Siege → Heavy Infantry, Wickan Lancer; enemy Unique → none.
- **Starved charge:** when the AI cannot pay for the unit it would train next and no income can close the gap, and it
  has at least `ai.starvedMinArmy` 1 combat unit free, then after `ai.starvedWaveAfter` 60 s in that state it sends
  every free combat unit at you whatever its wave threshold (or hunts, if it knows none of your buildings), again after
  each wave's cooldown, at every difficulty. A raid on its base resets the clock.
- **Far expansion:** when its own gold is spent (short of gold for its next unit, no Laborer on gold and no gold mine it
  knows), it sends a Laborer to look at the start locations nobody took. Once it has seen a free mine (at one of those
  starts or anywhere else) within `ai.farExpandReach` 384 m of its Garrison Keep, with no enemy drop-off by it, it
  builds a Quartermaster's Depot (`malazan_depot`, 0 gold / 75 wood) beside it and moves gatherers there;
  a Garrison Keep only if it can pay for one. One at a time. The reach holds for every mine, a start location's
  included: a free start farther than that from the keep is not walked to, and a mine beyond it is never taken.
- **Where the shift doesn't show (BUG-1156):** a Legion Barracks that already takes a Heavy Infantry every time its
  queue empties (the `standard` opening's one barracks in Age I, short of wood) cannot train more of them for the
  heavier weight, so against cavalry the shift shows only when the bank, not the barracks, is the limit. An enemy Line
  army seen in Age I shifts nothing, since no opening's Age I mix lists a Cadre Mage or a Catapult.

### Opening 1: `standard` (Combined arms)

The order the AI has played since D14, step for step; Easy plays only this one. D21 (2026-10-18) gave it its name and
description and lowered the wave from 20 army pop / 1,544 to 16 / 1,235: at 20 the Malazans never sent a wave against the
Whirlwind's raider rush (seed 2: the base was defended at 779 of its 1,200 thinks and the army peaked at 15 pop).

| # | At | Do | Id | Count | Why |
| --- | --- | --- | --- | --- | --- |
| 1 | time 0 | train | `malazan_laborer` | 15 | Workers to 20 in Age I (5 at the start + 15) |
| 2 | pop 6 | build | `malazan_billet` | 1 | First Billet |
| 3 | pop 8 | build | `malazan_depot` | 1 | Quartermaster's Depot by the nearest known forest |
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
command sent and no Billet placed. On the menu's map (20 minutes, the other seat idle): Normal, seed 1: the cap reaches 100 at 771 s; the last Billet step (step 18) is done at a cap of 100; skipped at the hard cap: steps 17, 18; 10 Billets stand at 20:00, 0 past the cap. The 12th Billet leaves 6 of its 8 pop unused when it is placed, so D21 measured taking it out:
without the 12th Billet step (step 18) on the same seed, the cap reaches 100 at 771 s, and 10 Billets stand at 20:00, 0
past the cap (re-printed 2026-11-16 for the sim's M6-S12: 789 s with and without the step). Re-printed 2026-10-26 for M5-4g (the reserve saves for the expansion): the second Keep now stands on this
seed, so the cap reaches 100 with 10 Billets finished and the 12th Billet step is skipped, and the run with and without
it is the same. Before M5-4g the cap reached 100 at 727 s with the 12th Billet placed at a cap of 90 (12 Billets), and
without the step at 985 s, the economy manager's house rule placing the 12th Billet anyway. The step stays in every
opening: at the hard cap it costs nothing, and on a seed where the second Keep does not stand it fills the cap.

**Spending on the menu's map (D21, 2026-10-18, gathering gold 0.85 / wood 0.75 a second; `Content/AiSpendingReportTests.GeneratedNormal_Spending_Report`).**
Against an idle seat, 12 forests and 8 mines (the menu's), 20 minutes. Normal, seed 1 (the smallest seed in 1-50 whose Normal pick is this opening): the idle base falls at 455 s; in hand at 5 min 498 gold / 45 wood; the first wave at 320 s; Age II in at 538 s (re-printed 2026-11-16 for the sim's M6-S12; was: the idle base falls at 465 s; in hand at 5 min 258 gold / 20 wood; the first wave at 338 s; Age II in at 502 s). Easy, seed 1 (Easy always plays the first opening): the idle base falls at 474 s; in hand at 5 min 192 gold / 25 wood; the first wave at 310 s; Age II in at 563 s (re-printed 2026-11-26 for the sim's M6-S14; was: the idle base falls at 491 s; in hand at 5 min 192 gold / 25 wood; the first wave at 310 s; Age II in at 563 s) (re-printed 2026-11-16 for the sim's M6-S12; was: the idle base falls at 478 s; in hand at 5 min 192 gold / 5 wood; the first wave at 314 s; Age II in at 539 s). Age II comes in about 2 minutes after its step is
due at 6:30: the army is wave-sized by then (the first wave left at 338 s), so the reserve (above) holds the gold for it. Re-printed 2026-10-26 for M5-4g:
before it Age II came in at 938 s (Normal) and 844 s (Easy), about 8 minutes after its step, and the Normal run razed
the idle base at 498 s; the 5-minute banks and the first waves did not move. The reserve costs army against an active
opponent: on the 27 Normal v Normal pairing runs (QA's `Stress/ReservePairingsStressTests`, M5-4g, all three openings)
the Malazan peak armies, summed, are a third smaller, 903 to 596 pop (the Whirlwind's 1,208 to 1,043), while the
Malazan waves stayed (79 to 81). D20's two-seed note (512 s / 536 s, seeds 1 and 7) measured the old 20-pop wave and
both seeds; the per-seed runs are now one per opening.

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
| 3 | pop 8 | build | `malazan_depot` | 1 | Quartermaster's Depot by the nearest known forest |
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
menu's map (20 minutes, the other seat idle): Normal, seed 5: the cap reaches 100 at 795 s; the last Billet step (step
19) is done at a cap of 100; skipped at the hard cap: step 19; 11 Billets stand at 20:00, 1 past the cap. Re-printed
2026-11-26 for the sim's M6-S14: the cap reached 100 at 711 s with steps 18 and 19 skipped and 10 Billets. Re-printed 2026-11-16 for the sim's M6-S12: the cap reached 100 at 798 s with step 19 skipped and 11 Billets. Re-printed 2026-11-15 for the sim's M6-SH2: the cap reached 100 at 708 s with steps 18 and 19 skipped and 10 Billets.
Re-printed 2026-10-26 for M5-4g: before it the cap reached 100 at 650 s, step 19 was done at a cap of 98, no step was
skipped and 12 Billets stood.

**Spending on the menu's map (D21, 2026-10-18, gathering gold 0.85 / wood 0.75 a second; `Content/AiSpendingReportTests.GeneratedNormal_Spending_Report`).**
Against an idle seat, the menu's map, 20 minutes. Normal, seed 5 (the smallest seed in 1-50 whose Normal pick is this opening): the idle base falls at 457 s; in hand at 5 min 230 gold / 55 wood; the first wave at 336 s; Age II in at 556 s (re-printed 2026-11-16 for the sim's M6-S12; was: the idle base falls at 470 s; in hand at 5 min 238 gold / 40 wood; the first wave at 335 s; Age II in at 556 s).
Re-printed 2026-10-26 for M5-4g (the reserve): Age II came in at 943 s before it; nothing else moved.

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
| 3 | pop 8 | build | `malazan_depot` | 1 | Quartermaster's Depot by the nearest known forest |
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
menu's map (20 minutes, the other seat idle): Normal, seed 7: the cap reaches 100 at 807 s; the last Billet step (step 16) is done at a cap of 100; skipped at the hard cap: step 16; 11 Billets stand at 20:00, 0 past the cap. Re-printed 2026-11-17 for D33's 16-pop wave: the cap reached 100 at 779 s, steps 15 and 16 were skipped and 10 Billets stood. Re-printed 2026-11-16 for the sim's M6-S12: 777 s. Re-printed 2026-11-15 for the sim's M6-SH2: the cap reached 100 at 811 s and step 16 was never done.
Re-printed 2026-10-26 for M5-4g: before it the cap reached 100 at 835 s, step 16 was done at a cap of 98, no step was
skipped and 12 Billets stood.

**Spending on the menu's map (D21, 2026-10-18, gathering gold 0.85 / wood 0.75 a second; `Content/AiSpendingReportTests.GeneratedNormal_Spending_Report`).**
Against an idle seat, the menu's map, 20 minutes. Normal, seed 7 (the smallest seed in 1-50 whose Normal pick is this opening): the idle base falls at 569 s; in hand at 5 min 228 gold / 575 wood; the first wave at 437 s; Age II in at 501 s (re-printed 2026-11-17 for D33's 16-pop wave; was: the idle base falls at 616 s; in hand at 5 min 228 gold / 575 wood, at 10 min 232 / 125; the first wave at 488 s; Age II in at 503 s; and before the sim's M6-S12: 644 s, 218 / 575 and 261 / 350, 511 s, 493 s). Still the fastest Malazan Age II (the others: 538 s and 556 s), though not the step's 6:00: the
one Barracks still spends gold before 400 piles up, and the reserve waits for a 16-pop army (the first wave, at 437
s). Re-printed 2026-10-26 for M5-4g: Age II did not move; the idle base fell at 619 s and the 10-minute bank read 99 /
490 before it.

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
- **Attack waves:** first at 16 army pop and 1,184 gold + wood (`attack`; the cost of 16 pop of the Age I mix, Heavy
  Infantry at 74 a pop; 18 and 1,332 until D33, 2026-11-17, BUG-0982's rule below). Mages cast Telas Fire on the
  largest enemy clump.

**This opening can fall to a rush without ever attacking (BUG-0982, D-H6, 2026-10-18; re-measured by D33, 2026-11-17).**
It is the greedy opening: one Barracks in Age I and the gold saved for Age II. Against the Whirlwind's raider rush, the
Malazan army stays small while the base is defended. On the pairing's five smallest seeds (7, 8, 14, 18, 23) it sends
no wave on three, and all three are destroyed (seed 14 at 11:21, 18 at 12:42, 23 at 13:44); seeds 7 and 8 wave and
stand at 20:00, seed 8 first at 7:48. A lower wave threshold doesn't help those three: at 16 or 14 army pop they still
send no wave, because the army never grows past 9 pop (seed 14), 7 (seed 18) or 10 (seed 23). It does help the two that
wave: D33 lowered the wave from 18 to 16 army pop by BUG-0982's rule, re-stated (below), so seed 8's first wave comes
at 7:48 (9:59 at 18) and seed 7's at 7:50 (8:20 at 18), and both still stand; at 14 seed 8 is destroyed at 13:09 where
at 16 and 18 it stands. The measurement is under "Fast Age II against the raider rush" at the end of this page.

## Difficulty

The `difficulties` block of the same `game/data/factions/malazan/ai.json` (docs/02 "AI opponent", docs/03 "For the data
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
- **As built (M6-V25, stand-ins; BUG-1602):** the figures on `main` follow these notes except the Sapper. The Cadre Mage
  matches: a team-coloured hood, an oxblood robe and a staff, no armor. The Sapper has its munition satchels (a pack on
  the back with a strap across the chest, a bag on each hip, a clay munition in hand) but no leather apron, and it
  stands upright at idle like the other figures; it bends low only in its throw. It is the outfit on disk recoloured,
  with box props (docs/04 "Fourth slice (M6-V25)", the team parts and props "developer's pick, owner may revisit"). M6-VH9
  changed no Malazan colour or prop: it timed the Sapper's throw so the munition leaves its hand at 28 % of the clip, as
  the sim releases the shot (docs/04, the manifest's `oneShot`), and left the Cadre Mage's dark robe beside a Heavy
  Infantry shield in a dark player colour (told apart by shape) for the owner's packs. The note above stays the target
  for a later art pass.

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

## Fast Age II against the raider rush (BUG-0982, D-H6, 2026-10-18)

Measured by `Content/AiOpeningPairsReportTests.Bug0982_CadreRushVersusRaiderRush_WaveThresholdTable`, which pins this
table to its output cell for cell. Malazan `cadre_rush` (Fast Age II) against Whirlwind `raider_rush`, two Normal AI
seats on the menu's map (12 forests, 8 mines), 20 minutes, on the three smallest seeds in 1-500 that make the pair (7,
8, 14). `attack.minArmyPop` is set to 18, 16 (shipped since D33) and 14, and `minArmyCost` is the cost of that pop of
the Age I mix, rounded down (Heavy Infantry at 74 a pop). The columns are the Malazan side's: the first wave (and the
number of waves), Age II, the defeat, and the largest army it had (read once a second).

| minArmyPop / minArmyCost | seed | waved at (waves) | Age II at | defeated at | peak army pop |
| --- | --- | --- | --- | --- | --- |
| 18 / 1,332 | 7 | 8:36 (4) | 8:13 | never | 57 |
| 18 / 1,332 | 8 | 7:32 (4) | 8:35 | never | 29 |
| 18 / 1,332 | 14 | never (0) | 8:23 | 11:17 | 9 |
| 16 / 1,184 | 7 | 8:05 (3) | 8:13 | never | 49 |
| 16 / 1,184 | 8 | 7:02 (1) | 8:14 | 17:49 | 19 |
| 16 / 1,184 | 14 | never (0) | 8:23 | 11:17 | 9 |
| 14 / 1,036 | 7 | 6:47 (4) | 8:13 | never | 65 |
| 14 / 1,036 | 8 | 6:56 (1) | 8:15 | 16:21 | 17 |
| 14 / 1,036 | 14 | never (0) | 8:23 | 11:17 | 9 |

Re-printed 2026-11-26 for the sim's M6-S14 (docs/03 "Implementation (M6-S14)"; no data changed but `ai.minWorkers`).
**The rule no longer picks the shipped 16 (BUG-1851, for the data track's re-measure after M6-S14):** at 16 pop seed 8
now sends one wave (7:02) and is destroyed at 17:49, so 16 fails "no seed that stands at 20:00 at 18 is destroyed", as
14 does (seed 8 destroyed at 16:21); the rule keeps 18. The prose below and its pin
(`QA/Content/DR35ProsePinQaTests.Malazan_Bug0982Prose_MatchesTheThresholdTable`) and the report's pick
(`Content/AiOpeningPairsReportTests.Bug0982_*`) are skipped with BUG-1851 until then; no number was tuned here. The D33
table read: 18 pop 8:20 (4) / 9:59 (3) / never, Age II 8:13 / 8:41 / 8:52, seed 14 destroyed 11:21, peaks 55 / 21 / 9; 16
pop 7:50 (4) / 7:48 (4), seed 8 standing, peaks 52 / 26; 14 pop 6:47 (5) / 7:18 (1), seed 8 destroyed 13:09, peaks 60 / 16.

Re-measured 2026-11-17 by D33 on the sim of M6-S12 (docs/03 "Implementation (M6-S12)"), before any D33 data change
(the table sets `cadre_rush`'s wave itself, so the shipped number does not move it). Seeds 7 and 8 wave at every
threshold and stand at 20:00 at 18 and 16 pop; at 14 pop seed 8 sends one wave (7:18) and is destroyed at 13:09. At
every threshold seed 14 is destroyed at 11:21 without a wave, after its Age II (8:52), with a peak of 9 pop. Age II
comes at the same time at 18 and 16 pop on every seed. The M6-S10 table (D-R39, 2026-11-15) read, at 18 pop: seed 7
first wave 10:44 (4), Age II 8:24, peak 36; seed 8 9:01 (4), Age II 8:22, peak 30; seed 14 no wave, Age II 8:19,
destroyed at 11:34, peak 13; at 14 pop seed 8 was destroyed at 19:28.

**The rule (the Producer's default for D-H6, re-stated by D33 under BUG-1524; the owner may revisit):** take the lowest
threshold of 16 or less at which seed 8 waves, no seed that stands at 20:00 at 18 is destroyed, and Age II comes on
every measured seed no more than 60 s later than at 18; else keep 18. D-H6's rule had only the first and the last
test; since the sim's M6-S10 seed 8 waves at every threshold, so it picked 14, where seed 8 is destroyed. The
re-stated rule picks 16: seeds 7 and 8 wave sooner (7:50 and 7:48, against 8:20 and 9:59 at 18) and both still
stand, and Age II does not move. `cadre_rush` ships 16 army pop and 1,184 since D33 (2026-11-17), and its description
says so. On seed 14 the army never reaches 14 pop, so no threshold in this range would send a wave. A fix there would
have to be something other than this number, for example the sim-side "send what you have when the base is being
lost" that BUG-0982's notes name (BUG-1010). That is a new mechanic, so it is not data.

QA's `QA/Content/DH6ThresholdQaTests` runs the same thresholds on the pairing's 4th and 5th seeds too (18 and 23), and
the same rule over all five seeds (its printout, not pinned): at every threshold seed 18 sends no wave, never reaches
Age II and is destroyed at 12:42 (peak 7 pop), and seed 23 sends no wave, reaches Age II at 8:11 and is destroyed at
13:44 (peak 10). The rule over all five seeds picks 16. Re-measured 2026-11-15 on the sim's M6-S10: seed 18 was
destroyed at 13:39 (peak 7) and seed 23 at 15:43 (Age II 8:18, peak 9), neither waving; on the sim's M5-H6
(2026-11-09) seed 23 sent three waves at 18 pop and was destroyed at 18:49.

The same thing happens to `crossbow_push` (BUG-1010; D-H6's third seed per pairing, `QA/Content/D21OpeningsQaTests`, and
QA's 4th and 5th, `QA/Content/DH6ThresholdQaTests`). Against `raider_rush` it sent no wave on 2 of the pairing's 5
smallest seeds (5, 11, 19, 25, 38), seeds 19 and 25, at the 2026-11-15 re-count; today (2026-11-18, D34 and its QA, BUG-1640)
both wave and seed 38 does not: seed 19's side sends its first wave at 6:46 (below), seed 25's at 6:50 (one wave;
defeated at 18:15, tick 21,906), and seed 38's sends none (defeated at 13:10, tick 15,812, peak 9 pop). Seeds 5 and 11
were not re-run, so the count is at least 1 of 5. Re-measured 2026-11-15 on the sim's M6-S10: seed 19's side was
defeated at 16:10 (tick 19,416) and seed 25's at 11:32 (tick 13,857), neither ever attacking. On the sim's M5-H6
(2026-11-09) seed 19's side was beaten down but still standing at 20:00 and seed 25's was defeated at 12:22
(tick 14,843; 12:33, tick 15,068, measured 2026-11-07). From the sim's M5-4d slice 2 (the AI rebuilds what it loses,
2026-11-07) until M6-S10, seed 19's hall was razed but its last worker kept rebuilding, and at 20:00 the side had 1
pop, no building and 502 gold (522 measured 2026-11-07; BUG-1116 / BUG-1119; docs/03 "Implementation (M5-4d slice
2)"). So seed 19 was a named allowance (`QA/Content/D21OpeningsQaTests.KnownBeatenAlive`, pinned by
`QA/Content/DR33QaTests`): its row passed while the side stood without a wave. Since M6-S10 the side no longer stands
without one, and the row printed "the known case seed 19 player 0 now passes the rule: take it out of KnownBeatenAlive".
Re-measured by D34 (2026-11-18): seed 19's side sends its first wave at 6:46 (6 waves by 20:00), reaches Age II at 11:31
and stands at 20:00, so D34 took the entry out and `DR33QaTests` pins both lists empty. Neither seed is on a list now, so
either row fails if its side is ever left standing at 20:00 without a wave.

How the two seeds got here: before M5-4g seed 19 read a peak of 8 pop and 6 pop / cap 16 with 670 gold and 60 wood at
20:00, and seed 25 was destroyed at 11:26. From M5-4g both stood at 20:00: on seed 19 the army peaked at 7 pop, under
its 14-pop wave, while it held the base, and at 20:00 it had 15 pop with a cap of 24, 910 gold and 15 wood and no Age
II; on seed 25 it peaked at 12 pop and was down to 1 pop at 20:00 (re-measured unchanged on 2026-11-06 by D-H7). M5-4d
(the AI hunts the last units once it knows no enemy building) finished both off, seed 19 at 10:24 and seed 25 at 12:34
(tick 15,084; 12:33 since slice 2), and D-R33 (2026-11-07) took both `KnownBeatenAlive` entries out (BUG-1110); slice 2
put seed 19's back the same day, and D34 (2026-11-18) took it out again.

Over the two openings, `raider_rush` beats the Malazans without a fight on at least 4 of 10 seeds (`cadre_rush` 3 of 5,
seeds 14, 18 and 23, re-measured by D33, 2026-11-17, at 16 pop as at 18; `crossbow_push` seed 38, with seeds 5 and 11 not
re-run: 2026-11-18, BUG-1640). It was 5 of 10 when re-counted 2026-11-15 on the sim's M6-S10 (`crossbow_push` 2 of 5,
seeds 19 and 25; `cadre_rush`'s three seeds 14, 18 and 23, with seed 8 waving), and 6 of 10 before slice 2, when seed 23
sent no wave either. `standard` sent a wave on each of its 9
measured pairing seeds (3 Whirlwind openings x 3 seeds).

## Age II against the Whirlwind (M5-4g, 2026-10-26)

When each Malazan side's Age II was in, Normal against Normal on the menu's map (12 forests, 8 mines), 20 minutes, for
every Malazan opening against every Whirlwind opening on the pairing's three smallest seeds in 1-500, "never" if not in
20 minutes, and the defeat if the side fell. The 1st seed is `Content/AiOpeningPairsReportTests`' run, the 2nd and 3rd
`QA/Content/D21OpeningsQaTests`'; each run pins its cell here and on the Whirlwind page's twin table. Since the sim's
M6-S10 (2026-11-15, the gather change) the Malazans reach Age II on 26 of the 27 sides; the one that doesn't is
`crossbow_push` seed 19, BUG-1010's beaten side, now defeated at 16:10. Three sides fall: seed 19, `cadre_rush` seed 14
at 11:34 (after its Age II, at 8:19) and `cadre_rush` seed 27 at 18:32. `crossbow_push` seed 32 reaches Age II at 13:01 and stands at 20:00 (since the sim's M6-S14; 15:01 under M6-S12 and D33, 12:21 under M6-S10 and M6-SH2). Before M6-S10 the count was 23 of 27: `standard` seed 2 and `cadre_rush` seed 14 were defeated
first, seed 19 stood at 20:00 without Age II, and `crossbow_push` seed 32 was defeated at 17:12 (16:54 before the sim's
M5-9a). Under M5-4g alone seed 32 stood at 20:00 (3 pop, no pop building left; 794 gold and 5 wood in hand; docs/03
"Implementation (M5-4g)") until M5-4d's hunt (the AI hunts the last units once it knows no enemy building) finished it,
and seed 19 with it; from M5-4d slice 2 (the AI rebuilds what it loses) until M6-S10 seed 19's side rebuilt and lived to
20:00 (docs/03 "Implementation (M5-4d slice 2)", BUG-1116).

| Malazan opening | Whirlwind opening | 1st seed | 2nd seed | 3rd seed |
| --- | --- | --- | --- | --- |
| `standard` | `standard` | seed 1: 9:34 | seed 22: 14:11 | seed 26: 11:18 |
| `standard` | `raider_rush` | seed 2: never (defeated 16:27) | seed 10: never (defeated 12:38) | seed 21: 8:14 (defeated 16:28) |
| `standard` | `holy_war` | seed 3: 9:34 | seed 4: 11:14 | seed 6: 11:08 |
| `crossbow_push` | `standard` | seed 9: 13:09 | seed 32: 13:01 | seed 37: 8:41 |
| `crossbow_push` | `raider_rush` | seed 5: 9:53 | seed 11: 9:28 | seed 19: never (defeated 18:20) |
| `crossbow_push` | `holy_war` | seed 12: 10:56 | seed 45: 8:40 | seed 71: never (defeated 17:28) |
| `cadre_rush` | `standard` | seed 13: 6:05 | seed 16: 8:11 | seed 17: 8:08 |
| `cadre_rush` | `raider_rush` | seed 7: 8:13 | seed 8: 8:14 (defeated 17:49) | seed 14: 8:23 (defeated 11:17) |
| `cadre_rush` | `holy_war` | seed 20: 8:16 | seed 24: 8:28 | seed 27: 8:51 |

Re-printed 2026-11-15 for the sim's M6-S10 (docs/03 "Implementation (M6-S10)"): `crossbow_push` against `holy_war` seed 45 reaches Age II at 8:40 (was 9:28) and stands at 20:00 again (was defeated at 19:02 from the sim's M5-H6; Age II at 8:42 under M6-S10 and M6-SH2, 8:38 under M6-S12, 8:40 since D33); `cadre_rush`
against `raider_rush` seed 8 reaches Age II at 8:22 (was 8:41) and stands at 20:00 (was destroyed at 13:47), and seed 14
reaches Age II at 8:19 (was never) and falls at 11:34 (was 14:16), as the BUG-0982 table above read then (D-R39).

Re-printed again 2026-11-15 for the sim's M6-SH2 (a construction site is no longer a drop-off; an AI with no worker
trains one; docs/03 "Implementation (M6-SH2)"): 24 of the 27 Malazan sides reach Age II; the three that don't are
`standard` against `raider_rush` seed 2 (stands at 20:00 without it), `crossbow_push` against `raider_rush` seed 19
(defeated 15:02) and `crossbow_push` against `holy_war` seed 71 (defeated 14:26). Seven sides fall: those two, `standard`
against `raider_rush` seeds 10 (14:28) and 21 (12:54), `standard` against `holy_war` seed 4 (19:42) and `cadre_rush`
against `holy_war` seeds 24 (15:29) and 27 (19:53). `cadre_rush` against `raider_rush` seed 14 now stands at 20:00 (Age
II at 8:33). The paragraph above the table describes the M6-S10 runs.

Re-printed 2026-11-16 for the sim's M6-S12 (the AI's rally point off one-cell passages, a rebalanced gatherer's node
measured from its drop-off, a blind seat's re-scout; docs/03 "Implementation (M6-S12)"): 26 of the 27 Malazan sides reach
Age II; the one that doesn't is `standard` against `raider_rush` seed 10 (defeated 12:38). Four sides fall: that one,
`crossbow_push` against `holy_war` seed 71 (16:14, after its Age II at 9:28), `cadre_rush` against `raider_rush` seed 14
(11:21, after its Age II at 8:52) and `cadre_rush` against `holy_war` seed 27 (19:41). `standard` against `raider_rush` seed 2
now reaches Age II (13:07) and `crossbow_push` against `raider_rush` seed 19 stands at 20:00 with Age II at 11:31. The two
paragraphs above describe the M6-S10 and M6-SH2 runs.

Re-printed 2026-11-17 for D33 (the data track's balance pass: `cadre_rush`'s wave at 16 army pop, was 18; the Whirlwind
`holy_war`'s at 14, was 20): 25 of the 27 Malazan sides reach Age II; the two that don't are both defeated, `standard`
against `raider_rush` seed 10 (12:38) and `crossbow_push` against `holy_war` seed 71 (17:49; under M6-S12 it reached Age II
at 9:28 and fell at 16:14). Three sides fall: those two and `cadre_rush` against `raider_rush` seed 14 (11:21, after its
Age II at 8:52). `cadre_rush` against `holy_war` seed 27 now stands at 20:00 (Age II at 8:42; it fell at 19:41), and
`crossbow_push` against `holy_war` seed 12 reaches Age II at 11:20 (was 17:04). The three paragraphs above describe the
M6-S10, M6-SH2 and M6-S12 runs.


Re-printed 2026-11-26 for the sim's M6-S14 (the AI-play session: the hunt's resume, the worker floor, the rally search's memory, the blind re-scout's count, the rally point's memory; docs/03 "Implementation (M6-S14)"; cells moved on every seed): 23 of the 27 Malazan sides reach Age II; the four that don't: `standard` against `raider_rush` seed 2 (defeated 16:27); `standard` against `raider_rush` seed 10 (defeated 12:38); `crossbow_push` against `raider_rush` seed 19 (defeated 18:20); `crossbow_push` against `holy_war` seed 71 (defeated 17:28). 7 sides fall: `standard` against `raider_rush` seed 2 (16:27); `standard` against `raider_rush` seed 10 (12:38); `standard` against `raider_rush` seed 21 (16:28, after its Age II at 8:14); `crossbow_push` against `raider_rush` seed 19 (18:20); `crossbow_push` against `holy_war` seed 71 (17:28); `cadre_rush` against `raider_rush` seed 8 (17:49, after its Age II at 8:14); `cadre_rush` against `raider_rush` seed 14 (11:17, after its Age II at 8:23). The paragraphs above describe the M6-S10, M6-SH2, M6-S12 and D33 runs. The old cells (D33): `standard` v `standard` seeds 1 / 22 / 26 10:19 / 15:25 / 11:16, `standard` v `raider_rush` seed 2 13:07, `crossbow_push` v `standard` seed 9 11:23, `crossbow_push` v `raider_rush` seed 5 9:50, `crossbow_push` v `holy_war` seed 12 11:20, `standard` v `raider_rush` seed 21 8:14, `standard` v `holy_war` seeds 4 / 6 11:24 / 11:01, `crossbow_push` v `standard` seeds 32 / 37 15:01 / 9:52, `crossbow_push` v `raider_rush` seed 19 11:31, `crossbow_push` v `holy_war` seed 71 never (defeated 17:49), `cadre_rush` v `raider_rush` seeds 8 / 14 8:41 / 8:52 (defeated 11:21), `cadre_rush` v `holy_war` seeds 24 / 27 8:26 / 8:42.

## Questions for the Producer (D-R39, 2026-11-15)

Raised by D-R39 (the page made true again after the sim's M6-S10 gather change; no data changed), each with the
developer's recommendation.

1. **BUG-0982's rule now picks 14 army pop; `cadre_rush` ships 18.** Seed 8 waves at every threshold since M6-S10, so
   the rule's first test passes at 14 (table under "Fast Age II against the raider rush"). At 14 seed 8 is destroyed at
   19:28 where at 18 it stands, and seeds 14, 18 and 23 fall without a wave at every threshold. Recommendation: leave 18
   to the balance pass (D33), which may also want to re-read the rule now that it no longer answers BUG-0982's question
   (seed 14 never reaches 14 pop). **Answered by D33 (2026-11-17, BUG-1524):** the rule is re-stated (a lower
   threshold may not lose a seed that stands at 18) and picks 16 on the re-measured table; `cadre_rush` ships 16.
2. **The BUG-0982 table was re-printed in D-R39, not in M6-S10.** The sentences pinned by
   `QA/Content/DR35ProsePinQaTests.Malazan_Bug0982Prose_*` read that table, so it was re-printed from a fresh run of
   `Content/AiOpeningPairsReportTests.Bug0982_*` (which stays red on its pick assertion, before it compares the table).
   Recommendation: accept; the row compares the table again once D33 settles the pick. **Done by D33:** the table is
   D33's run, and both `Bug0982_*` rows compare it again.
3. **`QA/Content/D21OpeningsQaTests.KnownBeatenAlive` still names `crossbow_push` against `raider_rush` seed 19.** Since
   M6-S10 that side is defeated at 16:10 and the row prints "now passes the rule: take it out of KnownBeatenAlive".
   Recommendation: take the entry out (and its `DR33QaTests` pin) in the next data task; the row passes either way.
   **Done by D34 (2026-11-18):** the entry is out and the pin reads an empty list; the side now waves from 6:46.

## Questions for the Producer (D33, 2026-11-17)

Raised by D33 (the start of the balance pass), each with the developer's recommendation.

1. **BUG-0982's rule is re-stated.** D-H6's rule (the lowest threshold of 16 or less at which seed 8 waves and Age II is
   no more than 60 s later) picked 14 on today's table, where seed 8 is destroyed at 13:09 although it stands at 16 and
   18. D33 added "no seed that stands at 20:00 at 18 is destroyed", which picks 16, and `cadre_rush` ships 16 / 1,184 (its
   description says 16). Recommendation: accept the re-stated rule; the alternative is to ship 14 by the old rule and
   lose seed 8, or keep 18 and drop the rule.
2. **`crossbow_push` against `holy_war` seed 71 now falls without Age II** (17:49; under M6-S12 it reached Age II at 9:28
   and fell at 16:14): the holy war's earlier wave (14 pop) is felt by the Malazan side on that seed. Recommendation: a
   finding for the next balance step, not this one (it waves and is not a dead end; the Age II table prints it).

