# BUG-0705: A Perf-tagged QA row goes red for a functional reason under the new win rule (a House-only player's SpawnUnits dropped)

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | fixed |
| Found | 2026-10-14-0946, task M5-4e |
| System | tests (defeat rule re-staging) |
| Fixed by | 9793159 (M5-4e fix round 1), verified by QA 2026-10-14-0946 round 1 |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~HundredBuildsRefusedForUnitInTheWayOrStoreFull"`
2. At `947a61a` (M5-4e): **Failed**, `System.InvalidOperationException : spawn refused` at `GatherMaps.Unit` (GatherMaps.cs:54)
   from `SimHardeningQaTests.HundredBuilds` line 140, called from line 174 (the "store full" half).
3. At the diff base `73c3c4a`: **Passed** (same command in a fresh clone).

## Expected
Brief criterion 6: "full suite green (Perf alone once)". The developer's report says the Perf rows that fail "fail
identically on base under load"; this one does not, and its failure has nothing to do with the clock.

## Actual
The "store full" scene (`LongWall(..., buildingCapacity: 1)`) gives player 0 one House straight into the store and no
unit, then spawns its 100 workers **through the door** (`GatherMaps.Unit` = a `SpawnUnit` command). Under M5-4e a House
alone doesn't keep a side in, so player 0 is defeated on the next tick and every later `SpawnUnit` is dropped: the scene
can't be built. It is `[Trait("Category","Perf")]`, so the non-Perf loop the developer ran never saw it; in a Perf run it
hides among the wall-clock failures of a loaded machine.

## Notes
- Fix: re-stage like the ~20 other rows (`TestSim.KeepIn(full, 0)` before the House, or spawn the workers first); don't
  loosen the 2 ms bound. QA-owned row, so the dev re-stages it the same way as the others (rule: a dev never loosens a QA
  row; re-staging with a keeper is what QA accepted elsewhere this session).
- QA also ran the whole suite in a scratch clone with a probe that throws whenever a player is defeated while it still
  has a live building (a defeat only the new rule makes), and one that throws when the door drops such a player's
  order, to find other scenes the rule changed silently: no other red row; 13 green rows that now test less are
  BUG-0706.

## Verification (QA, round 1, 9793159)
- `TestSim.KeepIn(full, 0)` before the House; no bound changed (still 2 ms).
- Perf alone on a quiet machine: 173 / 176 passed, 3 skipped, 0 failed, this row included.
- Mutation: with the keeper line removed (scratch clone) the row fails again with `spawn refused`; with it, green.
- Door-drop probe over the whole suite (scratch clone): this row no longer hits it.
