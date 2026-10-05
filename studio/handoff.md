# Handoff: next session brief

Written by the Producer at the end of each session for the next one. The next session's
Producer starts from this, verifies it against the repo, and then plans. One section per track.

## Where we are (both tracks)

M0 Done. First two-track session (2026-10-05-1446) planned at `main` @ 96c9f64: build 0/0,
non-Perf suite 1021 passed / 10 skipped / 0 failed (1 m 19 s), last full run 1090 / 13 / 0, smoke
PASS. Open bugs 11 S3 + 4 S4, none block. Sessions today 3/8 before this one. Inbox empty after
processing the owner's two-track note.

**First things at the next PLAN:** confirm both session branches (`studio/2026-10-05-1446-sim`,
`-view`) merged to main and the build is green *after* the view merge (the view compiles against
the sim's new code); verify one claim per track (e.g. the golden replay file exists and
`RTS_REGEN_GOLDEN` is not set anywhere; the `--screenshot` flag produced a PNG the session log
links); check `studio/qa/coverage.md` merged cleanly (Replays and Camera rows are adjacent lines).

## Sim track

What the sim does now (`MovementSystem`, docs/03 "Local movement"): Moving units sorted by (goal
cell, slot); build pass (oldest order first, cap 2); **Plan** per unit from start-of-tick state;
`Constrain` against standing non-groupmates (friendly shovable Idle units yield; enemies holding
ground are hard walls with a `ClosestAllowed` fallback); arrival within 1 m or touching an arrived
groupmate; no progress -> stuck or *queued*; stuck 20 ticks -> give up. **Apply**, **ApplyShoves**,
`RecheckAnchors`. `StateHash` covers `State/Goal/GoalCell/OrderTick/StuckTicks/BestRemaining`,
cache metadata and `Move`'s handle; `Speed/Radius`, spatial hash, scratch arrays and field
contents are derived.

### Current session plan (sim): M1-6 — Replay format, determinism test, golden replay (BUG-0014 first)

Type feature · QA full · design clear, budget up to 1,500 changed lines.

Goal: give M1 its proof of determinism that outlives this session: a replay file (seed + commands
+ checkpoint hashes), a player that re-runs it and compares, and one checked-in golden that every
later movement change must reproduce or deliberately regenerate. BUG-0014 (seed mixing) changes
every map hash, so it lands in the same task before the first golden hash is recorded.

Scope:
- (a) **BUG-0014 first:** mix the seed (SplitMix64 or equivalent) in `SimRng(seed, streamId)`
  before it enters the PCG state, so `ulong.MaxValue` and `0` give different streams. Un-skip
  `QA/MapQaTests.SeedMaxValue_AndSeedZero_GiveDifferentMaps`; update `SimRngTests` expectations;
  regenerate the 15 pinned hashes in `MapGeneratorTests` and the QA `PreBug0015*` oracle pins; the
  commit message says why every pinned hash changed.
- (b) `Rts.Sim.Replay` namespace (new folder `sim/Rts.Sim/Replay/`): `Replay` (header: format
  version 1, `SimInfo.Version`, data hash, map = `MapGenParams` fields, seed, player count,
  checkpoint interval; command log of stamped `Command`s; checkpoint list `(tick, hash)`),
  `ReplayRecorder` attached to a `Simulation` (captures every command accepted by `Enqueue` as
  stamped, and `StateHash()` every N ticks, N = 100 default, after the tick), `ReplayFormat`
  (write/read, line-based UTF-8 text, invariant culture, floats as exact IEEE bit patterns so a
  round trip is bit-exact), `ReplayPlayer.Run(replay, data)` (builds `SimConfig`, feeds each
  command when `TickNumber == command.Tick - 1` in log order, re-stamped sequence numbers must
  match the log; compares every checkpoint; returns the first mismatch tick or success).
- (c) `GameData.ContentHash()`: a stable `StateHasher` hash over every field of every def in id
  order (no `HashCode`, no string hash codes: hash the chars). Playback refuses a replay whose data
  hash differs (message text is a `ReplayError` enum/code, not player-facing prose in C#).
- (d) Golden: `sim/Rts.Sim.Tests/Replays/cross_map_seed1.replay`: the `CrossMapScenario` seed-1
  march, 200 units, exactly 1,500 ticks, checkpoints every 100. Test replays it and compares all
  15 checkpoints. Env var `RTS_REGEN_GOLDEN=1` rewrites the file and then *fails* with a message
  ("golden regenerated; rerun without the flag"), so a regen can never pass silently.
- OUT: binary format, save files, Godot-side playback UI (M6), AI commands (M5), a replay of
  arbitrary length limits, compression, cross-machine guarantees.
- Keep `SimConfig`, `Simulation` constructors and `DataLoader.LoadAll` additive this session (no
  new `required` members, no signature changes): the view track compiles against them in parallel.

Acceptance criteria:
1. `SimRngTests` show seed `ulong.MaxValue` and seed `0` produce different first 8 draws on stream
   0, and `MapQaTests.SeedMaxValue_AndSeedZero_GiveDifferentMaps` is un-skipped and passes; BUG-0014
   status `fixed`.
2. Round trip: record a 300-tick run with spawns and moves → write → read → the parsed `Replay`
   equals the original field for field (commands, checkpoints, header), and replaying it reproduces
   every checkpoint hash.
3. Determinism test: two sims, same seed and command list → equal `StateHash()` every 100 ticks
   for 2,000 ticks (with movement); different seeds → different hash by tick 100.
4. Golden test passes on the checked-in file; with `RTS_REGEN_GOLDEN=1` it rewrites the file and
   fails with the documented message; the file is < 64 KB and text (diffable).
5. Data-hash mismatch (load data, change one stat in a temp copy) → `ReplayPlayer` refuses with
   the data-mismatch code before running a tick; format-version mismatch refuses likewise.
6. Robustness: truncating the golden at every line boundary and flipping any single byte in the
   header or a command line is rejected by `ReplayFormat.TryRead` with a `ReplayError` (no
   exception escapes, no partial `Replay` returned); a replay whose command references a player
   `>= PlayerCount` is rejected at read.
7. A recorder attached to a sim allocates 0 bytes during `Tick()` (checkpoint buffer preallocated
   from the planned tick count; `AllocationProbe.AssertZero` over 500 ticks incl. 5 checkpoints);
   `Enqueue` growth, if any, is documented as off-tick.
8. Full suite green in one run; `ArchitectureTests` scan still passes (no `HashCode`, no
   `DateTime`, no Godot); docs/03 "Save/load and replays" and "Testing strategy" updated with the
   format (field list, float encoding, regen flag) and the BUG-0014 seed mixing noted under
   "Determinism"; coverage map Replays row filled by QA.

Design references: docs/03 "Save/load and replays" (header fields, refuse on data hash),
"Testing strategy" (golden replays in `sim/Rts.Sim.Tests/Replays/`, regenerate with a test flag,
explain in the commit), "Determinism" (`SimRng` PCG32 seeding, state hash contents), "Tick model"
(`Enqueue` stamps `TickNumber + 1`); BUG-0014 notes; CLAUDE.md rules 4 and 5.

Tests required: `SimRngTests` (mixing), `ReplayFormatTests` (round trip, truncation/bit-flip
fuzz, bad player/kind/tick order), `ReplayPlayerTests` (checkpoint compare, data hash refuse,
format version refuse, sequence re-stamp equality), `ReplayGoldenTests` (golden + regen flag),
`DeterminismTests` (criterion 3), `AllocationTests` addition (criterion 7), un-skipped
`MapQaTests` row, regenerated `MapGeneratorTests` pins and `PreBug0015*` oracles.

Constraints: no Godot or `System.Text.Json` dependency inside the replay types' hot path (text
I/O with `StreamWriter`/`StreamReader` is fine; it runs outside `Tick`); no `HashCode`/
`string.GetHashCode`; no `Dictionary` iteration; no new NuGet; `Simulation.Tick()` stays
allocation-free; one implement commit; the commit message explains the pinned-hash regeneration.

QA focus (sim): attack the parser (truncation at every byte, not just lines; CRLF vs LF; BOM;
duplicate header keys; a checkpoint before tick 0; commands out of tick order; a command for a
dead handle, which must *replay* (it was dropped at record time too) rather than be rejected);
re-stamping (record with two players interleaved and verify sequence numbers survive); the golden
under `--filter` alone and inside the full suite; `GameData.ContentHash()` sensitivity (every
field of every def flips it; renaming a unit folder does not); seed mixing (1,000 random seed
pairs differ on all streams; the mixed seed of 0 is not 0-state-equivalent to any of them);
recorder allocation with 2,500 units. Stress: replay 50 recorded random-command runs (seeds 1-50,
500 ticks) and all must reproduce; measure replay file size for a 1,000-unit 2,500-tick march.

## View track

What exists in `game/`: `project.godot` (Forward+, C#, main scene `Main.tscn`), `RtsGame.csproj`
(Godot.NET.Sdk 4.7.2, references `Rts.Sim`), `scripts/Main.cs` printing `Rts.Sim <version>`
(the smoke gate greps for that banner). Public sim surface the view may use read-only:
`DataLoader.LoadAll(dir)` → `DataLoadResult` (data or errors), `new SimConfig(seed, players,
unitCap, cmdCap) { Data, Map }`, `new Simulation(config)`, `Tick()`, `Enqueue(Command)`,
`StateHash()`, `World.Heightmap` (`Width/Height/LevelAt/ElevationAt/IsRamp/Elevations`),
`World.NavGrid` (`IsPassable/WorldToCell/CellCenter/FlagsAt`), `World.Units` arrays
(`Alive/Position/PrevPosition/Facing/Owner/TypeId/Radius/State`), `MapConstants.CellSize = 2`,
`LevelHeight = 4`. Sim `Vector2(x, y)` is ground (x, z); Godot Y is up = elevation.

### Current session plan (view): M2-1 — Match scene runs the sim: SimRunner, terrain mesh, RTS camera, screenshot flag

Type feature · QA standard · first Godot task, so budget ~1,000 changed lines (uncertain:
Godot specifics), production code small and plain.

Goal: turn the empty window into a 3D view of a generated map with the sim ticking underneath,
plus the one tool every later visual task is verified with: a `--screenshot` flag. No units are
drawn yet; that is M2-2.

Scope:
- `game/scenes/Match.tscn` instanced by `Main.tscn` (Main keeps printing the banner, loads
  `game/data/` via `DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"))`; on data
  errors it prints each `DataError` with `GD.PushError` and quits with exit code 1).
- `game/scripts/SimRunner.cs` (Node): owns a `Simulation` (`[Export]` seed default 1, players 2,
  unit capacity 2,000, command capacity 4,096; `--seed <n>` and `--speed <x>` user args override);
  in `_Process` it advances a pure `Rts.Sim.ViewApi.FixedStepClock` (accumulates `delta *
  GameSpeed`, yields whole 50 ms ticks, at most 5 per frame and the accumulator clamped so a stall
  can't snowball, exposes `Alpha` in [0, 1)) and calls `Tick()` that many times; `GameSpeed`
  property (0.25-8); exposes `Simulation`, `Alpha`, last tick cost in ms (`Stopwatch` is fine in
  the view).
- `sim/Rts.Sim/ViewApi/TerrainMeshBuilder.cs` (pure, no Godot, reads `Heightmap` only): returns
  positions/normals/colors/indices as plain arrays: plateau cells flat at their elevation, cliff
  faces as vertical quads between neighbouring cells of different height, ramp cells a continuous
  slope (not stair steps), vertex color by level (3 tints) with ramps and cliff faces visibly
  distinct. `game/scripts/TerrainView.cs` copies them into an `ArrayMesh` on a `MeshInstance3D`
  with a vertex-color `StandardMaterial3D`; one `DirectionalLight3D` and a `WorldEnvironment`
  with ambient light so faces read.
- `game/scripts/RtsCamera.cs` (Camera3D under a pivot): pitch fixed 55°, no rotation; zoom with
  the wheel between 20 and 60 m above the focus point (default 40); pan with screen edges (8 px
  margin), arrow keys, and middle-mouse drag; pan speed scales with zoom; the focus point is
  clamped to the map rectangle `[0, Width*2] x [0, Height*2]`. Input actions in `project.godot`
  (`camera_pan_left/right/up/down`, `camera_zoom_in/out`, `camera_drag`) so they're rebindable.
- `--screenshot <path> --screenshot-after <seconds>` (default 2 s) user args (after Godot's `--`):
  save the viewport image as PNG to the path and quit 0; headless (dummy renderer) logs that a
  screenshot is unavailable and quits 0 without an ERROR line. Screenshots are never committed.
- A debug `CanvasLayer` + `Label` (top-left): tick number, game speed, last tick ms, FPS.
  Dev-only text, not player-facing.
- OUT: unit views, selection, orders, minimap, HUD, audio, trees/rocks, biomes beyond per-level
  tints, nav-grid overlay, any change to `sim/**` outside `ViewApi/`, `game/data/**`, `tools/**`,
  `RtsGame.sln`.

Acceptance criteria:
1. `powershell -File tools/qa/smoke.ps1` prints PASS with `Match.tscn` instanced (banner present,
   no ERROR lines, sim ticks headless).
2. Windowed `& $env:GODOT --path game -- --screenshot <path> --screenshot-after 2` writes a PNG and
   exits 0; the PNG (attached by path in the dev report) shows plateaus at distinct heights,
   vertical cliff faces, sloped ramps, three level tints, and the debug label with a tick number
   > 30. The dev *looked at it* and says what is in it.
3. `FixedStepClockTests` (in `sim/Rts.Sim.Tests/ViewApi/`): 60 frames of 1/60 s → 20 ticks total,
   alpha always in [0, 1); speed 2 → 40 ticks; one 1 s frame → 5 ticks and the leftover is capped
   (next normal frame yields at most 1); speed 0 → no ticks, alpha frozen; a 0 or negative delta
   yields nothing and doesn't throw.
4. `TerrainMeshBuilderTests`: for a hand-made 4x4 heightmap with levels 0/1 and one ramp cell:
   every vertex Y equals a cell elevation or lies between the two levels on the ramp; every index
   in range; no NaN; at least one face with a horizontal normal (cliff) between the level-0 and
   level-1 cells; ramp vertices are monotonic along the slope; level colors differ. On the default
   128x128 generated map (seed 1): vertex/index counts are consistent (indices multiple of 3),
   build < 100 ms (report, not Perf-tagged), and `Heightmap.ContentHash()` and `Simulation.StateHash()`
   are unchanged by building the mesh twice.
5. `ArchitectureTests` still passes (no Godot in `Rts.Sim`, including `ViewApi/`); the view code
   never writes sim state (`Units` arrays, `TickNumber`) except through `Enqueue`/`Tick` (QA
   greps `game/scripts` for assignments into `World.`).
6. Camera: in a windowed run, arrows / edge / middle-drag pan and the wheel zooms; the camera can't
   be panned past the map edge or zoomed outside 20-60 m (dev verifies by hand and reports; QA
   reads the clamp code and tests any pure clamp helper if one is extracted into ViewApi).
7. docs/03 "Rendering and presentation" gains an "Implementation (M2-1)" paragraph (scene tree
   as built, Y-up mapping, mesh method, flag syntax); "Presentation timing" notes `FixedStepClock`;
   "Debug tooling" notes the label and the flag as shipped. CLAUDE.md is not edited.
8. `dotnet build RtsGame.sln` 0 warnings; full `dotnet test sim/Rts.Sim.Tests` green in one run.

Design references: docs/03 "Presentation timing" (accumulator, max 5 ticks per frame, alpha),
"Rendering and presentation" (scene layout, picking without physics for later), "Testing
strategy" (screenshot flag `--screenshot <path> --screenshot-after <seconds>`), "Debug tooling";
docs/02 "Controls and camera" (55° pitch, no rotation, zoom 20-60 m, edge/arrows/middle-drag) and
"Map and terrain" (cell 2 m, levels 4 m apart); CLAUDE.md rules 1, 2, 3, 7.

Tests required: `sim/Rts.Sim.Tests/ViewApi/FixedStepClockTests.cs`,
`sim/Rts.Sim.Tests/ViewApi/TerrainMeshBuilderTests.cs` (incl. the hash-unchanged check); smoke
PASS; one screenshot inspected. No other test file in `sim/Rts.Sim.Tests` is touched.

Constraints: stay inside `game/**` (not `game/data/**`) and `sim/Rts.Sim/ViewApi/**` plus
`sim/Rts.Sim.Tests/ViewApi/**`; `Rts.Sim` builds with warnings as errors, so ViewApi code needs
`///` summaries on public types; no `using Godot` in `ViewApi/`; no physics bodies; C# only, text
scenes (`.tscn`); no new NuGet; no binary files committed (`.gitattributes` LFS rule); don't run
`dotnet test` with Perf while the sim track's QA may be running (use `--filter Category!=Perf`
for the quick loop, full run once at the end); one implement commit.

QA focus (view): prove `ViewApi` is read-only (reflection or source scan: no setter/method on
`Heightmap`/`World` is called; build the mesh 100 times between two sims and hash-compare);
`FixedStepClock` fuzz (random deltas 0-2 s, speeds 0.25-8, 10,000 frames: tick count equals
floor of accumulated scaled time within the cap rule, alpha never leaves [0, 1), no drift when
deltas are exactly 1/60); `TerrainMeshBuilder` on adversarial heightmaps (1x1, 1xN, all one
level, checkerboard levels, ramps at the map edge, 1024x1024 time and memory report);
headless + windowed boot with bad args (`--seed abc`, `--speed -1`, `--screenshot` with no path or
an unwritable path: no crash, no ERROR line unless the path is truly unwritable); the data-error
path (temp copy with a broken JSON: exit 1 with each error printed); the camera clamp at all
four map edges and both zoom limits if a pure helper exists. Tests only under
`sim/Rts.Sim.Tests/QA/ViewApi/` and `game/tests/`.

## Watch out for (both tracks)

- The two branches merge sim first, then view; the conductor reruns build/tests after each merge.
  Adjacent rows in `studio/qa/coverage.md` (Replays, Camera) and docs/03 sections may conflict:
  docs/studio conflicts are the conductor's to resolve, code conflicts escalate.
- Budget history: M1-5 ran ~2,300 changed lines with ~165 production; keep production slices small.
- Movement is chaos-sensitive; M1-6 touches no movement code, but the seed mixing changes every
  generated map, so any test pinned to a seed's layout (not only hashes) may need a new seed.
- Determinism rules: all unit-unit interaction reads start-of-tick state; neighbor order ascending
  slot. The golden pins all of this from now on.
- `TestSim.Config(...)` is the one way tests build a `SimConfig`. `ArchitectureTests` and
  `SpatialHashTests.Source_UsesNoHashCollectionsOrLinq` grep sim source (ViewApi included).
  `SimRng` is a mutable struct: always `ref world.Rng(stream)`. Full run ~2 min.
- Next sim session is a hardening one (4/4 after M1-6): M1-4d-3 crowd routing + BUG-0037/0038,
  BUG-0034 + the chaos-sensitive `MoreGoalsThanCacheSlots` row, BUG-0030, BUG-0025/0026, BUG-0005.
- Next view session: M2-2 unit views + selection + right-click move (needs no sim change).
