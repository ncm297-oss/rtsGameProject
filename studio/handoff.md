# Handoff: brief for the current session

Written by the Producer at the PLAN of session 2026-10-06-0655 (both tracks GO, feature sessions).
Base commit cf7ca3f (= `origin/main`). The ACCEPT replaces this with the brief for the next session.

## Where we are

- `main`: build 0 warnings; smoke PASS (Rts.Sim 0.0.1, match ticked to 86, no ERROR); non-Perf
  suite green (Producer rerun at PLAN). Open bugs 22 (S3 14, S4 8), none block.
- Sessions on 2026-10-06: this is 1 / 8. Feature sessions since last hardening before this one:
  sim 1 / 4, view 3 / 4. After this session: sim 2 / 4 (then the M1 end-of-milestone hardening,
  which also resets it), view 4 / 4 (next view session is a hardening session).
- Bug numbers this session: sim from BUG-0057, view from BUG-0067.
- M1: 7 / 8 (M1-8 CLI this session, then the end-of-milestone hardening and sign-off). M2: 4 / 10.

## Sim track

### Current session plan: M1-8 headless CLI + read-only flow-field peek (feature, QA standard)

Goal: the last M1 criterion, "a tiny CLI in `tools/` runs a scenario headless and prints hashes and
timings", plus the view track's request 2 (read access to a cached flow field's directions for
the M2-5 overlay), which is a few lines in the sim and belongs with this task.

Scope:
- New console project `tools/Rts.Cli/Rts.Cli.csproj` (net8.0, nullable, warnings as errors,
  BCL only, references `Rts.Sim` only; no xUnit, no NuGet), added to `RtsGame.sln`. Structure it
  as `Program.Main` → `static int CliRunner.Run(string[] args, TextWriter stdout, TextWriter stderr)`
  so tests can call it in-process.
- `run --seed <n> --units <n> [--ticks <n>] [--players 1|2] [--checkpoint <ticks>] [--record <path>] [--data <dir>]`:
  loads data (`DataLoader.LoadAll`, default dir = `game/data` found by walking up from the exe or
  cwd to the folder containing `RtsGame.sln`; `--data` overrides), builds a sim, attaches a
  `ReplayRecorder` before any enqueue, spawns the army through `Command.SpawnUnit` and orders it
  through `Command.Move` (a cross-map march: west-edge block to the farthest passable cell, every
  unit type round-robin; `ViewApi.StartLayout.Block` + `FlowField.Build` / `CostAt` are public and
  RNG-free, so no test code needs copying), ticks, prints one line per checkpoint
  (`tick <n> hash <16 hex>`), then timing (`ticks N avg ms p99 ms worst ms`, measured with a
  `Stopwatch` around `Tick()` in the CLI, never in the sim) and, with `--record`, writes the
  `.replay` through `ReplayFormat.WriteFile`. Exit 0.
- `play <path> [--data <dir>]`: `ReplayFormat.TryReadFile` + `ReplayPlayer.Run`; prints the result
  (ticks run, first mismatch tick with both hashes, or OK) and exits 0 on match, 2 on any
  `ReplayError` (name the code), 1 on usage / data / file errors. Expected failures print one line
  on stderr, never a stack trace.
- Sim: `FlowFieldCache.PeekCached(int targetCell)` → `FlowField?` (public): returns the cached
  field if present and current (`Version == grid.Version`), else null; touches no `_lastUse`,
  `_clock`, count or hash. Document "valid until the next `Tick()`" and that `FlowField.DirectionAt`
  / `CostAt` are the read surface. The existing internal `TryGetCached` / `Get` stay as they are.
- Docs: docs/03 "Debug tooling" (CLI usage, exit codes) and "Flow fields" (the peek); README.md
  command line. Do not edit CLAUDE.md (owner's file; the Producer lists the one-line addition for
  the owner).
- OUT of scope: any movement / order rule change; AI-vs-AI runs; `tools/export.ps1`; BUG-0002
  (`.sln` Release mapping) unless it is one line; a `--twice` determinism mode (tests cover it).

Acceptance criteria:
1. `dotnet run --project tools/Rts.Cli -- run --seed 1 --units 200 --ticks 1500` exits 0 and prints
   15 checkpoint lines and a timing line; the hash at tick 1500 equals a direct `Simulation` fed the
   same commands (test: `CliTests.Run_PrintsTheSameHashesAsADirectSim`).
2. `run ... --record x.replay` then `play x.replay` exits 0 and reports every checkpoint matched;
   a replay with one checkpoint hash edited exits 2 naming `CheckpointMismatch` and the tick; a
   truncated file exits 2; a missing file or `--data` dir exits 1 (tests for each, in-process).
3. Two `run` invocations with the same args print byte-identical hash lines (test).
4. Bad usage (`--units -1`, `--ticks 0`, unknown verb, missing value) exits 1 with a one-line
   usage message on stderr, no exception text (test over a table of bad arg lists).
5. `PeekCached`: hit returns the same instance `Get` would with no state change (hash before ==
   after over 10,000 peeks between ticks of a 500-unit march); miss and stale-version → null;
   out-of-range cell → null (no throw). Tests in `FlowFieldCacheTests`.
6. `Rts.Cli.csproj` has no `PackageReference`; `Rts.Sim.csproj` unchanged except nothing (no new
   packages); `ArchitectureTests` green; `dotnet build RtsGame.sln` 0 warnings; non-Perf suite green;
   smoke PASS (the game is untouched, so a single run is enough).
7. docs/03 and README updated as in scope; the test project may reference `tools/Rts.Cli` (an exe
   project reference is fine) so `CliRunner.Run` is tested in-process.

Design references: docs/05 M1 last criterion; docs/03 "Save/load and replays" (recorder attach
rules: before any enqueue, not on a hand-made map), "Testing strategy", "Debug tooling",
"Flow fields" (LRU is hashed state: a peek must not count as a use); docs/03 "Implementation
(M2-2)" for `StartLayout.Block`.

Tests required: `sim/Rts.Sim.Tests/Cli/CliTests.cs` (criteria 1-4), `FlowFieldCacheTests` peek
rows (criterion 5), a `StateHashTests` or QA-style twin for the peek (hash unchanged).

Constraints: no Godot in the CLI or the sim; only `Command.*` factories build commands (BUG-0054:
raw `Command` structs with stray flags break `ReplayFormat.Write`); `Stopwatch` lives in the CLI;
no allocation added to `Tick()`; keep the sim's public setup API additive (no `SimConfig` changes).
Size: ~400-700 lines total.

### QA focus (sim)
- The peek: prove it is read-only (hash twin with peeks on every cell of the map between ticks,
  during a 64-goal churn, after evictions and after a `NavGrid` version bump); stale fields never
  returned; `int.MinValue` / `MaxValue` cells.
- CLI argument fuzz: negative / huge / non-numeric values, `--units` above capacity, `--ticks`
  above the 24 h replay limit, non-ASCII and relative paths, unwritable `--record` path, a
  `--data` dir with a broken JSON (error list printed, exit 1), `play` on every truncation of a
  recorded file and on format-1 files; expected errors must never surface as exceptions.
- Determinism: CLI hashes equal `CrossMapScenario`-style direct runs for seeds 1-5 at 200 / 500
  units; two processes (real `dotnet run`, not in-process) agree.
- Architecture: the CLI never references xUnit or Godot; `Rts.Sim` gained nothing but the peek.

### Watch-outs (sim)
- Keep `Rts.Sim` free of new NuGet packages; the CLI may use only the BCL.
- `ReplayRecorder` must attach before the first `Enqueue` (spawns included) or the replay misses
  the army.
- Next sim session is the M1 end-of-milestone hardening (BUG-0044 / 0045 / 0046 / 0047 / 0049 /
  0050 / 0054 / 0055 / 0056, `MovementSystem` blank lines, docs sweep), then the M1 sign-off.

## View track

### Current session plan: M2-3 orders and selection keys (feature, QA standard)

Goal: the keyboard half of M2's selection and order criteria: A attack-move, S stop, H hold,
Shift-queue, double-click / Ctrl-click type select, control groups, Tab subgroups. The sim's kinds
and queue are on `main` (M1-7), so after this the only M2 selection/order work left is polish.

Scope (production budget 500 lines in `game/` + `ViewApi/`; total with tests under 1,200):
- Input map (`game/project.godot`, docs/02 "Controls and camera"): `order_attack_move` (A),
  `order_stop` (S), `order_hold` (H), `order_queue` (Shift; a second action on the same key as
  `select_add`, so each is rebindable alone), `select_type` (Ctrl, for Ctrl + click),
  `group_assign` (Ctrl), `group_add` (Shift), `group_1` .. `group_9` (digit keys),
  `subgroup_next` (Tab), `order_cancel` (Esc).
- Generalise `SelectionController.OrderMoveTo(Vector2)` into one order path
  `Order(CommandKind kind, Vector2? point, bool queued)` used by the 3D right-click, the minimap
  (right-click = Move, queued when `order_queue` is held) and the keys; keep the whole-order
  capacity check (one command per selected unit, dropped whole with the existing warning). Keep
  a public `OrderMoveTo(Vector2)` wrapper or update the minimap call; either is fine.
- A: pressing A enters a targeting state; the next left-click on the map issues `AttackMove`
  to the picked ground point (queued if Shift is held at the click) and leaves the state; Esc or
  a right-click cancels it (the right-click issues nothing); a click off the map issues nothing
  and keeps targeting. While targeting, left-click never selects. Show the state in the debug
  label (`A` suffix), nothing else yet (no cursor art).
- S / H: `Stop` / `HoldPosition` for every selected unit at once; with Shift held, queued.
  Shift + right-click: queued Move. No persistent "holding" indicator (BUG-0056 item 3: Hold ends
  when a queued order starts, so an indicator would lie).
- Double-click on an own unit, or Ctrl + click: select every own live unit of that unit type whose
  projected centre is inside the viewport (in front of the camera); plain replaces, with Shift adds.
  Use the `select` event's `DoubleClick` flag; a double-click on empty ground acts as a click.
- Control groups 1-9: Ctrl + digit assigns (replaces the group with the selection), Shift + digit
  adds the selection to the group, digit recalls (replaces the selection with the group's live
  units), a second press of the same digit within 0.3 s (view wall clock is fine) centres the
  camera on the group's mean position. Groups are view state: a pure `ViewApi.ControlGroups`
  (9 `SelectionSet`s, fixed capacity, `Prune` every frame like the selection; no sim reference).
- Tab subgroups: within the current selection, the "active subgroup" is one unit type; the
  subgroups are the selection's distinct types in ascending type id; Tab advances and wraps;
  any selection change resets to the first. Pure `ViewApi.Subgroups` helper (sorted distinct
  types + index, allocation-free after init). Shown in the debug label as `sub <typeId> <n>/<m>`
  (dev text). Nothing else consumes it until the command card (M3).
- Docs: docs/03 "Implementation (M2-3)" (actions, states, one order path, groups, subgroups);
  docs/02 stays as is unless a key changed.
- OUT of scope: M + click and P + click (no Patrol kind yet), cursor art, HUD command card,
  formations, minimap attack-move, sounds (M2-6), BUG-0052 / 0053 / 0064 (view hardening next).

Acceptance criteria:
1. Windowed check: `& $env:GODOT --path game`, box-select the blue army, press H, send a second
   group through them: holders are never pushed; press S on walkers: they stop next tick; A then
   click: they walk (AttackMove = Move until M4); Shift + right-click four points: they visit
   them in order. Screenshot taken and looked at (`--screenshot`), as evidence of the label states.
2. Headless scene `game/tests/OrdersTest.tscn` ("ORDERS TEST PASS") with injected events: S on
   10 walkers → all `Idle` with `GoalCell == -1` within 2 ticks; H → `Hold[slot]` true for every
   selected unit; Shift + right-click to 3 points → each unit visits all three in order (at speed
   8, within a bound); A + click → the units move to the clicked point and the controller's
   per-kind counter shows `AttackMove` commands equal to the selection count; Esc cancels A; a
   right-click while targeting issues nothing; Shift + S queues (a moving unit keeps moving and
   has `QueueCount` 1 before the next tick, or stops after arriving).
3. Same scene (or `SelectionTest.tscn` extended): double-click on one unit selects exactly the
   own on-screen units of that type (enemies and off-screen ones excluded); with Shift, adds;
   Ctrl + click does the same.
4. Control groups: Ctrl+1 with 5 selected, click empty ground, press 1 → the same 5 selected;
   Shift+2 adds; a unit freed and respawned (as the enemy) is not recalled; a double-tap of 1
   moves the camera focus to the group's mean within 1 m.
5. Tab: a mixed selection of 3 types cycles 0 → 1 → 2 → 0 by ascending type id; Tab with an
   empty selection does nothing; a new selection resets to 0.
6. xUnit: `ViewApi/ControlGroupsTests` and `ViewApi/SubgroupsTests` (prune, recycled handles,
   wrap, reset, capacity edges), a `ViewApiAllocationTests` row (groups + subgroups update at
   2,000 units: 0 bytes). QA's hash twin extended to the new kinds (Stop / Hold / AttackMove /
   queued) — the view-driven sim must equal a bare twin fed the same commands every tick.
7. Order path: every kind goes through the one method; the full-queue check applies to Stop /
   Hold too (1,000 selected against a nearly full queue: dropped whole, one warning).
8. smoke PASS; 0 warnings; non-Perf suite green; docs/03 section matches; production lines
   reported (budget 500).

Design references: docs/02 "Controls and camera" (table rows A, S, H, Shift, Ctrl + 1-9,
Shift + 1-9, 1-9 double-tap, Tab, Double-click / Ctrl + click); docs/03 "Orders and unit
states" (what each kind does, queue of 8, Hold released by a queued order); docs/03
"Implementation (M2-2)" and "(M2-4)" (the existing picking and order path, minimap routing).

Tests required: `OrdersTest.tscn` (criteria 2-5 or split with `SelectionTest`),
`ControlGroupsTests`, `SubgroupsTests`, the allocation row; QA hash twin with the new kinds.

Constraints: `ViewApi/` additions are pure (no sim reference, no mutation); the view changes
sim state only through `Simulation.Enqueue`; dev-only text may stay in C# but nothing
player-facing is added; new HUD-free (no panels this task); input must not swallow events
outside its purpose (the minimap rule). Bug numbers from BUG-0067.

### QA focus (view)
- Hash twin with all four kinds and the queued flag over 3 maps, including A-mode clicks, Esc,
  Shift combos and control-group recalls between orders.
- Modifier edge cases: Shift+Ctrl+digit, Ctrl held during a box drag (must not type-select),
  a double-click whose second click lands on empty ground, a double-click on an enemy,
  key presses with an empty selection, A pressed twice, A then minimap click, Tab with one type.
- Groups: 9 groups x 1,000 units, assign / recall after deaths and respawns (recycled slots
  never recalled), prune cost per frame, digit double-tap timing boundaries.
- Overflow: S / H / Shift-queue with 1,000 selected against a nearly full command queue (dropped
  whole, one warning, no partial order); a 9th queued order per unit is dropped silently by the
  sim (the view may warn; QA checks nothing crashes).
- The windowed screenshot is evidence, not a test: QA re-runs `OrdersTest.tscn` itself.

### Watch-outs (view)
- Production budget 500 lines (M2-4 held 365). Project.godot action blocks are long; they count
  as data, not production, but say how many lines they are.
- `select_add` and `order_queue` both on Shift: use the right one in each place; Shift + click on
  empty ground still keeps the selection.
- Next view session is the view hardening session (4 / 4): BUG-0052, BUG-0053, BUG-0064, the dev
  mesh-test wall mutant, export hygiene notes.

## Shared watch-outs
- Merge order sim → view. STATE, the session log, the handoff and roadmap ticks are written in the
  sim worktree only. Expected overlap: `studio/bugs/README.md`, `studio/qa/coverage.md`, docs/03
  (different sections).
- The view track must not touch `FlowFieldCache`; its M2-5 overlay waits for the sim's
  `PeekCached` to land on `main` (this session).
- Perf failures count only when they fail again alone (both tracks' QA run at the same time).
- Remote Control is unavailable in unattended sessions; don't retry it.
- `CLAUDE.md` still says "Current milestone: M1" and its Commands table has no CLI line (owner's
  file; drift noted for the owner in STATE).
