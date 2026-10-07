# BUG-0124: M3-V3's tests queue Age II at a Town Hall with no halls, so merged with M3-6 the view branch is red (and `main`'s smoke gate fails until it lands)

| Field | Value |
| --- | --- |
| Severity | S2 (holds the view branch off `main`; `main` fails `tools/qa/smoke.ps1` meanwhile) |
| Status | open |
| Found | 2026-10-07-1415 integration (conductor), after the view's QA had passed against a stand-in for M3-6's enums |
| System | view tests: `sim/Rts.Sim.Tests/ViewApi/ProductionHudTests.cs`, `game/tests/ProductionHudTest.cs` (criterion 3 `QueueStripRun`, the Age II flash), probably `game/tests/QaV3Test.cs` (`GreyingEveryFrame`, `QueueStripEveryFrame`, the 600-tick hash twin that researches Age II) |
| Fixed by | |

## Repro
1. `origin/studio/2026-10-07-1415-view` at 5f89068 (M3-V3 merged with `origin/main` da654c6 = M3-6).
2. `dotnet test sim/Rts.Sim.Tests --filter Category!=Perf` → 1 failed / 3255 passed:
   `ProductionHudTests.QueueStrip_CountAndHeadFill_FollowTheQueueThroughLaborerAgeIILaborer`, line 80, expected 3
   got 2: the `Research(age_ii)` is dropped because `CanResearch` answers `Requires` (Age II needs any two of the four
   halls finished; the fixture spawns only a Town Hall).
3. The Godot scenes were not run by the conductor; `ProductionHudTest` criterion 3 queues `[laborer, age_ii, laborer]`
   the same way and expects the Age II flash, so expect the same failure there (and wherever `QaV3Test` researches
   Age II).

## Expected
The view's tests set up the sim state their scenario needs: two finished halls of distinct slots (for example a
Barracks and an Armory through the dev `SpawnBuilding`, which ignores requirements) before queueing Age II, and, for the
greying rows, a case with no halls that expects `research.requires` ("Locked"). The branch then merges green.

## Actual
As above. `main` (da654c6) carries M3-6's `PlacementError.Requires` without the view's `ui.json` `placement.requires`,
so `tools/qa/smoke.ps1` on `main` fails with `ERROR: ui.json: missing placement.requires` until the view branch lands
(the game runs, but logs the error and hides the command card and the selection panel).

## Notes
- Same class as BUG-0112: a sim gating change hit a fixture in another track written when nothing was locked. The
  view's QA tested "boots against an M3-6 stand-in" (enum members only), not against the real gating.
- Fix is view-owned and small (fixture setup + the scene's setup); first item of the view's next session, which also
  checks every scene against the held D3 `buildings.json` (building locks) before reporting.
