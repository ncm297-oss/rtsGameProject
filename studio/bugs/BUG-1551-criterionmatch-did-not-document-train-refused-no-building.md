# BUG-1551: `CriterionMatch` did not document `Train:TrainRefused:NoBuilding`

| Field | Value |
| --- | --- |
| Severity | S4 (test oracle) |
| Status | fixed (M6-S12, 2026-11-16-2036) |
| Found | 2026-11-16-2036, task M6-S12 (the developer) |
| System | AI match oracle (tests) |
| Fixed by | M6-S12 (efa7b31): `CriterionMatch.Documented` gains the kind; docs/03 M5-5's refusal list |

## Repro
1. The FFA criterion row (Vathar Crossing seed 2, 4 seats) on M6-S12's tree before the entry: stops at tick 17,381 on check (e).

## Expected
A Train sent at a producer that is razed before the command applies is the Train's form of "a target that died the
tick the order was sent": a documented refusal, not an AI fault.

## Actual
Player 1, defending, sent a Priest Train at its producer, razed before the command applied; the oracle had no entry.

## Notes
The AI judged from its view a tick before. The row then ends at 38,690.
