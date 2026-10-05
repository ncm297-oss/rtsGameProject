# BUG-0021: Since the build cap, the flow-field cache's contents change unit movement, but the cache is not hashed

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-04-0120, task M1-4b (fix round 1 re-check) |
| System | pathfinding (FlowFieldCache) / movement / determinism |
| Fixed by | 8c96b53 (M1-4c); QA/FieldCacheHashQaTests |

## Repro
1. Un-skip `Rts.Sim.Tests.QA.FieldBuildCapQaTests.BuildCap_FieldCacheIsDerivedState_PrewarmingItDoesNotChangeTheSim`.
2. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~PrewarmingItDoesNotChangeTheSim"`

The test sets up two identical sims (seed 4021, 64 units) and gives both the same Moves to 8 goal
cells. Sim B also calls `World.FlowFields.Get(cell)` on those 8 cells. Before any tick the two
StateHashes are equal.

## Expected
docs/03 says: "The cache is derived state and not hashed: a field depends only on the grid and its
target." Two worlds with equal StateHash should stay equal no matter what their caches hold. That
is the assumption behind save/load ("save/load mid-run equals an uninterrupted run") and behind
using the hash to detect desyncs.

## Actual
```
tick 1: equal-hash sims diverged because only one had the fields cached
```
Sim B moves all 8 groups on the first tick. Sim A, capped at one build per tick, starts them one
tick apart. Whether a unit waits now depends on cache contents (which fields are cached, and their
LRU order), and the hash covers neither.

## Notes
- Today, same seed plus same commands still gives the same hash, because nothing but
  MovementSystem touches the cache. So this is latent, and S3 for now.
- It becomes an S1 determinism break as soon as any of these lands:
  - Save/load (roadmap: "Save/load and replay playback"). A loaded game starts with an empty
    cache and diverges from the uninterrupted run.
  - Any other caller of the public `World.FlowFields.Get`, such as AI, view or debug overlays.
- Possible fixes, for the dev or Producer to choose:
  - Make the wait rule independent of the cache. For example, serve goals in a deterministic
    order kept in hashed sim state, such as a pending-build queue keyed by goal cell and order
    tick.
  - Or treat the cache key, version and LRU order as sim state (hash and save it), and make
    `FlowFields` non-public.
- The docs/03 sentence quoted above needs updating either way. The fix round also added "units
  don't interact yet, so the order changes no result". That is no longer quite true, because the
  goal order decides who waits (see BUG-0022).

**Producer triage (2026-10-04-0120):** S3 today, latent S1; agreed. docs/03 corrected in the
ACCEPT commit (the cache is no longer called derived state). Decision (docs/01 change log): treat
the cache's keys, versions and LRU order as sim state: hash them in `StateHash`, save them with
save/load (fields rebuild from keys at load), and keep `Get`/`TryGetCached` sim-only (a read-only
peek for views and AI, if ever needed). Planned as the first part of the next movement task,
before steering builds on the cap. Un-skip the QA test when fixed.

**QA verification (2026-10-04-2056, M1-4c):** fixed as decided. `StateHash` now covers the cache
clock, count, and each used slot's requested cell, version and last-use stamp. It also covers
each live unit's `OrderTick`. `Get`/`TryGetCached` are internal.
- `QA/FieldCacheHashQaTests`: the hash changes on every mutation path (miss build, Get hit,
  TryGetCached touch, stale rebuild, fill, eviction). `Contains` and a TryGetCached miss leave it
  unchanged.
- A reflection audit classifies every FlowFieldCache/FlowField field as hashed or derived. The
  public surface is read-only (`CapacityFor`, `Contains`, getters).
- Save/load proxy: 50 seeds x 500 ticks of random Moves, with one-sided and mirrored internal
  Gets. On every tick, the hash is equal exactly when QA's reflection signature (units plus cache
  metadata) is equal; 49 seeds diverged and none collided.
- Mutation check: dropping last-use or OrderTick from the hash is caught.
- The dev's rewrite of QA's pre-warm test (now `..._PrewarmingChangesTheHash_IdenticalPrewarmsStayEqual`)
  matches the decision.
- Watch item: `NavGrid.Version` itself is not hashed. That is fine while the grid is immutable.
  When passability can change (M3+), the grid state must join the hash.
