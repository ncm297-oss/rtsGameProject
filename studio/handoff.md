# Handoff: next session brief

Written by the Producer at the end of each session for the next one. The next session's
Producer starts from this, verifies it against the repo, and then plans.

_Written: 2026-10-03 (session 2026-10-03-1235, ACCEPT). Plan section added by session 2026-10-03-2220._

## Current session plan (2026-10-03-2220)

**TASK_ID:** M1-4a · **Title:** Fix BUG-0011/0012/0013, then add the spatial hash

Plan checks: inbox empty; main @ 4fa3037 build 0/0, 361 passed / 8 skipped (non-Perf), smoke
PASS; verified NavGrid `Version`/`PassableCount`/cliff-as-`Cliff|Blocked` claim; no docs drift;
no S1/S2; sessions today 4/10.

### Brief for game-dev

Goal: close roadmap M1 criterion 3 ("...spatial hash") and clear the three map bugs QA filed
against M1-3, so M1-4b (flow fields + steering) can query neighbours on a nav grid whose ramps
are true corridors. Two commits: bug fixes first, spatial hash second.

Scope:
- **Commit 1 (bug fixes, report a line before continuing):**
  - BUG-0011: in `NavGrid`, the lower-level cells flanking a ramp along its length become
    `Cliff | Blocked` ("ramp walls"). The generator already keeps that one-cell ring flat lower
    ground. Connectivity and >= 50% passable invariants must hold; `MapAssert`, `MapQaChecker`
    and all seed sweeps stay green; un-skip `QA.MapQaTests.RampSides_NoPassableStepSteeperThan30Degrees`.
    One sentence in docs/03 "Implementation (M1-3)".
  - BUG-0012: upper bounds in `MapGenParams.Validate` on `RampWidth`, `RampLength`, `EdgeMargin`,
    `Level2Inset` (at most the smaller map side) checked *before* any arithmetic; `(long)width *
    height` in `Heightmap`; un-skip the two QA theories.
  - BUG-0013: tighten `Validate` caps (`RampTries` <= 128, `MaxAttempts` <= 16, plateaus <= 32)
    so the worst valid case stays ~2 s; lower the guard in `Stress.MapStressTests.WorstCaseValidParams_StillBounded`.
- **Commit 2 (spatial hash):** `Rts.Sim.Spatial.SpatialHash` owned by `World`, rebuilt every
  tick inside `Simulation.Tick()` right after commands apply (spawned units queryable the same
  tick). Bucket = 2 x 2 cells (constant derived from `MapConstants.CellSize`, no literal 4).
  Counting sort into flat arrays sized once in the ctor from `UnitCapacity` and map dims; zero
  allocation per rebuild/query. Units outside the map clamp into the edge bucket (still findable).
  Queries write into a caller `Span<int>` of slot indices and return the count (truncate at
  buffer length, report via `out bool`/return convention, document it):
  `QueryRadius(Vector2 center, float radius, Span<int>)` (point distance <= radius),
  `QueryRect(Vector2 min, Vector2 max, Span<int>)` (inclusive, normalizes swapped corners),
  `NearestEnemy(Vector2 center, float radius, int player, out int slot)` (returns false if none;
  ties -> lowest slot). Result order: ascending slot index (document and test). The hash is
  derived state: it does NOT feed `StateHash`.
- OUT: flow fields, steering, separation, `Move` command, BUG-0005, BUG-0014, collision radii.

Acceptance criteria:
1. The three skipped BUG-0011/0012 QA tests run and pass; `WorstCaseValidParams_StillBounded`
   passes at `MaxAttempts = 16` cap under ~2 s; 200-seed and QA 2,000-seed sweeps green.
2. On every swept seed, no passable 4-neighbour step exceeds 30° (QA checker extended if needed).
3. `SpatialHash` results equal a brute-force scan for >= 1,000 fuzzed (positions, radius/rect)
   cases incl. units on bucket boundaries, outside the map, radius 0, radius > map.
4. `NearestEnemy` matches brute force incl. ties; ignores own-player units and dead slots.
5. Rebuild + 1,000 queries on 500 units allocate 0 bytes (extend `AllocationTests`).
6. Perf: rebuild with 2,500 units < 0.5 ms avg; 500 radius-8 m queries on 500 units < 1 ms
   (`[Trait("Category","Perf")]`).
7. Same world state -> identical query output order twice and across two `Simulation`s.
8. `ArchitectureTests` green; no `Dictionary`/`HashSet`/LINQ in the hash; docs/03 "Spatial hash"
   gets an "Implementation (M1-4a)" paragraph.
9. Build 0 warnings; all tests green; smoke PASS (game/ untouched -> run it anyway).

Design references: docs/03 "Spatial hash" (4 m buckets, counting sort, three queries), docs/03
"Navigation grid" + "Implementation (M1-3)", docs/02 "Map and terrain" 30° rule, docs/03
"Entity model" (slot arrays). Budget ~450 production lines; tests extra.

Tests required: dev `SpatialHashTests` (brute-force fuzz, order, boundaries, outside-map,
truncation), `AllocationTests` extension, Perf test, `NavGridTests` ramp-wall test, `MapGenParams`
bounds tests; un-skip the 3 QA tests; adjust `WorstCaseValidParams_StillBounded`.

Constraints: no allocation in rebuild/query (no `new`, LINQ, closures); deterministic order (no
hash-set iteration); constants derived from `MapConstants`; `ref world.Rng(stream)` never copied;
no Godot; docs updated in the same commit.

### QA focus
Brute-force every query against an independent scan with fuzzed worlds (1-2,500 units, random
+ clustered + all-in-one-cell layouts, 32x32 and 1024x1024 maps); units at exact bucket edges and
at negative / beyond-map coordinates; radius 0, NaN/Inf radius, inverted rect; buffer shorter
than matches (no overrun, count correct); order stability; 0-byte allocation over 1M queries;
rebuild after `Free` and respawn into the same slot (stale entries). Ramp walls: all seeds, step
rule <= 30°, connectivity, passable share, diagonal leaks at ramp corners. BUG-0012: every
`MapGenParams` field at int.Min/Max/-1/0 -> `Validate` throws or map valid, never index/overflow.
BUG-0013: time the worst valid param set.

## Where we are

M0 Done. M1 is 2/8 plus half of criterion 3: sim core (M1-1), data loader + first data (M1-2),
terraced heightmap generator + nav grid (M1-3). Branch `studio/2026-10-03-1235` @ 4fc5e23 is
green: build 0/0, 372 passed + 9 skipped (all known-bug tests), `tools/qa/smoke.ps1` PASS. The
conductor merges it to main. Open bugs: 4 S3, 4 S4, none blocking. Sessions today: 4/10.

What exists in the sim: `World(config)` now owns `Heightmap` + `NavGrid` (`Rts.Sim.Map`),
generated from `RngStream.MapGen` with `config.Map` (`MapGenParams`, default 128x128). NavGrid
queries (`InBounds`, `IsPassable`, `LevelAt`, `FlagsAt`, `CostAt`, `WorldToCell`, `CellCenter`)
are allocation-free and read outside-map as blocked. Cliffs are blocked cells; ramps keep the
lower level; `NavGrid.Version` exists (0, nothing bumps it yet); `PassableCount` is public.
Nothing moves yet; `GameData` is loaded but unused.

## Next task candidates

1. **M1-4a: spatial hash + fix BUG-0011/0012/0013 first** (closes roadmap criterion 3).
   First commit (ask explicitly for a separate commit and a report line before continuing):
   - BUG-0011: in `NavGrid`, mark the lower-level cells flanking a ramp along its length as
     `Cliff | Blocked` (ramp walls). Keep connectivity and >= 50% passable; `MapAssert`,
     `MapQaChecker` and the seed sweeps must stay green; un-skip
     `QA.MapQaTests.RampSides_NoPassableStepSteeperThan30Degrees`. docs/03 "Implementation
     (M1-3)" paragraph gets one sentence.
   - BUG-0012: upper bounds on `RampWidth`/`RampLength`/`EdgeMargin`/`Level2Inset` before any
     arithmetic; `(long)width * height` in `Heightmap`; un-skip the two QA tests.
   - BUG-0013: tighten `Validate` caps (`RampTries`, `MaxAttempts`, plateau counts) so the worst
     case is ~2 s; adjust `Stress.MapStressTests.WorstCaseValidParams_StillBounded`.
   Then the spatial hash per docs/03 "Spatial hash": 4 m buckets (2x2 cells; constant derived
   from `MapConstants.CellSize`, no literal), rebuilt every tick by counting sort into flat
   arrays, zero allocation; queries: within radius, nearest enemy within radius, in rectangle;
   deterministic result order (by handle index). Tests: brute-force comparison over fuzzed
   positions, 500/2,500 units rebuild cost (Perf), allocation test, units outside the map.
   Budget ~500 production lines.
2. **M1-4b: flow fields + LRU cache + steering** (docs/03 "Flow fields", "Local movement"):
   8-connected Dijkstra, no corner cutting (use `IsPassable` on both side cells), LRU 32 tagged
   with `NavGrid.Version`; desired velocity, separation via the spatial hash, arrival slots,
   shoving; `Move` command. Fix BUG-0005 here (per-player buckets instead of the sort).
3. M1-5 scenario test (200 units across 128x128 with obstacles). Needs 4b.
4. M1-6 replay + determinism golden: **first commit fixes BUG-0014** (mix the seed in `SimRng`)
   because it changes every stream; decide whether terrain hashes into `StateHash` directly.

## Watch out for

- `SimRng` is a mutable struct: always `ref world.Rng(stream)`, never copy it (docs/03).
- Map generation draws only from `RngStream.MapGen`; QA tests pin the first draws of Combat/AI.
- `StateHash` already covers the post-generation MapGen RNG state, so any change to the
  generator's draw sequence changes hashes. No golden replays exist yet, so that is free until M1-6.
- `ArchitectureTests.SimSource_UsesNoForbiddenApis` greps sim source for `Math.*` trig,
  `Random`, `DateTime`, `HashCode`, `Parallel`, `Vector<`, `using static System.Math`. New sim
  code must adjust it only by adding patterns. `MathF.Floor`/`Sqrt` and `System.Numerics.Vector2`
  are allowed.
- Zero-allocation tests (`AllocationTests`, `NavGridTests.HundredThousandMixedQueries_...`,
  QA's 1M-query test) catch any `new`, LINQ or closure in per-tick paths. Map generation and
  `NavGrid` construction are load time and may allocate.
- `MapAssert.FindViolation` (dev) and `QA/MapQaChecker` (independent) both assert the terrain
  invariants; a representation change (ramp walls) must satisfy both, and QA's checker must be
  updated deliberately, not weakened.
- `World` builds a `NavGrid` twice (once inside `MapGenerator.Generate` for validation). Load
  time only; fold it if it ever matters.
- `Heightmap` queries throw outside the map; `NavGrid` reads blocked. Intentional.
- `QA/DataLoaderQaTests.GameDataTypes_ExposeOnlyImmutableMembers` reflects over every type in
  `Rts.Sim.Data`: new def types must be sealed, init-only, ImmutableArray/primitives/strings only.
- `DataJsonContext` disallows unknown members: adding a JSON field means adding it to the
  `*Json` shape in `DataJson.cs` first, or every shipped file fails to load.
- `tools/qa/smoke.ps1` is the smoke gate (CLAUDE.md); `$env:GODOT` can be empty in the
  non-interactive shell, the script falls back to the User-scope variable itself.
- Diffs have run ~40% over budget two sessions running; size the next brief to ~500 production
  lines and say tests are extra.
- `.sln` Release config builds the game project in Debug (BUG-0002, S4): irrelevant until M6.
