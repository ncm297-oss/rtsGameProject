# Handoff: brief for the current session

Written by the Producer at the PLAN of session 2026-10-06-1255 (3 / 8 today, scheduled). Base
a710e71 (= `origin/main`). Both tracks GO, both feature sessions (feature counters 0 / 4 -> 1 / 4).

## Where we are

- `main`: build 0 warnings; non-Perf 1882 passed / 10 skipped / 0 failed (4 m 10 s, Producer rerun
  at this PLAN); smoke PASS (Producer rerun). Inbox: nothing new.
- Open bugs 16 (S3 10, S4 6), none S1/S2. Roadmap: M0 Done, M1 Done, M2 6 / 10 (view), M3 0 / 8 (sim).
- Bug numbers this session: **sim from BUG-0073, view from BUG-0083.**
- Shared-file rule: both tracks append to docs/03, docs/01's change log and `studio/qa/coverage.md`
  in *different* sections (sim: Economy / Navigation grid / Data format / Known limits; view: Debug
  tooling / Rendering). Neither track adds a public member to `FlowFieldCache` (QA pins its surface).

## Sim track

### Current session plan: M3-1 resource entities (feature, QA full)

**Goal.** First economy slice: gold mines and trees exist in the sim as a data-driven resource entity
store. Trees (and mines) block movement; a depleted node frees its cells, bumps `NavGrid.Version`, and
the store plus `NavGrid.Version` join the state hash (closing the docs/03 known limit). This is the
foundation for M3-2 gathering and for the view's trees (M2 criterion 3).

**Scope (in).**
- `game/data/common/resources.json` (required file from now on): node *types* `tree` (resource
  `wood`, footprint 1 x 1) and `gold_mine` (resource `gold`, footprint 2 x 2), each with `id`,
  `displayName`, `description`, `resource` (`gold` | `wood`), `footprint {width, height}`. Amounts
  stay in `rules.json` as docs/02 has them: a placed tree holds `treeWood`, a placed mine
  `startMines.gold` (2,500) or `expansionMines.gold` (2,000; the placer takes an amount per node, M3-1
  uses the start value for every mine until start locations exist). `ResourceDef` in `GameData.Resources`,
  ids dense in ordinal order, validation like the other files (snake_case unique ids, kind in the set,
  footprint sides 1-4, unknown fields are errors, `DataLimits` bounds), `ContentHash` covers it.
- `ResourceStore` (`sim/Rts.Sim/Entities/`), SoA with generational handles like `UnitStore`: `Alive`,
  `Generation`, `TypeId`, `Cell` (anchor = lowest x,y cell of the footprint), `Remaining` (int),
  free list; capacity `SimConfig.ResourceCapacity` (new optional property, default 4096), arrays
  allocated once. Public read (`World.Resources`), **internal** mutation: `Spawn(typeId, cell, amount)`
  (refuses, never throws, when full or when any footprint cell is not a flat passable non-ramp cell)
  and `Take(handle, amount) -> int taken` (clamped; at 0 the node is freed, its cells reopened,
  generation bumped). Depletion is the primitive M3-2's gather system will call in tick phase 4.
- `NavFlags.Resource = 8` (always with `Blocked`); `NavGrid` gains internal `SetResource(cell)` /
  `ClearResource(cell)` that update flags + cost and bump `Version` (one bump per node freed, not per
  cell). Cliff / ramp / border cells never take a resource. Nothing else in NavGrid changes.
- Placement at world construction (after the nav grid, before any tick), from new `MapGenParams`
  fields **defaulting to 0** so every existing map, test and the golden's trajectories stay identical:
  `Forests` (count), `ForestMinTrees` / `ForestMaxTrees` (e.g. 12 / 40), `GoldMines` (count),
  `MineSpacing` (m, e.g. 24). Draws come from `RngStream.MapGen` *after* the heightmap's draws (the
  heightmap must be byte-identical with and without resources). Rules: nodes only on flat passable
  cells of one level that are not adjacent (8-neighbour) to a cliff, ramp or border cell; no overlaps;
  a forest grows from a seed cell as a connected blob; mines at least `MineSpacing` apart (greedy,
  bounded tries; a short count is allowed and reported); **after each node or forest a flood fill must
  still reach every passable cell, else that placement is undone** (depletion only opens cells, so no
  pocket can ever appear later). Iteration in cell / slot order; no Dictionary / HashSet.
- `StateHash`: add `NavGrid.Version` and the resource store (per slot, like units). Replay header
  carries the new `MapGenParams` fields (format 3; a format-2 file is refused with the existing
  `FormatVersionMismatch` code); golden regenerated **once** in the same commit with the reason
  (hash composition + format; trajectories unchanged: say which rows you checked).
- CLI: `run --forests <n> --mines <n>` (default 0), counts printed in the header line, recorded in
  the replay.
- Docs: docs/03 "Economy implementation" gets "Implementation (M3-1)"; "Navigation grid", "Data
  format" tables and "What ships today" updated; the known-limits entry "NavGrid.Version not
  hashed" removed; docs/01 change-log row for the Producer decisions (mine footprint 2 x 2, depleted
  mine cells reopen like trees, defaults 0, capacity 4096). `studio/qa/coverage.md` Economy row.

**Out of scope.** Workers, the gather / return loop, `Gather` command, drop-offs, buildings, HUD
resource bar, the view's tree / rock meshes (view track, next session), start locations and map
symmetry (M3-3 / M6), biomes, rocks (decorative, view-only), `PrevFacing` (request 4; fold in only if
you touch `UnitStore`, otherwise leave it).

**Acceptance criteria.**
1. `resources.json` ships `tree` and `gold_mine`; `ShippedData_LoadsWithNoErrors` passes; a missing
   file, bad `resource` kind, non-snake_case id, footprint 0 or 5, duplicate id and an unknown field
   each produce one `DataError` (tests that failed first); `DataContentHashTests` covers every
   `ResourceDef` field.
2. `ResourceStoreTests`: spawn / free / stale handle / generation / capacity refusal (no throw) /
   `IsAlive`; `Spawn` on a ramp, cliff, border or occupied cell is refused; `Take` clamps, frees at 0,
   returns 0 for a dead or stale handle; `AllocationTests`: 0 bytes for a tick with 4,000 live nodes
   and for 100 `Take`s including frees.
3. A spawned tree's cells read `Blocked | Resource` and `IsPassable` false; after the last `Take` the
   cells are `None`, passable, `Version` +1 exactly; a flow field cached for a goal behind a tree
   line is stale after the free and the next `Get` rebuilds it with a lower cost; scenario on a hand
   map: a unit ordered across a 1-cell tree line gives up (or detours) before, walks straight through
   after depletion, two-sim hash twin every tick.
4. `StateHash` changes when one node's `Remaining` differs, when a node is alive vs freed, and when
   `NavGrid.Version` differs with all else equal (`BumpVersionForTests`); `DeterminismTests` extended:
   `Forests = 12, GoldMines = 8`, scripted `Take`s between ticks, 2,000 ticks, equal every checkpoint.
5. Placement on seeds 1-20 (128 map, `Forests = 12, GoldMines = 8`): every footprint cell was a flat
   passable non-ramp cell of one level with no cliff / ramp / border 8-neighbour; no overlaps;
   flood-fill reach == passable count; mines >= `MineSpacing` apart; forest sizes in range; identical
   placement on a second world; heightmap levels and pre-placement flags byte-identical to
   `Forests = 0`; `RngStream.Combat` / AI streams untouched.
6. Defaults keep M1: `MapGenParams` defaults 0 / 0; every existing movement, crowd, scenario, CLI and
   replay test passes **without a bound change**; golden regenerated once, reason in the commit;
   `ReplayFormatTests` "every MapGenParams field" row covers the new fields; format-2 refused.
7. `CliTests`: `--forests` / `--mines` parsed, header prints the placed counts, record / play
   round-trips them, bad values (0x10, -1, 1e3) are usage errors.
8. Perf (Serial, report + guard): the 500-moving-units criterion row with `Forests = 12, GoldMines = 8`
   stays < 4 ms; hashing 4,096 resource slots < 0.05 ms; a tick with 2,000 live trees and no
   depletion costs at most +0.05 ms over none.
9. Docs as listed in scope; `SimConfig` / `Simulation` / `Command.*` / `FlowFieldCache` public
   surfaces unchanged except the new optional `ResourceCapacity`.

**Size.** ~1,000-1,300 lines incl. tests (design now specified; the nav-grid / hash interaction is
the new part). If over budget drop criterion 7 (CLI flags) first, then the perf reports; never 3-6.

**Design references.** docs/02 "Economy" (tree 100 wood, mines 2,500 / 2,000, drop-off and search
radius are M3-2) and "Map and terrain" (trees block movement, grouped into forests); docs/03 "Entity
model" (SoA, handles, `ResourceStore`), "Navigation grid" (`Version` increments whenever passability
changes; pockets sealed at build time only), "Flow fields" (fields tagged with `Version`, `PeekCached`
semantics), "Economy implementation" (nodes are entities with a remaining amount; the gather slot
list is M3-2), "Data format" (unknown fields are errors, `DataLimits`, ordinal ids, `ContentHash`),
"Tick model" phase 4, "Known limits (M1)".

**Constraints most at risk.** No per-tick allocation (placement and flood-fill scratch are
construction-time; `Take` and hashing allocate nothing); RNG only through the MapGen stream, in cell
order; no literal amounts or footprints in C# (data only; `DataLimits` holds schema bounds); no
public mutators (the view may only read); keep the setup API additive; regenerate the golden in the
same commit and say why; docs in the same commit.

### After this task (sim)
M3-2 worker gather / return loop (Gather command, worker state machine, drop-off choice; needs a
Town Hall or Camp as a drop-off: a minimal `BuildingStore` with pre-placed drop-offs, or M3-3 first);
M3-3 building placement + construction; M3-4 production queues; M3-5 Age II; M3-6 full data. Debt
for the next sim hardening (3 feature sessions away): BUG-0071, BUG-0072, BUG-0008 / 0010 (or fold
into M3-6), BUG-0005 before M5.

## View track

### Current session plan: M2-5 debug overlay (feature, QA standard)

**Goal.** The developer overlay from docs/03 "Debug tooling": one key shows the nav grid on the
ground, the flow arrows of the selected units' goal, a tick-time graph and entity counts; off by
default and free when off. Completes M2 criterion "`--screenshot` debug flag; debug overlay (nav grid,
flow arrows, tick time)".

**Scope (in).**
- Input action `debug_overlay` bound to F12 (rebindable in `project.godot`); launch flag
  `--debug-overlay` starts with it on (so `--screenshot` can capture it). Off by default; when off,
  no overlay node processes anything and nothing is built.
- **Nav grid layer:** a 3D mesh of one quad per cell a few cm above the terrain, vertex-coloured by
  flags (passable faint / `Blocked` red / `Cliff` dark red / `Ramp` orange, alpha-blended), built by a
  pure `ViewApi` helper (e.g. `ViewApi.NavOverlayBuilder`) from `NavGrid` reads; rebuilt only when
  the overlay turns on or `NavGrid.Version` changes (M3-1 will bump it when a tree is cut). Treat
  flag bits you don't know generically: any cell with `Blocked` set is blocked whatever else is set
  (the sim branch adds a `Resource` bit this session; don't depend on it).
- **Flow arrows:** goal = `GoalCell` of the lowest-slot selected unit with `GoalCell >= 0`;
  `World.FlowFields.PeekCached(goal)`; if non-null, one arrow per passable cell inside a window
  around the camera focus (about 40 x 40 cells, or the visible trapezoid) from `DirectionAt`
  (`NoDirection` = no arrow; mark the goal cell). Direction-to-offset mapping must be the sim's
  (read it from the sim's table or test it against movement). Peek every refresh, never keep a
  `FlowField` across ticks; refresh when the goal cell, the field's `Version`, or the window changes.
  Budget: overlay work <= 1 ms per frame at 2,000 units, zoom 60, Debug.
- **Tick-time graph:** a Control under `DebugOverlay`: ring buffer of the last 120 `LastTickMs`
  samples (view-side), bars or a line with the 4 ms budget line, avg / worst text. The label gains
  entity counts (live units, Moving units, cached fields via `FlowFields.Count`).
- Tests: `sim/Rts.Sim.Tests/ViewApi/` (nav overlay colours per flag on hand maps incl. edge rows;
  version-driven rebuild; arrow mapping for all 8 directions + none against the sim's table; window
  clipping at corners; no cached field -> 0 arrows; a hash twin proving the helpers never change
  `StateHash`; 0 bytes per refresh at steady state). Scene `game/tests/DebugOverlayTest.tscn`
  (headless; off by default, toggle via the action, `--debug-overlay`, arrows appear after a Move
  once the field is cached and match `DirectionAt` at sampled cells, graph <= 120 samples, counts;
  prints "TEST PASS"). Screenshot with the overlay on (seed 1, army ordered) inspected.
- Docs: docs/03 "Debug tooling" overlay bullet (M2-5: key, flag, what it shows, costs, deferred
  items), "Implementation (M2-5)" under Rendering; docs/01 change-log row (Producer decisions:
  F12, one goal's field, camera window, 120-sample graph, labels deferred).

**Out of scope.** Per-unit state labels and the dev console (docs/03 lists them; not in the M2
criterion; labels may come with M2-7 if cheap), trees / rocks meshes (next view session, after M3-1
merges), audio (M2-6), minimap changes, anything in `sim/` outside `ViewApi/`.

**Acceptance criteria.**
1. F12 toggles the overlay; `--debug-overlay` starts with it on; off by default; `project.godot`
   has the action. Scene test proves on / off / flag.
2. Nav grid mesh: one quad per cell, colour by flag per the rule, rebuilt exactly once per
   `NavGrid.Version` change (QA can bump it) and never per frame; unknown bits handled.
3. Arrows match `DirectionAt` of the current cached field at every sampled cell; none for a goal
   with no cached field; none when the selection has no goal; a field evicted or rebuilt between
   frames is never drawn stale (next refresh matches or is empty).
4. Graph holds the last 120 samples with the budget line; counts correct against the sim.
5. Read-only: hash twin (overlay on, toggled, selection changing, fields churning) equal every tick;
   0 bytes per frame on and off at steady state; <= 1 ms per frame on at 2,000 units.
6. `tools/qa/smoke.ps1` PASS; all existing `game/tests` scenes PASS; non-Perf suite green.
7. Docs as listed; screenshot inspected by the dev.

**Size.** ~800-1,100 lines incl. tests.

**Design references.** docs/03 "Debug tooling" (overlay bullet: F12, nav grid, flow arrows for the
selected group, tick time graph, entity counts), "Flow fields" (`PeekCached` is a snapshot for this
tick only), "Rendering and presentation" + "Implementation (M2-1)" (debug label, screenshot flag),
docs/02 "Controls and camera" (no key conflicts: F12 is free).

**Constraints most at risk.** ViewApi reads only (no `Tick`, no `Enqueue`, no writes); no held
`FlowField` references; no per-frame allocation; absolute paths for any file I/O; `game/data/`
untouched; no public member added to `FlowFieldCache`.

### After this task (view)
M2-6 placeholder audio (generated tones; no downloads), M2-7 playable check (100 units at 60 FPS)
plus trees / rocks MultiMesh from M3-1's `World.Resources` (read-only accessors in `ViewApi`), then
the M2 end-of-milestone hardening (BUG-0069, BUG-0070, export hygiene notes) and M2 sign-off.

## Watch-outs

- Perf rows fail from CPU contention while the other track's QA runs: a failure counts only when it
  fails again alone (tight blob 4.33 ms vs 4.5).
- The sim branch changes `NavFlags`, `NavGrid` (internal setters), `World`, `SimConfig`, `StateHash`,
  `MapGenParams`, `ReplayFormat` (format 3), the golden; the view branch must not depend on any of it.
  Merge order: sim first, then view.
- `CLAUDE.md` is the owner's file: still says "Current milestone: M1" and has no CLI line.
- Builders: absolute paths only; never touch the owner's main checkout. Remote Control unavailable.
