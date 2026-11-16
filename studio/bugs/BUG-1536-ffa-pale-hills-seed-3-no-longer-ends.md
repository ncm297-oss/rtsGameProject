# BUG-1536: Stress row `HuntStressTests.FourAiSeats_OnPaleHills_EndWithTheOracleOnEveryHunt(seed 3, FFA)` red, not named in the M6-SH2 report: the four-seat match no longer ends by 144,000

| Field | Value |
| --- | --- |
| Severity | S2 (a match that stops ending; Stress red outside the rows M6-SH2's criterion 11 allows) |
| Status | open |
| Found | 2026-11-15-2136, task M6-SH2 (QA) |
| System | AI end game (hunt), moved by the drop-off rule (BUG-1520) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~HuntStressTests.FourAiSeats_OnPaleHills_EndWithTheOracleOnEveryHunt"`
2. Row `(seed: 3, teams: False)` fails after about 17-47 minutes: `seed 3: no result by 144,000 ticks`.

## Expected
The free-for-all on Pale Hills, seed 3, four Normal AI seats, ends with one seat left inside 144,000 ticks, the hunt
oracle clean on every hunting think (as on the base).

## Actual
| Tree | Result |
| --- | --- |
| base `fdc8710` | green: over at 107,419 (89:30); p0 defeated 42:36, p2 61:52, p1 (holy_war) wins with 3 hunts |
| base + item 0 only (no retrain) | red: no result at 144,000. p2 defeated 28:47, p3 out; **p0 (standard, 32 units, 5 hunts) and p1 (holy_war, 13 units, 0 hunts) both alive** |
| M6-SH2 head | red, the same message |

The hunt oracle itself stays clean (leaks 0, misses 0, oracle v fog 0). Two seats with armies stand off for more than
an hour of game time, and the hunting seat's five hunts never find the other's last units. This is BUG-1068 / BUG-1116's
family (the developer's BUG-1523 is the same shape on Pale Hills seed 5, two seats).

## Notes
- The retrain plays no part: item 0 alone gives the same result.
- With BUG-1523 that makes two Pale Hills matches that stopped ending under the drop-off rule. Pale Hills is also the
  map where Hard stopped leading (BUG-1522). Worth one trace of what the economy change does on Pale Hills in particular
  before the pins are accepted.
