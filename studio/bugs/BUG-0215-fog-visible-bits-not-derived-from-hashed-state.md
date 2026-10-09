# BUG-0215: The fog's visible bits are not a function of hashed state: equal state hashes, different futures

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-08-1435, task M4-3a |
| System | fog of war / state hash / (M6) save-load |
| Fixed by | M4-H1 (sim track): the visible bits are hashed, packed one `ulong` a 64 cells a player (`FogStore._visible`, read by combat); golden regenerated once (with the visible bits left out locally the old golden reproduced every `k` line); `QA/FogQaTests.TwoWorldsWithTheSameStateHash_PlayTheSameFuture` un-skipped, `Bug0215_TwoWorldsWhoseFogDiffers_HashDifferently` asserts the opposite of the old pin; `StateHashTests.Hash_CoversEveryVisibleBit_OfEveryPlayer`, `PackedVisibleBits_MatchTheByteMap_AndAreWhatTheFogAnswers`; docs/03 "State and hashing" and docs/01 row (d) rewritten |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~FogQaTests.Bug0215"` (passes: it pins today's behavior).
   Two flat-map sims, each a Catapult (sight 18), a holding enemy 21 m away and a holding Laborer spotter. Both
   explore the same cells; at the last fog update A's spotter stands beside the enemy and B's far away; right after
   that update A's spotter is put where B's is (a test seam). Output: `hash a == hash b`, `same fog: False`.
2. The same `Attack(catapult, enemy)` is enqueued in both: A takes it (its fog shows the enemy), B drops it; the hashes
   differ from the next tick.
3. `FogQaTests.TwoWorldsWithTheSameStateHash_PlayTheSameFuture` (skipped with this id) is the row that should pass.

## Expected
docs/03 "State and hashing" and the brief call the visible bits derived ("rebuilt every update, not hashed ... a save
(M6) must run an update after it restores the units"), like the plateau ids: a function of the hashed state.

## Actual
Between updates the visible bits are a function of where the units stood at the **last update**, which the state no
longer holds (positions move every tick; a dead spotter's circle stays until the next update). Combat reads them
(`FogStore.SeesUnit` / `SeesBuildingCells`). So two states with one hash can play differently, and a save taken
between updates and re-stamped at load (the documented M6 plan) would not equal the uninterrupted run, unless saves
happen only right after an update tick.

## Notes
Twins and replays are unaffected today (same history). Options: hash (and later save) the visible bits too, packed like
the explored bits (one more `ulong` a 64 cells a player; the brief allowed ~20 us); or save the visible bytes in M6 and
fix the docs/03 sentence; or restrict saves to the tick after an update and say so. Producer's call; the docs/03 line
is wrong either way.
