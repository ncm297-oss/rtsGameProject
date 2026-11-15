# BUG-1434: Pale Hills, Normal v Normal seed 1: a returning worker goes 4.4 minutes without a delivery near (112, 381), with the gather overlap off

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-11-14-2156, task M6-S9 (QA, while probing why Pale Hills no longer ends at 0.5) |
| System | sim: economy / movement (return leg), shipped map `pale_hills` |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~GatherOverlapPaleHillsProbeQaStressTests" --logger "console;verbosity=detailed"`
   (Stress, about 7 min a row; the 0 row is today's shipped game).
2. The probe tracks, for every unit with a gather loop whose node lives, the ticks since its last delivery (a new node or
   no node restarts the clock).

## Expected
A worker on its loop delivers well inside a minute or two; Pale Hills is 256 cells (512 m) a side and a Laborer (4 m/s)
crosses it corner to corner in about three minutes, while a hall is never that far from a worker's node.

## Actual
```
0:   longest a worker on its loop went without a trip: 5334 ticks (unit 7 p1 Returning at <112.09452, 380.71405> cargo 10 stuck 14 tick 40461)
0.5: longest a worker on its loop went without a trip: 3133 ticks (unit 40 p1 Returning at <112.58138, 382.48874> cargo 10 stuck 3 tick 37988)
```
Both runs wedge a seat-1 worker carrying a load at the same spot, 4.4 min with the knob off and 2.6 min at 0.5.

## Notes
- Not caused by M6-S9 (it happens with the field absent). Possibly BUG-1431's family (a returning worker held off a
  drop-off's corner), or a return leg to a far hall after the near one fell (the probe doesn't separate the two; a 267 s
  gap is longer than the map's diagonal walk, and the worker shows a stuck count).
- Same probe, for the Producer's table (b): Pale Hills at 0.5 does not end because seat 1 is down to **one worker with no
  loop, no building and no army from tick 66,000** and seat 0 never finds it in the last 6,000 ticks (end-game hunt, not an
  economy wedge); at 0 seat 0 is the one wiped out and the match ends at 50,140.
