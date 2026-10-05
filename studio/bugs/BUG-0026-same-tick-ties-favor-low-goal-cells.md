# BUG-0026: In same-tick order bursts, the player whose goals have low cell indexes starts 2-4 ticks sooner on average

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-04-2056, task M1-4c |
| System | movement (MovementSystem build pass tie-break) |
| Fixed by | |

## Repro
1. Run `Rts.Sim.Tests.QA.FieldBuildFairnessQaTests.Fairness_TwoBases_2000Ticks_OldestFirst_BoundedWait_PlayersEqual`
   with detailed console logging, and compare the `simultaneous: True` rows with the alternating rows.
2. Setup: 64 groups of 4 units. Player 0's base is at row ~10 and its goals are at row ~118 (high
   cell indexes). Player 1 is mirrored, so its goals have low cell indexes. Both players re-order
   4 or 8 groups on the same ticks, every 100-tick cycle, for 2,000 ticks.

## Expected
The spec sets the tie-break: within one order tick, the lower goal cell goes first (docs/03
"Build cap"). Its bias is documented as bounded by ceil(N / 2) ticks.

## Actual
```
batch 4, same tick:   mean wait P0 11.79 / P1  9.91 ticks, worst 24 / 22
batch 8, same tick:   mean wait P0 14.67 / P1 10.95 ticks, worst 28 / 24
batch 4, alternating: mean wait P0  7.17 / P1  7.24 ticks
batch 8, alternating: mean wait P0 11.19 / P1 10.81 ticks
```
The bias stays within the documented bound. It is also systematic: the player attacking toward
row 0 always wins same-tick ties, so map side decides who moves first. The player and the AI
issuing orders on the same tick is the normal case once the AI exists.

## Notes
Filed so the Producer can decide whether the bound is acceptable. Possible cheaper tie-breaks:
player order rotated by tick, or a seeded hash of the goal cell mixed with the tick. Either stays
deterministic and removes the fixed map-side preference.
