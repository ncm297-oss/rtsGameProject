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

## Re-check 2026-11-14-2156, fix round 1 (4377d68)
- Item 1 (the floor at a small value): now documented in a docs/03 bullet that names BUG-1433; no fading floor was built.
  That's acceptable as documented, and stays open only as a design choice for the next PLAN.
- Item 2 (enemy scenes): **half fixed.** The 6-v-6 scene now has player 1's Keep beside player 0's: 17,253 / 14,703 enemy
  pair-ticks within 3 m (off / 0.5), asserted at least 500. QA re-ran it green. But the one-worker-each hash-equal row
  (`Criterion4_TwoPlayersWorkersOnOneMine_OneEach_IsHashEqualAtZeroAndHalf`) still never brings the two workers together.
  QA rebuilt the same scene (`QA/GatherOverlapQaTests.EnemyScene_OneEach_DoTheTwoWorkersMeet_Report`): **0 ticks within 3 m
  in 2,400, closest 5.51 of the radii sum**, at 0 and 0.5 (both banks 260). The row proves nothing about enemy contact; it
  would be hash-equal under any rule scoped to gather pairs. Criterion 4 is still met through the 6-v-6 scene and QA's
  12-v-12 rows.
- Item 3 (the harness): fixed. Seed 13 now fails with "player 1's Keep was refused (no Keep), so the fuzz scene can't be
  built on this seed" (`Seed13_TheSceneCannotBeBuilt_AndTheHarnessSaysSo`, green). The left-out seeds are listed in `Seeds`
  and `KnownWedges` with their reasons.

Status: open (only item 2's one-each row).
