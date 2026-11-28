# BUG-1894: Draw rule: states that never become spent though nobody can train, and two small notes (M6-SH3)

| Field | Value |
| --- | --- |
| Severity | S4 (all a later draw, never an earlier one; none seen in an AI match) |
| Status | open |
| Found | 2026-11-27-0817, task M6-SH3 (QA, focus 1, "the other side") |
| System | draw rule (`World.CanStillTrain`), AI economy |
| Fixed by | |

## Repro / findings (read from the code and checked against the rule as written in docs/02 "Game flow" 4)
1. **A head whose unit can never be placed.** A started head that finished but whose spawn finds no free cell stays in the
   queue at full progress (`ProductionSystem.Step` returns without `PopTrained`) and counts in `TrainingUnits` for ever, so
   a seat whose producer is walled in is never spent. The old rule had the same hole (any queued unit).
2. **(b) asks "can pay for a unit type", not "has a building that trains it".** A seat with workers, a finished Depot and gold
   on the map but no wood anywhere (bank and map) and no Keep reads "can train" (the Laborer's 50 gold is reach) although the
   Laborer trains only at the Keep (275 / 275) and nothing it can build trains a unit. Never spent. As specified ("can produce:
   a finished unit-producing building or a live worker"); rare (wood is plentiful on every shipped map).
3. **Map resources nobody can reach count.** `GoldLeft` / `WoodLeft` sum every live node, walled-off or unpathable ones
   included (BUG-1891's 726 unmined gold on Pale Hills seed 4 is such a total), so such a seat is never spent. As specified
   ("what is left on the map").
4. **Note (C2, BUG-1852):** `NoteDroppedGather` now judges an Idle worker with a full load when a drop-off stood at both
   thinks. If that worker is Idle because it cannot path to any drop-off (not because its node is gone), a live remembered
   node out of sight is marked dropped until seen again. Harmless (the mark clears on sight), not seen in any run.
5. **Note (QA's own staging):** a re-sent Gather after the node dies under a walking worker: the worker's first Gather is
   cleared as "at work" at the think it walks (`Gatherers`, `board.GatherNode[slot] = default`), so when the node then dies
   the worker gets one more Gather to the gone node (refused once, then marked). Per worker and node: one live send and at most
   one refused send (`M6Sh3AiQaTests.TheNodeDiesWhileTheWorkerWalks_*`: 27 sends to already-gone remembered nodes in 120 thinks,
   each a different node or a different worker, none repeated). Within criterion 7's "refused at most once".

## Expected
docs/01 2026-11-25: a match is drawn when nobody can make a unit any more.

## Actual
Items 1-3 keep such matches open for ever (as M6-S13 did); none appears in QA's four-seat or Raraku tables.

## Notes
Grouped as one S4 per the QA rules (several small related findings). Item 1 is the only one a cheap counter could fix
(a "finished, waiting for a cell" head could count only while a cell near the building is free).
