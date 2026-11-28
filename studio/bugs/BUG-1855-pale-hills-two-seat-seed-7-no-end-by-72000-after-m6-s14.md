# BUG-1855: Pale Hills Normal v Normal seed 7 (and 8, the same match) no longer ends by 72,000

| Field | Value |
| --- | --- |
| Severity | S4 (a report row; not a criterion row) |
| Status | open (re-stated with M6-SH3 A's trace; a finding for the next AI task) |
| Found | 2026-11-26-1847, task M6-S14 (`Stress/MatchRowsM6S10Report`, the session's two-seat probe) |
| System | AI end game (two seats) |
| Fixed by | |

## Repro
1. `Stress/MatchRowsM6S10Report` or `MapFileTests.MapMatch("pale_hills", 7, ...)` to 72,000: `ROW pale_hills seed 7, 2 seats:
   over False at 72000 (60:00); winner none; waves 24, hunts 2; refused: none; error none`.
2. Seat 0 hunts twice; seat 1 is stamped starved at 65,940 and waves starved at 69,240. Never spent (no draw).

## Expected
docs/02: the AI finishes the match. M6-S12's table read 27,017 (p0).

## Actual
No end by 72,000; every check clean. Every other seed 1-8 on Raraku, Pale Hills and Vathar Crossing ends.

## Notes
- Not traced in M6-S14 (measure and report under rule 48).

## M6-SH3 A (2026-11-27-0817): traced, not cured
`Stress/PaleHillsStallTraceTests` (docs/03 "Implementation (M6-SH3)" A). The match is not spent and should not be: at 72,000
seat 0 has 1,784 gold / 10,575 wood and seat 1 75 / 2,755, both can pay for units. Seat 0 (52 army units at its population
cap, 6 paid units waiting at the cap) hunts from before 48,000 but never knows a seat-1 building: every one stands on ground it
explored before it was built, so it has no ghost. Seat 1's waves (about every 2,900 ticks) pull seat 0's army home to defend,
and the hunt's leg to its first sweep point (point 200, the east edge at (511, 179) m, about 370 m away) is given up before it
is reached and re-sent after each defense to the same point (14 timeline entries at "sweep 200", 49,701-70,341). Seat 1's base
is at about (322, 398) m, the other way. Once seat 1's army is gone (its starved wave at 69,240) the sweep moves on (202-206 by
71,941). Cure (not done: more than 30 lines of new hunt behaviour): a hunt that resumes after a defense should keep the
points it passed or sweep from where the army stands rather than re-send the same leg.
