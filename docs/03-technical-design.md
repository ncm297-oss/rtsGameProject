# 03 — Technical Design

How the game is built. The rules in [CLAUDE.md](../CLAUDE.md) are the short version; this doc is
the reasoning and the detail.

## Platform and versions

| Component | Version | Notes |
| --- | --- | --- |
| Godot | 4.7.x, .NET build (4.7.2 at time of writing) | `GodotEngine.GodotEngine.Mono` on winget |
| .NET SDK | 8.0 | Godot 4.7's C# projects target `net8.0` |
| Language | C# 12 | |
| Tests | xUnit | |
| Renderer | Forward+ (Vulkan / D3D12) | Desktop only; Compatibility renderer not needed |
| Target hardware | Windows 10/11 x64, mid-range GPU | 60 FPS with ~400 animated units |

**.NET 8 support ends on 10 Nov 2026.** That's fine for a single-player offline game, but
revisit at M6: move every project to the next LTS target as soon as the Godot version we use
supports it, and do it in one commit for the sim and game projects together.

## Overview

```
┌──────────────────────── game/ (Godot, C#) ─────────────────────────┐
│  Input & camera ──► CommandQueue ─┐     ┌──► Unit/building views    │
│  HUD, minimap, menus              │     │    (mesh + AnimationPlayer)│
│  Audio                            │     │    Terrain + fog shader    │
│                                   ▼     │    MultiMesh props         │
│                    SimRunner (accumulator, interpolation)           │
└───────────────────────────────────┬───────────────▲─────────────────┘
                     commands       │               │ snapshot + events
┌───────────────────────────────────▼───────────────┴─────────────────┐
│ sim/Rts.Sim (pure .NET 8, no Godot)                                  │
│  Simulation.Tick(): commands → AI → systems → vision → events        │
│  World: struct arrays, spatial hash, nav grid, flow fields, RNG      │
│  Data: JSON definitions loaded from game/data/                       │
└──────────────────────────────────────────────────────────────────────┘
```

Why the split matters:

- The sim is testable with `dotnet test` in milliseconds, with no engine running.
- Claude's edit-build-test loop stays tight: most work happens in a plain class library with
  compiler and test feedback.
- Godot's per-node physics and scene-tree overhead never limit unit counts.
- Determinism (and therefore replays and replay-based regression tests) is achievable because
  the sim controls every input.

## Projects

| Project | Type | References | Purpose |
| --- | --- | --- | --- |
| `sim/Rts.Sim` | `net8.0` class library | none | Game state, rules, AI, data loading, replays, save/load |
| `sim/Rts.Sim.Tests` | xUnit test project | Rts.Sim | Unit, scenario, determinism, replay, data-validation, perf tests |
| `game/RtsGame.csproj` | Godot.NET.Sdk 4.7.x | Rts.Sim | Presentation, input, UI, audio |
| `tools/*` | console apps / scripts | Rts.Sim (map gen) | Asset import, map generator CLI, export script |

`RtsGame.sln` sits at the repo root. Godot's `dotnet/project/solution_directory` setting points
to `..` so the editor uses it.

## Tick model

The sim runs at a fixed **20 Hz** (`TickMs = 50`). One call to `Simulation.Tick()` runs these
phases in this fixed order:

1. **Apply commands** queued for this tick, sorted by (player, sequence number), then rebuild
   the spatial hash so later phases (and this tick's spawns) see current positions. A `Move`
   only records the unit's goal here; the walking happens in phases 8-9 of the same tick.
2. **AI think:** each AI player runs on its own cadence (default every 10 ticks, staggered by
   player index) and enqueues commands for the *next* tick, the same as a human.
3. **Production:** training and research timers, spawning finished units at rally points.
4. **Construction and economy:** building progress, gathering, drop-offs.
5. **Status effects and zones:** expire, tick DoTs and regen, apply zone effects.
6. **Abilities:** cast timers, effect resolution.
7. **Orders and targeting:** order queue advance, target acquisition (staggered scans).
8. **Pathfinding requests:** build or fetch flow fields for new move targets. (M1-4c: the first
   step of `MovementSystem.Run`: Moving units are sorted by (goal cell, slot), every goal that
   needs a field and has it cached is touched, and the missing ones are built oldest order first,
   without allocating, at most `MovementConstants.MaxFieldBuildsPerTick` (2) per tick; see "Build cap".)
9. **Movement:** flow-field direction + steering + collision against the nav grid
   (`MovementSystem.Run`, units in (goal cell, slot) order; since M1-4d-1 every unit plans from
   start-of-tick state before any unit moves, and since M1-4d-2 the Idle units they shoved move
   last; since M1-4d-3 a unit a shove cut off its blob walks back once, see "Local movement").
10. **Combat:** attack wind-ups and cooldowns, projectile flight and impact, splash.
11. **Damage and death:** apply queued damage, kill entities, emit death events.
12. **Vision and detection:** recompute fog every 4 ticks (5 Hz) per player.
13. **Cleanup:** free dead handles, finalize the tick's event list, bump `TickNumber`.
14. **State hash** (debug builds and tests): a 64-bit hash of all gameplay state. Since M1-6 an
    attached `ReplayRecorder` hashes here when the new `TickNumber` is a multiple of its checkpoint
    interval (see "Save/load and replays").

### Presentation timing

`SimRunner` (a Godot `Node`) accumulates real frame time × game speed. While the accumulator holds
at least 50 ms it runs one tick (max 5 ticks per frame, to avoid a death spiral after a stall).
Views render at `alpha = accumulator / 50 ms` between each entity's previous and current
position and facing. Commands from input are stamped with the next tick number.

Since M2-1 the accumulator is the pure class `Rts.Sim.ViewApi.FixedStepClock` (unit-tested
without Godot): `Advance(delta, speed)` returns the ticks to run this frame and `Alpha` is in
[0, 1). A tick fires when the accumulator is within 1 µs of 50 ms, so 60 frames of 1/60 s give
exactly 20 ticks. When the 5-tick cap is hit, the leftover is cut to just under one tick, so the
next normal frame runs at most one tick (the backlog is dropped, not replayed). Zero, negative,
or non-finite deltas and speeds add nothing, so speed 0 freezes ticks and alpha. `SimRunner`
clamps its `GameSpeed` to 0.25-8.

`TickNumber` is the number of the tick that runs on the next `Tick()` call (0 before the first).
`Simulation.Enqueue` always stamps `TickNumber + 1` plus a per-player sequence number, whether it
is called between ticks (input) or during one (AI think), so a command never changes the tick in
progress. The cost is at most one tick (50 ms) of input latency. Pending commands live in a
fixed-capacity `CommandQueue` (size from `SimConfig.CommandCapacity`) that is insertion-sorted in
place each tick, so applying commands does not allocate. `Enqueue` on a full queue throws and
leaves state unchanged (the sequence counter advances only once the command is accepted). At
apply time, `Command.IsValid()` drops malformed commands (today: a `SpawnUnit` or `Move` with a
NaN or infinite position), the same policy as a spawn into a full unit store; new command kinds add
their checks there. Checks that need the world run in `Simulation.Apply`: a `SpawnUnit` whose
`TypeId` is not a unit in `SimConfig.Data` is dropped, and a `Move` is dropped when its unit
handle is stale, the unit belongs to another player, or the target is outside the map.

The sim runs on the main thread in v1. If profiling demands it later, move `Tick()` to a worker
thread with double-buffered snapshots. The sim's design (no Godot calls, explicit snapshot
output) already allows this.

## Determinism

Goal: the same seed and the same command list produce the same state hash, on the same machine,
every run. Cross-machine lockstep (fixed-point math) is **not** a goal, since there's no multiplayer.

Rules:

- One seeded RNG (`SimRng`, xorshift/PCG) owned by `World`, with separate streams for map gen,
  combat, and each AI player, so adding a random call in one system doesn't shift the others.
  **Implementation (M1-1):** `SimRng` is **PCG32 (XSH-RR)**: 64-bit LCG state, 32-bit output,
  seeded like O'Neill's `pcg32_srandom_r(seed, streamId)`, except that the seed first goes through
  `SimRng.MixSeed` (SplitMix64's output function, a bijection) before it is added to the state
  (M1-6, BUG-0014): unmixed, seed `s` and `s - 1` differ by one LCG step, so seed `ulong.MaxValue`
  gave seed 0's stream shifted by one draw and the same map. Mixing changed every seed's streams,
  so every map and pinned hash changed once in M1-6. Every stream uses the match seed;
  the stream id (`RngStream.MapGen = 0`, `Combat = 1`, `Ai(player) = 2 + player`) selects the
  LCG increment, so streams are independent sequences. `NextInt` uses Lemire's
  multiply-and-reject (no modulo bias); `NextFloat` uses the top 24 bits, giving [0, 1).
  `SimRng` is a mutable struct: always access it by `ref` (`ref world.Rng(RngStream.Combat)`).
- No `System.Random`, `DateTime`, `Stopwatch`, `Guid`, `string.GetHashCode()`, or `HashCode` in
  gameplay code.
- No iteration over `Dictionary`/`HashSet` where order affects outcomes. Entities update in
  array index order.
- Floats are fine. Use `System.Numerics.Vector2` operations (+, -, ×, dot, `Sqrt` are
  IEEE-exact) and `SimMath` for trigonometry (lookup tables / polynomials), since `Math.Sin`,
  `Atan2`, etc. may differ between runtime versions. No `Vector<T>`, no `Parallel.For`, no
  hardware-width-dependent code. This aims to make golden replay hashes match across the owner's
  two machines (both x64 Windows, same .NET version) too; if they don't, the replay test reports
  it and we investigate.
  **Implementation (M1-1):** `SimMath` uses polynomials only (no lookup table, so no
  runtime-computed table either). `Sin` reduces the angle to [-pi, pi] with `floor`, folds it into
  [-pi/2, pi/2], and evaluates the Taylor series to x^9 (max error ~3.6e-6; `Cos(x) = Sin(x + pi/2)`).
  Accuracy degrades for |x| beyond a few hundred radians because the reduction is done in float;
  keep angles normalized. Past ~1e8 the reduced angle is meaningless, so it is clamped to
  [-pi, pi] and the result to [-1, 1]: a huge input gives a bounded wrong answer, never a value
  outside [-1, 1] or infinity (BUG-0003). NaN in gives NaN out. `Atan2` folds into one octant and uses the Abramowitz & Stegun 4.4.49
  minimax polynomial for atan on [0, 1] (max error ~1.2e-5 rad); `Atan2(0, 0)` returns 0.
  `Sqrt` wraps `MathF.Sqrt`, which is IEEE-exact. `ArchitectureTests` forbids `Math.Sin`,
  `Cos`, `Atan`, `Atan2` (and the `MathF` versions) in sim source.
- **State hash:** `Simulation.StateHash()` is 64-bit FNV-1a (`StateHasher`) over the tick number,
  every unit slot's generation and alive flag, every live unit's fields (including the give-up
  state `StuckTicks` and `BestRemaining`, since they decide when a unit stops, and since M1-4d-3 the
  walk-back state `WalkBack`, which decides whether and when a unit walks back), the free list, all RNG
  states, per-player command sequence counters, and pending commands (including a `Move`'s unit
  handle), and the flow-field cache's metadata (its clock and count, and each used slot's
  requested cell, grid version and last-use stamp, in slot order): under the build cap the cache
  decides who waits, so it is sim state (BUG-0021, see "Flow fields"). Derived state is left out:
  the spatial hash, the fields' contents (they follow from the grid and the key), and a unit's
  `Speed`/`Radius` (copied from its `TypeId`'s data).

## Entity model

```csharp
public readonly record struct EntityHandle(int Index, int Generation);
```

- `UnitStore` keeps parallel arrays (structure of arrays) indexed by slot: `Position`,
  `PrevPosition`, `Velocity`, `Facing`, `Hp`, `Owner`, `TypeId`, `State`, `TargetHandle`,
  `AttackTimer`, `StatusMask`, `OrderQueueStart`, and so on. A free list recycles slots, and the
  generation counter invalidates stale handles.
- `BuildingStore`, `ProjectileStore`, `ResourceStore` (mines, trees), `ZoneStore` follow the same
  pattern.
- Positions are 2D (x, z) in meters. Height is looked up from the heightmap only for rendering
  and slope rules. The sim is a 2D game on a plane, like StarCraft 2 and Age of Empires.
- Type data (`UnitDef`, `BuildingDef`, ...) is immutable, loaded once from JSON, referenced by
  int `TypeId`. Per-player modifiers (upgrades, faction bonus) are applied through a per-player
  `StatModifiers` table, never by mutating defs.
- Order queues live in a pooled flat array (ring per unit, max 16 queued orders).

**Implementation (M1-4b):** `UnitStore` has `Position`, `PrevPosition`, `Velocity` (m/tick),
`Facing` (radians, `SimMath.Atan2` of the last step), `Owner`, `TypeId`, `Speed` (m/tick) and
`Radius` (m), `State` (`UnitState.Idle` / `Moving`), `Goal` (m), `GoalCell` (nav cell index of
the goal's flow field, -1 for none) and `OrderTick` (the tick the current Move applied, M1-4c),
plus `Alive` and `Generation`. `Speed` and `Radius` are copied
from `UnitDef.SpeedPerTick` / `Radius` at spawn, so movement never looks up data per tick. The
match's data travels with the setup: `SimConfig.Data` is a required `GameData` (from
`DataLoader.LoadAll`), exposed as `World.Data`; the sim never reads files itself. The other fields
in the list above arrive with the systems that use them.

### Spatial hash

A uniform grid of 4 m buckets (2×2 cells), rebuilt every tick with a counting sort into flat
arrays (no allocation). Queries: units within radius, nearest enemy within radius, units in a
rectangle (box select support for tests/AI). Buildings are also indexed in the nav grid.

**Implementation (M1-4a):** `Rts.Sim.Spatial.SpatialHash`, owned by `World` (`world.Spatial`) and
rebuilt inside `Simulation.Tick()` right after commands apply, so units spawned this tick are
queryable by every later phase. Buckets are `BucketCells` (2) cells wide, so `BucketSize` =
2 × `MapConstants.CellSize` = 4 m. `Rebuild` counting-sorts live slots into flat arrays sized once
from `UnitCapacity` and the map size, copying each slot's position and owner; rebuilds and queries
never allocate (2,500 units rebuild in about 0.06 ms). Units outside the map clamp into the nearest
edge bucket and are still found. Queries: `QueryRadius(center, radius, Span<int>)` (match rule, in
float: `dx*dx + dy*dy <= radius*radius`), `QueryRect(a, b, Span<int>)` (edges inclusive, corners in
any order) and `NearestEnemy(center, radius, player, out slot)` (any other owner counts as an
enemy until teams exist; ties go to the lowest slot). Results are slot indices in ascending order.
A query writes at most `results.Length` slots (the lowest ones) and returns the total match count,
so a return value above the buffer length means the buffer was too small. Negative or NaN radii
and non-finite centers match nothing. The hash is derived state and does not feed `StateHash`.

## Pathfinding

### Navigation grid

128 × 128 cells, 2 m each. Per cell: passability flags (terrain slope, building footprint, tree,
water), a movement cost byte (1 normal, 255 blocked), and an elevation `Level` (0-2) used by the
high-ground vision rule. The map generator builds terraced terrain: flat plateaus at multiples of
4 m joined by ramps, with steep (impassable) plateau edges. A grid `Version` counter increments
whenever passability changes (building placed or destroyed, tree depleted).

**Implementation (M1-3):** `Rts.Sim.Map`. `World` builds the terrain in its constructor:
`MapGenerator.Generate(config.Map, ref world.Rng(RngStream.MapGen))` returns a `Heightmap`
(per-cell `Level` and height in meters), and `new NavGrid(heightmap)` derives passability.
Fixed geometry lives in `MapConstants` (`CellSize` 2 m, `LevelHeight` 4 m, `MaxLevel` 2, max ramp
slope tan 30°); every tunable (size, plateau counts and sizes, ramp width/length, retry bounds) is
in `MapGenParams`, defaulting to a 128 × 128 map. The generator raises level-1 rectangles on open
ground, level-2 rectangles inside level-1 ones, then cuts ramps (default 3 cells wide, 4 long, so
the slope is about 22°) into plateau sides where the whole footprint plus a one-cell ring is flat
lower ground. Representation (Producer decision, 2026-10-03): cliffs are **blocked cells**, not
per-edge rules. A cell is a cliff when a 4-neighbor is lower and that neighbor is not a ramp
exactly one level down, so plateau rims are blocked except at a ramp's top ("mouth"). A ramp cell
keeps the lower level (docs/02) and its height steps evenly, strictly between the two levels; it
joins level L to L+1 only. The flat lower cells flanking a ramp along its length are cliffs too ("ramp walls", M1-4a, BUG-0011), so a ramp is a corridor entered only at its mouth and foot and no passable 4-neighbor step is steeper than 30°. The outer ring of cells is blocked. Passable cells outside the largest
4-connected region (pockets no ramp reaches) are blocked at build time, so every passable cell is
reachable; later passability changes (M3 buildings) don't re-seal. A layout with under
`MinPassableFraction` (50%) passable cells or with a level missing is redrawn, at most
`MaxAttempts` times, then the generator falls back to the first layout that was passable enough,
or a flat map: it never loops forever. `MapGenParams.Validate` caps every size at the smaller map side before using it in arithmetic, and caps `RampTries` (128), `MaxAttempts` (8, the default) and plateau counts (32). A ramp try costs O(1) plus O(ramps placed) plus O(`RampWidth`) for the mouth, not O(`RampWidth` × `RampLength`): a summed-area table of off-limits cells (border ring, cells with a lower neighbor) and rectangle overlap tests against the ramps already placed replace the cell-by-cell footprint scan, accepting exactly the same tries (BUG-0015). Each attempt also builds a full nav grid. The worst valid case, a 1024 × 1024 map with every count at its cap and any ramp size, takes about 1 s in a Debug build (how the tests run) and about 0.2 s in Release. Default generation takes about 4 ms. Queries (`InBounds`,
`IsPassable`, `LevelAt`, `FlagsAt`, `CostAt`, `WorldToCell`, `CellCenter`) never allocate and read
cells outside the map as blocked; `WorldToCell` floors (so -0.1 m is outside, not cell 0) and
rejects NaN and infinities. Cell (x, y) covers meters [2x, 2x + 2) on each axis. Per-cell data is
ready for 8-connected flow fields: no-corner-cutting only needs `IsPassable` on the two side cells.
Terrain does not feed `StateHash` yet (M1-6 decides). Trees, water, gold, symmetry, and hand-made
maps arrive in M3/M6.

### Flow fields

On a move order, the group's target cell gets a flow field:

1. **Integration field:** Dijkstra from the target over 8-connected cells (diagonals cost 1.41,
   no corner cutting past blocked cells). 16K cells, well under 1 ms.
2. **Direction field:** each cell points at its lowest-cost neighbor.

Fields are cached by target cell in an LRU cache (32 to 128 entries, scaled with the unit cap) and tagged with the grid
version. Any passability change invalidates the cache (simple; refine to region versions only if
profiling shows rebuild cost). All units heading to the same target share one field. Units whose
collision radius is at most half a cell share one size class, which is why the design caps
collision radius at 1 m.

If the target cell is blocked (a building, a forest), the field targets the nearest reachable
cell. Unreachable targets (an island) send units to the closest reachable point.

**Implementation (M1-4b):** `Rts.Sim.Pathfinding`. A `FlowField` holds a float cost and a
direction byte per cell (0-7: east, then clockwise with +y down the rows; odd numbers are
diagonals; 255 = none). It is built by Dijkstra from the target over 8-connected cells: a straight
step costs 1, a diagonal 1.41421, and a diagonal step needs both cells it passes between to be
passable (no corner cutting). All passable cells cost 1 today; the nav cost byte isn't read yet.
Each reached cell points at the neighbor that starts its shortest path, lowest direction number on a
tie; the target, blocked cells and unreachable cells have no direction. The priority queue is a
bucket queue on the whole-number part of the cost (Dial's algorithm: every step costs at least 1, so
cells in one bucket can't improve each other), which gives the same costs as a binary heap but builds
a 128 × 128 field in about 0.7 ms in a Debug build. A blocked target cell resolves to the nearest
passable cell by squared cell distance, ties to the lowest (y, x); `FlowField.NearestPassable` is
that rule. It searches square rings outward from the cell and stops once a ring can't beat the
best distance found, so its cost grows with the distance to passable ground, not the map size. Unreachable targets can't happen yet: the nav grid seals every pocket, so all passable
cells connect. `FlowFieldCache` (on `World.FlowFields`) keeps
`FlowFieldCache.CapacityFor(UnitCapacity, cells)` fields: `clamp(UnitCapacity / 8, 32, 128)`, then at
most `max(32, 64 MiB / (5 bytes × cells))`, so the default map (16,384 cells, 80 KB per field) is
never memory-capped (512 unit slots: 64 fields, 5 MB; 1024 and up: 128 fields, 10 MB) and a
1024 × 1024 map stays at 32. Fields are keyed by the requested target cell and tagged with `NavGrid.Version`; a hit returns the same instance and marks it
most recently used, a miss fills a free slot or rebuilds the least recently used one in place, and
a field whose version is stale is rebuilt on its next use; `TryGetCached` returns a current field
(marking it used) or null without building. Every field array, the queue, and a
per-cell step mask (which of the 8 steps are legal, shared by all fields and recomputed when the
version changes) are allocated in the constructor: about 5 bytes × cells × capacity (2.6 MB for 32
fields on the default map, 160 MB for 32 on a 1024 × 1024 map), plus a 4-byte cell-to-slot index that makes every lookup O(1) instead of a scan of up to 128 slots. A field's *contents* depend only on
the grid and its target, but under the build cap (below) *which* fields are cached decides which
units wait a tick, so the cache's metadata is sim state (Producer decision 2026-10-04, BUG-0021):
`StateHash` covers its clock, count and every used slot's requested cell, version and last-use
stamp, and save/load will save the keys and rebuild the fields at load. `Get` and `TryGetCached`
are `internal`, for the sim only (a call moves the hashed LRU state); views and AI see only
`Capacity`, `Count`, `BuildCount` and `Contains`, which change nothing.

**Build cap (BUG-0018, BUG-0022).** Fetching a field per unit in slot order thrashed the LRU once live goals
outnumbered its slots (goals interleaved by slot evict exactly the field the next unit needs: a
full build per unit per tick, ~320 ms). Now `MovementSystem` sorts Moving units by (goal cell,
slot) into a preallocated `World.MoveOrder` buffer. A build pass then walks the goal groups: a
group needs a field if one of its units stands on the map outside the goal cell; a needed field
that is cached is touched (so a build evicts a field nobody used this tick whenever one exists),
and a missing one goes into the preallocated `World.FieldMisses` buffer keyed by (the group's
oldest `OrderTick`, goal cell). The buffer is sorted and the first
`MovementConstants.MaxFieldBuildsPerTick` = 2 are built: oldest order first. The units' walk then
only reads cached fields; a unit whose field is still missing waits that tick (still Moving,
velocity 0, position unchanged; it never steps without a field). Units already in their goal cell
need no field. Cost: two builds are about 1.4 ms on the default map in Debug, against the 4 ms
average tick budget. A burst of N new goals in one tick starts its groups over ceil(N / 2) ticks
(32 new goals: the last starts 0.8 s late), and within one burst tick ties break by goal cell, so
the remaining positional bias is bounded by ceil(N / 2) ticks (BUG-0026, S4); while live goals fit
the cache, an order never waits behind a newer one. A re-issued Move gets a new `OrderTick`. With
more live goals than cache slots, fields of groups still walking are evicted by LRU, which in the
build pass means by lowest goal cell touched that tick, not by order age: older groups can stall
behind newer ones and the cap is spent on rebuilds every tick until enough groups arrive
(BUG-0025, S3; fix candidate: evict the live field with the newest order, or wait instead of
evicting). A unit that can't get through gives up after `MovementConstants.GiveUpTicks` (see
"Local movement"), so a blocked group no longer holds its slot forever. One build of a large map still
costs more than the tick budget (512 × 512 ≈ 10 ms in Debug); time-sliced builds are future work.

### Local movement

- **Desired velocity** = flow direction × speed, or direct steering toward the target when it
  is in the same or an adjacent cell with a clear line.
- **Separation** from nearby units (spatial hash query), weighted by overlap, boids-style.
- **Arrival (crowded arrival, M1):** a unit stops within 1 m of the click point, or when it
  touches a groupmate that already stopped there, so a group packs into a blob around the point
  instead of 30 units fighting over one cell. Units also stop when blocked for a short time.
  (Producer decision 2026-10-05, owner may revisit: this replaces formation offsets for M1;
  offsets can return at M2 with group commands.)
- **Shoving:** idle friendly units step aside for moving ones. Holding units don't move.
- **Collision:** hard push-out from blocked cells; soft unit-unit separation.
- **Flying units** (Great Raven) ignore the nav grid and separation from ground units.

**Implementation (M1-4b):** first half only. `Command.Move(player, unit, target)` orders one
unit; a group move is one command per unit, and they share one cached field because they share the
target cell. Applying it sets `State = Moving`, `Goal` = the target, and `GoalCell` = the target's
cell; if that cell is blocked, the goal becomes the center of the nearest passable cell (the same
rule as the field). `MovementSystem.Run` then moves each Moving unit, in (goal cell, slot) order (units don't interact
yet, and which goals got a field this tick is settled by the build pass before the walk, oldest
order first, so the walk order changes no result): in the goal cell, within `MovementConstants.ArrivalDistance`
(`CellSize / 2` = 1 m) of the goal it arrives (Idle, velocity 0), otherwise it heads straight at the
goal. Arrival only counts inside the goal cell, so a unit 0.85 m from its goal across a blocked
corner walks the long way round instead of arriving (BUG-0020). Elsewhere it heads at the center of the
neighbor cell its current cell's direction points to. A straight line to that center stays inside
the current cell, that neighbor, and (for a diagonal) the two side cells, which no-corner-cutting
keeps passable, so it can't clip a cliff corner. The step is `min(Speed, distance to the aim
point)`, so units slow only to land exactly on a cell center. A step whose destination cell is
blocked or off the map is refused: the unit stays put with velocity 0 and keeps its order. A unit
standing on blocked ground or off the map (only possible through a raw `SpawnUnit`) stops, since no
field leads out of a blocked cell. `Velocity` is the step taken and `Facing` its `SimMath.Atan2`.

**Implementation (M1-4d-1):** steering, separation, crowded arrival and giving up. `MovementSystem`
now runs two passes over the sorted Moving units: *plan* reads only start-of-tick positions,
states and velocities and writes each unit's outcome and step into scratch on `World`
(`PlannedStep`, `PlannedAction`, `PlannedRemaining`); *apply* then moves units and changes states.
So separation is symmetric and no result depends on which unit is walked first. Per unit, plan:

- **Neighbors:** one `Spatial.QueryRadius` of own `Radius` + the largest radius in the data
  (`World.MaxUnitRadius`) + `MovementConstants.AvoidRange` (1 m), into `World.Neighbors` (sized to
  `UnitCapacity`, so never truncated). Neighbors that walked last tick are *walkers*; the rest
  (idle, waiting for a field, refused) are *standing*. A standing Idle unit with the same
  `GoalCell` is an arrived *groupmate*.
- **Aim:** in the goal cell, the goal; when the goal cell is a legal 8-neighbor step (both side
  cells passable for a diagonal), the goal itself (adjacent-cell steering: the line stays inside
  the same cells); otherwise the next cell's center, or the one after it if a standing unit covers
  that center (walking at it would pin the unit against it).
- **Separation:** for each overlapping walker or groupmate, a push away weighted by overlap depth:
  half the overlap against a walker (`SeparationShareMoving`; the walker takes the other half),
  all of it against a groupmate (`SeparationShareStill`). The summed push is capped at the
  deepest single share, so a packed unit doesn't overshoot.
- **Sidestep:** a standing unit ahead, or a walker ahead coming the other way, within touching +
  `AvoidRange` adds a sideways step away from its side (dead ahead: to the right), weighted by
  closeness. Two units meeting head-on both step right and pass, even in a 1-cell corridor.
- **Walls:** standing units that aren't groupmates are hard: `Constrain` removes the part of the
  step that would end inside one (collide and slide). M1-4d-2 changed two things, below: it no
  longer pushes out of an existing overlap, and friendly Idle units that can be shoved yield.
  Other players' units that aren't walking (Idle) are *hard* walls: a step never goes deeper into
  one, also when the unit touches several walls at once (BUG-0035, below).
- **Step:** flow step + sidestep + push, clamped to `Speed`, then constrained. A step that would
  end in a blocked cell, off the map, or across a blocked corner is refused; the unit then tries
  sliding along the wall (the step's x or y part alone, whichever gains more toward the aim), then
  the flow step alone, else stays put.
- **Arrival:** within `ArrivalDistance` of the goal inside the goal cell, or overlapping an
  arrived groupmate reached through a legal step (never across a blocked corner): the unit stops
  if no neighbor's center is closer than `ArrivalSpacing` (0.6) × the radii's sum, else it backs
  off along the push alone and tries again next tick. So a stopped unit overlaps its neighbors by
  at most 40%, and the group packs into a blob around the point. An arrived unit keeps its
  `GoalCell`, which is what makes it an anchor for later groupmates.
- **Giving up:** progress is measured on an estimate of the path left: the current cell's field
  cost (meters) less the way already made from the cell's center toward the aim. A walking tick
  is progress if the estimate after the step beats the unit's best so far (`UnitStore.BestRemaining`)
  by `StuckFraction` (0.25) × speed; otherwise `UnitStore.StuckTicks` counts up, and at
  `GiveUpTicks` (20 ticks = 1 s) the unit goes Idle with `GoalCell = -1` (so it never anchors a
  blob). A best that only falls means jostling back and forth, even across a cell edge, never
  resets the count. Backing off counts as a stuck tick too (it leaves the best alone), so a
  back-off refused by a wall, or one swinging in and out at a blob's edge, ends (BUG-0027): a unit
  that reaches `GiveUpTicks` while backing off is at its goal or touching its blob, so it stops
  there, crowded, and *keeps* its `GoalCell`. Waiting for a field counts as neither progress nor
  stuck. A `Move` to a new goal cell resets both fields. A `Move` to the goal cell the unit already
  has is the same order (click spam, AI refreshes; BUG-0029): an arrived (Idle) unit stays put, and
  a Moving one takes the new point but keeps its order tick and stuck count. Since M1-4d-3
  (BUG-0030) an Idle unit's "same order" needs the new point within `ArrivalDistance` of its stored
  goal; a point farther away in the same cell is a new order, and a Moving unit's retarget moves its
  best estimate by exactly what the new point changes in the estimate where it stands (see
  "Implementation (M1-4d-3)"). Units stopping off
  the map or on blocked ground also drop their goal. A tick without progress while a groupmate
  just ahead is still making progress is *queued*, not stuck (M1-5, below).

**Implementation (M1-4d-2):** shoving, still inside the two passes. Rules:

- **Who is shoved:** an Idle unit of the walker's own player that is not its arrived groupmate.
  Moving units (walking, or waiting for a field) and other players' units are never shoved.
  - A unit with no goal (never ordered, or gave up) is pushed straight away from the walker.
  - A unit holding another goal (it arrived there) is pushed too, but `KeepLinks` scales its step
    down so it stays touching every Idle groupmate it touches (half the slack toward one that is
    shoved too): the blob bends, it isn't cut apart.
  - A unit standing on its point (within `ArrivalDistance` of its goal) holds it, except when it
    stands there *alone* (touching no groupmate) and the walker has been stuck for
    `MovementConstants.PushAfterStuckTicks` (10) ticks: then it is pushed like a unit with no goal
    (BUG-0033: a unit parked in a 1-cell corridor blocked its own army). While a walker moves only
    by pushing such a unit, its stuck count holds (neither resets nor grows).
  - Measured, why: pushing blob members freely (round 1) let shoves carry a point's units away and
    un-anchor whole blobs; 2,500 units to 4 points arrived 264, below the 563 with no shoving.
- **Plan:** `Constrain` treats a shovable unit as a wall that yields as far as its own speed carries
  it along the push (only if that step is legal for it), so the walker may press that far into
  it. A walker stuck for `PushAfterStuckTicks` plans a second step in which lone parked units yield
  too, and takes it if it moves forward. After its step is final, a walker (also when backing off) adds to `World.ShoveStep[j]` the
  overlap its step would leave with each shovable neighbor, past touching, measured on
  start-of-tick centers (so an existing overlap is shoved out too), in that unit's shove
  direction. Shoves accumulate in the sorted walk order, so each sum is the same every run.
- **Apply:** after every walker has moved, each shoved unit takes its sum clamped to its own
  `Speed`, kept touching its groupmates (`KeepLinks`), trimmed so it ends no closer than `MovementConstants.ShoveSpacing` (0.5) × the radii's
  sum (plus 0.1 mm, so float rounding of positions never lands it a hair inside) to any unit standing still (half the room toward one also being shoved; without this,
  walkers pressed friendly units on top of each other), and so its disk goes no deeper into a
  blocked side neighbor of its cell. A step that ends in a blocked cell, off the map or across a
  blocked corner is refused (it stays put). All steps are worked out before any shoved unit
  moves, from positions after the walkers moved. A shoved unit stays Idle with velocity 0, no
  order, and its `OrderTick` unchanged; `ShoveStep` is zeroed as it is applied (no allocation).
- **Anchors:** every goal group that had a member moved by a shove re-checks, at end-of-tick
  positions, the arrival rule for all its Idle members: a unit keeps its `GoalCell` only if it is
  in the goal cell within `ArrivalDistance` of its goal, or linked to such a unit by a chain of
  touching Idle groupmates (each link a legal step); the rest get `GoalCell = -1`. A whole-group
  check, not one per shoved unit, because a shove can also cut off the units that touched the blob
  only through the shoved one. A unit that lost its goal can be sent back by re-issuing the Move;
  since M1-4d-3 it also walks back once by itself (see "Implementation (M1-4d-3)").
- **Constrain (BUG-0031):** it removes only the part of a step that goes deeper into a standing
  unit than touching; an existing overlap is no longer pushed out, so a unit moves at most its own
  step. Before, every candidate step of a unit overlapping a standing unit with a cliff behind it
  was pushed into the cliff and refused, whatever its order.

No new hashed state: `ShoveStep` is zero between ticks, and the shoves themselves show in the
hashed positions and goal cells. The two passes are still order-independent for shoves (a test
crosses a walker column through an idle crowd in both spawn orders, bit-equal), but a walker's own
push, sidestep and wall trims still sum neighbors in ascending slot order, so where it touches three
or more units at once its last bit can depend on slot order. Same seed and commands give the same hash.

Measured (M1-4d-2, Debug). Arrived counts are `MoveScenario.Arrived` (Idle, holding the goal, linked
to the point). The test scenarios alternate two players by slot; the "1 player" figures are the same
draws with every unit owned by player 0 (QA's `ShoveQaTests`). "No shoving" is this code with
shoving switched off (the `Constrain` fix only). Criterion 6 of the task asked for the targets shown.

| Scenario | Target | Before (M1-4d-1) | No shoving | M1-4d-2, 2 players / 1 player |
| --- | --- | --- | --- | --- |
| 500 units to 4 points 6 m apart, arrived | 80% | 78 | 112 | 174 (35%) / 219 (44%) |
| 2,500 units to 4 points 6 m apart, arrived | 60% | 329 | 563 | 891 (36%) / 996 (40%) |
| 500 units, 500 random goals, gave up | 3% | 59 / 52 | 56 / 44 | 30 (6.0%) / 28 (5.6%) |
| 128 units, 64 neighboring goals, gave up | 5% | 41 | 29 | 26 (20%) / 23 (18%) |
| 500 units to 1 point / 2,500 to 1 point | | all / almost all | | all / almost all |

Every target is missed, with one player as well as two, so enemies are not the main cause (an
earlier version of this section said they were; QA showed otherwise, BUG-0032). What is:

- **The flow field ignores units.** Walkers aim straight through other groups' blobs instead of round
  them. With 4 points 6 m apart and 125-625 units each, the blobs overlap each other's points, so
  most of a group arrives from across another group's blob.
- **Blobs only bend.** Units holding a goal are shoved only as far as they stay touching their
  blob, and units on their point hold it; otherwise shoves un-anchor blobs and fewer units count
  as arrived (see above). So a walker meeting a dense blob of another goal still often gives up.
- **Units waiting for a field are walls.** Under the build cap (2 fields per tick) hundreds of
  units stand Moving and waiting in the random-goal row (about 1.4 million unit-ticks), and Moving
  units are never shoved.
- **Enemies are walls** in the two-player rows, and are never shoved.

Getting near the targets likely needs unit-aware routing (or a flow-field cost for crowded cells),
or letting units pushed off their blob walk back to their goal; both are outside this task.

Known limits: walkers crossing a settled blob of mixed owners mostly give up (QA's crossing scenario,
seed 73: 2 of 200 arrive, 28 with no shoving): they press into friendly blob units that can't
yield far, where without shoving they would slide round. Same-owner blob: 12 and 29 of 200 (0 and 9
before shoving). Units that stop at the back-off limit, or give up, can still overlap a neighbor by
more than 40%. Cost (Debug): 500 units converging on one point average about 0.1 ms per tick, 2,500
about 0.7 ms; a 500-unit tight blob 0.1 ms, 2,500 about 2.7 ms; 200 walkers crossing a settled
300-unit blob 0.2-0.3 ms.

**Implementation (M1-5): queued walkers don't give up.** A crowd funneling through a ramp or a gap
between cliffs pins some units against the corners while the rest of the group streams past. Such a
unit makes no progress for more than a second although its group is moving, and under the M1-4d-1
rule it gave up. Now a walking tick without progress is *queued* (count holds) instead of stuck
(count up) when, at the start of the tick, a neighbor with the same `GoalCell` is ahead of the unit
(in front of its walking direction), within touching + `AvoidRange`, walking, and made progress
last tick. "Made progress last tick" reads the neighbor's `StuckTicks == 0`: only a progress tick
resets the count while a unit is Moving, and a queued tick holds the count at no less than 1, so a
queued unit never passes the signal on. Every hold therefore traces back to a real progress tick, and
each order has finitely many (each beats the best estimate by `StuckFraction` x speed), so a jammed
group still gives up once nobody in it moves forward. The test that pins this
(`CrowdJammedAtAGapPluggedByAnEnemy_QueuedTicksHold_ButAllGiveUpInBoundedTime`, BUG-0036) jams 60
units at a 1-cell gap plugged by an enemy, asserts queued holds do happen, checks every tick that a
Moving unit reads `StuckTicks == 0` only right after a new best and that no best ever rises, and
bounds the time until all give up; dropping either guard of the rule fails it. (The older six units
in a 1-cell corridor behind an enemy never queue a tick: the units ahead are blocked, so standing.)
Back-off, push and wait ticks are unchanged. No new state: it reads the hashed
`StuckTicks` and velocities from start-of-tick state, like the rest of the plan pass.

**Measured (M1-5), the M1 headline scenario** (`ScenarioTests`, `CrossMapScenario`, Debug). An army of
200 units, every shipped type (14), one player, spawns at random points of the cells within 12 path
cells of the passable cell nearest the middle of the west edge (level 0). The goal is the center of
the passable level-1-or-higher cell with the greatest path cost from that start cell (ties to the
lowest index), so the route climbs at least one ramp. **Time limit** = 2 x the undisturbed walk of
the slowest shipped unit over the longest start-to-goal path: `2 * ceil(maxCost * CellSize /
slowestSpeedPerTick)` ticks, with `maxCost` the largest field cost (cells) from any unit's spawn
cell to the goal and the speed read from the data (catapult, 2.2 m/s), so a data change moves the
limit with it. Seeds 1-8: paths of 192-304 m from the start cell (longest spawn 212-311 m), one level
change and 4 ramp cells each (seed 14 of 30 climbs to level 2: 2 changes, 8 ramp cells); the army
settles in 1,438-2,534 ticks against limits of 3,858-5,656 (at most 45% of the limit); all 200
arrive, none gives up, no unit is ever on blocked ground, the pack check passes, and two sims with
the same seed and commands hash equal at every 100-tick checkpoint. A whole crossing allocates 0
bytes. Give-ups before the queued rule: over seeds 1-30, 23 units with one player (9 of 30 runs
lost 1-9) and 19 with owners alternating 0/1 (4 runs); all but one on level-0 ground, at gaps between
plateaus and corners of ramp walls (the ones traced were pinned there while their group flowed
past), none on a ramp itself; after it: 0 and 0. Two of those
60 runs (seed 14 one player, seed 30 two players) end with a pair closer than half their radii's
sum: a unit stopped at the back-off limit (the known limit above, unchanged by this rule). The
two-owner row arrives 200 of 200 for seeds 1-8.

Re-measured with the queued rule (before, after): a 3-cell gap, 100 units, seeds 1-10: 18 gave up,
0. Funnel fuzz (100 seeds x 60 units through gaps): 13, 5 given up of 6,000. 2,500 units to 4 points
arrived 891, 923 (two players) and 996, 1,105 (one player); 500 units to 4 points 174, 170 and 219,
215; 2,500 to 1 point 2,493, 2,499; the random-goal rows (30 / 28) and the 64-goal row (26 / 23) are
unchanged. Same-owner blob crossing: 12, 43 and 29, 28 of 200. Cost: units that used to give up now
keep walking, so the 2,500-unit tight blob averages about 3.6 ms per tick (was 2.7; the target is
4.5 ms), 1,000 walkers crossing a 1,500-unit blob 3.3 ms (was 2.0), the enforced 200-walker row
0.3 ms (unchanged), 500 moving units 0.14 ms (target < 4 ms).

**Fix (BUG-0035): enemies are hard walls.** `Constrain` clips a step against each wall once, in
slot order. Clipping against one wall can carry the step back into a wall already passed: a walker
between two standing units slid along the second straight into the first, and so worked its way
through a slit far narrower than itself. Three wide enemies plugging a 3-cell gap (0.2 m slits) let
20-27 of 60 walkers through per seed, overlapping an enemy by up to 1.17 m. Now, after the clips,
the step is checked against every *hard* wall it touches (a unit of another player that isn't
walking: Idle now, holding or fighting later); if it still enters one, the step becomes the one
closest to the desired step that all hard walls allow: the desired step, its projection onto one
wall's line, or the corner of two walls' lines, whichever is allowed and closest (zero always is).
No allocation: the hard walls' normals and limits go in two `World` scratch arrays. Tests: 2- and
3-cell gaps plugged by enemies, every walker gives up and none passes; every tick, a walker ends no
deeper into an enemy than it started the tick.

The army's own standing units, and units mid-move standing for a tick (waiting for a field,
refused), keep the single clip: a walker pressed between two of them may still slip through.
Holding those to the exact rule too was measured and rejected: 2,500 units to 4 points (two players)
arrived 680-854 (the floor is 825), 500 random goals gave up 38-44 (cap 35), the 64-goal row 37
(cap 28). Holding all Idle units hard, or all enemy units including waiting ones, also failed rows.
Producer decision (M1-5), owner may revisit: enemies holding their ground are hard walls; the
army's own standing units stay soft (single clip, shovable). Two known gaps (both fixed in M1-4d-3, below), both S3 for the M1
hardening session: an Idle enemy that holds the walker's own goal cell is treated as an arrived
groupmate (every groupmate check reads `GoalCell` only, never `Owner`), so it is no wall and the
walker "arrives" up to 0.16 m into it (BUG-0037); and when the hard-wall fallback fires it restarts
from the desired step against hard walls only, so the single clip of a friendly standing unit is
lost that tick (up to 0.14 m into an anchored friendly, BUG-0038).

Re-measured (before, after the fix; Debug): two players, 2,500 units to 4 points arrived 923, 859
(floor 825); 500 to 4 points 170, 182; 2,500 to 1 point 2,499, 2,499; 500 random goals gave up 30,
30 (one player 28, 28); the 64-goal row 26, 22 (one player 23, 23); funnel fuzz 5, 5 of 6,000. The
cross-map scenario (one player) is unchanged: all 200 arrive in 1,438-2,534 ticks over seeds 1-8,
as is its two-owner row. One-player rows also move, by the 0.1 mm shove margin alone (above), which
shifts trajectories chaotically: 2,500 to 4 points 1,105, 1,121; 500 to 4 points 215, 184; the
same-owner blob crossing 43, 15 and 28, 28 of 200 (with a 0.05 mm margin instead: 1,160, 198, 12
and 29), so these rows vary by that much from noise alone. Cost: the 2,500-unit tight blob averages
3.8 ms per tick (target 4.5), 1,000 walkers crossing a 1,500-unit blob 3.1 ms, 500 moving 0.15 ms.

**Implementation (M1-4d-3): crowd routing, first part.** Local rules only; flow fields stay
unit-agnostic (a crowd cost in the fields is future work). The rules as built:

- **Detour (BUG-0028).** While a walker follows its field (aiming at a cell center, not at the goal
  itself), the standing units in the way of its line to the aim (walls: Idle units that aren't its
  groupmates, and Moving units that didn't move last tick, except its own group's, which it follows)
  each block an interval of directions: the directions in which its disk would run into theirs, the
  whole half-plane for one it already touches, plus `MovementConstants.DetourMargin` (0.05 rad). The
  intervals that chain to the straight line form one block; the walker turns to the nearer edge of
  the block (the shorter arc toward the aim), ties to the right. A side is taken only if the turn is
  at most `MaxDetourTurn` (90 degrees) and the ground is open by the walkers' own rule (the first
  step legal, the tangent point and the point level with the edge wall on passable cells), else the
  other side is tried, else no detour. The block is a union of intervals and its edges a min and a
  max, so the side doesn't depend on slot order; the walls are collected in the neighbor loop
  (no extra pass), the sidestep and queuing are worked out again along the new direction on the
  ticks a detour turns. Near the goal (aiming at the goal itself) there is no detour: steering round
  the units packed there cost arrivals (128 units to 64 goals gave up a median 31.5 with the detour
  everywhere, 24 with it only on the way).
- **Order-free sums.** A walker's separation push and sidestep are summed in 64-bit fixed point
  (2^32 units per meter, a power of two so the scaling is exact), so they no longer depend on the
  slot order of its neighbors: the detour test and the M1-4d-2 column-through-a-crowd test give
  bit-equal positions in both spawn orders (the column used to differ in the last bit of a velocity
  once it touched three units).
- **Queued, widened (BUG-0028 b).** A no-progress tick is queued (count holds, at least 1) when, at
  the start of the tick, a walker ahead or touching, within touching + `QueueRange` (2 m; was 1 m and
  groupmates only), made progress last tick, or a unit ahead is waiting for its field (Moving,
  standing, outside its goal cell, field not cached; `FlowFieldCache.Contains` reads no LRU state).
  Moving units are still never shoved. Every hold still traces back to a real progress tick, so the
  termination argument of M1-5 holds. Crossing groups used to jam for a second at each other's
  streams and give up; the 2 m reach fixes a unit pinned in a ramp-wall corner while its group
  streamed past just out of reach (cross-map seed 1).
- **Deep overlaps push out.** Two units closer than `ShoveSpacing` x their radii's sum always get the
  half-share separation push, whatever they are doing. Two of a group pressed together and both
  standing took each other for walls (walls never push out of an overlap) and froze at the back-off
  limit inside the pack rule (cross-map seeds 4 and 25 of 30).
- **Walk-back (once per order).** A unit the anchor re-check cuts off its blob (`GoalCell` set to -1
  while it still has a `Goal`) becomes pending (`UnitStore.WalkBack`, hashed). Once nobody has shoved
  it for `WalkBackDelayTicks` (= `GiveUpTicks`, so a walker still pushing it shoves it again or gives
  up first), it walks back: Moving to its stored goal and that goal's cell, stuck count and best
  estimate reset, order tick kept. Then its walk-back is used until a new order, so a corridor can't
  bounce it forever (`CrowdRoutingTests.UnitShovedOffItsPoint_WalksBackOnce_AndNoMore`).
- **Chain shove; parked lines yield (BUG-0033).** A walker's shove also moves the line of touching
  Idle units of its player ahead of the shoved one (along the push), at most `MaxChainShove` (3)
  units: goal-less units always, units holding a goal only for a walker stuck for
  `PushAfterStuckTicks`, and only if the line holds every touching groupmate of its members (so a
  blob is never cut). That same test replaces the lone-anchor rule: a unit on its point yields to a
  blocked walker only if its whole line yields, so a pair parked in a 1-cell corridor is pushed along
  and the walker gets through, while a blob's point holds. The line goes along the push; sideways
  yielding (the brief's "where there is room") isn't built: in the open the detour goes round. A
  chain stops at a unit touching another player's standing unit: crowds behind an enemy plug
  otherwise squeezed given-up units past it.
- **Owner-aware groups (BUG-0037).** Every groupmate test (plan, walls, the covered-aim check, the
  queued check, the anchor re-check, `KeepLinks`, the chain) compares `Owner` as well as `GoalCell`:
  an enemy holding the walker's goal cell is a hard wall, and nobody anchors through it.
- **Fallback keeps friendly clips (BUG-0038).** When the hard-wall fallback fires, `ClosestAllowed`
  runs over every wall the step touches, the army's own standing units too (walls whose limit is at
  least twice the desired step can't bind and are left out). The single clip of a friendly is no
  longer lost.
- **Same-cell re-orders (BUG-0030).** See "Giving up" above. The estimate depends on the goal point
  only where the unit aims at the goal itself (in its goal cell or a legal step from it); there the
  best estimate moves by exactly the new point's change at the unit's position, elsewhere not at all.
  So a jittered re-order can't count as progress, and a re-order of a walking unit costs it nothing
  (fix round 1, BUG-0043: lowering the best by 2 x the shift made walking units give up mid-route).

**Fix round 1 (QA findings BUG-0042..0049).**

- **Queuing behind a field wait counts slowly (BUG-0048, S1).** Behind a unit waiting for its field a
  no-progress tick still counts toward giving up one tick in `QueueOnWaitStride` (4). Waiting itself
  counts nothing, so two units waiting in turn and each queuing behind the other held each other
  forever when live goals outnumber cache slots (64-goal row, seed 51: 94 units Moving for 20,000+
  ticks, 2 field builds every tick). Now seed 51 stops at about tick 500 (73-77 of 128 give up; base
  92). Holds behind walkers that made progress still hold fully: those trace back to progress ticks.
  Cost (BUG-0050): 500 units to 500 random goals give up a mean of 25 over seeds 1-10 (16 before; base
  40 on QA's harness). Measured alternatives, fix round 2: never counting behind a wait gives 15 but
  never terminates under churn (units whose field is never built keep their neighbors holding);
  counting 1 tick in 8, 16 or 32 gives 23-25; holding fully only while the waiter's order is younger
  than 100 or 300 ticks gives 25-26 and worse 64-goal maxima. No bounded rule tried recovers it.
- **A unit walking back never pushes an arrived unit off its point (BUG-0042).** The pair a walker
  pushed through a corridor walked home and shoved the walker back off its goal. A unit whose
  walk-back is used doesn't get the `PushAfterStuckTicks` push; it waits behind or gives up.
- **No sidestep or detour into a wall in a 1-cell passage (BUG-0042).** Inside a cell walled on two
  opposite sides, a walker sidesteps a standing unit only toward a side where its center could pass
  it, and doesn't detour at all; it presses on and pushes the parked line along. Sidestepping there
  wedged the walker against the wall at a slant, past a small unit and onto a wide one, and its
  shoves drove the pair into the wall. Corridor pair on QA's 20 varied seeds: walker at its goal once
  everything settles on 20 of 20 (base 1, first M1-4d-3 version 6). The same room check in the open
  cost crowd arrivals (2,500 to 4 points, seed 6: 699 -> 547), so it applies in passages only.
- **Enemy plugs hold (BUG-0045).** A unit overlapping another player's standing unit that is part of
  a plug may only move within 45 degrees of straight away from it: walkers get two cone half-planes in
  the hard-wall constraints, shoves are refused otherwise; and a shove's final step (after the wall
  trim, which could undo the squeeze trim) may not go past the pack limit into it. A plug is the enemy
  alone in a 1-cell passage, or a line of standing enemies, each too close to the next for the unit to
  pass between, that reaches blocked ground on two opposite sides (each wall gap under the unit's
  diameter too) within `MaxPlugSpan` (4) units: corridors 1-3 cells wide plugged by one wide enemy per
  row hold (fix round 2; QA's 2- and 3-cell rows). Sliding round an enemy while inside it carried units
  through plugs. Applied to every enemy, or to any enemy next to blocked ground, it cost the two-player
  crowd rows (500 to 4 points min 119 / 187 against 206): enemy blobs pressed against one cliff are no
  plug, since their walls are all on one side.
- **Recorder (BUG-0047).** `ReplayRecorder.ToReplay` refuses a recording longer than
  `Replay.MaxTickCount` (24 h) instead of writing a file `Replay.Validate` refuses.
- **Perf (BUG-0044).** The per-neighbor helpers of Plan's loop are written out inline (Debug builds,
  where the tick budget is measured, call every helper), the passage test runs only when needed, and
  the enemy shove check only when a standing enemy is in reach.

New hashed state: `UnitStore.WalkBack` (in `StateHash`, so in every replay checkpoint, and in QA's
reflection audit). New scratch on `World` (derived, not hashed): `HardWalls`, `ChainMembers`,
`DetourLo`/`DetourHi`/`DetourWall`. Tunables: `DetourMargin`, `MaxDetourTurn`, `MaxChainShove`,
`QueueRange`, `WalkBackDelayTicks`, `QueueOnWaitStride`, `MaxPlugSpan` in `MovementConstants`.

**Both players at one point are enemies now.** `MoveScenario.Spawn` alternates owners in spawn
order, but commands apply sorted by (player, sequence), so player 0 takes the low slots and player 1
the high ones; picking a goal by `slot % points` therefore sent *both* players to *every* point (the
crowd rows' "neighboring points belong to different players" never held). Before BUG-0037 both
players' units shared one anchor there; now each point is contested by enemies. Scenarios meant as
one blob per point (all one-point rows, the tight-blob perf row, the spam tests) use one player now;
the 4-point and 64-goal rows give each point one player (`CrowdRows`, neighbors the enemy's), and
the old split stays as a report row.

Measured (M1-4d-3, Debug, this machine; "base" is the M1-6 code measured the same way the same day):

| Row | Target | Now (brief) | Base, same scenario | M1-4d-3 |
| --- | --- | --- | --- | --- |
| 500 units to 4 points, one player per point, arrived | >= 60% | 182 (36%) | PreMix(1404) 206; seeds 1-10 mean 235, min 164 | PreMix(1404) 220 (44%); seeds 1-10 mean 256 (51%), min 230 |
| same, both players at every point (the old split) | | 182 (36%) | 182 | PreMix(1404) 121 (24%); seeds 1-3: 135, 197, 185 |
| 2,500 units to 4 points, one player per point, arrived | >= 50% | 859 (34%) | PreMix(3404) 740; seeds 1-10 mean 720, min 494 | PreMix(3404) 895 (36%); seeds 1-10 mean 853 (34%), min 699 |
| same, both players at every point | | 859 (34%) | 859 | PreMix(3404) 529 (21%); seeds 1-3: 429, 500, 398 |
| 500 units, 500 random goals (seed 5018), gave up | <= 3% | 30 (6%) | 13 (2.6%); one player 10 | 16 (3.2%; 11 before fix round 1); one player 8 (1.6%); QA, seeds 1-10: mean 2.3% |
| 128 units, 64 goals, one player per goal, gave up, seeds 1-40 | median <= 10% | 22-26 on one map | median 28 (22%), max 54 | median 24 (19%), min 17, max 42 |
| same, both players at every goal | | | | median 38 (30%), max 50 |
| 200 walkers crossing a settled 300-blob, PreMix(73): mixed / same owner, arrived | >= 100 / >= 150 | 2 / 15-28 | 3 / 15 | 111 / 22 |
| Parked friendly pair in a 1-cell corridor | arrives | gives up | gives up | arrives |
| Cross-map seeds 1-8, one player | 200/200, 0 give up | 200/200 | 200/200 | 200/200, 0 give up, 1,185-2,525 ticks (at most 40% of the limit); seeds 1-30: 0 give-ups, 0 pack violations |
| Cross-map seeds 1-8, two owners (report) | | 200/200 | 200/200 | 100-142 arrive: both players to one goal contest it |
| Perf: 500 moving avg | < 4 ms | 0.15 ms | 0.16 ms | 0.17-0.18 ms |
| Perf: 2,500 tight blob avg, one player | <= 4.5 ms | 3.8 ms | 4.44-4.55 ms (this machine, fix round 1) | 4.61-4.66 ms (+3%; first version +11%) |
| Perf: 2,500 tight blob avg, two players (report) | | | 4.44-4.47 ms | 6.57-6.66 ms (a contest of enemies since BUG-0037: more units stay Moving) |
| Perf: 1,000 walkers crossing a 1,500 same-owner blob (report) | report | 3.1 ms | 6.27-6.40 ms (probe, 400 ticks) | 14.6-14.8 ms: 2.1x the walking unit-ticks (291k vs 140k: walk-backs, detours and holds keep units walking), +9% per walking unit-tick |

Rows below target, and why (miss rule: each beats its base on the same scenario):

- **The 4-point rows** stay far from 60% / 50%. The four blobs merge into one mass about 12 m across,
  and a unit arriving from the far side of it has to go round the whole mass: the field (unit-agnostic)
  points through it, the detour only sees the 3 m round the walker, and the 1 s give-up runs out. A
  crowd cost in the fields, or routing to the near side of one's own blob, is what would move these.
  With the old split (both players at every point) the rows fell below "now" (121 and 529): that is
  the BUG-0037 fix, not the routing (each point is a contest of enemies now).
- **The 64-goal row**: neighboring goals of the other player are hard walls and arrived units of
  one's own other goals only bend, so a unit whose goal is surrounded gives up; median 19%.
- **Same-owner blob crossing**: 22 of 200. The blob is 20 m across and dense; its members only bend
  (`KeepLinks`), and going round it takes longer than a give-up. Letting a blocked walker shove blob
  members freely (walk-back would repair them) was measured: no change here, fewer arrivals in the
  two-player rows.
- **Perf**: walkers now hold their count while traffic round them still progresses, and blob units
  walk back, so more units are walking on any tick. One-player tight blob: +3% over the base on the
  same machine (4.6 vs 4.5 ms here; the base itself is at 4.5 today, 3.8 when the target was set).
  The 1,000-walker crossing (no walker arrives in either version) costs 2.3x per tick, almost all of
  it from twice as many walking unit-ticks.

Re-bounded tests (BUG-0039): the crowd stress rows run on plain seeds (one point: seeds 1-3 and 1;
4 points: seeds 1-10 each) and assert bounds that hold on every swept seed (500 to 4 points >= 40%,
2,500 >= 22%); the 64-goal dev test and QA's twin sweep seeds 1-40 (build cap, termination, every
Idle unit arrived or gave up, at most 48 of 128 give up); its pack rule breaks on 15 of 40 maps
(worst pair 0.768 m against 0.8 m) and is report-only. `TestSeeds.PreMix` is gone from these rows.
BUG-0034: the distinct-targets perf row averages 100 ticks, budget unchanged.

## Orders and unit states

Orders: `Move`, `AttackMove`, `Attack(target)`, `Hold`, `Stop`, `Patrol`, `Gather(node)`,
`ReturnCargo`, `Build(site)`, `Repair(building)`, `Cast(ability, target)`. Shift-queued orders
append to the queue; unqueued orders replace it.

Unit states: `Idle`, `Moving`, `Chasing`, `Attacking` (wind-up / cooldown), `Gathering`,
`Returning`, `Building`, `Casting`, `Dead`.

Target acquisition: idle, attack-moving, holding, and patrolling units scan for enemies within
sight every 4 ticks (staggered by index). Priority: enemies attacking me > units that can attack
> other units > buildings; then nearest. Units retaliate when hit while idle.

## Combat implementation

- An attack starts when a target is in range and the cooldown is ready. At the wind-up point,
  melee damage is queued, or a projectile spawns. Damage applies in phase 11, so tick order never
  decides who "shot first" inside one tick.
- Projectiles store origin, impact point, speed, damage payload, and splash/friendly-fire flags.
  Hit/miss is resolved on arrival (see [02 Projectiles](02-game-design.md#projectiles)).
- The damage formula lives in one function (`DamageCalc.Compute`) with exhaustive unit tests,
  including the worked example from 02.

## Economy implementation

- Resource nodes are entities with remaining amount and a gather slot list (for queuing visuals).
- Workers run a small state machine: walk to node → gather (timer) → walk to nearest drop-off →
  deposit → repeat. Drop-off choice uses straight-line distance, re-evaluated per trip.
- Production queues: 5 slots per building, resources deducted at queue time and refunded on
  cancel. Population is reserved when training *starts*; a full cap pauses the queue.
- Construction sites are buildings in `UnderConstruction` state with progress; HP grows with
  progress.

## Abilities, statuses, zones

- `AbilityDef` (data) → `AbilitySystem` executes cast timers and resolves `effects[]`.
- `StatusSystem` stores active statuses per unit in a small fixed array (max 8) with
  `(statusId, magnitude, ticksRemaining, sourcePlayer)`. Derived stats (speed, attack speed,
  sight) are recomputed when the status set changes, not every tick.
- `ZoneSystem` keeps active zones; each tick it applies their statuses to units inside (via the
  spatial hash) and marks their cells in a per-player "vision blocker" mask used by the vision
  pass.

## Vision, detection, fog

- Per player: `byte[] Visibility` (0 unexplored, 1 explored, 2 visible) and `bool[] Detected`.
- Every 4 ticks: clear "visible" to "explored", then stamp a precomputed circle mask for each
  unit and building's sight radius, and for each detector's detection radius. Apply zone masks
  (cells inside an enemy Darkness/Sandstorm become not-visible unless the viewer is inside).
- **High ground:** the nav grid stores a `Level` byte per cell (from map data; ramps take the
  lower level). While stamping, a cell is marked visible only if
  `Level[cell] <= viewerLevel || distance <= 4 m || viewer.IsFlying`. This is a per-cell compare
  inside the existing stamping loop, not a raycast, so it costs almost nothing. Attacks fired from
  a higher level apply a 2 s `Revealed`-to-victim flag on the attacker (visibility only for that
  one enemy player).
- Target validity check (used by acquisition, commands, and the AI): the target's cell is
  visible to the attacker's owner, and the target is not stealthed or is detected.
- The AI reads the world only through a `PlayerView` facade that applies these checks.
- **Rendering:** the local player's visibility grid uploads to a 128×128 R8 texture each vision
  update. The terrain shader samples it with smooth filtering: unexplored = black, explored =
  desaturated and dark, visible = full color. Unit views are hidden when not visible. Last-seen
  building ghosts come from a per-player "last known buildings" list in the sim.

## Data format

All definitions are JSON under `game/data/`, loaded by both the game and the tests (tests find the
folder relative to the repo root).

```
game/data/
  common/
    damage_table.json        # type × armor class multipliers
    statuses.json            # status definitions
    rules.json               # starting resources, pop cap, gather rates, age costs
  factions/<faction_id>/
    faction.json             # id, displayName, bonus, palette, resource display names
    units.json
    buildings.json
    techs.json               # forge upgrades (shared ids), faction upgrade
    abilities.json
    ai.json                  # build orders, compositions, attack thresholds per difficulty
  maps/<map_id>.json         # size, seed or heightmap ref, start locations, resources, biome
```

Example unit definition (one entry of the `"units"` array in `units.json`):

```json
{
  "id": "malazan_crossbowman",
  "displayName": "Crossbowman",
  "description": "Fires heavy bolts that punch through armor.",
  "slot": "ranged",
  "model": "malazan/malazan_crossbowman",
  "hp": 55,
  "armor": 0,
  "armorClass": "light",
  "attack": { "value": 9, "type": "pierce", "cooldown": 2.2, "range": 15, "windup": 0.45,
              "projectile": "bolt", "bonusVs": { "heavy": 1.3 } },
  "speed": 3.2,
  "sight": 18,
  "radius": 0.4,
  "cost": { "gold": 36, "wood": 45 },
  "pop": 1,
  "trainTime": 16,
  "trainedAt": "malazan_crossbow_range",
  "requires": [],
  "tags": ["infantry", "biological"]
}
```

- Parsing uses System.Text.Json with source generation (no reflection).
- The loader validates every reference (ids, models, abilities) and reports all errors at once.
  A test loads all shipped data and fails on any error.
- The loader converts seconds to ticks and resolves string ids to ints.
- Stats in data are the **final** faction values (the faction bonus is already applied where it's
  a flat stat change); dynamic bonuses (regen, stealth rules, gather multipliers) are faction
  modifiers in `faction.json`.

### What ships today (M1)

`Rts.Sim.Data.DataLoader.LoadAll(dataDir)` returns a `DataLoadResult`: either an immutable
`GameData` or the full list of `DataError`s (file relative to `game/data/`, field path such as
`units[2].attack.type`, message). It never throws on bad data: a missing file, malformed JSON
(reported with line and byte), or a bad field each become one error, and every file is still
checked. The data-validation test is `DataValidationTests.ShippedData_LoadsWithNoErrors`.

Shipped files: `common/damage_table.json`, `common/rules.json`, and `faction.json` + `units.json`
for `malazan` and `whirlwind`. All five are required. Everything else in the tree above
(`statuses`, `buildings`, `techs`, `abilities`, `ai`, `maps`, other factions) arrives with the
milestone that consumes it.

| File | Shape |
| --- | --- |
| `damage_table.json` | `armorClasses: [{id, displayName}]`, `damageTypes: [{id, displayName, ignoresArmor?, multipliers: {<armorClass>: x}}]`. Every type must list every class. `ignoresArmor` (default false) is how Magic skips armor |
| `rules.json` | docs/02 Economy table: `startingGold`, `startingWood`, `startingWorkers`, `popCap`, `workerCarry`, `gatherRate {gold, wood}` (per second), `startMines` / `expansionMines {count, gold}`, `treeWood`, `nodeSearchRadius`. Pop provided by buildings comes with `buildings.json` (M3); age costs with `techs.json` |
| `faction.json` | `id` (must equal the folder name), `displayName`, `description`, `bonus {displayName, description}`, `resources {gold, wood: {displayName}}`, `palette {primary, secondary, accent}` as `#RRGGBB` |
| `units.json` | `{ "units": [ ... ] }`, entries as in the example. `attack` also takes optional `minRange`, `splash` (m), and `friendlyFire` (docs/02 "Combat / Stats"); they default to 0 / false. Melee range is written as `0.5` (edge to edge) |

Validation rules: ids are `snake_case` and unique (a unit id is unique across all factions);
`slot` is one of the seven template slots; `armorClass`, `attack.type`, and `bonusVs` keys exist in
the damage table; hp, speed, sight, radius, `attack.cooldown`, `trainTime`, and gather rates are
positive; `radius` is within 0.4-1.0 m; `pop` is a multiple of 0.5; every number is checked
against an upper bound before it is narrowed (`DataLimits`: integers at most 1,000,000, decimals at
most 1,000,000, durations at most 3600 s), so nothing overflows to Infinity or a wrapped int
(BUG-0007); every `requires` and `tags` entry is a `snake_case` id (BUG-0009); unknown JSON fields are errors
(a typo must not silently fall back to a default). The only literals in C# are these schema limits,
in `DataLimits`.

Conversions at load: durations (`cooldown`, `windup`, `trainTime`) become ticks,
`round(seconds × 20)` (2.2 s → 44, 0.45 s → 9); speed and gather rates become per-tick values;
`pop` and `popCap` become half-pop integers (1 → 2, 1.5 → 3). String ids become dense ints in
ordinal-sorted order of the id strings (units across all factions, factions, armor classes,
damage types), so ids never depend on file order or file-system enumeration. `GameData` holds
`ImmutableArray`s indexed by those ids; `FindUnit` / `FindFaction` map a string id back by binary
search, for load time, tests, and tooling only.

Not resolved yet (kept as plain strings): `trainedAt` and `requires` (resolved when
`buildings.json`/`techs.json` land in M3/M4), `model` (M2/M6 asset pipeline), and `projectile`
(M4 combat). Unit passives, abilities, detection, and faction modifiers (e.g. Whirlwind's gather
bonus) are also not in the M1 schema; they arrive with `abilities.json` / `statuses.json` and the
systems that use them. Collision radii in the shipped units (0.4 foot, 0.7 mounted, 0.9 siege) are
first-pass values; the faction pages don't list them.

## Rendering and presentation

Scene layout (planned):

```
Main.tscn                 # boot: loads data, shows main menu
Match.tscn
  SimRunner               # owns Simulation, ticks, exposes snapshots/events
  World3D
    Sun, WorldEnvironment
    TerrainView           # MeshInstance3D generated from heightmap; ShaderMaterial (splat + fog)
    PropsView             # MultiMeshInstance3D per prop type (trees, rocks)
    UnitViews             # pooled unit view scenes
    BuildingViews
    ProjectileViews       # MultiMesh per projectile type
    SelectionDecals       # MultiMesh rings + health bars
  RtsCamera               # pitch 55°, pan/zoom, clamps to map
  SelectionController     # picking, box select, command issuing
  Hud (CanvasLayer)       # resource bar, minimap, selection panel, command card, alerts
```

- **Unit views:** a `PackedScene` per unit type (glTF model + AnimationPlayer + team-color
  material slot). Pooled by type. Each frame the view reads interpolated position/facing from the
  snapshot and maps sim state to an animation (idle, walk, attack, cast, work, death). Attack
  animations line up with the wind-up via the `AttackStarted` event.
- **Picking without physics:** ground clicks intersect the camera ray with the heightmap (ray
  march + refine). Unit picking projects unit positions to screen space and tests against a
  per-type screen radius. Box select is a screen-space rectangle test. No collision shapes on
  units.
- **Team color:** a shader parameter on a dedicated material slot (banners, shields, trim).
- **Health bars and selection rings:** MultiMesh billboards/decals, one draw call each.
- **Minimap:** an `ImageTexture` redrawn at 5 Hz from sim data: baked terrain colors, fog,
  resource markers, unit dots, alert pings.
- **Budget:** 60 FPS on a mid-range GPU with ~400 animated units. If skinned animation becomes
  the bottleneck, switch distant or numerous units to vertex-animation textures on MultiMesh.
  Only do this when profiling demands it.

### Implementation (M2-1)

What exists today (no unit views yet):

```
Main.tscn  (Main.cs: prints "Rts.Sim <version>", loads res://data, PushError + exit 1 on DataErrors)
  Match    (Match.tscn, Match.cs: Start(data, options) wires everything below)
    SimRunner        Node; owns the Simulation; FixedStepClock; Seed/PlayerCount/UnitCapacity/CommandCapacity exports (1, 2, 2000, 4096)
    World3D
      Sun            DirectionalLight3D with shadows
      WorldEnvironment  procedural sky, color ambient
      TerrainView    MeshInstance3D; ArrayMesh + vertex-color StandardMaterial3D
    RtsCamera        Camera3D, pitch 55°, no rotation
    DebugOverlay     CanvasLayer + Label (tick, speed, last tick ms, FPS)
    Screenshotter    Node; --screenshot capture
```

Godot starts children before parents, so `Main._Ready` loads the data and then calls
`Match.Start`, after every node of the match exists.

- **Axes:** sim `Vector2(x, y)` on the ground maps to Godot `(x, elevation, y)`; Godot Y is up and
  is elevation in meters. Cell `(cx, cy)` covers `[cx·2, cx·2+2] × [cy·2, cy·2+2]` m
  (`MapConstants.CellSize = 2`, `LevelHeight = 4`).
- **Terrain mesh:** `Rts.Sim.ViewApi.TerrainMeshBuilder.Build(Heightmap)` is pure and only reads
  the heightmap; it returns a `TerrainMesh` (positions, normals, sRGB colors, triangle indices,
  clockwise front faces as Godot expects). Every cell gets its own four vertices. Plateau cells are
  flat at their elevation. A ramp cell is a plane tilted along the axis whose two neighbours
  straddle its height; on that axis an edge meets a plateau neighbour at the plateau's height and
  a ramp neighbour halfway between the two, so the slope runs from the low ground to the plateau lip
  with no step. Wherever two neighbouring cells' heights differ along their shared edge, a vertical
  wall quad fills the gap (cliffs and ramp side walls), facing the lower cell. Colors: one tint per
  level, plus a ramp tint and a cliff tint (placeholders until biome materials). `TerrainView`
  copies the arrays into an `ArrayMesh`.
- **Camera:** state is a ground focus point and a zoom (height above the focus, 20-60 m, default
  40, 4 m per wheel notch). The camera sits `zoom / tan 55°` behind the focus on +Z, looking
  toward -Z, so screen-up is map -Z. Pan: arrow keys, an 8 px screen-edge band (off for scripted
  screenshots and when the window is unfocused), middle-mouse drag; speed is `zoom × 1` m/s. The
  focus is clamped to `[0, Width·2] × [0, Height·2]`. Limits live in the pure
  `Rts.Sim.ViewApi.CameraLimits`. Input actions in `project.godot`: `camera_pan_left/right/up/down`
  (arrows), `camera_zoom_in/out` (wheel), `camera_drag` (middle button).
- **Launch flags** (user args after `--`): `--seed <n>`, `--speed <x>` (clamped 0.25-8),
  `--screenshot <path> --screenshot-after <seconds>` (default 2). Bad values log a warning and are
  ignored. Example: `& $env:GODOT --path game -- --screenshot C:\temp\shot.png --screenshot-after 2`.

## AI architecture

The AI lives in `Rts.Sim.Ai`, inside the sim assembly, because it must be deterministic (it uses
its own RNG stream) for replays to work. A replay only needs the human's commands and the seed.

```
AiPlayer
  BuildOrderExecutor    # walks the faction's ai.json build order
  EconomyManager        # worker targets per resource, houses before cap, expansions
  ProductionManager     # keeps halls busy toward the target composition
  MilitaryManager       # army grouping, defense, attack waves, retreat, focus fire (Hard)
  AbilityManager        # when and where to cast (simple heuristics per ability kind)
  ScoutManager          # early scout, periodic map checks
  Blackboard            # what the AI believes about the world (from its PlayerView only)
```

- Cadence and quality knobs per difficulty come from `ai.json` (think interval, build-order
  slack, wave size multiplier, micro flags, gather bonus).
- The AI never reads `World` directly; it gets a `PlayerView`, which enforces fog and stealth.

## Save/load and replays

- **Replay file** (M1-6, `Rts.Sim.Replays`): a `Replay` holds a header, the command log, and
  checkpoints.
  - Header: format version (`Replay.CurrentFormatVersion` = 1), `SimInfo.Version` (informational,
    not checked on playback), data hash (`GameData.ContentHash()`), seed, player count, unit and
    command capacity (both decide outcomes: the unit capacity is in the state hash and a full
    store drops spawns), checkpoint interval, tick count, and every `MapGenParams` field. There is
    no map id yet: the map is rebuilt from seed + params.
  - Command log: every command `Simulation.Enqueue` accepted, as stamped (tick, player, sequence,
    kind, type id, position, unit handle), in enqueue order. Commands stamped for a tick after the
    recorded span are left out.
  - Checkpoints: `(tick, StateHash())` right after each tick whose new `TickNumber` is a multiple of
    the interval (default 100, 5 s), so exactly `tickCount / interval` of them.
- **Recording:** `new ReplayRecorder(sim)` attaches to a sim that hasn't ticked or queued anything
  and wasn't built on a hand-made map; one recorder per sim. `Enqueue` reports each accepted command
  and `Tick` reports its end (phase 14). The checkpoint buffer is preallocated (default: one hour of
  ticks), so recording adds no allocation to `Tick()`; past that it doubles. The command log doubles
  when full inside `Enqueue`, which input calls between ticks; once the AI enqueues during a tick
  (M5), size the recorder's command capacity for the match. `ToReplay()` snapshots the recording.
- **Text format** (`ReplayFormat`, extension `.replay`): ASCII, LF line endings, one `key value`
  line per header field in a fixed order, `commands N` then N lines
  `c tick player sequence kind typeId x y unitIndex unitGeneration`, `checkpoints N` then N lines
  `k tick hash`, `end`, and last `checksum H`: FNV-1a 64 over every byte before that line, so any
  changed byte is caught. Integers are invariant-culture decimal in canonical form (no `+`, no
  leading zeros); hashes are 16 uppercase hex digits; floats are their exact IEEE-754 bit pattern
  as 8 uppercase hex digits (`0.5` is `3F000000`, NaN round-trips). `TryRead` never throws on bad
  bytes: it returns a `ReplayError` code and no replay. Read rules (`Replay.Validate`): command
  ticks run from 1 to the tick count without going backwards, each player's sequences count 0, 1,
  2, ..., players are below the player count, kinds are known, no tick has more commands than the
  command capacity, and the checkpoints are exactly the interval's multiples. Format limits: at
  most 16 players, capacities up to 1,000,000, and (M1-4d-3, BUG-0040) a tick count and checkpoint
  interval of at most 1,728,000 (24 h at 20 Hz), so a small file can't declare years of playback.
  The interval may exceed the tick count: the recorder writes replays shorter than one interval.
- **Playback** (`ReplayPlayer.Run(replay, data)`): refuses before any tick with
  `FormatVersionMismatch`, another validation code, or `DataMismatch` when the data hash differs.
  Otherwise it builds the `SimConfig`, enqueues each command when `TickNumber == command.Tick - 1`
  in log order, checks the sim stamps it with the logged tick and sequence, hashes with its own
  recorder, and stops at the first checkpoint whose hash differs, reporting the tick and both hashes.
- **Data hash:** `GameData.ContentHash()` is FNV-1a (`StateHasher`) over every field of every def
  in id order, strings char by char (never `string.GetHashCode`). Display text is included, so any
  data edit, a balance change or a rename, refuses old replays. A test changes every field of every
  def type in turn, so a new field that isn't hashed fails it.
- **Save file:** a versioned binary snapshot of the full `World` (stores, RNG states, pending
  commands, AI blackboards) plus the replay log so far. Loading restores the snapshot directly.
- If a replay's data hash doesn't match the current data, the game says it was recorded with
  different balance data and refuses to play it (no silent desync). The sim returns only the
  `ReplayError.DataMismatch` code; the text comes from data (M6 playback UI).

## Testing strategy

| Test kind | What it proves | Runs |
| --- | --- | --- |
| Unit tests | Each system's rules (damage calc, gathering, flow fields, status stacking) | Every change |
| Scenario tests | Small hand-built worlds play out as designed (10 Line vs 10 Shock: Line wins) | Every change |
| Data validation | All shipped JSON loads and every reference resolves | Every change |
| Determinism | Same scenario twice → same hash; different seeds → different hash | Every change |
| Replay golden tests | Recorded replays reproduce checkpoint hashes | Sim changes |
| Perf benchmarks | Tick cost under budget: 500 units, average tick < 4 ms, p99 < 8 ms | `Category=Perf` |
| Headless smoke | Godot boots the main scene, runs an AI-vs-AI match for 600 frames, exits 0 | `game/` changes |
| Visual check | Windowed run + screenshot inspected by Claude | Visual changes |

- Golden replays live in `sim/Rts.Sim.Tests/Replays/`. The first (M1-6) is
  `cross_map_seed1.replay`: `CrossMapScenario` seed 1, 200 units ordered across the map, exactly
  1,500 ticks, checkpoints every 100 (about 16 KB). `ReplayGoldenTests` plays it back and fails on
  the first mismatching checkpoint. When a deliberate change alters outcomes (movement, tick order,
  RNG use, any `game/data` edit), run the tests once with the environment variable
  `RTS_REGEN_GOLDEN=1`: the test rewrites the file and then fails with "golden regenerated; rerun
  without the flag". Rerun without it, and explain why in the commit message.
- `DeterminismTests`: two sims with the same seed and commands keep equal hashes every 100 ticks
  over 2,000 ticks of movement; different seeds differ by tick 100.
- Tests whose bounds or preconditions were measured on one pre-M1-6 map pass their old seed
  through `TestSeeds.PreMix`, which inverts `SimRng.MixSeed` so they get exactly the old streams.
  New tests use plain seeds.
- Perf thresholds are generous (they catch 2× regressions, not 5% noise).
- Every `Category=Perf` test and every allocation-measuring test is in the xUnit collection
  `SerialCollection` (`DisableParallelization = true`), so it runs alone after the parallel batch;
  classes that also hold heavy unmeasured tests put the measured ones in a nested `Serial` class.
  Allocation tests go through `AllocationProbe.AssertZero`, which re-runs the block once after a
  non-zero count and fails only if the re-run allocates too, reporting both counts (BUG-0017,
  BUG-0024). One `dotnet test sim/Rts.Sim.Tests` is the whole check.
- Headless Godot uses the dummy renderer, so screenshots need a windowed run. Since M2-1 the game
  accepts `--screenshot <path> --screenshot-after <seconds>` after `--` so Claude can grab frames
  without an MCP server (see "Debug tooling"). Screenshots are never committed.

## Debug tooling

- **Debug label** (M2-1, always on for now): top-left text with the next tick number, game speed,
  the last `Tick()` cost in ms (`Stopwatch` in the view, never in the sim), and FPS. Dev-only text,
  not player-facing, so it is not in data.
- **Screenshot flag** (M2-1): `& $env:GODOT --path game -- --screenshot <path> --screenshot-after <seconds>`
  waits that many seconds of real time (default 2), saves the viewport as PNG, and quits with
  exit code 0 (1 if the file can't be written). Edge panning is off during it so the mouse can't
  move the shot. Headless runs print "Screenshot unavailable in headless mode" and quit 0 without
  an ERROR line. `--seed <n>` and `--speed <x>` pick the map and game speed.
- **Debug overlay** (F12, dev builds): nav grid, flow field arrows for the selected group, unit
  state labels, tick time graph, entity counts.
- **Dev console** (backtick): `spawn <unit> <n>`, `give <gold> <wood>`, `reveal`, `speed <x>`,
  `ai <player> <difficulty>`, `win`, `lose`, `hash`.
- **Logging:** `SimLog` with categories (AI, Path, Combat, Econ) written to `user://logs/`. In
  headless runs it also goes to stdout so Claude can read it.

## Build and export

- Godot export preset "Windows Desktop" (committed in `game/export_presets.cfg`; Godot 4.3+ keeps
  credentials out of it).
- Export templates for 4.7.x .NET must be installed once per machine (SETUP.md).
- `tools/export.ps1`: builds the solution in Release, runs the tests, exports
  `build/RtsGame/RtsGame.exe`, and zips it as `build/RtsGame-<version>.zip`.
