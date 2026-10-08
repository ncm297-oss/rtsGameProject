# BUG-0153: M4-2a nits: a queued Attack's target lives in the public `QueuePosition` array, a shove row lost its combat-on run, a brief row that can't be un-skipped

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open (items 1-2 fixed in `fea7963`; 3-4 are notes for the Producer) |
| Found | 2026-10-08-0313, task M4-2a |
| System | sim: orders queue storage; QA rows; brief wording (sim track) |
| Fixed by | `fea7963`: item 1 (doc remarks on `QueuePosition` / `QueueTypeId` point to `QueuedTarget`), item 2 (ShoveQaTests 2-player row back on combat) |

## Repro / Actual
1. **`UnitStore.QueuePosition` holds non-positions.** A queued Attack keeps its target as `(slot, generation)` floats in
   `QueuePosition` and its building flag in `QueueTypeId` (the developer's documented deviation, to stay inside the
   228 MB memory row). Both arrays are `public readonly`, documented as target points. Nothing reads them outside the sim
   today (grep: no `game/` or `ViewApi/` reader), but a queued-waypoint overlay (the obvious view feature for Shift
   orders) would draw an Attack entry at (slot m, generation m). `UnitStore.QueuedTarget(entry)` exists; the remarks on
   `QueuePosition` / `QueueTypeId` should say "a point, except on an Attack entry (see `QueuedTarget`)", or the view
   API should expose a typed accessor.
2. **`ShoveQaTests.BuildCap_500UnitsTo500RandomGoals...(players: 2)` now runs combat off.** On combat its checker
   reports `tick 6733: shoved unit 38 order tick 6732 -> 6734`: a chaser that steps, plants and stands down in one tick
   looks like a shove to it. The switch is reasonable for a movement row, but the shove rules for two owners now have no
   combat-on check at all; teaching the checker that a chase step re-sets `OrderTick` would keep it.
3. **Criterion 5 asks for `QA/CombatFriendExceptionQaTests` "un-skipped and green".** Its only skipped row
   (`StalledChaser_SwitchesToAnEnemyBehindAWall_WalksRoundAndFightsIt`) is skipped for BUG-0149 (open, not in M4-2a's
   scope), not for the ram; it is still skipped. Nothing to fix in M4-2a; the brief's line should name BUG-0149.
4. **`GatherWedgeQaTests` checks only checkpoints 1-19** of the seed 21 replay (the fix changes the game from tick 20,
   so no longer prefix can match). The row still proves the header substitution plays the recorded game; it can't prove
   "bit-exact to its checkpoints" as criterion 1 words it. Noted for the Producer, not a defect.

## Expected
Docs and accessors say what the arrays hold; combat-on coverage kept where a checker can be taught; brief rows that can
be met.

## Notes
- The seed 6 two-cell corridor oscillation (BUG-0146's dev-seen note) was not reproduced by QA this session either: the
  M3 Playable scene passed headless on seed 6 three times (and seeds 1, 2 and 21 once each; seed 21 failed on the pre-fix code), with no stall replay saved. Not filed separately; BUG-0146's note stands.

## Re-check (2026-10-08-0313, round 1, QA)
- Item 1: `UnitStore.QueuePosition` / `QueueTypeId` remarks now say an Attack entry is not a point and name
  `QueuedTarget`; still nothing outside the sim reads them (grep of `game/` and `ViewApi/`). Accepted for an S4. A small
  note: the generation is stored as a float, exact up to 2^24 reuses of one slot, far out of reach.
- Item 2: `ShoveQaTests.BuildCap_500UnitsTo500RandomGoals...(players: 2)` runs combat on again and passes in the suite.
- Items 3-4: unchanged (BUG-0149 still open; the seed-21 checkpoint note stands).
