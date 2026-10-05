# Handoff: current session plan

Written by the Producer at PLAN (session 2026-10-05-1609) so the briefs survive if the session
dies. One section per track. Verified at this PLAN on `main` 3645d53: build 0 warnings; non-Perf
suite 1254 / 12 skipped / 0 failed (1 m 36 s); smoke PASS ("Match stopped at tick 85"); golden
replay present (15,526 bytes); `Main.tscn` instances `Match.tscn`; coverage.md and bugs index
merged cleanly. Open bugs 17 (S3 12, S4 5; STATE said 16, fix at ACCEPT). Sessions today 4 / 8.
Bug numbers this session: sim from BUG-0042, view from BUG-0052.

## Sim track

### Current session plan: M1-4d-3 hardening batch (Type hardening · QA full)

Feature sessions since the last hardening session: 4 / 4 (0742, 1013, 1234, 1446). Budget up to
1,500 lines; crowd routing is the uncertain part, keep it local (~800 lines) and plain.

**Goal.** Close the M1 movement debt before the perf test (M1-7), the CLI (M1-8) and sign-off:
walkers go round other groups' blobs instead of pressing in and giving up, parked groups yield in
chokes, enemies are walls also when they hold the walker's goal cell, and the movement test bounds
hold on many maps instead of one.

**Scope, in priority order (drop from the bottom if the budget runs out, never the golden rule or
item 3's re-bounding):**

1. **Crowd routing, first part (closes BUG-0028, BUG-0032, BUG-0033 remainder).** Flow fields stay
   unit-agnostic (cache validity, build cap); a crowd cost inside fields is OUT of this slice.
   Recommended rules (the dev may deviate with measurements written into docs/03):
   - *Local detour:* when a walker's aim is covered by standing units that aren't its groupmates
     (another group's blob, enemies, field-waiting units), steer tangentially round them: choose
     the side with the shorter arc toward the aim; ties go right. Read start-of-tick state only;
     neighbors in ascending slot order (the choice must not depend on walk order).
   - *Field-waiting units are not "stuck" walls (BUG-0028 b):* a walking tick blocked only by Moving
     units waiting for a field counts as queued (count holds), not stuck. Moving units are still
     never shoved.
   - *Walk back:* a unit un-anchored by the shove re-check (`GoalCell` set to -1 while it still has a
     `Goal`) returns to Moving toward its stored goal, same goal cell, `StuckTicks` reset. At most one
     walk-back per order (new per-unit hashed field, e.g. a byte counter; it must join `StateHash`
     and the QA reflection audit), so a corridor can't ping-pong forever.
   - *Parked groups yield in chokes (BUG-0033):* extend the lone-parked-unit rule: after
     `PushAfterStuckTicks`, a parked unit on its point yields when every groupmate it touches is
     shoved this tick too (chain shove), sideways where there is room, else along the push; the
     existing anchor re-check decides who keeps the goal. Proof: un-skip
     `QA/ShoveQaTests.WalkerInOneCellCorridor_PastAParkedFriendlyPair_Arrives`.
2. **BUG-0037 + BUG-0038 (same code).** Every groupmate check in `Plan` / `WallLimit` / `AimCovered`
   / `RecheckAnchors` / the queued check compares `Owner` as well as `GoalCell`; the hard-wall fallback
   keeps the friendly clips (start `ClosestAllowed` from the clipped step, or carry the friendly walls
   as constraints when allowed). Un-skip both `QA/HardWallQaTests` repros. Re-measure the two-owner
   cross-map rows (both players to one goal no longer share an anchor).
3. **BUG-0039 + BUG-0034 (test bounds).** After 1-2, sweep `MoreGoalsThanCacheSlots` over new-seed
   maps 1-40 and `Crowd_ToOneOrFourClosePoints(2500, 4, ...)` over at least 10 seeds; the pack rule
   (no two Idle units closer than 0.5 x the radii's sum) should hold on every map after the BUG-0038
   fix; if it still fails somewhere, the row becomes report-only and docs/03 names the limit with the
   worst value. Give-up / arrival bounds: a bound that holds on every swept map, or report-only.
   Retire `TestSeeds.PreMix` from these tests (new seeds) and un-skip or rewrite the `SeedSweepQaTests`
   twin. BUG-0034: `Perf_500Units_DistinctTargetsInterleavedBySlot_CostPerTick(32)` averages at least
   100 ticks or uses a median; the 4 ms budget stays.
4. **BUG-0040 part 1 (few lines).** `Replay.Validate`: `ticks` at most 1,728,000 (24 h) and
   `checkpoint-interval` in [1, ticks]; un-skip `QA/ReplayQaTests.AbsurdTickCount_IsRefusedAtRead`;
   one format-limits line in docs/03. Part 2 (in-tick AI enqueue) stays a design note for M5.
5. **BUG-0030 (few lines).** `Simulation.ApplyMove`: a Move to the unit's current `GoalCell` is the
   same order only when the new point is within `ArrivalDistance` of the stored `Goal`; otherwise it
   is a new order to the same cell (the unit takes the new point and restarts). Click-spam to one
   point (BUG-0029 tests) must still be a no-op. Un-skip
   `QA/LocalMovementRecheckQaTests.ArrivedLoneUnit_OrderedToTheOppositeCornerOfItsGoalCell_MovesThere`.

**Targets for this slice** (Producer-set; original criterion-6 targets in parentheses; "now" is the
docs/03 M1-5 table, two players unless stated):

| Row | Now | Target |
| --- | --- | --- |
| 500 units to 4 points 6 m apart, arrived | 182 (36%) | >= 60% (80%) |
| 2,500 units to 4 points, arrived | 859 (34%) | >= 50% (60%) |
| 500 units, 500 random goals, gave up | 30 (6%) | <= 3% (3%) |
| 128 units, 64 neighboring goals, gave up (median over new-seed maps 1-40) | 22-26 (17-20%) on one map | <= 10% (5%) |
| 200 walkers crossing a settled 300-blob, `PreMix(73)`: mixed owners / same owner, arrived | 2 / 15-28 | >= 100 / >= 150 |
| Parked friendly pair in a 1-cell corridor | walker gives up | walker arrives (QA repro un-skipped) |
| Cross-map scenario seeds 1-8 (`ScenarioTests`) | 200 / 200, 0 give up | unchanged: 200 / 200, 0 give up, within the limit |
| Perf (Debug): 500 moving avg; 2,500 tight blob avg; 1,000 walkers crossing a 1,500-blob | 0.15 ms; 3.8 ms; 3.1 ms | < 4 ms; <= 4.5 ms; report |

Miss rule: a row below its target is acceptable only if it beats "now", docs/03 states the measured
number and the structural cause, and the dev's report lists it (the Producer re-sets the bound at
ACCEPT). Not acceptable: any row worse than "now", the cross-map scenario failing, perf over budget.

**Acceptance criteria**
1. Items 1-2 implemented with the rules above (or documented deviations); the three un-skipped QA
   repros (`PastAParkedFriendlyPair`, `IdleEnemyHoldingTheWalkersGoalCell`,
   `FallbackStillRespectsTheFriend`) pass without edits to their assertions.
2. Every row of the targets table meets its target or the miss rule; the measured table is in docs/03
   under a new "Implementation (M1-4d-3)" heading, with the rules as built.
3. New dev tests, each failing on the old code: detour round a foreign blob in the open (both sides),
   field-waiting blocker counts as queued, walk-back happens once then stops, parked pair yields,
   owner-aware groupmate (enemy at a shared goal cell is a wall), fallback keeps the friendly clip,
   BUG-0030 same-cell reorder, BUG-0040 limits.
4. Determinism: two sims with the same seed and commands hash equal every tick through the crowd
   rows (500 to 4 points, two owners; the corridor pair; the blob crossing), and a reversed-spawn-order
   run of the detour test gives bit-equal positions; the per-tick code paths added allocate 0 bytes
   (`AllocationTests` rows for a 2,500-unit crowd with detours and walk-backs).
5. BUG-0039: the swept rows use new seeds, assert bounds that hold on every swept map or are
   report-only (named so), `TestSeeds.PreMix` is gone from them; BUG-0034's row averages >= 100 ticks
   or a median.
6. Golden: regenerated exactly once (`RTS_REGEN_GOLDEN=1`, then a clean run), still 1,500 ticks, 400
   commands, 15 checkpoints; the commit message names the rule that moved the checkpoints. The 15
   `MapGeneratorTests` pins do not move.
7. `dotnet build` 0 warnings; full suite (incl. Perf, rerun alone if it fails under load) green in one
   run; `ArchitectureTests` and the UnitStore hash audit green with the new field; bugs 0028, 0030,
   0032, 0033, 0034, 0037, 0038, 0039, 0040 (part 1) updated with status and proof.

**Design references:** docs/03 "Local movement" (M1-4d-1/2, M1-5, BUG-0035 fix, the measured tables
and known limits), "Tick model" phases 8-9 and 14, "Determinism", "Save/load and replays" (format
limits); CLAUDE.md rules 2-5; `MovementConstants`.

**Constraints most at risk:** determinism (walk order, slot order, no `HashCode`, no Dictionary
iteration, start-of-tick reads); per-tick allocation; a new hashed field must be in `StateHash`,
the replay checkpoint and the QA reflection audit; no stat in C# (any new tunable goes into
`MovementConstants` with a one-line why); sim track files only (`sim/**` except `ViewApi/`,
`game/data/**` untouched this session); keep `SimConfig`/`Simulation`/loader signatures additive.

**QA focus (full):** two-sim hash twins every tick on every new scenario; slot-order and
spawn-order independence of the detour side choice; termination and oscillation hunts (walk-back
loops, corridor ping-pong, detour around a ring of enemies, two groups swapping points through one
gap) with per-tick invariants (never on blocked ground, never across a blocked corner, never deeper
into an enemy, displacement <= speed, `StuckTicks` in range); enemy plugs still let nobody through;
the 40-map sweeps on the re-bounded rows and 20 more seeds on the corridor pair; perf at 2,500
units with crowds crossing and allocation 0 bytes; the golden regenerated exactly once (diff the
file: header unchanged, checkpoints moved) and the 15 map pins untouched; BUG-0030 vs click-spam.
Bug numbers from BUG-0042.

**After this session:** M1-7 perf test (500 moving units < 4 ms avg; BUG-0023 cap note), M1-8 CLI in
`tools/` (reuse `ReplayPlayer`: record / play `.replay`, print hashes and timings), then the M1
sign-off check (all 8 criteria, coverage ✅ Unit/Invariant/Determinism, no S1/S2; this session is
M1's end-of-milestone hardening if nothing else big lands).

## View track

### Current session plan: M2-2 unit views + selection + right-click move (Type feature · QA standard)

View feature sessions since the last hardening: 1 / 4. Budget up to 1,500 lines total; aim for about
700 lines of production code.

**Goal.** The first thing the owner can *do* in the game window: see both armies as coloured
placeholder units standing on the terrain, select them by click or box, and right-click to send
them somewhere, with the sim's movement showing smoothly at 60 FPS. This ticks the `SimRunner`
(interpolation) and "placeholder unit views" criteria and starts the selection and order rows.

**Scope**
- **`UnitViews`** (`Node3D` under `World3D` in `Match.tscn`): one `MeshInstance3D` per unit *slot*,
  created lazily the first time a slot is alive and hidden when not (so a respawn into the same slot
  reuses it; no per-frame `new`). Mesh: one shared `CapsuleMesh` (or cylinder) per unit type, radius
  = `Data.Units[typeId].Radius`, height about 2 x radius + 1 m; one shared `StandardMaterial3D` per
  owner. **Team colour = `PrimaryColor` of the faction of the unit's type**
  (`Data.Factions[Data.Units[typeId].Faction].PrimaryColor`, 0xRRGGBB → `Color`): player 0 spawns
  the Malazan roster, player 1 the Whirlwind roster, so the faction colour doubles as the team colour
  until M6's per-player colour (Producer decision; no colours in C#). No physics bodies.
- **Per frame:** for each alive slot, position = lerp(`PrevPosition`, `Position`, `SimRunner.Alpha`)
  mapped to Godot `(x, h, y)` with `h` from a new pure helper **`Rts.Sim.ViewApi.TerrainHeight.At(Heightmap,
  x, y)`** that returns the surface height the mesh draws (flat on plateau cells, the tilted plane on
  ramp cells, clamped to the map); yaw from `Facing` (sim `Facing = Atan2(vy, vx)` on the ground plane;
  verify the sign with a unit walking +x, document the convention in docs/03).
- **Spawning:** `Match.Start` enqueues `Command.SpawnUnit` for 100 units per player (`--units <n>`
  launch flag, 0-1000 per player, default 100): player 0's block to the west of the map centre,
  player 1's to the east, on passable cell centres laid out by a pure **`ViewApi.StartLayout`** helper
  (deterministic from the heightmap/nav grid, no RNG, all positions passable, unique, inside the
  map, spread so radii don't overlap). The camera starts focused on player 0's block.
- **`SelectionController`** (`Node` under `Match`): input actions `select` (LMB), `command` (RMB),
  `select_add` (Shift) in `project.godot`. Left click: the nearest own (player 0) alive unit whose
  projected screen centre is within its projected radius (minimum 12 px); drag of 4 px or more:
  box, every own unit whose projected centre lies inside; a plain click or box replaces the
  selection, Shift + click toggles, Shift + box adds; a click on empty ground clears; enemy units are
  never selected. Selection = handles (`new EntityHandle(slot, World.Units.Generation[slot])`),
  pruned of dead handles every frame. Pure **`ViewApi.ScreenPicker`** does the geometry (inputs: screen
  points + radii + the click or rect; output: chosen slots; deterministic tie-break = nearest to the
  click, then lowest slot).
- **Selection rings:** one `MultiMeshInstance3D` (flat ring or quad, unshaded), one instance per
  selected unit at the interpolated position, slightly above the ground, scaled by radius.
- **Right-click move:** ground point from the camera ray through pure **`ViewApi.GroundPicker`**
  (ray march cell by cell from the camera, refine against the cell's surface, plateau or ramp plane;
  first surface hit wins; returns none off-map); enqueue `Command.Move(0, handle, point)` for each
  selected unit. Off-map clicks enqueue nothing.
- Debug label adds `sel N`. docs/03 gets an "Implementation (M2-2)" section (scene tree, unit views,
  picking, axes and facing convention, launch flag).
- **Fold in BUG-0041** (few lines): `FixedStepClock.Advance` rejects `delta <= 0` and `speed <= 0`
  separately (un-skip the `FixedStepClockQaTests` row); `LaunchOptions.Parse` doesn't consume a value
  starting with `--`; `Match` logs `unchecked((ulong)Seed)`.
- **OUT:** attack-move / stop / hold / shift-queue (M2-3, needs new command kinds from the sim),
  double-click or Ctrl-click type select, control groups, Tab subgroups, minimap, HUD, audio, health
  bars, animations, sim events, trees/rocks, enemy selection, formations, order markers.

**Acceptance criteria**
1. Windowed `& $env:GODOT --path game -- --screenshot <png> --screenshot-after 2` shows 200
   placeholder units in two team colours standing on the terrain near the start camera; the dev
   and QA inspect the PNG and name the file in their reports.
2. Interpolation: view position = lerp(`PrevPosition`, `Position`, alpha) with alpha in [0, 1),
   never extrapolated; the view's height equals `TerrainHeight.At` within 1 mm; `TerrainHeight.At`
   equals the `TerrainMeshBuilder` surface at every cell corner and centre of 20 generated maps plus
   the 4x4 hand map (`ViewApi/TerrainHeightTests`); a headless scene (`game/tests/UnitViewsTest.tscn`)
   checks the midpoint at alpha 0.5 and prints `UNITVIEWS TEST PASS`.
3. Pooling: a slot freed and reused keeps its view node; after the first frame a slot is used, no
   nodes, meshes or materials are created; the per-frame update of 2,000 alive units allocates 0
   managed bytes (measured in the test scene with `GC.GetAllocatedBytesForCurrentThread`, reported).
4. Selection behaves as scoped (click, box, Shift toggle/add, clear on empty ground, enemies never
   selected, dead handles pruned, one ring per selected unit): `ViewApi/ScreenPickerTests` (pure
   geometry incl. overlapping units, zero-area box, inverted drag corners, points off-screen) and
   `game/tests/SelectionTest.tscn` driving injected input (as `CameraClampTest` does) through click,
   box and Shift-add, printing `SELECTION TEST PASS`.
5. Right-click: one `Command.Move` per selected unit to the picked point; the point is within 0.1 m
   of the true ray-terrain intersection on plateaus and ramps at both zoom limits and on high ground
   (`ViewApi/GroundPickerTests` on hand maps and generated maps); off-map clicks enqueue nothing;
   in the headless scene, 3 s of game time after the order the selected units are `Moving` or closer
   to the point.
6. Read-only: the view changes sim state only through `Simulation.Enqueue` and `Tick` (grep of
   `game/scripts` and `ViewApi/`); `ViewApi` helpers take `Heightmap` / `NavGrid` / arrays as inputs
   and hold no sim reference; `ArchitectureTests` and the forbidden-API scans stay green; QA's hash
   twin (the view's spawn and move commands fed to a bare sim) hashes equal every tick.
7. BUG-0041 fixed: the three nits, the un-skipped QA row passes, `--seed --speed 2` keeps speed 2.
8. `tools/qa/smoke.ps1` PASS; build 0 warnings; full suite green in one run; docs/03 "Implementation
   (M2-2)" matches the code; `StartLayout` tests (passable, unique, inside the map, non-overlapping).
9. Perf report: `-- --units 1000` (2,000 units) windowed at zoom 60 over an army: FPS from the overlay
   and the tick ms, reported (M2-7 enforces 60 FPS; this session reports).

**Design references:** docs/03 "Rendering and presentation" (unit views, picking without physics, team
colour, selection rings as MultiMesh), "Implementation (M2-1)" (axes, camera maths), "Presentation
timing"; docs/02 "Controls and camera" (left-click / drag, Shift, right-click); CLAUDE.md rules 1, 7, 8;
docs/07 ownership table (view tests in `sim/Rts.Sim.Tests/ViewApi/`, QA in `QA/ViewApi/`, scenes in
`game/tests/`).

**Constraints most at risk:** ViewApi is read-only and sim-reference-free (no `Tick`, `Enqueue`,
`World`, `Simulation` inside `ViewApi/`; `ArchitectureTests` and `SpatialHashTests.Source_UsesNoHashCollectionsOrLinq`
grep it); no Godot types in `Rts.Sim`; views hold no gameplay state (selection is a view concern,
fine; no unit stats in the view); colours and names from data, none in C#; no per-frame allocation in
the update loop; view track files only (`game/**` except `game/data/**`, `sim/Rts.Sim/ViewApi/**`,
tests under `ViewApi/`); shared files append-only (coverage.md, bugs README, docs/03).

**QA focus (standard):** the hash twin (read-only proof); pool reuse across free/respawn of the same
slot (`UnitStore.Free` is public, drive it from a ViewApi test); alpha never extrapolates; ramp
heights consistent with the mesh (no floating or sinking units, worst error reported); `GroundPicker`
at both zoom limits, high ground, grazing angles, rays missing the map, rays through a cliff wall
(must hit the upper plateau, not the lower cell behind it); `ScreenPicker` ties, units behind the
camera or off-screen, 2,000 units in one pixel; selection survives a unit dying mid-drag; 2,000 views
FPS and per-frame ms; screenshot inspected; BUG-0041 rows. Bug numbers from BUG-0052.

**After this session:** M2-3 needs `Stop`, `HoldPosition`, `AttackMove` command kinds and
shift-queued orders from the sim: the Producer plans that sim request right after this hardening
session (it is already in STATE's "Requests for the sim track").

## Watch out for (both tracks)

- Merge order sim → view; the conductor reruns build/tests after each merge. Likely conflicts:
  `studio/qa/coverage.md`, `studio/bugs/README.md`, docs/03 (sim appends "Implementation (M1-4d-3)"
  under "Local movement"; view appends "Implementation (M2-2)" under "Rendering and presentation").
  Resolution: take both.
- The sim track regenerates the golden this session; the view track must not touch `game/data/`
  (a data edit would force a second regen and a conflict).
- The view's `UnitViewsTest`/`SelectionTest` scenes run headless; the smoke gate only runs `Main.tscn`.
- Both tracks' QA suites overlap in wall-clock: a Perf failure counts only when it fails again alone.
