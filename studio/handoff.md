# Handoff: next session brief

Written by the Producer at the end of each session for the next one. The next session's
Producer starts from this, verifies it against the repo, and then plans.

_Written: 2026-10-03 (session 2026-10-03-1151, ACCEPT)._

## Current session plan (2026-10-03-1235, PLAN: GO)

**TASK_ID:** M1-3 · **Title:** Add the terraced heightmap generator and nav grid (fix BUG-0007/0009 first)

Checks at plan time: inbox empty; `main` @ 31556b3 builds 0/0, non-perf tests 196 passed / 7
skipped (known-bug tests), `tools/qa/smoke.ps1` PASS; verified claim: Whirlwind Raider JSON
matches `docs/factions/whirlwind.md` row exactly; no docs drift; open S1/S2: none; sessions
today 3/10.

**Goal.** Give the sim its map: a seeded generator that produces terraced terrain (levels 0-2,
plateaus joined by ramps, cliffs elsewhere) and a `NavGrid` that movement (M1-4) and the
scenario test (M1-5) can query without allocating. This is the first half of roadmap M1
criterion 3; the spatial hash moves to M1-4 so this stays under budget.

**Scope.**
- Commit 1 (small, first): fix BUG-0007 and BUG-0009 in `DataLoader.Checker`, un-skip
  `HugeFloat_IsRejected_NotStoredAsInfinity`, `HugePopCap_IsRejected_NotOverflowedToNegative`,
  `NullEntryInRequiresOrTags_IsRejected`; set both bug files to `fixed`.
- Commit 2+: new namespace `Rts.Sim.Map` with `MapConstants` (CellSize 2 m, LevelHeight 4 m,
  MaxLevel 2; cite docs/02 "Map and terrain"), `MapGenParams` (width/height in cells, plateau
  count, ramp width, etc.; defaults = 128x128 per docs/02; `Validate()`), `Heightmap` (per-cell
  level + per-cell height in meters, ramps interpolated, `ContentHash()`), `MapGenerator`
  (`Generate(MapGenParams, ref SimRng)`), `NavGrid` (per cell: `Level`, flags Blocked/Cliff/Ramp,
  `Cost` 1 or 255, grid `Version`; `InBounds`, `IsPassable`, `LevelAt`, `WorldToCell`,
  `CellCenter`), built from a `Heightmap`. `World` owns `Heightmap` + `NavGrid`, built in its
  constructor from `SimConfig` (new optional `MapGenParams` member with a default so existing
  call sites compile), drawing only from `RngStream.MapGen`.
- Representation decision (Producer, document it): cliffs are blocked *cells* on the plateau
  edge (not per-edge rules), the outer border ring is blocked, ramp cells connect level L to
  L+1 only. This keeps M1-4 flow fields per-cell.
- Docs: docs/03 "Navigation grid" gets an "Implementation (M1-3)" paragraph; docs/02 "Map and
  terrain" Elevation row may add "(discrete terraces: a level change without a ramp is a cliff)".
- OUT of scope: spatial hash, flow fields, steering, movement (M1-4/5); trees, water, gold
  placement, symmetry/mirroring, hand-made maps or `maps/*.json` loading (M3/M6); rendering;
  changes to `StateHash` (M1-6 decides what terrain contributes); BUG-0005/0008/0010.
- Budget: production code about 500 lines plus tests; whole diff under ~900 lines.

**Acceptance criteria.**
1. BUG-0007: `Checker.Pos`/`NonNeg`/`Int`/`Ticks` reject non-finite values and values above
   limits held in `DataLimits` *before* narrowing to float/int; error messages no longer print
   overflowed numbers. BUG-0009: every `requires`/`tags` entry passes `Checker.Id` with path
   `units[i].requires[j]`. The three QA tests are un-skipped and green; bug files `fixed` with
   commit + test name.
2. `MapGenerator.Generate` with default params yields levels only in 0..`MaxLevel`, all three
   levels present, and `Heightmap` height = level x `LevelHeight` on plateau cells and strictly
   between the two levels on ramp cells.
3. `NavGrid` from that heightmap: border ring blocked; for every 4-adjacent pair of passable
   cells |dLevel| <= 1, and dLevel = 1 only when one of them is a ramp cell; `Cost` is 1 for
   passable and 255 for blocked.
4. Connectivity: all passable cells form one 4-connected component; passable fraction >= 50%
   at default params. A test checks criteria 2-4 for 200 consecutive seeds at 128x128 and 100
   seeds at 32x32. Generation has a bounded retry/iteration count (never hangs).
5. Determinism: same (seed, params) twice -> identical level/flag arrays and equal
   `ContentHash()`; different seeds -> different hashes; map generation changes only the
   `MapGen` stream (the first draw of `Combat` and `Ai(0)` is identical with and without a
   larger map). Uses `ref SimRng` throughout (never a copy).
6. `World(config)` exposes `NavGrid` and `Heightmap`; all existing tests compile unchanged;
   a `Category=Perf` test shows default 128x128 generation under 50 ms.
7. Zero allocation: 100,000 mixed `NavGrid` queries allocate 0 bytes
   (`GC.GetAllocatedBytesForCurrentThread`); existing `AllocationTests` stay green.
8. No numeric literals for cell size, level height or level count outside `MapConstants`; no
   tunables outside `MapGenParams`; `ArchitectureTests` green (no `Math.*` trig, `Random`,
   `Dictionary` iteration, Godot).
9. docs/03 and (if wording changes) docs/02 updated in the same commit.
10. `dotnet build RtsGame.sln` 0 warnings; `dotnet test sim/Rts.Sim.Tests` green with only the
    4 remaining known-bug skips (BUG-0005, BUG-0008, BUG-0010 x2).

**Design references.** docs/02 "Map and terrain" (128x128, 2 m cells, levels 0-2 4 m apart,
ramps as chokepoints, >30 deg slopes impassable); docs/02 "High ground" (per-cell `Level` feeds
the M4 vision rule); docs/03 "Navigation grid", "Flow fields" (8-connected, no corner cutting:
leave the per-cell data ready for it), "Determinism" (`SimRng` by ref, `RngStream.MapGen`),
"Entity model" (positions are 2D meters; height is lookup only).

**Tests required.** `DataLoaderQaTests` three un-skips (regressions); `MapGeneratorTests`
(invariants over many seeds, bounded termination, params validation), `NavGridTests` (border,
level-step rule, connectivity flood fill, `WorldToCell` edge cases incl. outside/negative/NaN
positions), `HeightmapTests` (ramp interpolation, `ContentHash` determinism), RNG-isolation
test, allocation test for queries, one Perf-tagged generation timing test.

**Constraints most at risk.** Rule 4 determinism (no `Math.Sin`/noise libs, no `HashCode`, no
hash-set iteration in generation); rule 5 no allocation in per-tick queries (generation may
allocate at load time only); rule 6 no hard-coded stats (constants with doc citations, params
record); `SimRng` is a mutable struct (copying it silently breaks the stream); `DataJson`
unknown-member policy if any JSON changes (none expected).

**QA focus.** Seeds 0, 1, `ulong.MaxValue`, and a sweep of 1,000; degenerate params (1x1, 3x3,
8x8, 128x64, 512x512, ramp width >= map, plateau count 0 and huge): either a valid map or an
`ArgumentOutOfRangeException` from `Validate()`, never a hang, index error or NaN. Re-check
connectivity with QA's own 4-connected flood fill and look for diagonal-only leaks through
cliff corners. Confirm the MapGen draw count is stable per seed and that Combat/AI streams are
untouched. Hammer `WorldToCell`/`CellCenter` with negative, boundary (exactly 256.0 m) and NaN
inputs. Measure 512x512 generation time and memory. Allocation check on queries in a tight loop.

## Where we are

M0 Done. M1 is 2/8: sim core (M1-1) and the data loader with the first real data (M1-2).
Branch `studio/2026-10-03-1151` @ b75e2b8 is green: build 0/0, 202 passed + 8 skipped (all
known-bug tests), `tools/qa/smoke.ps1` PASS. The conductor merges it to main. Open bugs: 3 S3,
3 S4, none blocking.

`Rts.Sim.Data.DataLoader.LoadAll(dir)` returns `GameData` (ImmutableArrays indexed by dense int
ids; `FindUnit`/`FindFaction` for strings) or all `DataError`s. Shipped: damage table, rules,
Malazan + Whirlwind factions and 7 units each. Nothing consumes `GameData` yet.

## Next task candidates

1. **M1-3: terraced heightmap generator + nav grid** (roadmap criterion 3, first half). Read
   docs/02 "Terrain" and docs/03 "Terrain / Pathfinding" before scoping. Suggested shape:
   `Heightmap` (elevation levels 0-2 per cell, seeded from `SimRng`, ramps between levels),
   `NavGrid` (passable, slope/ramp flags, per-cell level, 2 m cells per docs/03), a generator
   that takes `SimConfig`-style parameters (map size, seed) with no literals. Tests: level
   bounds, ramps connect levels, same seed -> same map, nav grid matches heightmap, no
   allocation when querying. Keep the spatial hash for M1-4 with flow fields so this stays
   under ~800 lines.
   First commit of the task: fix BUG-0007 (finite / in-range bounds in `Checker.Pos`, `NonNeg`,
   `Int`, `Ticks` before narrowing; `popCap` upper bound) and BUG-0009 (run list entries of
   `requires`/`tags` through `Checker.Id`), un-skip their QA tests
   (`HugeFloat_IsRejected_NotStoredAsInfinity`, `HugePopCap_IsRejected_NotOverflowedToNegative`,
   `NullEntryInRequiresOrTags_IsRejected`). Each under ~15 lines.
2. M1-4 spatial hash + flow fields with LRU cache + steering; fix BUG-0005 there (per-player
   buckets instead of the sort).
3. M1-5 scenario test (200 units across 128x128 with obstacles).

## Watch out for

- `SimRng` is a mutable struct: always `ref world.Rng(stream)`, never copy it (docs/03).
- Map generation must draw from a dedicated `RngStream` so unit RNG does not shift the map.
- `ArchitectureTests.SimSource_UsesNoForbiddenApis` greps sim source for `Math.*` trig,
  `Random`, `DateTime`, `HashCode`, `Parallel`, `Vector<`, `using static System.Math`. New sim
  code must adjust it only by adding patterns. `MathF.Floor`/`Sqrt` are allowed.
- Zero-allocation tests (`AllocationTests`) catch any `new`, LINQ or closure in `Tick()`. Map
  generation is load time and may allocate; nav queries per tick may not.
- `QA/DataLoaderQaTests.GameDataTypes_ExposeOnlyImmutableMembers` reflects over every type in
  `Rts.Sim.Data`: new def types must be sealed, init-only, ImmutableArray/primitives/strings only.
- `DataJsonContext` disallows unknown members: adding a JSON field means adding it to the
  `*Json` shape in `DataJson.cs` first, or every shipped file fails to load.
- `tools/qa/smoke.ps1` is the smoke gate (CLAUDE.md); `$env:GODOT` can be empty in the
  non-interactive shell, the script falls back to the User-scope variable itself.
- `.sln` Release config builds the game project in Debug (BUG-0002, S4): irrelevant until M6.
