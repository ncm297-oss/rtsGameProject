# Handoff: brief for the current / next session

Written by the Producer at the PLAN of session 2026-10-06-1503 (4 / 8 today). Base 94a5061 (= `origin/main`).
Both tracks GO, both feature sessions (feature counters after this session: sim 2 / 4, view 2 / 4).

## Where we are

- `main` at 94a5061: build 0 errors, 1 warning (CS8602 `game/tests/DebugOverlayTest.cs:173`, BUG-0084);
  non-Perf tests 2135 passed / 13 skipped / 0 failed (5 m 3 s); smoke PASS (`Rts.Sim 0.0.1`, no ERROR).
  CLI twin `run --seed 1 --units 200 --ticks 300 --forests 12 --mines 8` printed identical hashes twice
  (`forests 12 trees 287 mines 8`). Inbox: nothing new.
- Open bugs 22 (S3 14, S4 8), none S1/S2. Roadmap: M0 Done, M1 Done, M2 7 / 10 (view), M3 1 / 8 (sim).
- Bug numbers this session: **sim from BUG-0077, view from BUG-0085.**
- Shared-file rule: both tracks append to docs/03 (sim: "Economy implementation" + "Data format"; view:
  "Rendering and presentation"), docs/01's change log (one row each, append) and `studio/qa/coverage.md`
  (sim: Economy row; view: Presentation row). Neither track adds or changes a member of `FlowFieldCache`
  (QA pins its surface; BUG-0073 is the *next* sim task, not this one).
- Sim API the view relies on (keep additive): `World.Resources` spans, `GameData.Resources`,
  `NavGrid.Version`, `MapGenParams.Forests` / `GoldMines`, `SimConfig.Map`, `Command.SpawnUnit`.

## Sim track

### Current session plan: M3-2 — Worker gather / return loop with automatic drop-off (feature, QA full)

**Goal.** Workers gather gold and wood: ordered onto a mine or tree they walk there, fill up at the data
rate, carry the load to the nearest own drop-off, deposit into the player's totals and go back, with no
further orders. This is M3 criterion 2 and the first thing the economy *does*; it also fixes BUG-0075 by
rule (only exposed nodes can be gathered, so no hollow ever forms).

**Scope.**
- Data: `game/data/factions/<faction>/buildings.json` (new, required) with the **Town Hall slot only**
  (`malazan_garrison_keep`, `whirlwind_holy_camp`, names from the faction pages); fields from docs/02's
  Buildings row: `id, displayName, description, slot ("town_hall"), footprint {4, 4}, hp 2400, armor 5,
  cost {gold 275, wood 275}, buildTime 90, popProvided 10, dropOff true`. Loader + validation (snake_case,
  unique across factions, slot known, footprint 1-4, bounds via `DataLimits`, unknown fields are errors),
  `GameData.Buildings` dense ids in ordinal order, `FindBuilding`, `ContentHash` covers every field. Other
  slots and `trainedAt` / `requires` resolution stay for M3-3 / M3-6.
- `BuildingStore` on `World.Buildings` (SoA, generational handles, LIFO free list, capacity
  `SimConfig.BuildingCapacity`, optional, default 256): `Alive, Generation, Owner, TypeId, Cell` (anchor),
  `Hp` (= max for now). Read-only outside the sim. Footprint cells get `NavFlags.Building` (16) `| Blocked`,
  cost 255, `NavGrid.Version` +1 once per building; buildings are walls to movement by being blocked cells.
  Placed only through a dev/test command `Command.SpawnBuilding(player, typeId, position)` (like
  `SpawnUnit`; reuses `TypeId` / `Position`, no new `Command` field, replay format stays 3): refused at apply
  (dropped, no throw) for an unknown type, a footprint that is not open ground (same rule as a resource node:
  passable, no ramp, one level, no node / building / cliff / border), or a live unit whose centre lies in it.
  Door: the kind is known; the queued flag on it is malformed (like `SpawnUnit`). Construction, ghosts,
  destruction, repair: M3-3.
- Per-player totals: `World.Players` (or `Gold[]` / `Wood[]` on `World`), ints, start at `rules.json`
  `startingGold` / `startingWood`; in `StateHash`.
- `Command.Gather(player, unit, position[, queued])`: the position is any point inside a node's footprint;
  the sim resolves the node at apply. Dropped (no throw) for a dead / foreign handle or a unit whose slot
  isn't `worker`. No live node at that cell → the depleted-node rule from that cell (below); nothing found →
  dropped. Shift-queued Gather goes through the existing queue (kind + position fit `QueueKind` /
  `QueuePosition`); popping it is the same as an unqueued one.
- Worker state machine (`UnitState.Gathering`, `Returning`) in **tick phase 4** (`EconomySystem.Run`,
  after commands, before orders and movement), per-slot fields on `UnitStore` (reset on Alloc / Free, all in
  `StateHash`): `GatherNode` (handle), `GatherProgress` (float), `Cargo` (int), `CargoKind`.
  - **Exposure rule (BUG-0075):** a node is *exposed* when at least one passable cell is 4-adjacent to its
    footprint. Only exposed nodes can be gathered. Interior trees become exposed as the forest is eaten
    from the outside, so felling never creates an unreachable cell.
  - **Reach:** a worker gathers or deposits when the distance from its centre to the footprint rectangle is
    at most `EconomyConstants.Reach` (1.25 m; engine geometry like `CellSize`, documented). To get there it
    uses the Move machinery with the goal on the nearest (Euclidean, ties by cell index) passable cell
    4-adjacent to the footprint; a worker that arrived or gave up out of reach re-issues that walk every
    `EconomyConstants.RetryTicks` (20) picking the nearest stand cell again: that is the "workers queue at
    a mine's edge" rule (docs/02: no hard cap).
  - **Gathering:** each tick in reach, `GatherProgress += rate` (`RulesDef.GoldPerTick` / `WoodPerTick`);
    each time it passes 1, `Take(node, 1)` and `Cargo += taken`; `Cargo == workerCarry` (10) → Returning.
    The node loses exactly what workers gain (conservation).
  - **Returning:** drop-off = the player's nearest live `dropOff` building by **straight-line** distance
    (docs/03 "Economy implementation"), chosen when the trip starts; in reach → player total += Cargo,
    Cargo = 0 → Gathering at the same node if it is alive and exposed, else the depleted-node rule. No own
    drop-off → Idle, cargo kept.
  - **Depleted / unexposed node rule (docs/02):** the nearest exposed node of the same resource kind within
    `nodeSearchRadius` (20 m) measured from the old node's centre (ties by slot); none → Idle, cargo kept.
  - **Cargo rules (Producer decisions, document them):** a `Move` / `Stop` / `Hold` keeps the cargo (a
    later Gather of the same kind continues from it); a Gather of a *different* kind discards the cargo
    (AoE II rule; smallest rule, owner may revisit). `ReturnCargo` as an explicit order is M3-3 / HUD.
  - Nothing allocates per tick; the search for stand cells and drop-offs is a bounded scan (footprint ring
    ≤ 20 cells; buildings ≤ capacity), no LINQ.
- CLI: `run --workers N` (per player, 0-200; requires `--forests` or `--mines` > 0, else exit 1 with a
  message): spawns one Town Hall per player at the nearest open 4 x 4 spot to its start block (toward the
  map centre) and N workers (the faction's worker type) next to it, ordered to gather: odd ones the nearest
  exposed mine, even ones the nearest exposed tree. Header gains `workers N`; a final line per player
  `player P gold G wood W`. Replays record the commands as usual.
- Fold in only if ≤ 20 lines: `UnitStore.PrevFacing` (view request 4; set where `Facing` is set; derived,
  not hashed).
- **OUT of scope:** BUG-0073 (open-only grid changes keeping fields usable: the **next** sim task, M3-2b;
  `FlowFieldCache` and the build pass are untouched here), construction / placement validity for players,
  Camp drop-off, `ReturnCargo`, Whirlwind's 15 % gather bonus (StatModifiers, M3-6), production, pop,
  worker animation / view, depletion events, BUG-0074, start locations.
- **Size:** ≤ 1,500 changed lines excluding tests; tests ≤ 1,000 lines. If the budget is tight, cut the
  CLI flag before anything else and say so.

**Acceptance criteria.**
1. Both `buildings.json` files ship the Town Hall; `DataValidationTests.ShippedData_LoadsWithNoErrors`
   passes; new validation rows (missing file, bad slot, footprint 0 / 5, duplicate id across factions,
   unknown field, non-bool `dropOff`) each produce exactly one error; `DataContentHashTests` covers every
   `BuildingDef` field.
2. `BuildingStoreTests`: slots, stale handles, generation, capacity refusal without throw; `SpawnBuilding`
   refusals (unknown type, cliff / ramp / border / node / building / two-level footprint, unit inside) leave
   the grid and hash unchanged; a success sets `Building | Blocked` on exactly the footprint and bumps
   `Version` once; a Move through the footprint routes round it.
3. `GatherCommandTests`: accepted for an own worker on a live node; dropped for a dead / foreign handle, a
   non-worker, and a cell with no node and nothing within 20 m; a cell with no node but a node within 20 m
   gathers that node; Shift-queued Gather after a Move runs when the Move ends.
4. `GatherLoopTests` (hand maps): one worker, one mine, Town Hall 8 m away: cargo reaches 10 after 14.3 s
   ± 1 tick in reach (wood: 16.7 s), the mine's `Remaining` dropped by exactly the cargo, the worker
   deposits and the player's gold rises by 10, then it is back in reach of the mine with no new order;
   after 120 s of game time gold ≥ 200 + 50. Five workers on one mine: ≥ 4x one worker's income over 120 s.
5. Exposure: an interior tree of a 3 x 3 grove is never gathered while its four 4-neighbours stand (the
   worker is redirected to an exposed tree of the grove); a worker set loose on the grove fells all 9 and
   after every fall every passable cell is reachable; QA's skipped `OrderIntoAFelledGroveMiddle_*` row is
   replaced or un-skipped as the regression test and BUG-0075 closes.
6. Depletion: when a mine runs out, its workers move to the nearest exposed mine within 20 m (test: two
   mines 12 m apart) and go Idle keeping cargo when none is within 20 m; a worker carrying wood ordered onto
   gold has `Cargo` 0 (documented).
7. Crowding and cost: 20 workers on one 2 x 2 mine all deposit at least once within 60 s and none is stuck
   forever (each is Gathering or Returning at tick 1,200); `AllocationTests`: a tick with 50 gathering
   workers and 200 marching units allocates 0 bytes; Perf (`Category=Perf`): 500 moving units + 50
   gathering workers average tick < 4 ms; 200 workers gathering alone < 1 ms.
8. Determinism: twin runs (50 workers + 200 marchers, 12 forests / 8 mines, 3,000 ticks) hash-identical
   every tick; a replay with `SpawnBuilding` and `Gather` commands records and plays back; the golden is
   regenerated **once** in the same commit (reason in the message) only if the hash composition changed,
   with the 15-checkpoint trajectory proof as in M3-1; replay format stays 3.
9. CLI: `run --seed 1 --units 50 --workers 10 --forests 12 --mines 8 --ticks 2000` twice prints identical
   hash lines and the same `player 0 gold G wood W` line with G > 200 and W > 200; `--workers` without
   resources exits 1; `CliTests` cover both.
10. docs/03: "Economy implementation" gets "Implementation (M3-2)" (states, exposure rule, reach, retry,
    straight-line drop-off, cargo rules, data shape, phase 4, constants), the "gather slot list" bullet is
    corrected to the edge-queue rule, the two BUG-0075 sentences ("Navigation grid", "Flow fields") get the
    condition "as long as nodes are only removed from exposed sides, which the gather rule guarantees";
    "Data format" lists `buildings.json`; "Tick model" phase 4 gets the M3-2 note; docs/01 change-log row
    (Producer decisions: Town-Hall-only data, dev `SpawnBuilding`, 4-adjacent exposure, reach 1.25 m,
    straight-line drop-off, cargo discard on kind change). `SimConfig` additive only; `Simulation`,
    `FlowFieldCache`, `Command` layout and `ViewApi` unchanged.

**Design references.** docs/02 "Economy" (rates, carry, drop-off, 20 m search, mine crowding) and
"Buildings" (Town Hall row, placement rule); docs/03 "Tick model" (phase 4), "Entity model"
(`BuildingStore` pattern), "Economy implementation" (state machine, straight-line drop-off), "Orders and
unit states" (`Gather(node)`, states `Gathering` / `Returning`), "Implementation (M3-1)" (`Take`,
exposure vs hollows), "Determinism"; docs/factions/malazan.md and whirlwind.md building tables;
BUG-0075 (rule), BUG-0073 (do NOT fix here; measure).

**Tests required.** Listed per criterion; plus `StateHashTests` rows for every new field (totals, cargo,
progress, node handle, building store), `UnitStoreTests` reset rows, `ReplayFormatTests` unchanged format,
`CommandDoorFuzz` extended with the two kinds.

**Constraints most at risk.** No allocation in phase 4 (stand-cell / drop-off search by scan, no LINQ);
no hard-coded stats (everything from `rules.json` / `buildings.json`; only `Reach` and `RetryTicks` are
engine constants and must be documented as such); determinism (ties by slot / cell index, no dictionary
iteration); `FlowFieldCache` untouched; absolute paths; the owner's main checkout is never written.

**QA focus (full).** Conservation invariant per kind every tick: Σ player totals + Σ cargo + Σ node
`Remaining` is constant and no `Remaining` goes negative (fuzz 8 seeds x 3,000 ticks, random Gather / Move
/ Stop / SpawnBuilding / SpawnUnit with hostile handles and cells). Exposure fuzz: random gather orders on
the 246-seed placement maps: after every fall every passable cell reaches every other, no interior node
ever taken. Crowding: 20 / 40 / 80 workers on one mine (nobody stuck forever, income monotone). Races: node
dies while a worker is walking to it, while another is in reach, while depositing; drop-off spawned after
the order; two players' workers on one mine; Gather onto a mine on another plateau (walk via the ramp);
Gather with the node 0.1 m outside reach of every stand cell on a cramped map. Door and apply rules for
both new kinds; replays with them. Hash twin 50 workers + 200 marchers; 0 bytes; Perf rows alone. Measure
BUG-0073 with real cadence (20 workers chopping, 32 marching groups): report the longest field wait and the
tick cost, and **do not re-file it** (it is the next sim task); file S2 only if gathering itself breaks.
`FlowFieldCache` surface pin must still pass. Bug numbers from BUG-0077.

### After this task (sim)
1. **M3-2b (QA full, small):** BUG-0073 by the decided route: `NavGrid.BlockVersion` bumps on closing
   changes only (node / building placed); a field whose `BlockVersion` matches is *usable* (never points
   into a blocked cell) even when its `Version` is stale; the build pass walks usable-stale groups this tick
   and refreshes stale fields under the cap after misses, oldest first; a unit standing on a cell with
   `NoDirection` in a usable field waits like a missing field; `PeekCached` returns the field units follow
   (documented); `BumpVersionForTests` is a closing bump so the view's never-stale tests hold; both
   versions hashed; coordinate the QA surface pin (no new public members). Then BUG-0074 if cheap.
2. M3-3 building placement + construction (the store exists), M3-4 production queues, M3-5 Age II,
   M3-6 full data.
3. Debt for the next sim hardening (2 feature sessions away): BUG-0071, BUG-0072, BUG-0074, BUG-0076,
   BUG-0008 / 0010 (or M3-6), BUG-0005 before M5, BUG-0025 / 0026.

## View track

### Current session plan: M2-3b — Trees and gold mines drawn as MultiMesh props; forests on in the match (feature, QA standard)

**Goal.** The game window shows the map's forests and gold mines, and the army is seen routing round
them. Completes M2 criterion 3 ("heightmap terrain mesh ...; trees and rocks as MultiMesh": rocks are
pure decoration with no sim footprint and are deferred to the M6 art pass, Producer decision).

**Scope.**
- `Match` / `SimRunner` build the map with resources: `SimConfig.Map = MapGenParams.Default with
  { Forests = 12, GoldMines = 8 }` (Producer defaults for the 128 map); launch flags `--forests N` /
  `--mines N` (0-64, parsed like `--units`, bad values refused with the existing message style) override;
  `--forests 0 --mines 0` gives the old bare map. The start-up log line gains `forests F trees T mines M`
  from `World.ResourcePlacement`.
- `PropsView` node (docs/03 scene layout) under `World3D`: one `MultiMeshInstance3D` per resource type.
  Placeholder meshes built in code (hard-coded colours like the terrain's, M2-1 rule): tree = dark-green
  cone (canopy radius ≤ 0.8 m, height ≈ 3.5 m) on a brown trunk cylinder (radius ≈ 0.15 m, 1 m), all
  inside its 2 m cell; mine = dark slate box covering the footprint (4 x 4 m, ≈ 1.6 m tall) with a smaller
  gold-tinted box on top. Sizes come from `ResourceDef.FootprintWidth / Height`, never literal cell counts.
- `ViewApi.PropLayout` (pure, read-only): from `World.Resources` spans, `GameData.Resources` and
  `TerrainHeight` builds per-type instance transforms (footprint centre on the terrain height; yaw from a
  simple multiplicative hash of the cell index for variety); relists only when `NavGrid.Version` differs
  from the last fill; a dead node is gone (compact count) or collapsed (scale 0): the dev's call, documented;
  0 bytes per frame when nothing changed; one refresh ≤ 1 ms at 4,096 nodes (Debug, this PC).
- Minimap: resource cells painted in the 5 Hz refresh under the unit dots (trees dark green, mines gold;
  constants in `MinimapRaster`), so a felled tree disappears within one refresh; refresh ≤ 0.3 ms at 2,000
  units + 4,096 nodes.
- Optional, ≤ 10 lines: `NavOverlayBuilder` tints `NavFlags.Resource` cells green instead of red.
- Scenes: `game/tests/PropsViewTest.tscn` (headless, prints `PROPS VIEW TEST PASS`), a windowed screenshot
  checked by the dev and QA (`-- --seed 1 --screenshot <abs path> --screenshot-after 2`).
- **OUT of scope:** rocks, biome vertex colours (M6), gather / worker feedback, depletion events (poll
  `Version`), HUD resource bar (M3 view side), any `game/data/**` or sim mutation, anything on
  `FlowFieldCache`, changes to the sim's start layout.
- **Size:** ≤ 800 changed lines excluding tests; tests ≤ 600 lines.

**Acceptance criteria.**
1. `& $env:GODOT --path game` builds a map with 12 forests and 8 mines (log line shows the counts);
   `-- --forests 3 --mines 2` and `-- --forests 0 --mines 0` are honoured; `--forests 65` and `--mines -1`
   are refused with a message; `LaunchOptionsTests` cover the rows.
2. `PropLayoutTests`: on hand maps and generated maps (seeds 1-5, 12 / 8), per-type instance count equals
   the alive count of that type; every transform sits at the footprint centre at `TerrainHeight` within
   1 mm; after `Take` to 0 (internal, from the test project) the node's instance is gone or scale 0 and
   exactly one relist happened for that `Version` change; no relist across 600 ticks with no change;
   0 bytes per frame when unchanged (`AllocationTests`-style probe).
3. Hash twin: a view-driven sim (props + minimap + unit views reading every frame) equals a bare sim every
   tick for 600 ticks with 12 / 8 and 2,000 units; `ViewApi` source scan: no store mutation, no
   `FlowFieldCache` member added.
4. `MinimapRasterTests`: tree and mine cells are painted their colours under dots, bare cells unchanged, a
   depleted node's cells return to the terrain colour on the next refresh; refresh cost row ≤ 0.3 ms.
5. `PropsViewTest.tscn` headless PASS (counts per type equal the store's, minimap pixel of a mine is gold);
   smoke PASS; all existing scenes still PASS.
6. Windowed screenshot (seed 1) inspected and described in the report: forests of cones, dark mines with
   gold tops, terrain visible between trees, units walking round a forest edge (send the army through a
   forest and screenshot it).
7. docs/03 "Rendering and presentation" gets "Implementation (M2-3b)" (defaults, flags, meshes, relist
   rule, minimap colours, the rocks decision); docs/01 change-log row (Producer decisions: 12 / 8 defaults,
   rocks deferred to M6, placeholder colours); roadmap text for criterion 3 is left for the Producer.

**Design references.** docs/03 "Rendering and presentation" (scene layout `PropsView`, minimap resource
markers, 60 FPS budget), "Implementation (M2-1)" (placeholder colour rule, `TerrainHeight`),
"Implementation (M2-4)" (minimap refresh budget 0.25 ms → 0.3 ms with nodes), "Implementation (M3-1)"
(`World.Resources`, `MapGenParams.Forests` / `GoldMines`, `Version` bump per node); docs/07 ownership
table (ViewApi read-only).

**Tests required.** `PropLayoutTests`, `MinimapRasterTests` resource rows, `LaunchOptionsTests` rows, hash
twin, allocation probe, `PropsViewTest.tscn`; QA: `QA/ViewApi/PropLayoutQaTests` (oracle over generated
maps, footprint edges, non-square maps, 4,096-node full store) and a `QaM23bTest.tscn` if needed.

**Constraints most at risk.** ViewApi reads only (`ReadOnlySpan` from the store; never `Get` /
`TryGetCached`); no `game/data/**` edits (sim-owned; it ships `resources.json` already); no literal
footprint sizes; absolute paths for every file write; never touch the owner's main checkout or the sim
worktree; `game/` has one known warning (BUG-0084), add none.

**QA focus (standard).** Instance oracle: recompute the expected transform list independently from the
store + heightmap on generated maps (seeds 1-20, 12 / 8 and the 64 / 64 caps) and compare after every
`Version` change in a 600-tick churn of `Take`s; a node on a level-2 plateau sits at the right height;
footprint centre of a 2 x 2 mine is on the cell corner (4 cells), not a cell centre; relist exactly once per
change; hash twin at 2,000 units; 0 bytes; minimap colours and refresh cost at 4,096 nodes; `--forests` /
`--mines` parsing extremes; frame time with 4,096 props at zoom 60 (report; budget: 60 FPS stays). Bug
numbers from BUG-0085.

### After this task (view)
1. M2-6 placeholder audio (QA light): generated tones for select and command, no downloads.
2. M2-7 playable check 100 units at 60 FPS (+ `PrevFacing` blending if it landed) → M2 end-of-milestone
   hardening (BUG-0069, BUG-0070, BUG-0083, BUG-0084, export hygiene) → M2 sign-off.
3. M3 view side: HUD resource bar (needs `World` player totals from M3-2), selection panel, command card,
   build ghosts (M3-3), worker / gather feedback.

## Watch-outs

- Perf rows fail from CPU contention while the other track's QA runs: a failure counts only when it fails
  again alone.
- Both tracks landed ~2x their size budget last session, mostly tests: this time the budgets separate code
  and tests; the Producer judges against both.
- Sim and view both edit docs/03 and docs/01: different sections, append-only; the conductor merges sim
  first.
- The sim task adds `NavFlags.Building` (16): the view's overlay treats any `Blocked` bit as blocked, so
  nothing breaks; the view must not depend on the sim branch (it isn't on `main` until merge).
- `CLAUDE.md` is the owner's file: still says "Current milestone: M1" and has no CLI line.
- Builders: absolute paths only; never touch the owner's main checkout. Remote Control unavailable.
