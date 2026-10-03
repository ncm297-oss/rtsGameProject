# Handoff: next session brief

Written by the Producer at the end of each session for the next one. The next session's
Producer starts from this, verifies it against the repo, and then plans.

_Written: 2026-10-03 (session 2026-10-03-1235, ACCEPT)._

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
