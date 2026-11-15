# BUG-1433: M6-S9 small findings: the knob is not continuous at 0 (any value turns on a floor off lacks), criterion 4's enemy scenes barely meet, the fuzz harness's left-out seeds and a bare "no Keep"

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-11-14-2156, task M6-S9 (QA) |
| System | sim: movement (`MovementSystem.GatherFloor`), tests (`Movement/GatherOverlapTests`, `Movement/GatherOverlapFuzzTests`), docs/03 "Implementation (M6-S9)" |
| Fixed by | |

## Repro
1. **Not continuous at 0.** `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~GatherOverlapQaStressTests.ATinyValue_Report" --logger "console;verbosity=detailed"`
   (24 workers on one mine, 5 min; banked | trips | stuck ticks | give-ups | closest pair (share of sum) | mean stand gap):
   ```
   Laborer 0:     3430 | 343 | 2796 | 36 | 0.119 m (0.15) | 0.754 m
   Laborer 0.001: 3450 | 345 | 2862 | 38 | 0.400 m (0.50) | 0.762 m
   Laborer 0.25:  3630 | 363 | 1475 | 18 | 0.300 m (0.38) | 0.581 m
   Gatherer 0:     2790 | 186 | 8613 | 185 | 0.208 m (0.14) | 1.126 m
   Gatherer 0.001: 2745 | 183 | 7990 | 187 | 0.749 m (0.50) | 1.173 m
   ```
   Any value above 0 turns on `GatherFloor` / `ShovedPastGatherFloor`, a hard floor at `ShoveSpacing x` the scaled sum that
   today's rules don't have (off, walkers come to 0.03-0.15 of the sum). So 0.001 keeps gather pairs *farther* apart than
   off, and at 0.25 their closest approach (0.38 of the sum) is still farther than off's (0.15). Docs/02 reads "may overlap a
   share of their touching distance"; nothing says a small value is stiffer than none. Worth one sentence in docs/03 (or a
   floor that fades in with the value) before the next PLAN picks a number.
2. **Criterion 4's enemy scenes barely meet.** `GatherOverlapTests.Criterion4_TwoPlayersWorkersOnOneMine_OneEach` has one
   worker a side (no gather pair in the scene at all), and the "two players' 6 workers each" scene of
   `Criterion4_ScenesWithGatherPairs_...` prints "2 / 2 pair-ticks within reach" (the two sides almost never meet). The
   enemy rule is covered by QA's `EnemyWorkersSharingAMine_KeepTheFullSumBetweenThem` (12 v 12 on one mine, 4 seeds:
   closest enemy pair 0.32 of the sum off, 1.00 at 0.5 and 0.75 for Gatherers; 0.82 off, 1.47 / 1.00 for Laborers).
3. **The fuzz harness.** `GatherOverlapFuzzTests.Run` throws a bare `InvalidOperationException: no Keep` on seed 13 (its
   `KeepCenter` finds no building for a player whose Keep was refused) instead of an assert saying the scene can't be
   built. The asserting rows' comment names seeds 1, 3 and 4 as left out but not 6 and 7 (7 at 0.75 fails: BUG-1430).

## Notes
None of these change the shipped game (the knob is off). Item 1 matters for the value chosen next session.
