# Handoff: next session brief

Written by the Producer at the end of each session for the next one. The next session's
Producer starts from this, verifies it against the repo, and then plans.

_Written: 2026-10-03 (session 2026-10-03-2220, ACCEPT)._

## Where we are

M0 Done. M1 is 3/8: sim core (M1-1), data loader + first data (M1-2), terraced heightmap +
nav grid (M1-3), ramp walls + param safety + spatial hash (M1-4a). Branch `studio/2026-10-03-2220`
@ f282aaf is green: build 0/0, 717 passed + 6 skipped (known-bug tests), `tools/qa/smoke.ps1`
PASS. The conductor merges it to main. Open bugs: 3 S3, 3 S4, none blocking. Sessions today: 5/10.

What exists in the sim: `World(config)` owns `Heightmap`, `NavGrid` and `Spatial`
(`Rts.Sim.Spatial.SpatialHash`). NavGrid: border ring blocked, plateau rims are cliff cells except
at ramp mouths, ramp flanks are cliff cells ("ramp walls"), pockets sealed at build; queries
allocation-free, outside-map reads blocked; `Version` exists (0, nothing bumps it yet). Spatial
hash: 4 m buckets, rebuilt in `Simulation.Tick()` right after commands apply; `QueryRadius`,
`QueryRect`, `NearestEnemy` write ascending slot indices into a caller `Span<int>` and return the
total match count (above buffer length = truncated); not part of `StateHash`. Nothing moves yet;
`GameData` is loaded but unused. Commands: `Noop`, `SpawnUnit` only.

## Next task candidates

1. **M1-4b: flow fields + LRU cache + `Move` command** (docs/03 "Flow fields"). 8-connected
   Dijkstra over `NavGrid` cost from the target cell, no corner cutting (both side cells must be
   passable for a diagonal), per-cell direction + integration cost, LRU cache of 32 fields keyed
   by target cell and tagged with `NavGrid.Version`; `Move(player, slots/handles, target)` command
   that sets a unit's goal; `MovementSystem.Run` moves units along the field at `speed` from
   `UnitDef` (data, not literal) with simple arrival (stop within one cell of the target).
   Separation/shoving OUT. Tests: field correctness vs. Dijkstra oracle on hand-built grids
   (ramps, pockets, walls), no diagonal through blocked corners, cache hit/evict/invalidate on
   `Version`, zero allocation per tick after warm-up, determinism across two sims, Perf: field
   build on 128x128 < 2 ms, 500 units moving < 2 ms/tick. Budget ~500 production lines.
2. **M1-4c: steering, separation, arrival slots, shoving + BUG-0005** (docs/03 "Local movement").
   Separation via `Spatial.QueryRadius` with collision radii from data; arrival slots around the
   target; shoving of idle units; fix BUG-0005 with per-player command buckets. Closes roadmap
   criterion 4 together with 4b.
3. M1-5 scenario test (200 units across 128x128 with obstacles). Needs 4b + 4c.
4. M1-6 replay + determinism golden: **first commit fixes BUG-0014** (mix the seed in `SimRng`)
   because it changes every stream; decide whether terrain hashes into `StateHash` directly.

## Watch out for

- Budget discipline: the last three diffs ran well over budget (M1-4a: ~660 production lines vs
  450, justified by the BUG-0015 rewrite). Keep 4b to flow fields + `Move`; push the rest to 4c.
- Don't ask for two commits per session: the session skill makes one implement commit. Ask for a
  report line after the bug-fix part instead.
- BUG-0017: `Stress.SimCoreStressTests.Flood_10000Commands_OneTick_AllocatesNothing` failed once
  in 11 QA runs (0 in the Producer's reruns). If main goes red on it, harden per the bug file
  (re-measure the block once, report both deltas) rather than loosening the assert.
- Generator pins: `MapGeneratorTests.Generate_MatchesMapsFromBeforeTheFastRampChecks` (15 hashes)
  and QA `QA/Oracles/PreBug0015*` + `MapGenEquivalenceQaTests` pin today's maps. A deliberate
  generator change must retire or regenerate them in the same commit and say why.
- `NavGrid.Flood` has no bounds checks: it relies on the outer ring always being blocked. M3
  passability changes (trees, buildings) must never unblock ring cells.
- Spatial hash costs: rebuild scales with bucket count (0.06 ms on 128 map, 0.65 ms on 1024);
  queries in one crowded bucket are dominated by `Span.Sort` (2,500 units in one bucket -> 500
  radius queries 8 ms). Separation in 4c issues one query per unit per tick: measure, consider
  skipping the sort for callers that don't need order (but keep determinism).
- `NearestEnemy` treats any other owner as an enemy; teams arrive with M5/M6 skirmish setup.
- `SimRng` is a mutable struct: always `ref world.Rng(stream)`, never copy it (docs/03).
- Map generation draws only from `RngStream.MapGen`; QA tests pin the first draws of Combat/AI.
- `StateHash` covers the post-generation MapGen RNG state, not terrain directly. No golden
  replays exist yet, so generator changes are still "free" until M1-6.
- `ArchitectureTests.SimSource_UsesNoForbiddenApis` greps sim source for `Math.*` trig, `Random`,
  `DateTime`, `HashCode`, `Parallel`, `Vector<`. `MathF.Floor`/`Sqrt`, `Math.Min/Max/Abs` and
  `System.Numerics.Vector2` are allowed. `SpatialHashTests.Source_UsesNoHashCollectionsOrLinq`
  greps `Spatial/`; extend it to `Pathfinding/` for 4b.
- Zero-allocation tests (`AllocationTests`, NavGrid 100k queries, QA 1M queries) catch any `new`,
  LINQ or closure in per-tick paths. Field builds hit the LRU miss path inside the tick, so the
  Dijkstra open list and field arrays must be preallocated in the cache.
- `QA/DataLoaderQaTests.GameDataTypes_ExposeOnlyImmutableMembers` reflects over `Rts.Sim.Data`;
  `DataJsonContext` disallows unknown members (add to `DataJson.cs` first).
- `tools/qa/smoke.ps1` is the smoke gate; it falls back to the User-scope `GODOT` variable.
- `.sln` Release config builds the game project in Debug (BUG-0002, S4): irrelevant until M6.
