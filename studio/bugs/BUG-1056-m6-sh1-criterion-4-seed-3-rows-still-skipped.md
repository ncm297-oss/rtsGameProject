# BUG-1056: M6-SH1 criterion 4 not met: two of the three `ScoutKilledAfterTheRing_*` rows are still skipped

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | fixed (M6-SH1 round 1, dev; QA confirmed 2026-11-05-2148 re-verify: both rows green at 5,541 / 5,619; the release has two follow-ups, BUG-1057 / BUG-1058): `ReachRuleQaTests.ScoutKilledAfterTheRing_Seed3_TheSeatStillGetsWood` un-skipped and green (5,541 / 5,619), `Ai/ScoutReplacementTests.AScoutSentAgain_KnowingAFarForestClearOfTheEnemy_IsReleasedToGather` / `..._KnowingOnlyAForestAtAnEnemysDoor_KeepsSweeping`. Seed 1 at 2,000 keeps its 32-tick margin (its forest is at an enemy's door, so no release) |
| Found | 2026-11-05-2148, task M6-SH1 (QA) |
| System | AI scout / economy (BUG-1053's re-scout), `QA/ReachRuleQaTests` |
| Fixed by | M6-SH1 round 1: a scout sent again is released to gather once the reach has lifted and it knows an enemy building and a node of every kind, its nearest forest not within `ai.workerFleeRadius` of an enemy building |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~ReachRuleQaTests.ScoutKilledAfterTheRing"`: 1 passed, 2 skipped.
2. The skipped rows were moved into a new method, `ScoutKilledAfterTheRing_Seed3_TheSeatStillGetsWood` (seed 3 at 200 and
   at 2,000), with `Skip = "BUG-1053 (open, S3) ..."`. Remove the `Skip` and both fail: no wood by tick 6,000.

## Expected
The M6-SH1 brief, criterion 4: "`ScoutKilledAfterTheRing_*` un-skipped and green" (scope (c): "wood > 0 by 6,000 on the
three rows").

## Actual
- Seed 1 at 2,000: green. Re-scout at tick 2,341, first wood at tick 5,968, which is 32 ticks inside the 6,000 bound.
  The run is deterministic, so the row is not flaky, but any change to movement or the AI's timing can push it over.
- Seed 3 at 200: the scout dies before the ring is seen. M6-S7 replaces it and the replacement lives, so the re-scout
  never fires. The sweep first sees a forest at about tick 4,250, 179 m off, and no gatherer is back by tick 6,000.
- Seed 3 at 2,000: the re-scout is sent (tick 2,341), but the same sweep order finds the same far forest too late.
- Over seeds 1-10 (`QA/ScoutCapQaTests.OneScoutKilled_At0_200_2000_OnAFarForestStart_Seeds1To10`, printed rows), the 90 m
  start with the scout killed at 2,000 gets wood by 6,000 on only 4 of 10 seeds. The unharmed run gets it on 9 of 10, but
  late (first wood at ticks 3,649-5,782). A kill at 2,000 costs about 2,000 ticks of sweeping, so most rows miss the bound.

## Notes
The developer reported this as PARTIAL and named the cause: a fix needs a sweep that looks round home before the far half
when no forest is known near it, which would move every first scout and so the pins. The Producer can waive the criterion
for seed 3, or move it to a follow-up with the sweep-order change. BUG-1053 stays open either way.
