# BUG-0883: BUG-0157's attack-move spam bound broken on the gap-10 brawl (mean 86.6 %, worst 71 %)

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | open |
| Found | 2026-10-17-0554, task M5-P2 |
| System | combat chase / movement (flank rule) under re-issued attack-moves |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~KeptChase_MeanBound_HoldsOnOtherBrawls"`
2. Row `(phase: 0, gap: 10, ranks: 5)` is red at 3ec22ee; green with `World.FlankRuleOn` defaulted off (QA's scratch clone).

## Expected
Producer decision 2026-10-08-2144 (BUG-0157): jittered attack-move spam over intervals 1-20 ticks at 40 v 40 deals on
average at least 90 % of one order's damage in 400 ticks, no interval under 80 %.

## Actual
`phase 0, gap 10, ranks 5: one order 2988; 1: 81 %, 2: 71 %, 3: 90 %, 4: 96 %, 5: 92 %, 6: 84 %, 7: 86 %, 8: 92 %, 10: 81 %,
12: 98 %, 15: 88 %, 20: 79 %; mean 86.6 %, worst 71 %`. The other four rows pass (means 90.8-104.9 %; gap 6 is at 90.8 %,
worst 81 %, close to the line).

## Notes
- The dev's reading (docs/03 slice-2 "Moved elsewhere"): the single order now deals +46 % (2,988 v 2,043) because back
  ranks walk round their own front; the spam deals more in absolute terms too (about 2,590 v 1,980). But clicking every 2
  ticks now costs 29 % of the fight, against a bound of 20 %: re-issuing an attack-move seems to throw away the flank's
  progress (a new order resets the walk's `BestRemaining` / order age, and the kept side reads the last step's velocity).
- QA does not move a Producer-decided bound. Either fix (keep the flank across a re-issued order to the same target) or the
  Producer re-decides the bound with this row's numbers.
