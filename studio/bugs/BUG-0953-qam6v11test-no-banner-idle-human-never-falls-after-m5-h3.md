# BUG-0953: QaM6V11Test red since the M5-H3 merge: "no banner by tick 21601" (seed 11's idle human no longer falls; still standing at 44,001)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-17-2344, task M6-V12 (developer report; reproduced by QA on base and head) |
| System | view QA test `game/tests/QaM6V11Test.cs` (the defeated-seat / banner row) on the M5-H3 sim; AI behaviour (sim track) |
| Fixed by | |

## Repro
1. Base b30462a (a fresh clone, built): `& $env:GODOT --headless --path game res://tests/QaM6V11Test.tscn`
2. Head 6a62850: the same (and in `tools/qa/scene-loop.ps1`).

## Expected
`QA M6-V11 TEST PASS`, as at the M6-VH3 QA (BUG-0921: seat 0 fell at tick 11,668, the first Age II at 21,001).

## Actual
Identical on base and head:
```
note: seat 2 first reached Age II at tick 17001 (14:10)
note: live end: 0,0,0,0,0,0,0,1,—,Standing | 4200,2490,76,10,24,16,1,0,—,Standing | 4880,4540,90,82,40,15,4,0,14:10,Standing | ...
QA M6-V11 TEST FAIL: no banner by tick 21601: the defeated-seat row did not run
```
QA ran base with `-- --ticks 44000`: still no banner at tick 44,001 (36:40). Seat 0 (the idle human: 5 workers and a
hall, no orders) has lost its one building but none of its units, so it is never out; the three AIs never hunt the
workers down.

## Notes
Not caused by M6-V12 (same on b30462a; no view change touches the match or the AI). It appeared with the 1624 integration
(the sim's M5-H3: BUG-0790's stand points / gathering from tick 19, BUG-0791's `AnyBuilder` / `StepNoSiteUntil`, the
flank fixes) merged into the view branch at 263aeca. Seed 11's first Age II moved back from 17:30 to 14:10 with it.
Two halves:
- **View test (QA-owned fix):** the banner row needs a seat to fall. Staging the defeat (as `DefeatStaging` does in other
  scenes) instead of waiting for the AI would stop this row tracking AI changes (the BUG-0147 / 0920 / 0921 class).
  Not done in this QA pass: raising `MatchTicks` does not help (44,001 is past the 240 s wall-clock cap anyway).
- **For the sim track / Producer (a note):** before M5-H3, on this seed the AIs razed the idle human's hall and killed its
  five workers by ~9:43; now they raze the hall and leave the workers for 36+ minutes. That is M5 criterion 3's "hunting
  the last units" (M5-4d) gap made visible; worth a look if an idle or turtling player should be finished.
