# BUG-0604: In the menu's skirmish against the AI, the AI never gets an economy going: the dev armies clash at tick 0 over the pop cap, and no gold mine is in sight of the start base

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-12-1930, task M6-V2 (QA) |
| System | view: the menu skirmish's start (`Match.Start`: `--units` default 100 a side, `StartBasePlan`); sim: AI economy without scouting (M5-3) |
| Fixed by | Partly, 9ec4a56 (M6-V3): the menu's match has no dev army (`--units 0`; QaM6V3Test: 18 openings, 1 hall + 5 workers each, no other unit). Still open: on this branch 4 of 36 halls see a gold mine at the start and `AiSeatTest` prints `PENDING start locations probe (menu): by tick 401 no AI worker is gathering`; closes when the sim's M5-4a start locations merge and the probe flips to a Check. |

## Repro
1. Main menu -> Skirmish -> Start (defaults: Malazan vs Whirlwind AI Normal), or `AiSeatTest`. The AI seat test logs
   `menu: tick 400, AI workers gathering / returning 0, queue items 5` and `--ai hard: tick 400, AI workers gathering / returning 0`.
2. QA re-ran the view's setup in xUnit (a scratch diagnostic, not kept: the view's `StartLayout.Block` + `ViewReads.Plan` +
   `Match.SpawnArmies` order, Whirlwind AI Hard, seed 3, 600 ticks):
   - 100 units a side (the menu's default): AI workers 20 at tick 50 (the roster round-robin includes workers), 8 by tick 200,
     **1 by tick 400**. 0 gathering, gold 0 and wood 0 from tick 100 on. The two armies touch at tick 0
     (`ShellTest` windowed shot `shell-match.png`: `Pop 163 / 10`).
   - `--units 0` (hall and 5 workers only): at tick 2 player 1 sees **0 resource nodes** on seeds 1 and 3 (32 trees and no gold on
     seed 7). The AI sends no `Gather` at all, 4-6 workers stand idle to tick 600, gold 0 from tick 100. On seed 7 it gathers wood only.

## Expected
The M6-V2 goal: "pick each slot's faction and the AI's difficulty and play against the AI from the menu". docs/02 "Economy":
2 gold mines at each start location.

## Actual
The menu's match is the dev sandbox match (100-unit armies a side, pop far over the cap, armies in contact), so the AI's
workers die in the opening brawl and its Train orders wait on population. Without the armies, the start base usually has no
mine in sight, and the AI, which does not scout until the sim's M5-3, never finds gold. The criterion "the AI acts by tick 400"
passes only through queue items.

## Notes
Partly known: the handoff expects a weak AI until M5-3, BUG-0581 item 2 notes that the armies touch, and docs/03 says start
locations (mines at the start) are M6 sim work. Filed so the Producer can decide whether the menu's skirmish should start
without the dev armies (`--units 0`, the docs/02 opening: hall and workers), and so a later QA can re-check the AI from the menu
once M5-3 merges. Not an M6-V2 criterion miss.
