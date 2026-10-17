# BUG-0887: A Move re-issued to two points 2 m apart behind an own line of 24-32 still swings and never arrives

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-17-0554, task M5-P2 (QA re-check after fix round 1) |
| System | movement, flank rule (`MovementSystem` flank mark / `reversing` step, BUG-0881 / BUG-0883 fix) |
| Fixed by | |

## Repro
1. `QA/FlankFixRound1QaTests.LongLine_MoveReissuedToAlternatePoints_Arrives`. The skipped rows are the failures. Scene:
   own Crossbowmen 1 m apart along y, planted on holding Raiders 14.5 m in front (out of the walker's sight). One Heavy
   Infantry 3 m behind, at `offset` m along the line. A Move to 5 m beyond the line's middle, re-issued every `every`
   ticks, alternating between that point and 2 m along the line. This is the dev's
   `LongOwnLine_MoveReissued_KeepsItsSideAndArrives` with other line lengths and offsets.
2. QA's grid (cbeb944, 900 ticks; arrived tick / turns along the line):

| line | offset | every 2 | every 3 | every 7 |
| --- | --- | --- | --- | --- |
| 16 | -0.3 / 0.2 / 1.5 / -4 | 130 / 112 / 145 / 140 | 138 / 114 / 145 / 144 | all arrive, <= 3 turns |
| 24 | 0.2 (the dev's row) | 166, 1 | 165, 1 | 164, 1 |
| 24 | -0.3 | **never, 275** | 190, 5 | 175, 2 |
| 24 | 1.5 | **never, 276** | 203, 7 | **530, 51** |
| 32 | 0.2 | **never, 265** | **never, 252** | **661, 59** |
| 32 | -0.3 | **never, 285** | **never, 178** | 228, 2 |
| 32 | -4 | **never, 204** | **never, 259** | **548, 35** |
| 32 | 1.5 | **never, 277** | **never, 282** | 306, 7 |

Re-issuing the same point is a no-op, and every row then arrives (24: 166-194, 32: 219-247).

## Expected
BUG-0881 / BUG-0883 fix round 1 (docs/03): "after a re-issued order (a fresh progress estimate: attack-move spam) the walk
down the line's far side counted as progress ... after, 1 turn, arrived at tick 166-169". QA's bound: the walker arrives
within 900 ticks with at most 8 turns along the line, as in the dev's row.

## Actual
Behind a 32-line, a Move re-clicked every 2-3 ticks (7-10 clicks a second) never gets round within 45 s. The walker
swings along the line 180-285 times, ending 1.4 m behind it. It never gives up either, because each new order resets the
count. At every 7 ticks, some rows take 27-33 s and 35-59 turns.

## Notes
- Not a regression against the rule off. With the rule off the same spam never arrives either: the walker presses into
  the line for all 900 ticks. Against the pre-fix code (3ec22ee) the 32 / -0.3 / 3 row also never arrives (77 turns).
- Likely cause (developer's call): the `reversing` replacement and the kept side only apply when the last flank was the
  tick before (`FlankTickOf(mark) == tick - 1`). A new order every 2-3 ticks starts a fresh walk whose first ticks make
  progress on the new estimate, so the progress step back down the line wins between flanks.
- Criterion 3 (the owner's 10-unit scene) and BUG-0881's single-order rows are met. This is the spam variant only.
