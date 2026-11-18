# BUG-1581: Stand-still delay edges: a 1-tick delay acts as 2 after a move, and a phase 1-4 push is dated a tick early

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open (both items are docs/03 "Implementation (M8-2)" Known limits (1) and Questions 1-2; filed so they are tracked if the answer is "leave it") |
| Found | 2026-11-17-0816, task M8-2 |
| System | `Abilities/StillStealthSystem` |
| Fixed by | |

## Repro
1. Item 1: data with `"stillStealth": { "status": "stealthed", "delay": 0.05 }` (the loader accepts it: 1 tick). A unit at
   rest, hidden; move it 1 mm between ticks k - 1 and k. By the rule it should be hidden again in tick k (one tick has
   passed since tick k - 1, when its position changed); it is hidden in tick k + 1. `StillStealthSystem.Run`: `moved`
   forces `qualifies = false` in the pass that sees the move, whatever the delay. For delays of 2 ticks and more the
   rule is exact: `QA/StillStealthQaTests.AfterAMove_IsVisibleAtOnce_AndHiddenAgainOnExactlyTheDelayTick` (2, 3, 10, 60
   ticks) and `FirstSeen_IsHiddenOnExactlyTheDelayTick` (1-1,200 ticks, including 1) are green.
2. Item 2: a building placed on a resting field unit pushes it in phases 1-4 of tick k; the pass dates the change k - 1, so
   the unit hides on tick k + 59, one tick before "60 after its position last changed".

## Expected
docs/02: "Stealthed once 3 s (a number of its unit type in the data) have passed since ... its position last changed."
For every delay the loader accepts, and for every cause of a position change.

## Actual
As above: one tick late for the shortest delay, one tick early after a placement push. No shipped unit carries the
field, and the designed delay (3 s) is far from 1 tick.

## Notes
The developer's Questions 1-2 propose leaving both. If the Producer agrees, closing this as wontfix with that reason is
fine; raising the loader's minimum to 2 ticks (0.1 s) would make item 1 impossible instead.
