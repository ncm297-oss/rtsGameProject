# BUG-0900: Faction pages' balance tables and spending paragraphs are stale after the flank rule (8 content rows red)

| Field | Value |
| --- | --- |
| Severity | S2 (data-owned; `main` would be red on 8 `Content/` rows until the re-print) |
| Status | open (the data track's merged-tree re-print at the 0554 integration, as BUG-0734; else the data track's first task next session) |
| Found | 2026-10-17-0554, task M5-P2 (the dev disclosed it in docs/03 "Implementation (pathing slice 2)", "Moved elsewhere"; the Producer filed it at ACCEPT) |
| System | content docs (`docs/factions/malazan.md`, `docs/factions/whirlwind.md` "Balance baseline" group tables and "Spending on the menu's map"), `Content/CounterTriangleMarginsTests` (7 rows), `Content/AiSpendingReportTests.GeneratedNormal_PagesAndDocs03_QuoteTheReport` |
| Fixed by | |

## Producer decision (2026-10-17, ACCEPT of session 0554)
Sim ACCEPT with no sim change: the flank rule is the goal (owner playtest note 5) and the cells are measurements. The sim
track may not edit `docs/factions/**` or `Content/**` (track rules). Standing rule 25: **the conductor merges sim → view, then
dispatches a data-track fix-mode dev on the merged tree** (the view has no `sim/` diff and the data track was STOP, so the merged
tree's numbers equal the sim branch's below) that re-prints both pages from the harness, and **pushes only when the full non-Perf
suite is green**. If the conductor can't dispatch it, the data track's first task next session is this re-print (D21), and `main`
carries exactly these 8 known-red rows until then (named in STATE's Build row).

## Repro
`dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~CounterTriangleMarginsTests|FullyQualifiedName~AiSpendingReportTests"`
on `studio/2026-10-17-0554-sim` (f99caa3): 8 of 13 fail: `EveryPrintedRow_EqualsItsPageRow` (both factions), 4 `AMutatedPageCell_*`
rows, `BUG0290_AHeaderMismatch_StillReportsTheStaleCells` (each expects only its own planted problem and also gets the stale cells),
`GeneratedNormal_PagesAndDocs03_QuoteTheReport`. Green on 321d7d6. `AllEightPairs_BothSeats_PrintTheMarginTable_AndTheDocWinnerWins`
**passes**: every winner is unchanged.

## Actual (the harness on f99caa3; both pages carry the same cells where they share a row)
Group table, `page → sim` (winner left / winner hp left / winner keeps (cost) / time to last death):

| Rule | Pair | Seat | Winner left | Winner hp left | Winner keeps | Time to last death |
| --- | --- | --- | --- | --- | --- | --- |
| Line beats Shock | Heavy Infantry v Horse Raider | 0 | 15 → **16** / 16 | 1462 → 1544 | 94 → **100 %** | 23.1 → **17.9 s** |
| Line beats Shock | Heavy Infantry v Horse Raider | 1 | 14 → **16** / 16 | 1556 → 1584 | 88 → **100 %** | 20.5 → **16.3 s** |
| Line beats Shock | Raider v Wickan Lancer | 0 | 19 → 18 / 19 | 1667 → 1592 | 100 → 95 % | 16.6 → 15.6 s |
| Line beats Shock | Raider v Wickan Lancer | 1 | 18 / 19 | 1581 → 1570 | 95 % | 18.3 → 15.9 s |
| Shock beats Ranged | Wickan Lancer v Desert Archer | 0 | 7 → 8 / 10 | 828 → 1044 | 70 → 80 % | 25.1 → **15.9 s** |
| Shock beats Ranged | Wickan Lancer v Desert Archer | 1 | 8 / 10 | 888 → 1050 | 80 % | 22.0 → 16.4 s |
| Shock beats Ranged | Horse Raider v Crossbowman | 0 | 9 → **12** / 13 | 1152 → 1332 | 69 → **92 %** | 20.9 → 14.8 s |
| Shock beats Ranged | Horse Raider v Crossbowman | 1 | 10 → 11 / 13 | 1197 → 1332 | 77 → 85 % | 18.2 → 14.6 s |
| Ranged beats casters | Crossbowman v Priest of the Whirlwind | 0 | 13 / 15 | 444 → 380 | 87 % | 11.6 → 13.4 s |
| Ranged beats casters | Crossbowman v Priest of the Whirlwind | 1 | 10 → 9 / 15 | 442 → 325 | 67 → 60 % | 11.9 → 13.6 s |
| Ranged beats casters | Desert Archer v Cadre Mage | 0 | 16 / 18 | 602 → 618 | 89 % | 11.4 → 9.6 s |
| Ranged beats casters | Desert Archer v Cadre Mage | 1 | 13 → 16 / 18 | 525 → 522 | 72 → 89 % | 13.0 → 11.4 s |

Siege tables: unchanged (the `AMutatedPageCell` Ram row fails only because of the group cells). Spending paragraphs: the idle
base falls at malazan seed 1 **505 s** (page 509), whirlwind seed 7 **539 s** (page 540); the 5-minute marks and the verdict
("wood is not the limit", 0 of 4) are unchanged. docs/03's "Spending re-measured (D20)" bullet was already re-printed by the dev.

Why: melee units blocked by their own ranks walk round them now, so melee blocks engage faster and more of them strike at once
(docs/03 "Moved elsewhere": one attack-move in the 40 v 40 gap-10 brawl deals +46 %); fights with a melee winner end 4-10 s
sooner and the winner keeps more. The two caster rows move the other way (Crossbowmen flanking their own front spread out under
the Priests' storms). Every row stays above the 40-65 % target band, as before (the band is a Producer default; D13 / D18 found
no lever of the line unit's own that reaches it).

## Fix (for the data fix-mode dev on the merged tree)
1. Re-print both pages' "Balance baseline" group tables from `AllEightPairs_BothSeats_PrintTheMarginTable_AndTheDocWinnerWins`'
   output on the merged tree; update any prose that quotes a moved cell (the paragraph under the table; D13's sweep paragraph
   quotes the four Line v Shock cells "at 1.5" **as measured then**: label it "(D13, measured on the movement before pathing slice
   2)" rather than re-running the sweep, unless a pin test demands the re-run); the table's heading line gets "re-printed with the
   sim's flank rule (M5-P2, 2026-10-17, BUG-0900)".
2. Re-print both pages' "Spending on the menu's map" paragraph (505 / 556 s malazan, 514 / 539 s whirlwind) from
   `GeneratedNormal_Spending_Report`; `D20PinQaTests` / `D20DefenseWordingQaTests` must stay green (they pin the gather rates and the
   knobs, which are unchanged).
3. Re-run `Content/BuildingKillTimeReportTests` (12 / 12 must hold; a failing cell is pinned at the measured count with a dated
   reason and an S3 filed, never loosened silently) and `QA/Content/AiHouseRuleReportTests`.
4. No `game/data/**` change; `data-hash` C059AD620DE581F1 unchanged. Push only when `CounterTriangleMarginsTests` 13 / 13,
   `AiSpendingReportTests`, `DataValidationTests`, `ReplayGoldenTests` and the full non-Perf suite are green on the merged tree.
5. Set this bug `fixed` with the commit; the owner's review table is already under STATE "For your review" (the M5-P2 entry).

## Notes
- The sim dev could not fix it (track rules) and said so in docs/03 and the report. Standing rule 25 (a sim brief names the
  measured data cells its change can move) was honoured: the 0554 brief said BUG-0143's brawl row and the crowd rows; the
  counter-triangle cells were not named, so the rule gets an addendum (handoff): any change to movement or chasing names the
  counter-triangle tables and the spending paragraphs too.
