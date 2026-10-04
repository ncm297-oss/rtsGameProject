# Handoff: next session brief

Written by the Producer at the end of each session for the next one. The next session's
Producer starts from this, verifies it against the repo, and then plans.

_Written: 2026-10-03 (session 2026-10-03-2220, ACCEPT)._

## Current session plan (2026-10-04-0120, PLAN: GO, task M1-4b)

**Title:** Wire GameData into the sim, add flow fields + LRU cache + `Move`.

**Goal:** Units can be ordered to a point and walk there along a cached flow field at their
data-defined speed. This is the first half of roadmap criterion 4 (M1-4c adds steering,
separation, arrival slots, shoving). The sim has no access to `GameData` today, so this task also
makes `SimConfig` carry it: speed and radius must come from `UnitDef`, never a literal.

**Scope (IN):**
- `SimConfig` gets `required GameData Data`; `World.Data`. `SpawnUnit` resolves `Data.Units[TypeId]`
  and drops the command when `TypeId` is out of range (same policy as a full store). `UnitStore`
  gains per-slot `Speed` (m/tick, from `SpeedPerTick`), `Radius`, `State` (Idle/Moving), `Goal`
  (Vector2), `GoalCell` (int). New gameplay fields go into `StateHash` (not `Speed`/`Radius`, they derive from `TypeId`).
- `Rts.Sim.Pathfinding`: `FlowField` (integration cost float + direction byte per cell, 8-connected
  Dijkstra from the target, diagonal cost x1.41421, no corner cutting: a diagonal step needs both
  side cells passable; blocked target cell -> nearest passable cell by squared distance, ties lowest
  (y, x)). `FlowFieldCache` (default 32 entries, keyed by target cell, tagged with
  `NavGrid.Version`, LRU evict, all arrays + Dijkstra heap preallocated in the ctor).
- `CommandKind.Move`, `Command.Move(player, EntityHandle unit, Vector2 target)`: one command per
  unit (a group move is N commands sharing one field). Dropped when: handle stale, unit not owned
  by `player`, target not finite or outside the map. Hash the new `Unit` field in `StateHash`.
- `MovementSystem.Run(World)` in tick phase 9 (after spatial rebuild): a Moving unit steers toward
  the *center of the next cell* its current cell points to (safe against corner clipping), or
  straight at `Goal` when already in the goal cell; moves `min(Speed, distance)`; arrives (Idle,
  velocity 0) within `MovementConstants.ArrivalDistance` = half a cell; a step whose destination
  cell is blocked is refused (unit stays, velocity 0). `Velocity` and `Facing` (via `SimMath.Atan2`)
  updated. Units update in slot order.
- docs/03: Implementation (M1-4b) paragraphs under "Flow fields" and "Local movement", tick-model
  phase 1/8/9 text, Entity model field list, `SimConfig.Data` sentence.

**OUT:** separation, shoving, arrival slots/formations, direct steering across adjacent cells,
`Stop`/`Hold`, order queues, BUG-0005, teams, any change to the map generator (15 pinned hashes
and QA oracles must stay green), any `NavGrid.Version` bump.

**Acceptance criteria:**
1. Every `UnitDef.Key` in shipped data spawns with `Speed == SpeedPerTick` and `Radius == Radius`
   from JSON; an out-of-range `TypeId` is dropped (test: `SimulationTests`).
2. Flow field equals a brute-force Dijkstra oracle (cost and direction choice) on at least three
   hand-built grids (ramp corridor, U-pocket, diagonal gap) and on 20 generated maps x 5 targets.
3. No direction ever points diagonally between two cells of which either side cell is blocked; no
   direction points into a blocked cell; target cell direction is "none" (test on all passable
   cells of 20 generated maps).
4. Blocked target cell resolves to the nearest passable cell (test with a cliff-cell target).
5. Cache: hit returns the same instance, 33rd distinct target evicts the least recently used,
   touching a cached field makes it most recent, and a `NavGrid.Version` mismatch rebuilds
   (test through an `internal` version bump or a test seam, no public setter).
6. `Move`: stale handle, foreign unit, NaN target and out-of-map target are all dropped with
   state unchanged; a valid Move sets `State = Moving` and `Goal`.
7. 200 units given a Move to one reachable point on the default 128x128 map all reach
   `ArrivalDistance` within 60 s of sim time (1,200 ticks), are never inside a blocked cell on any
   tick, and end Idle with zero velocity. (A single-point target with no separation will stack them;
   that is fine for 4b.)
8. Determinism: two sims, same seed, same Move commands, 600 ticks -> identical `StateHash` every
   tick; a different seed -> different hash.
9. Zero allocation per tick after warm-up with 500 moving units (extend `AllocationTests`),
   including a tick that misses the cache (field build inside the tick).
10. Perf (`Category=Perf`): field build on the default map < 2 ms average; 500 units moving
    < 2 ms/tick average (incl. spatial rebuild), Debug build as tests run.
11. `ArchitectureTests` green; `Source_UsesNoHashCollectionsOrLinq` extended to `Pathfinding/`;
    `dotnet build` 0 warnings; full suite green; `tools/qa/smoke.ps1` PASS (game project
    references `SimConfig`, so it must still compile; the Godot side needs no data loading yet
    beyond what compiles).

**Design references:** docs/03 "Flow fields" (Dijkstra 8-connected, 1.41 diagonals, no corner
cutting, LRU 32 tagged with grid version, blocked target -> nearest reachable), "Local movement"
(desired velocity = flow direction x speed), "Tick model" phases 8-9, "Entity model" (SoA,
positions 2D meters), "Determinism" (`SimMath.Atan2`, slot-order updates, no hash collections).
docs/02 line 7 (speed in m/s in data; loader converts) and the unit tables (speeds 2.2-6.2 m/s,
radii <= 1 m, so all units share one size class).

**Tests required:** `FlowFieldTests` (oracle, corner cutting, blocked target), `FlowFieldCacheTests`
(hit/evict/LRU order/version), `MovementSystemTests` (speed from data, arrival, refuses blocked
step, 200-unit scenario), `SimulationTests` additions (Move validation, spawn drops bad TypeId),
`StateHashTests` (Goal/State/Unit hashed), `AllocationTests` + Perf tests above. Replace the 35
`new SimConfig(` sites in tests with a shared `TestData.Shipped`-style helper that loads
`game/data` once (static) so the suite doesn't slow down.

**Constraints most at risk:** no literal stats (speed/radius from `UnitDef`; arrival distance is a
named sim constant derived from `CellSize`, not a tuning number); no allocation in per-tick code
(preallocate fields, heap, scratch in the cache ctor); no `Math.Atan2` (use `SimMath`); no
Dictionary/HashSet in `Pathfinding/`; one implement commit; budget ~500 production lines.

**QA focus:** fuzz flow fields vs. oracle on 200 random maps incl. ramps/pockets; property "no
unit ever occupies a blocked cell" with 500 units, random targets re-issued every 20 ticks, 50
seeds, 2,000 ticks; cache thrash (64 distinct targets per tick) for allocation and time; Move with
stale handles after Free/realloc of the same slot, foreign owners, player -1 / PlayerCount, NaN/Inf,
targets at -0.1 m and exactly at the map edge, targets on cliff and ring cells; two-sim determinism
with interleaved spawns/moves; perf at 1x/2x/5x (500/1,000/2,500 units).

## Where we are

M0 Done. M1 is 3/8: sim core (M1-1), data loader + first data (M1-2), terraced heightmap +
nav grid (M1-3), ramp walls + param safety + spatial hash (M1-4a). Branch `studio/2026-10-03-2220`
@ f282aaf is green: build 0/0, 717 passed + 6 skipped (known-bug tests), `tools/qa/smoke.ps1`
PASS. The conductor merges it to main. Open bugs: 3 S3, 3 S4, none blocking. Sessions today: 5/10.

What exists in the sim: `World(config)` owns `Heightmap`, `NavGrid` and `Spatial`
(`Rts.Sim.Spatial.SpatialHash`). NavGrid: border ring blocked, plateau rims are cliff cells except
at ramp mouths, ramp flanks are cliff cells ("ramp walls"), pockets sealed at build; queries
allocation-free, outside-map reads blocked; `Version` exists (0, nothing bumps it yet). Spatial
hash: 4 m buckets, rebuilt in `Simulation.Tick()` right after commands apply; `QueryRadius`,
`QueryRect`, `NearestEnemy` write ascending slot indices into a caller `Span<int>` and return the
total match count (above buffer length = truncated); not part of `StateHash`. Nothing moves yet;
`GameData` is loaded but unused. Commands: `Noop`, `SpawnUnit` only.

## Next task candidates

1. **M1-4b: flow fields + LRU cache + `Move` command** (docs/03 "Flow fields"). 8-connected
   Dijkstra over `NavGrid` cost from the target cell, no corner cutting (both side cells must be
   passable for a diagonal), per-cell direction + integration cost, LRU cache of 32 fields keyed
   by target cell and tagged with `NavGrid.Version`; `Move(player, slots/handles, target)` command
   that sets a unit's goal; `MovementSystem.Run` moves units along the field at `speed` from
   `UnitDef` (data, not literal) with simple arrival (stop within one cell of the target).
   Separation/shoving OUT. Tests: field correctness vs. Dijkstra oracle on hand-built grids
   (ramps, pockets, walls), no diagonal through blocked corners, cache hit/evict/invalidate on
   `Version`, zero allocation per tick after warm-up, determinism across two sims, Perf: field
   build on 128x128 < 2 ms, 500 units moving < 2 ms/tick. Budget ~500 production lines.
2. **M1-4c: steering, separation, arrival slots, shoving + BUG-0005** (docs/03 "Local movement").
   Separation via `Spatial.QueryRadius` with collision radii from data; arrival slots around the
   target; shoving of idle units; fix BUG-0005 with per-player command buckets. Closes roadmap
   criterion 4 together with 4b.
3. M1-5 scenario test (200 units across 128x128 with obstacles). Needs 4b + 4c.
4. M1-6 replay + determinism golden: **first commit fixes BUG-0014** (mix the seed in `SimRng`)
   because it changes every stream; decide whether terrain hashes into `StateHash` directly.

## Watch out for

- Budget discipline: the last three diffs ran well over budget (M1-4a: ~660 production lines vs
  450, justified by the BUG-0015 rewrite). Keep 4b to flow fields + `Move`; push the rest to 4c.
- Don't ask for two commits per session: the session skill makes one implement commit. Ask for a
  report line after the bug-fix part instead.
- BUG-0017: `Stress.SimCoreStressTests.Flood_10000Commands_OneTick_AllocatesNothing` failed once
  in 11 QA runs (0 in the Producer's reruns). If main goes red on it, harden per the bug file
  (re-measure the block once, report both deltas) rather than loosening the assert.
- Generator pins: `MapGeneratorTests.Generate_MatchesMapsFromBeforeTheFastRampChecks` (15 hashes)
  and QA `QA/Oracles/PreBug0015*` + `MapGenEquivalenceQaTests` pin today's maps. A deliberate
  generator change must retire or regenerate them in the same commit and say why.
- `NavGrid.Flood` has no bounds checks: it relies on the outer ring always being blocked. M3
  passability changes (trees, buildings) must never unblock ring cells.
- Spatial hash costs: rebuild scales with bucket count (0.06 ms on 128 map, 0.65 ms on 1024);
  queries in one crowded bucket are dominated by `Span.Sort` (2,500 units in one bucket -> 500
  radius queries 8 ms). Separation in 4c issues one query per unit per tick: measure, consider
  skipping the sort for callers that don't need order (but keep determinism).
- `NearestEnemy` treats any other owner as an enemy; teams arrive with M5/M6 skirmish setup.
- `SimRng` is a mutable struct: always `ref world.Rng(stream)`, never copy it (docs/03).
- Map generation draws only from `RngStream.MapGen`; QA tests pin the first draws of Combat/AI.
- `StateHash` covers the post-generation MapGen RNG state, not terrain directly. No golden
  replays exist yet, so generator changes are still "free" until M1-6.
- `ArchitectureTests.SimSource_UsesNoForbiddenApis` greps sim source for `Math.*` trig, `Random`,
  `DateTime`, `HashCode`, `Parallel`, `Vector<`. `MathF.Floor`/`Sqrt`, `Math.Min/Max/Abs` and
  `System.Numerics.Vector2` are allowed. `SpatialHashTests.Source_UsesNoHashCollectionsOrLinq`
  greps `Spatial/`; extend it to `Pathfinding/` for 4b.
- Zero-allocation tests (`AllocationTests`, NavGrid 100k queries, QA 1M queries) catch any `new`,
  LINQ or closure in per-tick paths. Field builds hit the LRU miss path inside the tick, so the
  Dijkstra open list and field arrays must be preallocated in the cache.
- `QA/DataLoaderQaTests.GameDataTypes_ExposeOnlyImmutableMembers` reflects over `Rts.Sim.Data`;
  `DataJsonContext` disallows unknown members (add to `DataJson.cs` first).
- `tools/qa/smoke.ps1` is the smoke gate; it falls back to the User-scope `GODOT` variable.
- `.sln` Release config builds the game project in Debug (BUG-0002, S4): irrelevant until M6.
