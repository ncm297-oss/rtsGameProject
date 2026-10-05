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
   (`MovementSystem.Run`, units in (goal cell, slot) order).
10. **Combat:** attack wind-ups and cooldowns, projectile flight and impact, splash.
11. **Damage and death:** apply queued damage, kill entities, emit death events.
12. **Vision and detection:** recompute fog every 4 ticks (5 Hz) per player.
13. **Cleanup:** free dead handles, finalize the tick's event list, bump `TickNumber`.
14. **State hash** (debug builds and tests): a 64-bit hash of all gameplay state.

### Presentation timing

`SimRunner` (a Godot `Node`) accumulates real frame time × game speed. While the accumulator holds
at least 50 ms it runs one tick (max 5 ticks per frame, to avoid a death spiral after a stall).
Views render at `alpha = accumulator / 50 ms` between each entity's previous and current
position and facing. Commands from input are stamped with the next tick number.

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
  seeded like O'Neill's `pcg32_srandom_r(seed, streamId)`. Every stream uses the match seed;
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
  every unit slot's generation and alive flag, every live unit's fields, the free list, all RNG
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
evicting). A unit that never arrives (blocked
forever until the give-up rules) can hold its slot indefinitely. One build of a large map still
costs more than the tick budget (512 × 512 ≈ 10 ms in Debug); time-sliced builds are future work.

### Local movement

- **Desired velocity** = flow direction × speed, or direct steering toward the target when it
  is in the same or an adjacent cell with a clear line.
- **Separation** from nearby units (spatial hash query), weighted by overlap, boids-style.
- **Arrival:** units in a group get target offsets in a loose formation around the click point,
  so 30 units don't fight over one cell. They slow down near their slot and stop when arrived or
  blocked for a short time.
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
Not yet: direct steering across adjacent cells, separation, arrival slots, shoving, giving up when
blocked, `Stop`/`Hold`, and order queues (M1-4d and later). Units sent to one point stack on it.

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

- **Replay file:** header (format version, game version, data hash, map id, seed, player setups)
  + command log `(tick, player, command)` + periodic state hashes. Small (KBs per match).
- **Save file:** a versioned binary snapshot of the full `World` (stores, RNG states, pending
  commands, AI blackboards) plus the replay log so far. Loading restores the snapshot directly.
- If a replay's data hash doesn't match the current data, the game says it was recorded with
  different balance data and refuses to play it (no silent desync).

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

- Golden replays live in `sim/Rts.Sim.Tests/Replays/`. Regenerate with a test flag when a
  deliberate change alters outcomes, and explain why in the commit message.
- Perf thresholds are generous (they catch 2× regressions, not 5% noise).
- Every `Category=Perf` test and every allocation-measuring test is in the xUnit collection
  `SerialCollection` (`DisableParallelization = true`), so it runs alone after the parallel batch;
  classes that also hold heavy unmeasured tests put the measured ones in a nested `Serial` class.
  Allocation tests go through `AllocationProbe.AssertZero`, which re-runs the block once after a
  non-zero count and fails only if the re-run allocates too, reporting both counts (BUG-0017,
  BUG-0024). One `dotnet test sim/Rts.Sim.Tests` is the whole check.
- Headless Godot uses the dummy renderer, so screenshots need a windowed run. The game will
  accept a `--screenshot <path> --screenshot-after <seconds>` debug flag (M2) so Claude can grab
  frames without an MCP server.

## Debug tooling

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
