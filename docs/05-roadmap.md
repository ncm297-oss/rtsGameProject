# 05 — Roadmap

Each milestone ends with a playable (or runnable) build, green tests, and a short retro before
the next one starts. Acceptance criteria are checkboxes so progress is visible in the doc itself.

Status legend: **Next** = start here, **Planned** = not started, **Done** = accepted.

| # | Milestone | Status | One-line goal |
| --- | --- | --- | --- |
| M0 | Environment & skeleton | **Done** (2026-10-03) | Tools installed, empty projects build, tests and headless boot pass |
| M1 | Core sim, no graphics | **Done** (2026-10-06, Producer sign-off) | 200 units path across the map deterministically, fast |
| M2 | Presentation | **Done** (2026-10-07, Producer sign-off after the M2-H2 hardening) | Move an army around a 3D map |
| M3 | Economy & buildings | **In progress** (sim + data tracks since 2026-10-06-1255, view track from 2026-10-07; 3 / 8; sim hardening M3-H1 done) | Build a Malazan base |
| M4 | Combat, fog, abilities | Planned | Malazan vs. Whirlwind armies fight with abilities and fog |
| M5 | AI opponent | Planned | Lose to a Whirlwind AI |
| M6 | Game shell & real art | Planned | A friend can play it |
| M7 | Teblor | Planned | Third faction: scale and population systems proven |
| M8 | Shadow | Planned | Fourth faction: stealth polish, summons |
| M9 | Tiste Andii | Planned | Fifth faction: darkness, flying, elite balance |

M7-M9 order is adjustable. Teblor goes first because scale and pop cost are the simplest new
mechanics.

## M0 — Environment & skeleton

**Done when:**

- [x] Godot 4.7.x .NET build, .NET 8 SDK, Git, and Git LFS installed per [SETUP.md](../SETUP.md);
      `$env:GODOT` points at the console exe.
- [x] `RtsGame.sln` at the repo root with `sim/Rts.Sim`, `sim/Rts.Sim.Tests`, `game/RtsGame.csproj`.
- [x] `Rts.Sim` targets `net8.0`, nullable on, warnings as errors, and has no Godot reference.
- [x] `game/project.godot` (Godot 4.7, Forward+, C#) with an empty `Main.tscn` that prints the
      sim library's version through a reference to `Rts.Sim`.
- [x] `dotnet build RtsGame.sln` succeeds; `dotnet test sim/Rts.Sim.Tests` passes one trivial test.
- [x] `& $env:GODOT --headless --path game --quit-after 600` exits 0 with no errors.
- [x] Commands in CLAUDE.md verified (fix any that are wrong).
- [ ] Optional: Godot MCP server configured for Claude Code.

_Required criteria met in session 2026-10-03-0826 (task M0-1). Owner signed off ("M0 accepted",
`studio/inbox.md`, 2026-10-03). The optional MCP item stays open and is not required._

## M1 — Core sim, no graphics

**Done when:**

- [x] `World`, entity stores with generational handles, seeded RNG streams, `SimMath`, command queue, fixed tick.
      _(session 2026-10-03-0907, task M1-1)_
- [x] Data loader for `game/data/` with validation; a test loads all data.
      _(session 2026-10-03-1151, task M1-2: `DataValidationTests.ShippedData_LoadsWithNoErrors`)_
- [x] Terraced heightmap generator (elevation levels 0-2, ramps); nav grid with slope passability and per-cell level; spatial hash.
      _(heightmap generator + nav grid: session 2026-10-03-1235, task M1-3; ramp walls + spatial hash: session 2026-10-03-2220, task M1-4a)_
- [x] Flow fields with LRU cache; steering, separation, arrival, shoving.
      _(flow fields + LRU cache + `Move` command + build cap: session 2026-10-04-0120, task M1-4b;
      hashed cache metadata, oldest-order-first cap of 2, cache sized to the unit cap: session
      2026-10-04-2056, task M1-4c; separation, crowded arrival, give-up rule, adjacent-cell
      steering: session 2026-10-05-0742, task M1-4d-1; shoving of friendly Idle units, anchor
      re-check, `Constrain` cap: session 2026-10-05-1013, task M1-4d-2. Crowd quality is below the
      Producer's targets (groups to nearby points arrive 35-44%, parked groups block chokes):
      BUG-0032/0033; the follow-up, M1-4d-3 crowd routing, is S3 debt for the M1 hardening
      session. The scenario test below (one army, one goal) does not depend on it (Producer,
      session 2026-10-05-1234). M1-4d-3 landed in the M1 hardening session 2026-10-05-1609:
      detour round foreign blobs, widened queuing, walk-back, chain shove, owner-aware groups,
      enemy plugs of up to 4 units hold; crowd rows still below the Producer's targets (4 points:
      51% / 34%), kept as S3 debt BUG-0028/0032 and BUG-0044/0045/0049/0050 for the M1
      end-of-milestone hardening session; see docs/03 "Implementation (M1-4d-3)".)_
- [x] Scenario test: 200 units ordered across a 128×128 map with obstacles all arrive within a
      time limit, none stuck, none inside blocked cells.
      _(session 2026-10-05-1234, task M1-5: `ScenarioTests.TwoHundredUnits_AcrossTheMap_UpARamp_AllArriveWithinLimit_NoneGiveUp_NeverOnBlockedGround`
      seeds 1-8 (200 units of every type, west edge to the farthest plateau, derived time limit,
      per-tick blocked-ground check, 100-tick hash twins) + QA `Stress/CrossMapStressTests` seeds
      1-50, level-2 goals, 500/1,000 units. Needed the queued-walker give-up rule; BUG-0035 (walkers
      squeezed through enemy plugs) fixed in the same task.)_
- [x] Replay format (seed + commands + checkpoint hashes); determinism test (same run twice →
      same hash) and one golden replay.
      _(session 2026-10-05-1446, task M1-6: `Rts.Sim.Replays` (`Replay`, `ReplayRecorder`,
      `ReplayFormat` ASCII/LF text with FNV-1a checksum, `ReplayPlayer`), `GameData.ContentHash()`;
      golden `sim/Rts.Sim.Tests/Replays/cross_map_seed1.replay` (200 units, 1,500 ticks, 15
      checkpoints, 15.5 KB; regen with `RTS_REGEN_GOLDEN=1`); `DeterminismTests` 2,000 ticks. BUG-0014
      seed mixing landed first (every pinned map hash regenerated once). QA: 50 fuzzed runs replay,
      parser refuses every truncation/bit flip, recorder allocates 0 bytes at 2,500 units.)_
- [x] Perf test: 500 moving units, average tick < 4 ms on the dev machine.
      _(session 2026-10-05-2330, task M1-7: `PerfCriterionTests.FiveHundredMovingUnits_AverageTickUnder4Ms`
      (Perf trait, serial; 500 units of every type on the default 128 map, asserts >= 95% Moving on
      every measured tick, 5 warm-up + 200 timed): avg 0.66-0.70 ms, p99 < 1 ms, worst ~1 ms alone
      (Debug, dev PC). Maps over 256 x 256 documented as unsupported (BUG-0023). The same task added
      `Stop` / `HoldPosition` / `AttackMove` command kinds, `Command.Flags` (shift-queue), a hashed
      8-entry order queue per unit (`OrderSystem`, phase 7) and replay format 2 (10-field command
      lines; golden regenerated, checkpoints byte-identical), for M2-3.)_
- [x] A tiny CLI in `tools/` runs a scenario headless and prints hashes and timings.
      _(session 2026-10-06-0655, task M1-8: `tools/Rts.Cli` (net8.0 console, references `Rts.Sim`
      only, in the `.sln`): `run --seed --units [--ticks --players --checkpoint --record --data]`
      prints `tick <n> hash <16 hex>` per checkpoint and `ticks N avg/p99/worst ms`, records a
      `.replay`; `play <path>` replays and exits 0 / 1 (usage, data, file) / 2 (replay refused or
      mismatched). `Cli/CliTests` (in-process; CLI hash equals a direct sim, two runs byte-identical,
      21 bad-usage rows), QA `QA/CliQaTests` (seeds 1-5 x 200/500, two real processes, every
      truncation, hostile command rows). Same task: `FlowFieldCache.PeekCached` for the M2-5
      overlay (read-only, hash-twin proven over 5.3 M peeks). BUG-0057 (S4 nits). All 8 M1 criteria
      are met; the M1 end-of-milestone hardening session and sign-off follow.)_

_All 8 criteria met by session 2026-10-06-0655. End-of-milestone hardening M1-9 in session
2026-10-06-0905 (holders block their own army, malformed commands refused at `Enqueue`, plug clusters
up to 32, plug-answer cache, stray-anchor fix, 64-goal re-bound, CLI nits, docs sweep).
`studio/qa/coverage.md`: every M1 row ✅ for Unit, Invariant fuzz and Determinism; no open S1/S2.
Signed off by the Producer on 2026-10-06 under autopilot (`stop_at_milestone_end: no`); open S3/S4
listed in the retro. Owner may revisit._

## M2 — Presentation

**Done when:**

- [x] `SimRunner` with accumulator and interpolation; game speed setting.
      _(accumulator (`ViewApi.FixedStepClock`, 5-tick cap, alpha) and game speed 0.25-8 shipped in
      session 2026-10-05-1446, task M2-1; ticked in session 2026-10-05-1609, task M2-2: unit views
      lerp `PrevPosition` to `Position` on alpha clamped to [0, 1], never extrapolating
      (`game/tests/UnitViewsTest.tscn` checks the midpoint at alpha 0.5 and bad alphas).)_
- [x] RTS camera: 55° pitch, edge pan, arrow keys, middle-drag, zoom, clamped to the map.
      _(session 2026-10-05-1446, task M2-1: `RtsCamera` + pure `ViewApi.CameraLimits`; zoom 20-60 m,
      8 px edge band, rebindable actions in `project.godot`; `game/tests/CameraClampTest.tscn` drives
      the real camera headless to every edge and both zoom limits.)_
- [x] Heightmap terrain mesh with biome vertex colors; trees and rocks as MultiMesh.
      _(terrain mesh shipped in M2-1: `ViewApi.TerrainMeshBuilder`, flat plateaus, vertical cliffs,
      sloped ramps, per-level placeholder tints. Trees and gold mines as MultiMesh props in session
      2026-10-06-1503, task M2-3b: pure `ViewApi.PropLayout` (one transform list per resource type
      from `World.Resources`, relisted only on a `NavGrid.Version` change, dead nodes compacted away),
      `PropsView` (placeholder cone-on-trunk trees, slate-and-gold mine blocks sized from the data
      footprint), the match map gets 12 forests / 8 mines by default (`--forests` / `--mines`),
      minimap resource layer (trees dark green, mines gold). QA oracle 43 map setups through Take
      churn, 0 mismatches; 396+ fps with 2,000 units and 1,773 props. **Scoped by the Producer:**
      biome vertex colours and rocks (decoration, no sim footprint) move to the M6 art pass with the
      real materials; ticked as "terrain mesh + trees as MultiMesh". BUG-0085 (S3, the seed-1 start
      army stands in a forest), BUG-0086 (S4 nits).)_
- [x] Placeholder unit views (primitive meshes, team colors), pooled, interpolated.
      _(session 2026-10-05-1609, task M2-2: `UnitViews`, one capsule `MeshInstance3D` per unit slot
      (reused on respawn), one mesh per unit type sized by its data radius, one material per faction
      with its `PrimaryColor`, placed on `ViewApi.TerrainHeight`; 2,000 views update in 0.47 ms and
      0 bytes per frame; QA hash twin: the view-driven sim equals a bare sim every tick.)_
- [x] Selection: click, box, shift-add, double-click type, control groups, Tab subgroups.
      _(click, box, Shift toggle/add and selection rings shipped in M2-2 (`SelectionController`,
      pure `ViewApi.ScreenPicker` / `SelectionSet`, enemies never selectable); double-click / Ctrl +
      click type select (own on-screen units of the type), control groups 1-9 (pure
      `ViewApi.ControlGroups`: Ctrl assigns, Shift adds, digit recalls, double-tap centres the
      camera) and Tab subgroups (pure `ViewApi.Subgroups`, ascending type id) in session
      2026-10-06-0655, task M2-3: `ControlGroupsTests`, `SubgroupsTests`, `OrdersTest.tscn`,
      QA `QA/ViewApi/OrdersControllerQaTests` + `QaM23Test.tscn`. BUG-0067 (S4 nits).)_
- [x] Right-click move, A attack-move (moves only for now), S stop, H hold, shift-queue.
      _(right-click move shipped in M2-2 (`ViewApi.GroundPicker`, within 1 mm of the drawn mesh,
      cliff faces resolve to the upper plateau); A targeting + click, S, H, Shift-queue (and a
      Shift-queued minimap right-click) through the one order path `SelectionController.Order`
      in session 2026-10-06-0655, task M2-3; QA hash twin with all four kinds on 3 maps up to
      1,000 units per player. BUG-0068 (S3): a minimap right-click while A is armed orders a Move
      instead of cancelling; view hardening session.)_
- [x] Minimap with click-to-move-camera and right-click orders.
      _(session 2026-10-05-2330, task M2-4: `Hud` CanvasLayer + `Minimap` Control (bottom-left,
      220 px), pure `ViewApi.MinimapRaster` (terrain baked once with the mesh palette, unit dots at
      5 Hz, 0 bytes per refresh) and `ViewApi.MinimapTransform` (letterboxed fit, pixel <-> metres);
      camera trapezoid every frame; left-click / drag moves the camera focus, right-click calls
      `SelectionController.OrderMoveTo`; edge pan suppressed over it; `--no-hud` flag;
      `game/tests/MinimapTest.tscn`; QA hash twin with 808 minimap orders over 3 map shapes.
      Fog, resources, pings and minimap zoom come with M3/M4. BUG-0064 (S4, dot readability).)_
- [x] `--screenshot` debug flag; debug overlay (nav grid, flow arrows, tick time).
      _(`--screenshot <path> --screenshot-after <s>` and a tick/speed/tick-ms/FPS label shipped in
      M2-1; the overlay in session 2026-10-06-1255, task M2-5: F12 (action `debug_overlay`) or
      `--debug-overlay`; pure `ViewApi.NavOverlayBuilder` (one quad per cell, colour by flag, refilled
      only on a `NavGrid.Version` change), `ViewApi.FlowArrowLayout` (the selection's goal via
      `FlowFieldCache.PeekCached`, 40 x 40 cells round the camera, never a held field),
      `ViewApi.TickTimeRing` (120 samples, 4 ms line) + entity counts; off by default and 0 bytes per
      frame for its layers; QA oracle 0 mismatches over 960 refreshes + 195 live frames, hash twin at
      2,000 units. BUG-0083 (S3, the label line allocates; docs claim 0), BUG-0084 (S4 nits).)_
- [x] Placeholder audio for select and command.
      _(session 2026-10-06-1744, task M2-6: `game/scripts/Sfx.cs` + `SfxEvent` (8-player pool, clips
      synthesized at start-up: `Select` one 1,320 Hz blip of 70 ms, `Command` a 660 → 990 Hz two-note
      confirm of 120 ms, cubic attack / release so nothing clicks, peak ~0.65), hooks in
      `SelectionController` (Select on a changed non-empty selection; Command once per order that
      enqueued anything), once per frame and 50 ms apart per event, `--mute`, `SfxVolumeDb` placeholder
      for the M6 setting; `game/tests/SfxTest.tscn`, QA `QaM26Test.tscn` (clip audit, spam at 2,000
      units, 0 bytes per play). Nobody could listen unattended: the owner's ears are the last check
      (STATE "For your review"). BUG-0087 (S3, a sound playing at quit leaks an ObjectDB warning).)_
- [x] Playable: the owner moves an army of 100 placeholder units around a generated map at 60 FPS.
      _(studio half, session 2026-10-06-2114, task M2-7: `--bench <seconds>` (`BenchRunner` + pure
      `ViewApi.BenchScript` / `FrameTimeStats`: box-select, order, four minimap corner jumps, zoom 20 / 60,
      A + click, three Shift-queued moves, H, S, looping every 10 s; one `bench:` line, exit 0), `--vsync on|off`,
      `UnitViews.BlendFacing` (short-arc `PrevFacing` → `Facing`), a four-shot screenshot set (`MarchShot.tscn`).
      Measured windowed, HUD + sound, default 12 / 8 map, 100 units per player, 60 s: avg 0.72 ms / p99 1.31 ms
      per frame with vsync off (1,372 fps); 59.5 fps on a 60 Hz display with vsync on; 1,000 per player at zoom 60:
      avg 2.26 / p99 3.34 ms. Pinned by `BenchTest.tscn` (avg < 16.7, p99 < 33 ms, windowed only). The owner's own
      playtest is the last check (STATE "For your review"). Open for the M2 hardening session: BUG-0101 (S3, the
      bench's "order across" only moves the army ~17 m), BUG-0102 (S3, `fps` field biased low on short runs),
      BUG-0103 (S4 nits).)_

_All 10 criteria met by session 2026-10-06-2114. End-of-milestone hardening M2-H2 in session
2026-10-07-0800 (the bench marches across the map and reports a true `fps`, start blocks stand in a
clearing, sounds stop at quit, the overlay label allocates only on change, 2 x 2 minimap dots, export
notes for M6; BUG-0069 / 0070 / 0083 / 0084 / 0085 / 0086 / 0087 / 0088 / 0101 / 0102 / 0103 fixed).
`studio/qa/coverage.md`: every M2 row ✅ for Unit, Invariant fuzz and Determinism (audio's Determinism
"—": `Sfx` has no sim reference); no open S1/S2. Signed off by the Producer on 2026-10-07 under autopilot
(`stop_at_milestone_end: no`); open S3/S4 listed in the retro. The owner's own playtest (STATE "For your
review") stands as feedback, not as a gate. Owner may revisit._

## M3 — Economy & buildings

**Done when:**

- [x] Gold mines and trees as resource entities; tree depletion updates the nav grid.
      _(session 2026-10-06-1255, task M3-1: `common/resources.json` (`tree` 1 x 1 wood, `gold_mine`
      2 x 2 gold; amounts from `rules.json`), `ResourceStore` (SoA, generational handles, capacity
      `SimConfig.ResourceCapacity` 4,096; internal `Spawn` / `Take`), `NavFlags.Resource`, depletion
      reopens the cells and bumps `NavGrid.Version` once; `ResourcePlacer` from `MapGenParams.Forests`
      / `GoldMines` (default 0; every passable cell stays reachable); `NavGrid.Version` and the store
      in `StateHash`; replay format 3; CLI `--forests` / `--mines`. QA oracle 246 seeds; 500 units with
      forests 0.82 ms per tick. Open: BUG-0073 (every fall invalidates every field: M3-2 design),
      BUG-0074 (placer assumes 1 x 1 trees), BUG-0075 (interior fell leaves a hollow: M3-2 gather rule),
      BUG-0076 (nits).)_
- [x] Worker gather/return loop with automatic drop-off choice.
      _(session 2026-10-06-1503, task M3-2: `Command.Gather` (unit order, Shift-queueable; the node
      is resolved at apply), `EconomySystem.Run` in tick phase 4 (states `Gathering` / `Returning`;
      walks use the Move machinery to the nearest passable cell 4-adjacent to the footprint; reach
      1.25 m; retry every 20 ticks = the queue at a mine's edge), rates / carry / search radius from
      `rules.json`, drop-off = the nearest own `dropOff` building by straight line, depleted-node rule
      within 20 m, exposure rule (only a node with an open 4-neighbour is gathered: closes BUG-0075),
      `BuildingStore` + dev `SpawnBuilding` with `buildings.json` (Town Hall slot only), per-player
      `World.Gold` / `Wood` in the hash, CLI `--workers`. QA: conservation exact over 8 seeds x 3,000
      ticks of hostile commands; exposure fuzz 246 maps / 2,866 falls / 0 pockets; 500 marchers + 50
      workers 0.92 ms a tick. Open: BUG-0077 (S3, a building dropped on a marching column makes half
      give up: M3-2b), BUG-0078 (S3, exposure counts a sealed pocket's cell: M3-3), BUG-0079 (S4
      nits), BUG-0073 (S3, continuous felling starves fields: M3-2b, next).)_
- [x] Building placement (ghost preview, validity), construction with multiple builders, repair.
      _(sim half, session 2026-10-06-2114, task M3-3: `World.CanPlace(player, type, anchor, out PlacementError)`
      (public, read-only, the one rule `Build` applies; reasons in order: UnknownType, WrongFaction, OffMap,
      Blocked, SealsGround, UnitInTheWay, CannotAfford, StoreFull), the never-seal rule `Map/SealCheck`
      (closes BUG-0078 for placements; the dev `SpawnBuilding` obeys it too), `Command.Build` (kind 8; pays and
      places a site, or joins an own site at that anchor; own non-holding units in the footprint are set down on
      the nearest free cell outside), construction `WorkNeeded = 3 x buildTicks`, n builders add n + 2 a tick
      (docs/02 `t x 3 / (n + 2)`: a House takes 400 / 300 / 200 / 120 ticks with 1 / 2 / 4 / 8), `Command.Cancel`
      (kind 9, floor refund of the unbuilt fraction, an opening change), `Command.Repair` (kind 10; `rules.json`
      `repair.rateFactor` 0.5 / `costFactor` 0.25, fixed-point accumulators, stops when the player can't pay),
      `BuildingStore.Damage` seam (0 hp frees, an opening change), hash + replay (format 3; golden regenerated
      for `data-hash` only). QA: never-seal matches an independent flood oracle on ~9,700 verdicts, conservation
      exact, twins identical, 0 B/tick; felling + building + 500 marchers 1.17 ms avg. The ghost preview and HUD
      are the view's part (M3 HUD criterion). Open: BUG-0091 (S3, refused Builds each run the seal flood),
      BUG-0093 (S3, a cancelled enclosed site leaves a pocket: BUG-0078's symptom via Cancel), BUG-0092 (S4 nits).)_
- [ ] Production queues (5 slots), rally points, population and cap, refunds on cancel.
- [ ] Age II research and unlocks; Forge upgrades.
- [ ] Malazan and Whirlwind factions fully defined in data (units, buildings, techs).
      _(In progress, data track: units landed in M1-2 (7 per faction); all ten buildings per faction in
      session 2026-10-06-1744, task D1 (`Content/BuildingContentTests`, QA `QA/Content/BuildingRosterQaTests`;
      descriptions carry "needs Age II" in text only until a `requires` field exists: BUG-0090, S4). Session
      2026-10-06-2114, task D2: the faction pages are the design source for every unit and building number
      (`Content/UnitContentTests` A-G pin `units.json` to the pages' Units tables; the pages' Buildings tables
      gained hp / armor / cost / build time / footprint / provides / requires, pinned by `BuildingContentTests.G`;
      QA `QA/Content/UnitRosterQaTests`); no data value changed. Techs wait for the `techs.json` schema (M3-5).)_
- [ ] HUD: resource bar, selection panel, command card with grid hotkeys, worker build menus.
- [ ] Playable: the owner builds a full Malazan base and reaches Age II.

## M4 — Combat, fog, abilities

**Done when:**

- [ ] Attack, attack-move, chase, retaliation, target acquisition priorities.
- [ ] Damage formula with type × class table and bonuses; unit tests include the worked example.
- [ ] Projectiles with travel time and misses; splash with falloff; friendly fire.
- [ ] Death, corpses, building destruction and rubble.
- [ ] Three-state fog of war per player; high-ground vision rule (low ground can't see up; attacker reveal); terrain fog shader; building ghosts.
- [ ] Ability system (target ground, self/aura, summon) and status effects; zones.
- [ ] Stealth and detection system (tested now, even though Shadow arrives in M8).
- [ ] Telas Fire, Sapper Sharpers + Cusser, Sandstorm, Zealot passives all work.
- [ ] Scenario tests for the counter triangle (Line beats Shock, Shock beats Ranged, Ranged beats
      Light, Siege beats buildings).
- [ ] Playable: Malazan vs. Whirlwind armies fight in a sandbox with fog on.

## M5 — AI opponent

**Done when:**

- [ ] `PlayerView` fog-filtered facade; AI cannot read hidden state (test enforces it).
- [ ] Build order executor driven by `ai.json`; economy, production, military, ability, scout managers.
- [ ] Expansion, defense, attack waves, retreat, rebuilding; scouts ramps and high ground before attacking up.
- [ ] Easy / Normal / Hard per [02 AI](02-game-design.md#ai-opponent).
- [ ] AI-vs-AI headless test: a 20-minute match completes without errors and one side wins.
- [ ] Playable: the owner can win on Easy and lose to a Whirlwind AI on Hard.

## M6 — Game shell & real art

**Done when:**

- [ ] Main menu, skirmish setup (map, 2-4 slots, faction, difficulty, color, team), pause menu.
- [ ] Victory/defeat detection and post-match stats screen.
- [ ] Save/load and replay playback.
- [ ] Settings: resolution, window mode, VSync, quality, volumes, keybinds.
- [ ] Art look test done (KayKit vs. alternatives, see [04](04-art-pipeline.md)); real models and
      animations for Malazan and Whirlwind; licensing log filled in.
- [ ] Real SFX and placeholder music; alerts with sound.
- [ ] 3 maps (Raraku, Pale Hills, Vathar Crossing) plus the procedural generator in skirmish setup.
- [ ] `tools/export.ps1` produces a Windows zip that runs on a machine without Godot or .NET installed.
- [ ] Revisit the .NET version (see [03 Platform](03-technical-design.md#platform-and-versions)).
- [ ] Playable: a friend can unzip it and play a match without help.

## M7 — Teblor

- [ ] Scale and per-unit population systems proven (1.5× visuals, 2-3 pop, half-step pop support).
- [ ] Giant armor class in play; regeneration.
- [ ] War-dog packs (multi-spawn queue item, Hamstring), Blood-oil Frenzy with aftermath slow.
- [ ] Teblor AI build order; models and animations; balance pass vs. Malazan and Whirlwind.

## M8 — Shadow

- [ ] Forest stealth and Shadow Archer stand-still stealth; stealth visuals for owner and enemies.
- [ ] Hounds (pack bonus, 7-alive limit), Aptorian Stalker, Shadow-step, Summon Wraiths.
- [ ] Shadow AI with ambush behavior; models; balance pass (watch early-game stealth, see faction page).

## M9 — Tiste Andii

- [ ] Darkness zones with double regen inside; regeneration bonus.
- [ ] Flying units (Great Raven): no pathing, targeting restrictions, flying view height.
- [ ] Champion with 2-alive limit; elite balance (cost, pop half-steps).
- [ ] Andii AI; models; full five-faction balance pass.

## Later, maybe

Not scheduled. Each needs a deliberate decision to start (see [01 Scope caps](01-vision.md#scope-caps)).

- Hero units (Rake, Karsa, Quick Ben, Coltaine... renamed for release)
- Walls and gates
- In-game map editor UI
- Campaign / scripted scenarios
- Vertex-animation-texture rendering for very large armies
- Public release on itch.io (requires the rename pass from [01](01-vision.md#ip-and-naming-policy))

## Retro template

Add one section per completed milestone below.

```
### Mx retro (YYYY-MM-DD)
- What shipped:
- What was harder than expected:
- What to change in the process or the plan:
- Decisions made (also recorded in 01-vision.md):
```

## Retros

### M0 retro (2026-10-03, signed off by the owner the same day)
- What shipped: `RtsGame.sln`; `sim/Rts.Sim` (net8.0, nullable, warnings-as-errors, no refs)
  with `SimInfo.Version = "0.0.1"`; `sim/Rts.Sim.Tests` (xUnit, 4 tests incl. architecture
  guards for the sim/Godot split and forbidden APIs); `game/` Godot 4.7.2 C# project whose
  `Main.tscn` prints `Rts.Sim 0.0.1`; `tools/qa/smoke.ps1` build+import+boot gate. One studio
  session, one dev round, QA PASS_WITH_ISSUES (S3 + S4 only).
- What was harder than expected: headless Godot neither compiles C# nor fails (exit code) when a
  script cannot load, so "smoke exits 0" is a weak gate on its own (BUG-0001).
- What to change in the process or the plan: use `tools/qa/smoke.ps1` (or a game-dev-owned
  equivalent) as the smoke gate in the definition of done; settle the `.sln` Release mapping
  for the game project before `tools/export.ps1` (BUG-0002, M6).
- Decisions made (also recorded in 01-vision.md): none.

### M1 retro (2026-10-06, signed off by the Producer under autopilot)
- What shipped: the whole simulation core with no graphics: entity stores with generational
  handles, seeded RNG streams, `SimMath`, a 20 Hz tick with a command queue that refuses malformed
  commands at the door; the data loader and validation for `game/data/`; terraced maps (3 levels,
  cliffs, walled ramps, sealed pockets) with a nav grid and spatial hash; flow fields with a hashed
  LRU cache and a 2-builds-per-tick cap; steering, separation, crowded arrival, give-up, shoving,
  walk-back, detours, enemy and holder plugs; `Stop` / `HoldPosition` / `AttackMove` and Shift-queues
  of 8; replays (format 2) with a golden file and determinism tests; the 500-unit perf criterion
  (0.63 ms vs 4 ms); the headless CLI (`tools/Rts.Cli`) and `PeekCached` for the view. 14 sim sessions
  (2026-10-03 to 2026-10-06), 3 of them hardening; 1,841 non-Perf tests green, 80 Perf.
- What was harder than expected: crowd movement. Four sessions (M1-4d-1 to M1-4d-3 plus M1-9) went
  into units that stop short, livelocks under field-cache churn, plugs that leak, and perf at 2,500
  units; the Producer's crowd targets (60% / 50% arrivals to 4 nearby points) were not met (51% /
  34%) because flow fields don't know where units stand. QA found an S1 livelock and several S2s
  in-session; every one was fixed before merge.
- What to change in the process or the plan: (1) a "crowd cost" in flow fields (BUG-0028 / 0032)
  is the real fix for crowd quality; decide after the M4 combat sandbox shows whether it matters in
  play, not before. (2) Fitted crowd bounds make any movement change expensive (BUG-0046's sort was
  built, measured and shelved because it re-rolled three bounds); prefer median / aggregate bounds
  over per-seed ones in new rows. (3) Perf rows fail from CPU contention when both tracks run QA at
  once: a Perf failure counts only when it fails alone (now a standing rule). (4) Keep the public
  setup API additive while the view track builds against it: it worked for five sessions.
- Open S3/S4 carried into M3 (none block): BUG-0046 (wall clips in slot order; known limit),
  BUG-0050 (4.7% random-goal give-ups vs 3%; known limit), BUG-0071 (shove-pass plug cache can depend
  on query order under a stale hash; deterministic), BUG-0072 (CLI `--record` pre-check misses invalid
  file names), BUG-0005 (per-player command buckets, before M5), BUG-0008 / 0010 (loader nits, M3 data
  task), BUG-0023 / 0025 / 0026 (field cache limits, documented), BUG-0028 / 0032 (crowd targets),
  BUG-0040 part 2 (M5 design note), BUG-0002 (`.sln` Release mapping, M6).

### M2 retro (2026-10-07, signed off by the Producer under autopilot)
- What shipped: the game window. A 3D terraced map (plateaus, cliff walls, ramps, sun and sky) with
  placeholder trees and gold mines; the RTS camera (55°, edge pan, keys, middle-drag, 20-60 m zoom,
  clamped); 200 capsule units in faction colours that walk smoothly between ticks and turn the short
  way round; click / box / Shift / double-click / Ctrl selection, control groups, Tab subgroups; every
  order key (right-click, A, S, H, Shift-queue) through one order path; a 220 px minimap with click-to-
  jump, right-click orders, resource layer and rimmed 2 x 2 dots; the F12 overlay (nav grid, flow arrows,
  tick graph); generated select / command sounds with `--mute`; `--screenshot`, `--bench`, `--vsync` and a
  measured 60 FPS with 10x headroom (0.73 ms a frame at 100 units per player). 9 view sessions
  (2026-10-05 to 2026-10-07), 2 of them hardening; 18 headless Godot test scenes; every M2 coverage row
  ✅ for Unit / fuzz / Determinism.
- What was harder than expected: measuring honestly without a human. The benchmark's first "order across"
  step moved the army 15 m (BUG-0101), its `fps` was biased by the load second (BUG-0102), and the default
  start block stood in a forest and on a cliff edge (BUG-0085); all three were caught by QA's independent
  measurements rather than by the pinned tests, and all three fixed in the hardening session. Godot's
  headless runs hide failures (exit 0 on script errors, leak warnings only at exit), so the smoke script,
  the `TEST PASS` banners and log greps carry the verdicts.
- What to change in the process or the plan: (1) pin visual and benchmark claims to a property with
  an independent oracle (pixel reads, distance travelled, frames / seconds), not to a number copied from
  the first run; (2) keep ViewApi pure and read-only (the hash-twin tests proved it every session; no view
  task ever changed a sim result); (3) the owner's playtest and listening are still owed (STATE "For your
  review"), and a milestone's "Playable" criterion is ticked on measurements until then; (4) numbered
  acceptance criteria belong in `studio/handoff.md` at PLAN time (missed once in session 2114).
- Decisions made (also recorded in 01-vision.md): placeholder colours and debug start blocks until M6;
  faction colour as team colour; clusters, not formations; 12 forests / 8 mines on the default map; F12
  overlay with one goal's arrows; generated tones instead of sound files; the 2 x 2 rimmed dot (owner may
  pick another style, BUG-0069); the bench's far target at 0.85 x width.
- Open S3/S4 carried into M3 (none block): BUG-0104 (S3, the 10 s bench moves seed 21's army 19.1 m
  against a 20 m bound fitted to seed 1), BUG-0105 (S4, minimap refresh row at 92-94 % of its 0.3 ms
  limit, two stale doc figures), BUG-0025 (sim, overlay arrows late with 64+ goals), the cosmetic notes
  in STATE's view backlog (ramp-end slope, no border skirts, edge-pan suppression unreachable, export
  hygiene for M6).
- Decisions made (also recorded in 01-vision.md): clusters instead of formations; 1 s give-up;
  cliff strips 2 m; ramps 6 x 8 m; 2 field builds per tick, cache metadata hashed; own standing units
  soft, enemies and holders hard; plugs up to 32; replays bound to the exact data content hash;
  malformed commands refused at `Enqueue`; maps over 256 unsupported; BUG-0046 kept as a known limit.
