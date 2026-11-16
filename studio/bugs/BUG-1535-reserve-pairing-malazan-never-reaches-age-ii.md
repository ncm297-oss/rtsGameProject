# BUG-1535: `ReservePairingsStressTests` red in two rows, neither named in the M6-SH2 report: the Malazan seat never reaches Age II on seed 2, and the 27-row totals pin moved (no-wave sides 3 -> 2)

| Field | Value |
| --- | --- |
| Severity | S2 (Stress red outside the rows M6-SH2's criterion 11 allows; an AI seat alive at 20:00 that never reaches Age II) |
| Status | open |
| Found | 2026-11-15-2136, task M6-SH2 (QA) |
| System | AI economy / reserve (M5-4g), moved by the drop-off rule (BUG-1520) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~ReservePairingsStressTests.Pairing_Twins_AgeTwoForEverySideAlive"`
2. Row `(malazan: 0, whirlwind: 1, nth: 1)` fails: `seed 2: side 0 alive at 20:00 without Age II`.

## Expected
The row's oracle: in the pairing (Malazan `standard` v Whirlwind `raider_rush`), every side still alive at 20:00 has
reached Age II.

## Actual
Measured by QA, the same row on three trees:

| Tree | Malazan seat, seed 2 |
| --- | --- |
| base `fdc8710` | green: Age II 12:38, first wave 11:37, gathered 4,570 by 10:00 and 8,330 by 20:00 |
| base + item 0 only (no retrain) | red: Age II never, first wave 6:51, gathered 4,410 by 10:00 and **5,940** by 20:00 |
| M6-SH2 head | red, the same line as item 0 only |

On the red trees the seat reserves for step 19 (Research `age_ii`, 400 / 200) from 6:51 and ends at 20:00 with 41 gold and
100 wood. The Whirlwind seat reaches Age II at 9:51. The income drop is in the second ten minutes (4,410 -> 5,940 against
4,570 -> 8,330), which looks like the raider rush's pressure after an earlier, smaller Malazan wave. QA found no stuck
or waiting worker behind it. It is a match outcome moved by the rule, not a wedge QA could point to.

## Also in the same class (full Stress run on the head)
- The 27-row totals (asserted by whichever row completes the set, here `(1, 0, nth 2)`) fail: `no-wave sides 2 (= 3)`.
  The other totals hold: Malazan waves 83 (>= 79), defeats 7 / 4 (<= 8 / 5), alive without Age II 1 of 43.
- All four `KnownNoAgeTwo` entries print "now reaches Age II or is defeated: take it out" (seeds 19, 32, 13, 9). The list
  and `NoWaveSides` need the one re-measure the brief's criterion 8 gives the other pins. The net move looks like an
  improvement (1 side of 43 without Age II, 4 before), except for the new seed-2 case above.

## Notes
- The retrain plays no part: item 0 alone gives the same line.
- For the Producer: trace (does the 6:51 wave, sent with the reserve held, throw the economy away?) or re-stage with the
  reason. The developer's report says "Stress is red only in rows named here". This row and BUG-1533 / BUG-1534 show the
  developer's Stress pass did not cover the whole category.
