# BUG-0096: Builds refused for SealsGround still pay a flood each: 100 in one tick cost 33 ms (BUG-0091 residual)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed (M3-H2) |
| Found | 2026-10-07-0800, task M3-H1 |
| System | construction / placement rule (`ConstructionSystem.Check`, `Map/SealCheck`) |
| Fixed by | 3f494c0 (M3-H2): `SealCheck` keeps the last flood's answer per (anchor, size, `NavGrid.Version`). Tests: `QA/SimHardeningQaTests.HundredBuildsRefusedForSealsGround_OneTick_Under2Ms` (0.42 ms, was 33 ms), `PlateauTests.HundredBuildsRefusedForSealsGround_InOneTick_FloodOnce`, `TheMemo_*`; QA 2026-10-07-1715 `QA/PlateauSealMemoQaTests.SealMemo_AfterAnyGridChange_EqualsAFreshFlood` (6 seeds x 1,500 grid changes, memo == fresh flood every time), `SealMemo_ACancelEarlierInTheSameTick_*`, `SealMemo_ATreeFelledByAWorker_*` |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~SimHardeningQaTests.HundredBuildsRefusedForSealsGround_OneTick_Report" --logger "console;verbosity=detailed"`
   (`QA/SimHardeningQaTests.cs`): the dev's 128 x 128 long-wall map with the wall carried down to the bottom border, so
   a House in the top gap seals the map; the player can afford it; 100 workers each send `Build` there in one tick.

## Expected
BUG-0091: a refused Build should cost about what the check that refuses it needs. The fix (cheap rules first on the
apply path) makes CannotAfford, UnitInTheWay and StoreFull refusals cheap (measured here: 0.07, 0.04 and 0.09 ms for
100 in a tick). The refusal that *is* the flood can't be cheap the first time, but it need not be paid 100 times.

## Actual
```
100 Builds refused (SealsGround) at the long-detour anchor: tick 33.052 ms   (alone, Debug)
```
Each refused Build floods one side of the wall (~7,900 cells) to prove the seal. A group Build of 20 workers at a red
ghost is a ~6.6 ms tick; the view will send one Build per selected worker.

## Notes
- Not a regression (the same before M3-H1) and outside M3-H1's criteria (criterion 4 measured CannotAfford only).
- BUG-0091's own alternative closes it: remember (anchor, type, `NavGrid.Version`) of the last SealsGround refusal
  within a tick and refuse repeats without flooding; or have the view send one Build for the group and queue the rest
  as joins.
