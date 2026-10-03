# Handoff: next session brief

Written by the Producer at the end of each session for the next one. The next session's
Producer starts from this, verifies it against the repo, and then plans.

_Written: 2026-10-03 (session 2026-10-03-0907, ACCEPT)._

## Where we are

M0 Done. M1 is 1/8: the sim core landed in M1-1 (`World`, `UnitStore` with generational
handles, `SimRng` PCG32 streams, `SimMath`, `CommandQueue`, `Simulation.Tick`/`StateHash`).
Branch `studio/2026-10-03-0907` @ ab41554 is green: build 0/0, 83 passed + 4 skipped (known-bug
tests), `tools/qa/smoke.ps1` PASS. The conductor merges it to main. Open bugs: 3 S3, 2 S4, none
blocking.

## Next task candidates

1. **M1-2: data loader for `game/data/`** (roadmap criterion 2). Read docs/02 and docs/03
   "Data" before scoping. Suggested shape: `System.Text.Json` (BCL, no new package) with source
   generation; `DataLoader.LoadAll(dir)` -> immutable `GameData` with `snake_case` ids mapped to
   ints and seconds converted to ticks; a validation pass that reports every bad id or missing
   reference; a test that loads all shipped data and a test that catches a broken reference.
   Start with a minimal schema (units, factions) so the slice stays under ~800 lines; buildings,
   techs, abilities, AI build orders can follow as data grows.
   First commit of the task: fix BUG-0003 (clamp/re-fold reduced angle), BUG-0004 (increment the
   sequence only after `Add` succeeds), BUG-0006 (drop non-finite `SpawnUnit` in `Apply`), and
   un-skip their three QA tests as regression tests. Each is under 10 lines.
2. M1-3 terraced heightmap generator + nav grid + spatial hash (after M1-2).
3. M1-4 flow fields + steering, and fix BUG-0005 there (per-player buckets instead of the sort).

## Watch out for

- `SimRng` is a mutable struct: always `ref world.Rng(stream)`, never copy it (docs/03).
- `Enqueue` always stamps `TickNumber + 1`, even between ticks. Fine for the sim; if the owner
  feels input lag at M2-1 (`SimRunner`), the fix is to stamp `TickNumber` when not inside a tick.
- `UnitStore` arrays and `World.Rng()` are publicly writable; QA tests rely on it. Don't lock it
  down without updating them.
- `ArchitectureTests.SimSource_UsesNoForbiddenApis` greps sim source for `Math.*` trig,
  `Random`, `DateTime`, `HashCode`, `Parallel`, `Vector<`, `using static System.Math`. New sim
  code must adjust it only by adding patterns.
- Zero-allocation tests (`AllocationTests`, `Flood_10000Commands_OneTick_AllocatesNothing`)
  will catch any `new`, LINQ or closure in `Tick()`; the data loader is allowed to allocate
  (load time), tick code is not.
- `tools/qa/smoke.ps1` is the smoke gate (CLAUDE.md); `$env:GODOT` can be empty in the
  non-interactive shell, the script falls back to the User-scope variable itself.
- `.sln` Release config builds the game project in Debug (BUG-0002, S4): irrelevant until M6.
