# BUG-0135: M4-1 turns 194 pre-M4 test rows red: two-player scenes now fight (suite red)

| Field | Value |
| --- | --- |
| Severity | S1 |
| Status | open |
| Found | 2026-10-07-2014, task M4-1 |
| System | combat (`CombatSystem`) vs the pre-M4 movement / economy / production test scenes |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests` on f2879b9: Failed 194, Passed 3,328, Skipped 10 (13 m 22 s).
2. 187 non-Perf rows in 62 methods and 7 Perf rows (`ContestedBlob2500_TwoPlayers_Report` 19.66 ms,
   `FourPoints2500_OnePlayerPerPoint_Report` 3.98 ms, `Perf_200WalkersCrossingASettled300UnitBlob`,
   `ASpawnWithNoFreeCellOnASmallPlateau...Under0Point2Ms`, `TwentyHallsAllWaitingForACell_EachTick_Under0point3Ms`,
   `APlacementEveryTwoSeconds_32MarchingGroups_LongestFieldWait_Report` 1.60 s, `ProductionScene_At1x2x5x_Report`).

## Expected
Brief criterion 9: non-Perf green. The door fuzz (`CommandDoorFuzzStressTests`) still green (QA focus).

## Actual
Red, as the developer reported. QA verified the cause independently:

- **Causality.** A scratch variant whose `CombatSystem.Engage` throws fails on every one of the 194 rows (each one
  takes a target at some point). A scratch variant with `CombatSystem.Acquire` returning at once (nobody ever fights)
  runs the full suite with all 194 rows green; only the new M4-1 combat rows fail there (29, all "nobody fought").
- **Classification of the 187 non-Perf rows:**
  - **175 rows: the old scene now fights, as designed.** The assertion is about something a fight legitimately
    changes: alive counts and hash twins against a fight-free expectation (`MinimapDrivenSim` 2,000 -> 1,907,
    `BlobCrossing` 500 -> 237, ...), arrivals (`Crowd_ToOneOrFourClosePoints` "46 of 500 arrived"), enemy plugs that
    now chase or die so walkers get through (all `CrowdRoutingQaTests` / `HardWallQaTests` / `QueuedGiveUpQaTests`
    corridor rows, holders of both players), money oracles that count only live units (every gap is a multiple of a
    unit cost, e.g. 150 / 75 / 50), invariants written as "a holder is Idle" (`CommandDoorFuzz`, `EconomyFuzz`:
    "holding but Attacking") or "a Stop leaves it Idle" (`OrderStress`), best-estimate / order-tick invariants broken
    by a pre-Move engagement (`CrowdJammedAtAGap`: the unit engaged before its Move applied), preconditions that a
    spawn waits for a full ring (units moved off to fight), and `ABuildIsDroppedExactlyWhenCanPlaceSaysSo` (traced:
    2 of 5 seeds the worker died, 3 of 5 a building died in the same tick the site was placed, so the count did not
    move).
  - **9 rows: a real combat defect, BUG-0137** (chase / settle livelock): `TwoHundred_TwoOwners_Seeds1To50` x3,
    `CrossMap_TwoOwners_Report`, `OpeningAndClosingChangesInterleavedWithOrders` x3,
    `MoreGoalsThanCacheSlots_Seeds81To140`, `FiveHundredUnitsTo500RandomGoals_Seeds1To4` (units still Moving after
    30,000 ticks). A scratch "livelock breaker" (a unit that re-walks a chase 12 times without planting drops the
    target and stops scanning for 4,000 ticks) turns the first 8 green; the 9th was traced directly (seed 1: all 8
    units still Moving at tick 8,000 cycle chase / lose sight / walk home every 8 ticks, none in a live fight).
  - **3 rows mixed:** `Crowd_ToOneOrFourClosePoints` rows whose message is "N units still Moving" (seed 1 / 2,500,
    seed 3 / 500, seed 3 / 2,500): crowd fights at four points where the livelock may contribute; not separable.
- **7 Perf rows:** the scenes now brawl (or their preconditions moved). The field-wait row (1.60 s against a 1 s
  threshold) is chasers' field requests competing with marching orders under the build cap; it should be
  re-measured once BUG-0137 is fixed.

## Notes
Not fixable in the sim alone without a decision. The developer's options: (a) a `SimConfig.Combat` switch that the
pre-M4 movement / economy / production scenes turn off (recorded in the replay header with M4-2), or (b) teams /
allies. QA's view: (a) keeps 175 tests meaning what they meant; the QA-owned rows among them (Stress/ and QA/) should
then be pinned to combat off by QA, not loosened. The 9 BUG-0137 rows must stay on combat and go green through the fix.
