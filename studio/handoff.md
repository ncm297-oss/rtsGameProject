# Handoff: next session brief

Written by the Producer at the end of each session for the next one. The next session's
Producer starts from this, verifies it against the repo, and then plans.

_Written: 2026-10-03 (session 2026-10-03-1151, ACCEPT)._

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
