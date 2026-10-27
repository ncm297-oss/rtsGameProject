# BUG-1043: Reverting QueryEnemies' bucket skip to the old owner compare passes every test (criterion 7)

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | fixed |
| Found | 2026-10-27-0158, task M6-S4 |
| System | Spatial hash (`SpatialHash.QueryEnemies`), tests |
| Fixed by | 0a2b9e2: `SkipsBucketFor` removed; `QueryEnemies` runs an internal `QueryEnemies(..., out bucketsRead)` overload; `SpatialHashTests.QueryEnemies_SkipsAllies_AndAnAlliedOnlyBucketIsSkippable` asserts the walked-bucket count for player 1 of team {0, 1} |

## Repro
1. In `sim/Rts.Sim/Spatial/SpatialHash.cs`, `QueryEnemies`, change the bucket skip back to the pre-M6-S4 line:
   `if (o == EmptyBucket || o == side) continue;` -> `if (o == EmptyBucket || o == player) continue;`
2. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~TeamTests|FullyQualifiedName~SpatialHashTests|FullyQualifiedName~QA.TeamQaTests"`

## Expected
M6-S4 criterion 7: "SpatialHash allied skip tests incl. single-owner bucket skip and OthersThan; mutation keeping the old
owner skip fails them."

## Actual
73 / 73 pass with the mutation (QA mutation run in a scratch clone of 311bcca). The mutation keeps results right (the entry
loop still skips the side), so only the skip is lost: for every player that isn't the lowest of its team (player 1 of a
team {0, 1}), no allied-only bucket is skipped any more, and a 2v2 scan reads every allied bucket entry by entry.

The developer's test (`QueryEnemies_SkipsAllies_AndAnAlliedOnlyBucketIsSkippable`) asks the test seam
`SpatialHash.SkipsBucketFor`, which re-implements the compare (`o == SideOf(player)`) instead of sharing it with
`QueryEnemies`, so the seam stays right while the query's own line regresses. Reverting the hash's side codes in
`Rebuild` (`int o = owner[s]`) is caught (by `EnemyQueries_WithTeams_EqualTheBruteForce` and QA's fuzz), but that is a
different line.

## Notes
- Suggested fix: make `QueryEnemies` and `SkipsBucketFor` use one private helper (e.g. `SkipsBucket(int bucket, int side)`),
  or give the seam a count of buckets `QueryEnemies` read in its last call and assert it in the allied-only bucket test.
- Perf only, no wrong result, but the criterion names this mutation explicitly, hence S2.

## Verification (QA, 2026-10-27-0158 round 1)
Scratch clone of 0a2b9e2, filter `SpatialHash|Team` (246 tests) or `SpatialHash|TeamArchitecture|TeamTests` (175):
- `o == side` -> `o == player` (this bug's repro): **1 failed** (`QueryEnemies_SkipsAllies_AndAnAlliedOnlyBucketIsSkippable`).
- No side skip (`o == EmptyBucket` only): 1 failed (same row).
- No `_ownerCodes` guard: 2 failed (`EnemyQueries_WithTeams_EqualTheBruteForce` seeds 2, 3).
- Rebuild bucket code last-writer-wins: 4 failed. Bucket code mixed on any second entry (speed only): 1 failed.
- Rebuild entry side = raw owner: 6 failed.
- `o == SideOf(player)` (equivalent): passes, as it should.
Unmutated: 246 / 246. Status: fixed.
