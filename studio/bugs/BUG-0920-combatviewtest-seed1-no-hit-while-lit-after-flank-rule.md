# BUG-0920: CombatViewTest seed 1 red since the M5-P2 merge: "402 flash hits, 0 while lit"

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed (M6-V12 6a62850, verified by QA 2026-10-17-2344: `CombatViewTest` PASS in the scene loop and alone; "lit again: 4 raiders on one Heavy Infantry, 3 flash hits, 1 while lit by tick 9" on seeds 1 and 6) |
| Found | 2026-10-17-1624, task M6-VH3 (developer report; reproduced by QA on base) |
| System | view tests: `game/tests/CombatViewTest.cs` (hit-flash row) on the M5-P2 sim |
| Fixed by | M6-V12 (6a62850): `CombatViewTest.LitAgain` stages the row on its own; the brawl's own count is printed, not required |

## Repro
1. Base f8036fc (before M6-VH3), built: `& $env:GODOT --headless --path game res://tests/CombatViewTest.tscn`
2. Same on M6-VH3 head c0a8d5b (and in `tools/qa/scene-loop.ps1`).

## Expected
`COMBAT VIEW TEST PASS`, as at the M6-V11 ACCEPT (view branch 93b24ae, loop 65 / 65).

## Actual
Identical on base and head:
```
seed 1: 32 unit deaths, 1 building deaths by tick 726, ... flash hits 402, 0 while lit; ...
seed 6: 36 unit deaths, ... flash hits 444, 7 while lit; ...
COMBAT VIEW TEST FAIL: seed 1: 402 flash hits, 0 while lit
```
The row `_hits > 50 && _hitWhileLit > 0` wants at least one unit hit again while its flash is still lit. On seed 1 no
unit is hit twice inside the flash window any more.

## Notes
Not caused by M6-VH3 (same numbers on f8036fc, no view change touches the flash). Appeared with the merge of the
M5-P2 sim (flank through your own line, 3ec22ee / cbeb944) into the view branch at 63e3a31: the brawl's hit cadence
changed, the view did not. The flash code is unchanged, and seed 6 still has 7 hits while lit, so this looks like a staged
scene whose fight shape moved (the BUG-0147 class), not a view defect. Fix in the view's test: stage the "hit while lit"
row on its own (two attackers on one target in the same tick window) instead of relying on seed 1's brawl, or count it
over both seeds. Keeps the scene loop red (63 / 65 with BUG-0921) until fixed.
