# BUG-1855: Pale Hills Normal v Normal seed 7 (and 8, the same match) no longer ends by 72,000

| Field | Value |
| --- | --- |
| Severity | S4 (a report row; not a criterion row) |
| Status | open (a finding for the sim's next AI task) |
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
