# BUG-1310: BUG-1280's own new test goes red the moment the data track ships the docs/03 M7-4 rows

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | open |
| Found | 2026-11-13-1118, task M7-6 (QA full) |
| System | sim test suite (M7-4 fixtures; cross-track sequencing, as BUG-1222 / BUG-1280) |
| Fixed by | |

## Repro
1. Scratch clone of `studio` at 18be020. Apply docs/03 "For the data track (M7-4)" to `game/data` exactly as written
   (QA's script: `python -I <scratchpad>/qa1310/tools/rows.py <clone>`: the `teblor_frenzied` row appended to
   `common/statuses.json`, `blood_oil_frenzy`'s effect pointed at it, the `statusDuration` effect appended to
   `teblor_blood_oil_anointing`, `"scale": 1.25` on every Teblor building; four files change).
2. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~BloodOilScenesTests"`

## Expected
The task's goal (brief M7-6): make the M7-4 test copy tolerate the shipped rows "so the data track can land the Teblor
rows ... next session". The data track owns only `Content/**` and `QA/Content/**`, so no sim-owned default-loop test
may go red when it lands exactly the rows docs/03 tells it to write (rule 58).

## Actual
```
Failed Rts.Sim.Tests.BloodOilScenesTests.Edit_OnACopyCarryingTheDataTracksRows_LeavesOneFrenzyRow_AndOneFourSecondStatusDuration
Assert.True() Failure  Expected: True  Actual: False
  at BloodOilScenesTests.cs:line 49
```
Line 49 is the premise `Assert.True(DataLoader.LoadAll(dir.Path).Ok)` after `DataTrackRows(dir)`. `DataTrackRows`
appends the four rows blindly, the same defect BUG-1280 fixed in `BloodOilScenes.Edit`: on data that already ships the
row it writes a second `teblor_frenzied` (duplicate id), so the copy does not load. The test is in the default loop
(criterion 2), so the data track's Teblor session would see one red sim-owned test it may not edit.

The developer's criterion-1 proof (428 / 428) used BUG-1280's original filter, which does not include
`BloodOilScenesTests`; QA's wider run on the same rows (BUG-1280's filter plus `BloodOilScenes`, `OpeningReserveShare`,
`AiLoaderTests`, `DataContentHashTests`, `PassivePerf`) was 619 / 621: this test, and a Perf row that passes alone
(CPU contention). See the QA report for the whole default loop on the rows.

## Notes
- Fix: make `DataTrackRows` idempotent too (remove an existing `teblor_frenzied` row and `statusDuration` effect before
  appending, as `Edit` now does), or skip the append when the shipped files already carry the row, and keep the
  premise. A regression test is QA's: run `BloodOilScenesTests` on a copy whose shipped files carry the rows.
- Severity S2: the task's goal and rule 58 (apply the data-track rows and run the classes), not a literal criterion;
  criterion 1's filter itself passes.
