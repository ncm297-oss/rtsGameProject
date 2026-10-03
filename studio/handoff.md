# Handoff: next session brief

Written by the Producer at the end of each session for the next one. The next session's
Producer starts from this, verifies it against the repo, and then plans.

_Written: 2026-10-03 (session 2026-10-03-0907, ACCEPT). Plan section added by session 2026-10-03-1151._

## Current session plan (2026-10-03-1151)

TASK_ID: M1-2 · TASK_TITLE: Add the data loader for `game/data/` with validation (+ fix BUG-0003/0004/0006)

**Goal.** Roadmap M1 criterion 2: a loader in `Rts.Sim` reads JSON under `game/data/`, maps
`snake_case` ids to ints, converts seconds to ticks, validates every reference and reports all
errors at once, and a test loads all shipped data. Ship the first real data: Malazan and Whirlwind
units and faction files plus the common damage table and rules, so later milestones add data
instead of systems.

**Scope.**
- Commit 1 (before any loader code): fix BUG-0003 (re-fold reduced angle / clamp Sin-Cos to
  [-1, 1]), BUG-0004 (bump the sequence only after the queue accepts the command), BUG-0006
  (reject non-finite `SpawnUnit` positions in `Apply`); un-skip their three QA tests (they must
  pass as regression tests); set bug status `fixed` in `studio/bugs/`. Each fix under ~10 lines.
- `sim/Rts.Sim/Data/`: `DataLoader.LoadAll(string dataDir)` -> `GameData` (immutable, arrays
  indexed by int id) or a `DataLoadResult` carrying a list of `DataError` (file, path, message).
  `System.Text.Json` with source generation (`JsonSerializerContext`; no reflection, no NuGet).
- Schema (minimal, exactly docs/03 "Data format"): `common/damage_table.json` (type x armor
  class, values from docs/02 "Damage type x armor class"), `common/rules.json` (docs/02 Economy
  table: starting resources, pop, gather rates, carry capacity, mine/tree amounts),
  `factions/<id>/faction.json` (id, displayName, description, bonus displayName/description,
  resource display names, palette), `factions/<id>/units.json` (the unit shape from docs/03,
  values from `docs/factions/malazan.md` and `docs/factions/whirlwind.md` unit tables).
- Validation (collect all, never throw on the first): duplicate ids; unknown `slot` (the seven
  template slots); `armorClass` / `attack.type` / `bonusVs` keys not in the damage table;
  non-positive hp/speed/cooldown; `radius` outside 0.4-1.0; `pop` not a multiple of 0.5;
  faction `id` not matching its folder; missing required files; malformed JSON (report file +
  position). `trainedAt`, `requires`, `model`, `projectile` are kept as strings and NOT
  resolved yet (buildings/techs/projectiles arrive in M3/M4): record this in docs/03.
- Conversions: seconds -> ticks via `SimConstants` (cooldown, windup, trainTime); pop stored as
  half-pop integers (docs/02); string ids -> dense ints assigned in ordinal-sorted order, so the
  result is independent of file-system enumeration order.
- Tests find `game/data/` by walking up from `AppContext.BaseDirectory` to `RtsGame.sln`.
- docs/03 "Data format": update to describe the shipped subset, the id/tick conversion rules and
  the deferred references. CLAUDE.md "Testing rules" already names the data-validation test;
  make sure its name matches.
- OUT of scope: buildings.json, techs.json, abilities.json, statuses.json, ai.json, maps,
  Teblor/Shadow/Andii data, any system that consumes the data (production, combat), Godot-side
  loading, BUG-0005, BUG-0002.

**Acceptance criteria.**
1. `DataLoader.LoadAll(<repo>/game/data)` succeeds on shipped data; test `DataValidationTests.
   ShippedData_LoadsWithNoErrors` (or equivalent name recorded in CLAUDE.md) is green.
2. Shipped data covers Malazan and Whirlwind: 7 units each with the stats from their faction pages
   (spot-check: Crossbowman hp 55, attack 9 pierce, cooldown 2.2 s -> 44 ticks, bonusVs heavy 1.3,
   cost 36/45); a test asserts at least these values after loading.
3. A broken reference (e.g. `armorClass: "titanium"`) and a duplicate id in a temp data dir each
   produce exactly one `DataError` naming file and field, and both are reported in one load
   (test proves "all errors at once": two faults -> two errors, no exception).
4. Malformed JSON and a missing required file produce a `DataError`, not an exception.
5. Seconds -> ticks conversion is tested at the 50 ms tick (2.2 s -> 44, 0.45 s -> 9) and pop is
   stored as half-pop integers (1 -> 2, 1.5 -> 3).
6. Ids are stable: loading the same directory twice gives identical int ids and identical
   `GameData` contents regardless of file order (test shuffles/renames nothing but asserts
   deterministic assignment: ids are ordinal-sorted).
7. No `displayName`/`description` text in C#; no Godot reference; `Rts.Sim` still builds with
   warnings as errors; `ArchitectureTests` green with no forbidden pattern added (the loader may
   use `Dictionary` at load time but `GameData` exposes only arrays/int ids to tick code).
8. BUG-0003/0004/0006 fixed in the first commit, their QA tests un-skipped and green, bug files
   set to `fixed` with the test name.
9. `dotnet build` 0 warnings; `dotnet test` green (expect 1 skip left: BUG-0005 Perf stress);
   `tools/qa/smoke.ps1` PASS (game/ gains data files, so the gate must run).
10. docs/03 "Data format" updated in the same commit.

**Design references.** docs/03 "Data format" (lines 264-317: layout, example unit, STJ source gen,
validate-all, seconds->ticks, ids->ints); docs/02 "Faction template" + "Template baseline stats",
"Economy" table, "Combat / Stats", "Damage type x armor class"; docs/factions/malazan.md and
whirlwind.md unit tables; CLAUDE.md rules 1, 4, 6, 8 and coding conventions (snake_case ids,
seconds in data, ticks in sim).

**Tests required.** `DataValidationTests` (shipped data loads; broken ref; duplicate id; malformed
JSON; missing file; all-errors-at-once), `DataConversionTests` (seconds->ticks, half-pop, id
stability), regression tests for BUG-0003/0004/0006 (the un-skipped QA tests), existing suites
unchanged and green.

**Constraints most at risk.** Rule 6 (no stat literals in C#: the only numbers allowed in loader
code are schema limits like radius 0.4-1.0, and those should come from a `DataLimits` constant
block with a comment pointing at docs/02); rule 8 (no player-facing text in C#; error messages
are developer-facing and may be literals); rule 4 (no `Dictionary` iteration leaking into
results; no `HashCode`; ordinal string comparison only); rule 1 (no `Godot.FileAccess`, plain
`System.IO`); no new NuGet packages in `Rts.Sim`.

**QA focus.** Attack the validator: fuzz JSON (truncated files, BOM, duplicate keys, wrong types,
negative/NaN/huge numbers, unicode ids, empty arrays, 10k-unit file for load time and memory);
confirm every fault yields an error with file + field and never an exception; confirm id
assignment is identical across two loads and across renamed temp copies; confirm `GameData` has
no mutable collections reachable from the sim; verify the three un-skipped bug tests actually
fail when the fix is reverted (mutation check); re-run determinism/allocation suites to prove the
loader did not touch tick code.

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
