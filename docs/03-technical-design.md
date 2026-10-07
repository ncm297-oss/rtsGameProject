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
| `tools/Rts.Cli` | `net8.0` console app (M1-8) | Rts.Sim | Headless sim runs: checkpoint hashes, tick timings, replay record/play |

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
3. **Production:** training and research timers, spawning finished units at rally points. (M3-4:
   `ProductionSystem.Run`: every finished building with a queue, in slot order, starts its head item when the owner's
   population has room, counts it, and spawns the unit when it is complete, sending it to the rally point; after the
   spatial-hash rebuild of phase 1 and before construction, so a cap raised by a House finished in phase 4 starts a
   waiting item on the next tick. See "Implementation (M3-4)". M3-5: a queue item may be a tech; it starts at once
   (no population), counts one tick a tick in the same building timer, and on its last tick sets the owner's tech flag
   instead of spawning, so a tech researched in phase 3 is visible to every later phase of the same tick. See
   "Implementation (M3-5)".)
4. **Construction and economy:** building progress, gathering, drop-offs. (M3-2:
   `EconomySystem.Run`: every live unit on a gather loop, in slot order, works, deposits, or starts
   its next walk; walks move in phases 8-9 of the same tick. Runs after the spatial-hash rebuild and
   before phase 7, so a worker that arrived last tick is taken up again before queued orders are
   looked at; see "Implementation (M3-2)". M3-3: then `ConstructionSystem.Run`, builders and
   repairers; see "Implementation (M3-3)".)
5. **Status effects and zones:** expire, tick DoTs and regen, apply zone effects.
6. **Abilities:** cast timers, effect resolution.
7. **Orders and targeting:** order queue advance, target acquisition (staggered scans). (M1-7:
   `OrderSystem.Run`: every live Idle unit with a shift-queued order, in slot order, starts the
   head of its queue, so a queued order given to an Idle unit walks in the tick it applies, and a
   unit that arrives or gives up in phase 9 starts its next leg on the next tick. No targeting yet;
   see "Orders and unit states".)
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
position and facing (facing since M2-7: the short way round, see "Implementation (M2-7)"). Commands from input are stamped with the next tick number.

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
leaves state unchanged (the sequence counter advances only once the command is accepted). Since
M1-9 (BUG-0054) `Enqueue` also refuses a command that isn't well formed (`Command.IsWellFormed()`:
an undefined `CommandKind`, a `Flags` bit other than `Command.QueuedFlag`, or `QueuedFlag` on a kind
that isn't a unit order, i.e. `Noop` or `SpawnUnit`, BUG-0056) with an `ArgumentException`, like an
unknown player: nothing is queued, recorded or hashed, and no sequence number is used, so
everything `Enqueue` accepts can be written to a replay and read back (`Replay.Validate` applies the
same rule). At apply time, `Command.IsValid()` (well formed, plus a finite position for `SpawnUnit`,
`Move` and `AttackMove`) drops what is left, e.g. a NaN ray cast, the same policy as a spawn into a
full unit store; new command kinds add their checks there. Checks that need the world run in `Simulation.Apply`: a `SpawnUnit` whose
`TypeId` is not a unit in `SimConfig.Data` is dropped, and a unit order (`Move`, `Stop`,
`HoldPosition`, `AttackMove`) is dropped when its unit handle is stale, the unit belongs to another
player, or (`Move`, `AttackMove`) the target is outside the map.

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
  requested cell, grid version, block-version tag (M3-2b) and last-use stamp, in slot order): under the build cap the cache
  decides who waits, so it is sim state (BUG-0021, see "Flow fields"). Since M3-2b it also covers
  `NavGrid.BlockVersion` and `World.SeenBlockVersion` (the movement pass's last view of it, which decides
  when progress marks reset; it trails the grid between a closing change and the next pass, so it is
  not derived). Since M3-1 it also covers
  `NavGrid.Version` and the resource store (`ResourceStore.AddToHash`: capacity, high-water mark,
  every used slot's generation and, when alive, remaining amount, type and anchor cell, and the free
  list above the never-used slots). Since M3-2 it also covers the building store
  (`BuildingStore.AddToHash`: capacity, high-water mark, each used slot's generation and alive flag,
  and when alive its owner, type, anchor cell and hit points, then the free list), every player's gold
  and wood, and each live unit's gather fields (`GatherNode`, `GatherSite`, `GatherProgress`, `Cargo`,
  `CargoKind`), flagged by bit 16 of the order word and added only when one isn't default.
  Resource slots go in through `StateHasher.AddWord`, one
  xor-multiply-fold step per 64-bit word (each step a bijection, so one changed word always changes
  the hash), because byte-wise FNV of a full 4,096-slot store took 0.18 ms in Debug; `Add` stays
  byte-wise FNV-1a (unrolled in M3-1, same output). Derived state is left out:
  the spatial hash, the fields' contents (they follow from the grid and the key), and a unit's
  `Speed`/`Radius` (copied from its `TypeId`'s data) and `PrevFacing` (render interpolation, M3-2).

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
in the list above arrive with the systems that use them. `ResourceStore` exists since M3-1 (see
"Economy implementation"), `BuildingStore` since M3-2 (see "Implementation (M3-2)").

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
reachable; later changes keep it so (placement never seals ground, freed cells follow the pocket rule
below, M3-H1). A layout with under
`MinPassableFraction` (50%) passable cells or with a level missing is redrawn, at most
`MaxAttempts` times, then the generator falls back to the first layout that was passable enough,
or a flat map: it never loops forever. `MapGenParams.Validate` caps every size at the smaller map side before using it in arithmetic, and caps `RampTries` (128), `MaxAttempts` (8, the default) and plateau counts (32). A ramp try costs O(1) plus O(ramps placed) plus O(`RampWidth`) for the mouth, not O(`RampWidth` × `RampLength`): a summed-area table of off-limits cells (border ring, cells with a lower neighbor) and rectangle overlap tests against the ramps already placed replace the cell-by-cell footprint scan, accepting exactly the same tries (BUG-0015). Each attempt also builds a full nav grid. The worst valid case, a 1024 × 1024 map with every count at its cap and any ramp size, takes about 1 s in a Debug build (how the tests run) and about 0.2 s in Release. Default generation takes about 4 ms. Queries (`InBounds`,
`IsPassable`, `LevelAt`, `FlagsAt`, `CostAt`, `WorldToCell`, `CellCenter`) never allocate and read
cells outside the map as blocked; `WorldToCell` floors (so -0.1 m is outside, not cell 0) and
rejects NaN and infinities. Cell (x, y) covers meters [2x, 2x + 2) on each axis. Per-cell data is
ready for 8-connected flow fields: no-corner-cutting only needs `IsPassable` on the two side cells.
The terrain itself is not hashed (it follows from seed + params). Since M3-1 passability changes
at run time: resource nodes (trees, gold mines) set `NavFlags.Resource` (always with `Blocked`,
cost 255) on their cells, and a depleted node clears them back to open ground (flags `None`, cost 1).
The internal `SetResource` / `ClearResource` take a node's footprint rectangle and bump `Version`
once per node placed or freed; `PassableCount` follows. `Version` is in `StateHash` since M3-1.
Cliff, ramp and border cells never take a node, and the placer keeps every passable cell reachable
(see "Economy implementation"), so the build-time pocket seal still holds as long as nodes are only
removed from exposed sides, which the gather rule guarantees (M3-2, BUG-0075: only a node with a
passable cell 4-adjacent to its footprint is gathered, so a felled cell always joins open ground).
Buildings (M3-2, `NavFlags.Building` with `Blocked`, cost 255, one `Version` bump per building) are
placed only where the never-seal rule allows (M3-3, see "Implementation (M3-3)").
**Pocket rule (M3-H1, BUG-0093).** Freeing cells (`ClearResource`, `ClearBuilding`: a node depleted, a building
destroyed or cancelled) never opens ground nobody can reach. The freed footprint and the `NavFlags.Pocket` cells
4-connected to it through other pocket cells reopen together, one `Version` bump, if any of them is 4-adjacent to a
passable cell; otherwise the footprint's cells stay `Blocked` and gain `Pocket` (32), cost 255, with no version bump
and `PassableCount` unchanged: no cell a unit can use changed, so nothing is published. So a site walled in by other
buildings and then cancelled stays blocked, and so does an interior tree felled by a test seam; the first later
opening beside them (a tree felled, an enclosing building freed) that joins them to open ground reopens the whole
chain of pockets with it, and a cell opened into pockets alone becomes a pocket too. To the flow-field builder,
`CanPlace` and the exposure rule a pocket cell is a blocked cell like any other. Invariant: after any sequence of
placements, cancels, destruction and felling, every passable cell reaches every other. The common case (no pocket
cell beside the footprint) reads only the footprint's 4-adjacent ring; next to pockets a flood over them runs on the
flow-field builder's queue storage (`FlowFieldCache.BuildScratch`, shared through `NavGrid.ShareScratch`), so it
allocates nothing. Load-time pockets stay plain `Blocked` and never reopen. Since M3-2b the grid tells *closing* changes from *opening* ones:
`NavGrid.BlockVersion` (public get, hashed) bumps once only when cells are blocked (`SetResource`,
`SetBuilding`: a node spawned, a building placed), while `Version` keeps bumping once on every change,
clearing included (`ClearResource`, `ClearBuilding`: a node depleted, a building removed; not when the pocket rule
keeps the cells blocked). The test
seam `BumpVersionForTests` is a closing change (both bump). So a flow field built at the current
`BlockVersion` never points into a blocked cell, whatever has opened since (see "Flow fields"). Water, symmetry and
hand-made maps arrive in M6.

### Flow fields

On a move order, the group's target cell gets a flow field:

1. **Integration field:** Dijkstra from the target over 8-connected cells (diagonals cost 1.41,
   no corner cutting past blocked cells). 16K cells, well under 1 ms.
2. **Direction field:** each cell points at its lowest-cost neighbor.

Fields are cached by target cell in an LRU cache (32 to 128 entries, scaled with the unit cap) and tagged with the grid
version and block version. A *closing* change (cells blocked: a building placed, a node spawned) makes
every cached field unusable; an *opening* change (a tree felled, a building removed) only makes them
stale: units keep following them while they are rebuilt under the build cap (M3-2b, BUG-0073; region
versions only if profiling shows rebuild cost). All units heading to the same target share one field. Units whose
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
cells in one bucket can't improve each other), which gives the same costs as a binary heap. Since
BUG-0082 (M3-2b) it is three buckets in a ring with lazy deletion (a cell whose cost drops a whole
number is queued again and its old entry skipped), driven inline in the build loop with the eight
directions written out and each cell's direction picked when a neighbor relaxes it: tests run Debug
builds, where the JIT inlines nothing, so this builds a 128 × 128 field in about 0.37 ms in Debug
(0.7 ms before) with the same costs and directions. A blocked target cell resolves to the nearest
passable cell by squared cell distance, ties to the lowest (y, x); `FlowField.NearestPassable` is
that rule. It searches square rings outward from the cell and stops once a ring can't beat the
best distance found, so its cost grows with the distance to passable ground, not the map size. Unreachable targets
are rare: the nav grid seals every pocket at load, nodes are only removed from exposed sides (M3-2, BUG-0075), no
building placement may cut passable ground in two (M3-3's never-seal rule, BUG-0078), and cells freed where nobody can
reach them stay blocked (M3-H1's pocket rule, BUG-0093, see "Navigation grid"). `FlowFieldCache` (on `World.FlowFields`) keeps
`FlowFieldCache.CapacityFor(UnitCapacity, cells)` fields: `clamp(UnitCapacity / 8, 32, 128)`, then at
most `max(32, 64 MiB / (5 bytes × cells))`, so the default map (16,384 cells, 80 KB per field) is
never memory-capped (512 unit slots: 64 fields, 5 MB; 1024 and up: 128 fields, 10 MB) and a
1024 × 1024 map stays at 32. Fields are keyed by the requested target cell and tagged with `NavGrid.Version`; a hit returns the same instance and marks it
most recently used, a miss fills a free slot or rebuilds the least recently used one in place, and
a field whose version is stale is rebuilt in place on its next `Get`; `TryGetCached` returns a usable field
(current, or stale by opened cells only: M3-2b, below; marking it used) or null without building. Every field array, the queue, and a
per-cell step mask (which of the 8 steps are legal, shared by all fields and recomputed when the
version changes) are allocated in the constructor: about 5 bytes × cells × capacity (2.6 MB for 32
fields on the default map, 160 MB for 32 on a 1024 × 1024 map), plus a 4-byte cell-to-slot index that makes every lookup O(1) instead of a scan of up to 128 slots. A field's *contents* depend only on
the grid and its target, but under the build cap (below) *which* fields are cached decides which
units wait a tick, so the cache's metadata is sim state (Producer decision 2026-10-04, BUG-0021):
`StateHash` covers its clock, count and every used slot's requested cell, version, block version and last-use
stamp. Since M3-2b a usable-but-stale slot's contents depend on the grid as it was at its build, not only on its
key and today's grid, so a save file stores the cached fields' contents too (see "Save/load and replays", BUG-0081). `Get` and `TryGetCached`
are `internal`, for the sim only (a call moves the hashed LRU state); views and AI see only
`Capacity`, `Count`, `BuildCount`, `Contains` and `PeekCached`, which change nothing.
`PeekCached(targetCell)` (M1-8, for the M2-5 flow-arrow overlay) returns the field units follow to
the target (since M3-2b: the cached field if it is *usable*, see below), else null, and null for an
out-of-range cell; it is not a
use (no LRU stamp, clock, count or build), so a test peeks 10,000 times between the ticks of a
500-unit march and the state hash matches an unpeeked twin every tick. The instance is valid until
the next `Tick()` (a later build may reuse it for another target); read it through
`FlowField.DirectionAt` / `CostAt` and don't keep it across ticks.

**Closing vs opening changes (M3-2b, BUG-0073).** Every field also records `FlowField.BlockVersion`
(the grid's `BlockVersion` at build). A cached field is *current* when `Version == NavGrid.Version`
and *usable* when `BlockVersion == NavGrid.BlockVersion`: only cells have opened since it was built,
so nothing it points into or diagonally past has been blocked; it may miss a shorter way through the
opened cells, and an opened cell has no direction in it. `Contains`, `PeekCached` and `TryGetCached`
answer for usable fields (current or not); `Get` returns a current field and rebuilds a stale or
unusable slot for its target in place (same instance, `Count` unchanged). Before, any change made
every field stale at once, so with one tree felled per tick only the 2 oldest goal groups ever had a
field and the rest stood for as long as felling went on (QA: 30 of 32 walkers stood 90+ of 100
ticks); now they walk their usable fields (longest wait 0 ticks in the same scene). The step masks
are still recomputed once per `Version`; since M3-2b inner cells read the grid's flag array directly
(`ComputeSteps`, same bits as the per-cell `StepMask` definition, which the outer ring still uses),
sliding a 3 × 3 window of open bits along each row, so a pass on 120 x 72 cells takes about 0.045 ms
in Debug instead of 1.5 ms. With one far tree felled every tick, the 2 builds a tick go to refreshes,
and the 32-group scene averages 0.40 ms a tick in Debug (1.76 ms before M3-2b; the perf row pins
< 0.5 ms).

**Build cap (BUG-0018, BUG-0022).** Fetching a field per unit in slot order thrashed the LRU once live goals
outnumbered its slots (goals interleaved by slot evict exactly the field the next unit needs: a
full build per unit per tick, ~320 ms). Now `MovementSystem` sorts Moving units by (goal cell,
slot) into a preallocated `World.MoveOrder` buffer. A build pass then walks the goal groups: a
group needs a field if one of its units stands on the map outside the goal cell; a needed field
that is cached is touched (so a build evicts a field nobody used this tick whenever one exists),
and a missing one goes into the preallocated `World.FieldMisses` buffer keyed by (the group's
oldest `OrderTick`, goal cell). The buffer is sorted and the first
`MovementConstants.MaxFieldBuildsPerTick` = 2 are built: oldest order first. Since M3-2b a goal
whose cached field is usable but stale is a *refresh* instead, in the preallocated
`World.FieldRefreshes` buffer keyed by (the field's `Version`, goal cell); refreshes take whatever the
misses leave of the same cap, oldest field first, ties to the lower goal cell. A stale field with no
direction where one of the group's units stands outside the goal cell (a cell opened after the build:
a unit spawned or shoved onto a felled tree's cell) makes the goal a miss, and that unit waits
(`ActWait`, not an abandon as on a current field) until the rebuild. The units' walk then
only reads cached usable fields; a unit whose field is still missing waits that tick (still Moving,
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
**Maps larger than 256 × 256 are unsupported until builds are time-sliced** (BUG-0023, Producer
decision 2026-10-04): one field build there takes longer than the tick budget (256: about 5 ms in
Debug; 512: 13 ms; 1024: 55 ms, a visible stall). The design map is 128 × 128. `MapGenParams` still
accepts up to 1024 because the generator stress suites use it; gameplay on such maps isn't supported.

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
  stuck. A `Move` to a new goal cell resets both fields. Since M3-2b (BUG-0077) a *closing* grid change
  (a building placed, a node spawned: `NavGrid.BlockVersion` differs from `World.SeenBlockVersion`, the
  value the last pass saw) resets every Moving unit's `BestRemaining` to +inf before the plan pass, so
  the first tick along the longer route round the new wall is measured against where the unit stands
  on the rebuilt field, not against a best its old, shorter route set (half of a 16-unit column used to
  give up behind a dropped Keep; now 16 of 16 arrive). `StuckTicks` follows the usual rule. An
  *opening* change only shortens routes, so it resets nothing. A `Move` to the goal cell the unit already
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
  Moving units (walking, or waiting for a field) and other players' units are never shoved; since
  M1-7 neither are units holding position (`UnitStore.Hold`): not singly, not in a chain shove, not
  as a parked line yielding, and they never walk back (see "Orders and unit states").
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
(Since M1-4d-3 the push and sidestep are summed order-free; what still depends on slots, the wall
clips among them, is listed under "Known limits (M1)" in "Implementation (M1-9)".)

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
army's own standing units stay soft (single clip, shovable); since M1-9 the army's own *holding*
units are hard too (Producer decision 2026-10-05, BUG-0055; holders are rare, so the crowd rows
don't move). Two known gaps (both fixed in M1-4d-3, below), both S3 for the M1
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
  row hold (fix round 2; QA's 2- and 3-cell rows). (M1-9 replaced the line of at most 4 by a cluster of
  at most `MaxPlugCluster` (32) units, and holders of the unit's own army count as plug members; see
  "Implementation (M1-9)".) Sliding round an enemy while inside it carried units
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
`QueueRange`, `WalkBackDelayTicks`, `QueueOnWaitStride`, `MaxPlugSpan` (`MaxPlugCluster` since M1-9) in
`MovementConstants`.

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
Idle unit arrived or gave up, at most 48 of 128 give up; re-bounded on seeds 1-80 in M1-9, below); its
pack rule breaks on 15 of 40 maps (worst pair 0.768 m against 0.8 m) and is report-only. `TestSeeds.PreMix` is gone from these rows.
BUG-0034: the distinct-targets perf row averages 100 ticks, budget unchanged.

**Implementation (M1-9): end-of-milestone hardening.** The M1 debt QA left open, in priority order.

- **Holders block their own army (BUG-0055).** `IsHardWall` counts a unit holding position as hard
  for every walker, its own army included (Producer decision 2026-10-05), and the plug rules follow
  (`StandsHard`): a holder can be a plug member, a chain shove stops at a unit touching one, and no
  shove presses a unit past the pack limit into one. QA's 1-cell corridor with a radius-0.9 holder:
  0 of 30 own walkers end past it (10 before, overlapping it by up to 1.05 m). No crowd row issues
  `HoldPosition`, and with no holder every rule reads exactly as before, so every crowd row, the perf
  rows and the golden replay are bit-identical: nothing to reverse.
- **Malformed commands are refused at the door (BUG-0054, BUG-0056 item 1).** See "Tick model":
  `Enqueue` throws for an undefined kind, an unknown flag bit or `QueuedFlag` on a `Noop` /
  `SpawnUnit`, and `Replay.Validate` applies the same `Command.IsWellFormed()`.
- **Plugs are clusters (BUG-0045).** A plug is now the hard unit alone in a 1-cell passage, or its
  *cluster* (every hard unit linked to it by gaps under the walker's diameter: other players' units
  that aren't walking, and holders of the walker's army) if the cluster has at most
  `MovementConstants.MaxPlugCluster` (32) members and its members' wall sides include two opposite
  ones. M1-4d-3 followed a line of at most 4 (`MaxPlugSpan`), so a 5-cell corridor plugged by one
  wide enemy per row, or 5-7 small enemies across a 2- or 3-cell corridor, let 3-12 units through.
  QA's five rows for those now hold (nobody through; closest friendly 0.75-0.81 x the radii's sum,
  the pack limit is 0.5). A cluster bigger than 32 is an army's blob, where sliding round enemies
  is how two-player crowds flow; its members are still hard walls, only the 45-degree cone is off.
- **Plug answers are cached (BUG-0044).** The answer depends only on the cluster, the walker's owner
  and the walker's radius, and nothing it reads changes within a pass (Plan reads start-of-tick
  state; the shove pass reads positions after the walkers moved). So `World.PlugAnswers` keeps it
  per unit, per walker owner and per radius class (`World.RadiusClassCount`: the distinct radii in
  the data, 3 today), stamped with `World.PlugEpoch` (bumped at the start of Plan and of the shove
  pass). A search writes its answer for every member it visits and stops early at a member already
  answered this pass (same cluster, same answer) or once the cluster outgrows 32, so each cluster
  is searched about once per pass. In the shove pass the spatial hash still holds start-of-tick
  points while units have moved up to `World.MaxUnitSpeed`, so the search's neighbor query is
  widened by that much (the exact gap test still decides): every member finds the same links, and
  the cached answer no longer depends on which member was asked first (M3-H1, BUG-0071). Preallocated, derived, not hashed. `Constrain` also tests "not a
  wall" (a walker or an arrived groupmate, most neighbors in a crowd) inline before calling
  `WallLimit` (Debug builds call every helper).
- **A unit giving up while backing off is checked again (found in M1-9).** A unit at its goal but
  too crowded to stop backs off; one that reaches `GiveUpTicks` doing so stops there and keeps its
  goal cell. But the back-off step can carry it out of reach (1.06-1.17 m from its goal with nobody
  of its group in touch): a stray anchor that later arrivals packed against (a stray blob 10 m from
  its point in QA's 2,500-unit one-owner row once a movement change re-rolled the trajectories).
  `Apply` now lists such units, and at the end of the tick (after the shoves) `SettleBackedOff`
  applies the arrival rule at their final positions: a unit keeps its goal only if it is in its goal
  cell within `ArrivalDistance` of its goal, or touches (through a legal step) an Idle groupmate
  holding the goal cell that isn't itself one of these units left out of reach; the others drop the
  goal and may walk back once, like a unit a shove cut off. Local, not the whole-group re-check:
  that cost the 2,500-unit tight blob 16% (hundreds back off at the edge of one blob), and nobody
  anchored on these units this tick (they were walking when it planned). Test:
  `CrowdRoutingTests.GivingUpWhileBackingOff_OutOfReach_DropsTheGoal`. The golden replay is
  byte-identical; it changes a few outcomes in big crowds (QA's 1,000-unit cross-map report, seed 4:
  989 arrived, now 983).
- **The 64-goal row is re-bounded on seeds 1-80 (BUG-0049).** The dev test sweeps seeds 1-40 and
  41-80 (two halves, run in parallel): build cap, termination, every Idle unit arrived or gave up on
  every map, at most 80 of 128 give up on any map, and each half's median at most 32. Measured: 1-40
  median 27.5 (max 41), 41-80 median 30.5 (max 44, except seed 51 with 76). Seed 51 is the BUG-0048
  map (more live goals than cache slots, the jam ends only by giving up; base 92, and before
  BUG-0048's fix it never stopped), hence the per-map 80; the medians guard the typical map. QA's
  41-80 sweep is un-skipped with the same per-map bound.
- **Random goals stay at 4.7% give-ups, a known limit (BUG-0050).** 500 units to 500 random goals,
  seeds 1-10: 23, 20, 40, 39, 10, 16, 18, 37, 17, 17 of 500 (mean 23.7, 4.7%; unchanged by this
  task). Alternatives measured (M1-4d-3 fix round 2): never counting a stuck tick behind a field
  wait gives 15 (3%) but never terminates under cache churn; counting 1 tick in 8, 16 or 32 gives
  23-25; holding fully only while the waiter's order is younger than 100 or 300 ticks gives 25-26
  with worse 64-goal maxima. And in M1-9, six slot-free wall clip orders (BUG-0046, below): 23.6-30.3.
  None reaches 3% with a jam that still ends; the target needs fewer units waiting for fields
  (time-sliced or cheaper builds, BUG-0023) or crowd-aware routing.
- **Wall clips still run in slot order, a known limit (BUG-0046).** Sorting `Constrain`'s walls by a
  slot-free key was built and measured: hard walls first (so a later clip can't push the step into
  a friendly without the hard-wall check seeing it, BUG-0038), then the farthest first, ties by
  position. QA's four spawn-permutation rows were then bit-equal, and the aggregates held (random
  goals 24.1 vs 23.7; 64-goal medians 28 / 29.5 vs 27.5 / 30.5; the dev 4-point rows' means 252 vs
  256 (500) and 842 vs 847 (2,500); a plain position order was worse: random goals 30.3). But changing
  the order re-rolls every crowd's chaotic outcome, and three bounds fitted to today's outcomes broke:
  QA's 500-to-4-points sweep (seed 30: 184 arrived, bound 200), the headline cross-map scenario's
  pack check (seed 4: two units 0.356 m apart, bound 0.4) and QA's 64-goal sweep (seed 3: 49, bound
  48). Not landed; whether to land it with bounds re-fitted on wider sweeps is the Producer's call.
  The QA rows stay skipped under BUG-0046.
- **Measured (M1-9, Debug, this machine, ms per tick; base 1f533aa and M1-9 alternated, three rounds
  each, one fresh `dotnet test` process per sample; the base matches QA's BUG-0044 figures):**

  | Row | Target | Base | M1-9 |
  | --- | --- | --- | --- |
  | 500 moving units (`PerfCriterionTests`) | < 4 | 0.66-0.67 | 0.62-0.63 |
  | 2,500 tight blob, one player (`CrowdPerfTests`, enforced) | <= 4.5 | 4.55-4.59 | 4.33-4.35 |
  | 2,500 tight blob, two players contesting the point (report, guard < 10.5) | | 10.53-10.65 | 6.82-6.84 (-36%) |
  | 2,500 to 4 points, one player per point, seed 1 (report, guard < 3.7) | | 3.74-3.76 | 3.08-3.09 (-18%) |
  | 1,000 walkers crossing a 1,500 same-owner blob (QA report) | | 14.85-14.95 | 14.18-14.21 |
  | 200 walkers crossing a 300-unit blob (enforced) | < 4 | 0.96 | 0.93-0.95 |

  The plug answers' cache pays back the M1-4d-3 regression in the two-player rows (the contested blob
  is still 1.5x the one-player blob: more units stay Moving in a contest). The one-player tight blob
  is under its target by the `Constrain` change alone, with 3% headroom on this machine.

  Crowd rows (base, M1-9): QA's 500 to 4 points over seeds 1-40 mean 255, 257 (min 206, 206); 2,500
  to 4 points over seeds 11-20 mean 888, 912 (min 762, 763); the dev 2,500 to 4 points rows (seeds
  1-10) mean 847, 856 (min 686, 688), 500 to 4 points 256, 255 (min 230, 227); the cross-map scenario
  one owner unchanged (200/200, 0 give-ups on seeds 1-8, and on QA's 60 seeds); two owners 559, 562 and
  516, 514 give-ups per 10 seeds; 64-goal and random goals as above. The two-player rows moved by the
  plug clusters, within the run-to-run spread; one-player rows only where a back-off was settled.

**Known limits (M1), as of M1-9.** What the code does not do, so nobody relies on it:

- **Flow fields ignore units** (BUG-0028 / BUG-0032, after M4): walkers aim through other groups'
  blobs and only the local detour (3 m) and give-up (1 s) handle them. Crowd rows stay below the
  Producer's targets: 4 points about 51% (500) and 34% (2,500) arrived, 64-goal median 22-24% given
  up, a same-owner blob crossing 22 of 200.
- **Random goals give up 4.7%** (target 3%, BUG-0050, above).
- **The 64-goal row's BUG-0048 map gives up 76 of 128** (seed 51; the bound allows 80).
- **The pack rule (no two stopped units closer than half their radii's sum) is not guaranteed:** a
  unit that stops at the back-off limit or gives up can be closer (the 64-goal row breaks it on
  about 40% of maps, worst 0.77 of the radii's sum; the headline cross-map scenario keeps it on its
  swept seeds).
- **Spawn-order independence is partial** (BUG-0046): same seed and commands always give the same
  hash, but a walker's path can depend on its neighbors' slots where it touches several walls at
  once (`Constrain` clips in slot order), and so can shoves (summed in walk order, (goal cell,
  slot); `SqueezeLimit` trims in slot order; a chain of at most `MaxChainShove` picks members in slot
  order; two coincident units push apart by slot). Don't build on order independence (e.g. a
  re-spawn from a snapshot must keep slots).
- **Plugs:** a cluster of more than 32 hard units is not a plug (units overlapping one may slide
  round it); outside plugs, shoves can press a given-up unit 0.15-0.32 m deeper into a standing enemy
  in one tick (pre-existing, BUG-0045 note). Enemy contact is redefined with combat in M4.
- **Holders are walls, nothing more:** no enemy scanning or retaliation until M4; a queued order
  after an unqueued Hold ends the Hold when it starts (by design, BUG-0056 item 3).
- **Moving units are never shoved** and units waiting for a field are walls; a unit's walk-back
  happens once per order.
- **Maps larger than 256 x 256 are unsupported** (one field build exceeds the tick, BUG-0023); the
  over-capacity cache eviction is plain LRU (BUG-0025) and same-tick build ties favor lower goal
  cells (BUG-0026).
- **Grid changes (M3-2b):** while trees keep falling, the build cap spends its 2 builds every tick
  refreshing stale fields (about 0.8 ms a tick on a 120 x 72 map in Debug, 1.4 ms of builds on the
  128 map; a calm walk costs 0.02 ms), and until its refresh a group follows the longer route its
  usable field knows. A closing change (a building placed, a node spawned) still makes every field
  unusable at once, and the cap rebuilds them oldest order first, 2 a tick. Measured with real
  closings (`SimHardeningTests.ClosingsEveryPeriodTicks_OnlyThe2xPeriodOldestGroupsWalk`, M3-H1): with
  a closing every p ticks only the 2p oldest goal groups ever get a field (the k-th oldest waits
  floor(k / 2) ticks after each closing) and the younger ones wait for as long as the closings last;
  once p reaches ceil(groups / 2) every group walks, the youngest after ceil(groups / 2) - 1 ticks
  (8 groups: closings every tick starve 6 for good, every 2 ticks 4, every 4 ticks none, longest wait
  3). So a player or AI placing a building every tick for a while stalls the younger armies for that
  while (BUG-0080). Keeping closed fields usable-stale (walkers clipped by the per-step check against
  blocked cells) was the preferred fix but was left: walkers pressed against a new building on an old
  field would count stuck ticks and give up in 20 ticks, where today they wait, and the closing
  semantics are pinned by the flow-arrow overlay, PeekCached and cache tests on both tracks.
  Time-sliced builds are BUG-0023.
- **Perf is measured in Debug on the dev machine**; the 2,500-unit two-player contest costs about
  1.6x the one-player blob (6.8 vs 4.3 ms). The 4 ms design budget is for 500 units (0.6 ms today).

## Orders and unit states

Orders: `Move`, `AttackMove`, `Attack(target)`, `Hold`, `Stop`, `Patrol`, `Gather(node)`,
`ReturnCargo`, `Build(site)`, `Repair(building)`, `Cast(ability, target)`. Shift-queued orders
append to the queue; unqueued orders replace it.

**What exists now (M1-7).** `CommandKind.Move` (2), `Stop` (3), `HoldPosition` (4) and
`AttackMove` (5), each one command per unit. `Command.Flags` bit 0 (`Command.QueuedFlag`) marks a
shift-queued order; factories `Command.Move(player, unit, target[, queued])`,
`Command.Stop/HoldPosition(player, unit, queued = false)` and
`Command.AttackMove(player, unit, target, queued = false)`. Every unit order is dropped for a dead
or recycled handle or another player's unit; a Move or AttackMove also for a target off the map
(queued or not, so it never takes a queue entry).

- **Unqueued** (phase 1): `Move` and `AttackMove` clear the unit's queue and Hold, then apply the
  Move rule of "Local movement" (same-cell re-orders included). `AttackMove` walks exactly like a
  `Move` until combat targeting arrives (M4); only the kind kept in a queue entry differs. `Stop`
  clears the queue and Hold and leaves the unit Idle with no goal (`GoalCell = -1`), velocity 0,
  `StuckTicks` 0, `BestRemaining` infinity and no walk-back: goal-less, so friendly walkers shove it
  freely and it anchors nothing. `HoldPosition` is `Stop` and then sets `UnitStore.Hold`.
- **Holding** units are never shoved (single, chain, parked-line yield) and never walk back, and
  since M1-9 (BUG-0055, Producer decision 2026-10-05) they are hard walls to everyone, their own army
  included: a walker never goes deeper into one, also when pressed between it and something else, a
  holder can be (part of) a plug, a chain shove stops at a unit touching it, and no shove presses a
  unit past the pack limit into it. A unit told to hold a choke lets nobody through (QA's 1-cell
  corridor row: 0 of 30 own walkers pass, 10 before). No enemy scanning yet (M4). Any later unqueued
  order, and a queued Move or AttackMove when it starts, clears Hold.
- **Queued** (`QueuedFlag`): appended to the unit's queue (kind and target); a full queue
  (`OrderConstants.QueueCapacity` = 8) drops the order silently. A queued order never restarts a
  Moving unit: its order tick, goal and stuck count are untouched. In phase 7 every live Idle unit
  (it arrived, gave up, stopped, or was never ordered) with a queued order pops the head and starts
  it through the same code as the unqueued order, except that a popped Move or AttackMove keeps the
  rest of the queue. A popped `Stop` or `HoldPosition` is terminal: it drops the rest. A unit that
  gave up on a leg still starts the next one.
- **Storage** (no per-unit lists): `UnitStore.Hold` (bool per slot), `QueueCount` (int per slot),
  and flat `QueueKind` / `QueuePosition` arrays of `Capacity * QueueCapacity` entries (slot i's
  queue starts at `i * QueueCapacity`, head first; entries past the count are always default).
  Alloc and Free reset them. All of them are in `StateHash`, and pending commands' `Flags` too.
  They go into the high 32 bits of the hashed state and kind words (always zero before), with the
  queue entries added only when flagged there, so a unit with no Hold and an empty queue, and a
  pending command with `Flags` 0, hash exactly as before M1-7: the golden replay's checkpoints did
  not move. QA's reflection audit mutates every queue entry of a live unit.

Since M3-2 there are also `Gather` (7, a unit order, Shift-queueable: see "Implementation (M3-2)")
and the dev command `SpawnBuilding` (6, not queueable), and the states `Gathering` and `Returning`.
Any other unit order (Move, AttackMove, Stop, HoldPosition) ends a gather loop and keeps the cargo.
Since M3-3 there are `Build` (8) and `Repair` (10), unit orders for workers (Shift-queueable; a queued
Build keeps its building type in `UnitStore.QueueTypeId`), `Cancel` (9, not a unit order, not queueable), and
the state `Building`; see "Implementation (M3-3)". Any other unit order ends building or repairing.
Since M3-4 there are `Train` (11), `CancelTrain` (12), `SetRally` (13) and `ClearRally` (14): production commands
addressed to a building, not unit orders, not queueable (the queued flag is refused at the door); see
"Implementation (M3-4)". A trained unit's rally order is the Move rule (or, for a worker rallied onto a resource node,
a `Gather`), given in phase 3.
Not built yet: combat targeting for AttackMove,
Hold's enemy scanning, `Patrol`, `Attack(target)`, formations and group moves.

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

- Resource nodes are entities with a remaining amount. There is no gather slot list: workers queue
  at a node's edge (docs/02 "Mine crowding": no hard cap). A worker that ends a walk out of reach
  stands and walks again every `EconomyConstants.RetryTicks`, choosing the nearest free side; see
  "Implementation (M3-2)".
- Workers run a small state machine: walk to node → gather (timer) → walk to nearest drop-off →
  deposit → repeat. Drop-off choice uses straight-line distance, re-evaluated per trip.
- Production queues: 5 slots per building, resources deducted at queue time and refunded on
  cancel. Population is reserved when training *starts*; a full cap pauses the queue.
- Construction sites are buildings in `UnderConstruction` state with progress; HP grows with
  progress.

### Implementation (M3-1)

Resource nodes exist in the sim; workers, gathering and drop-offs are M3-2.

- **Data.** `common/resources.json` (required) lists node *types*: `id`, `displayName`,
  `description`, `resource` (`gold` | `wood`, `DataLimits.ResourceKindIds`) and `footprint {width,
  height}` in cells, each side 1 to `DataLimits.MaxFootprint` (4); a `wood` type must be 1 x 1, since the
  forest placer grows forests cell by cell (M3-2b, BUG-0074: the loader refuses anything else). Shipped: `gold_mine` (gold, 2 x 2)
  and `tree` (wood, 1 x 1). At least one type of each kind is required (M3-H1, BUG-0076: an empty list, or one
  without a gold or a wood type, is a `DataError`, not a map that silently places nothing). Amounts stay in `rules.json` (docs/02): a placed tree holds `treeWood`
  (100), a placed mine `startMines.gold` (2,500; every mine until start locations exist, M3-3/M6).
  `GameData.Resources` is indexed by dense ids in ordinal order (`FindResource`); `ContentHash`
  covers every field.
- **Store.** `Rts.Sim.Entities.ResourceStore` (`World.Resources`): structure of arrays with
  generational handles like `UnitStore` (`Alive`, `Generation`, `TypeId`, `Cell` = the footprint's
  lowest x, y cell, `Remaining`), a LIFO free list, capacity `SimConfig.ResourceCapacity` (optional,
  default 4,096), allocated once. Outside the sim it is read-only (`ReadOnlySpan` properties,
  `IsAlive`, `HandleOf`, `Fits`). The sim mutates it through two internal calls:
  `Spawn(typeId, cell, amount, out handle)` refuses (false, never throws) when the store is full, the
  type is unknown, the amount is below 1, or any footprint cell is off the map, blocked (cliff,
  border, sealed pocket, another node), a ramp, or on another level; `Take(handle, amount)` returns
  what it took (clamped; 0 for a dead or stale handle or an amount of 0 or less), and a node taken to 0
  is freed: its cells reopen, `NavGrid.Version` bumps once, its generation moves on. `Take` is the
  primitive M3-2's gather system calls in tick phase 4; it never allocates. Spawning bumps `Version`
  once too, so a field cached before a node appears is stale.
- **Placement.** `World` places nodes right after the nav grid and before the flow-field cache, from
  `MapGenParams.Forests`, `ForestMinTrees` / `ForestMaxTrees` (12 / 40), `GoldMines` and
  `MineSpacing` (24 m; 0 or more, and -0 is refused like the other odd float encodings, M3-H1). Both counts default to 0, so the M1 maps, tests and golden trajectories are
  unchanged, and then the placer draws nothing. Otherwise it draws from `RngStream.MapGen` after the
  terrain (the heightmap is identical with and without resources). Mines first, then forests. A node
  covers only "open" cells: passable, not a ramp, and no cliff, ramp or border cell among the 8
  neighbors; a footprint is one level. A mine's anchor is drawn at random and its center must be at
  least `MineSpacing` from every earlier mine's. A forest draws its size from the range, then grows a
  4-connected blob from a random seed cell, adding a random frontier cell at a time. No new mine or
  forest cell may touch (8-neighbor) an earlier node, so a mine is reachable from every side and each
  forest is its own 8-connected group. Before a mine or forest is committed, the placer checks that
  every passable cell still reaches every other: first a local test (the cells around it are passable
  and connected among themselves, which is enough on its own and rejects forests that enclose a hole
  without touching the rest of the map; since M3-H1 it decides, BUG-0076), then a flood fill of the whole map that
  only Debug builds run, as an assertion that can't fail (Producer decision). A failed placement is
  dropped and retried elsewhere, at most `TriesPerPlacement` (32) times; one that never fits is
  skipped. `World.ResourcePlacement` reports what was placed (forests, trees, mines). Tree slots follow
  cell order within a forest. Caps: `Forests` and `GoldMines` at most 64, forest size 1-256. Setup
  cost (Debug): 12 forests and 8 mines add about 5 ms to a 128 map; the worst case (1024 map, 64
  forests of 256, 64 mines) about 2.2 s on top of the terrain's 0.3 s in Debug (QA measurement, BUG-0076; most of
  it is the full-map flood fill after the ring test, which Release builds skip since M3-H1). Large forests (100+ trees) often enclose a hole and are skipped.
- **Navigation.** See "Navigation grid": `NavFlags.Resource` with `Blocked`, `Version` bumps, and
  flow fields rebuild through a gap on their next use (the cache's version check, unchanged).
- **Hash and replays.** `StateHash` covers `NavGrid.Version` and the store (see "Determinism").
  Replays are format 3: the header carries `resource-capacity` and every new `MapGenParams` field;
  format 2 files are refused with `FormatVersionMismatch`. The golden was regenerated for the hash
  composition, the format and the data hash; its unit trajectories are unchanged.
- **CLI.** `run --forests <n> --mines <n>` (0-64, default 0) builds the map with resources; the header
  line ends with `forests F trees T mines M` (what was placed), and the replay records them.
- **Not yet:** gathering, the `Gather` command, workers, drop-offs, depleted-node events for views,
  expansion mines and start-location placement, biomes.

### Implementation (M3-2)

Workers gather gold and wood and carry it home with no further orders (M3 criterion 2). BUG-0075 is
closed by rule: only exposed nodes are gathered.

- **Data.** `factions/<faction>/buildings.json` (required) lists building types: `id`,
  `displayName`, `description`, `slot` (one of the ten template slots, `DataLimits.BuildingSlotIds`:
  `town_hall`, `house`, `camp`, `infantry_hall`, `ranged_hall`, `shock_hall`, `forge`, `caster_hall`,
  `siege_works`, `watch_tower`), `footprint {width, height}` (1-4 cells), `hp`, `armor`,
  `cost {gold, wood}`, `buildTime` (seconds, to ticks), `popProvided` (to half-pop) and `dropOff`
  (bool, required). Ids are snake_case and unique across factions; unknown fields are errors. Shipped:
  M3-2 shipped the Town Hall slot only (`malazan_garrison_keep`, `whirlwind_holy_camp`: 4 x 4, 2,400 hp,
  armor 5, 275 / 275, 90 s, +10 pop, drop-off); the data track's D1 (session 2026-10-06-1744) filled the
  other nine slots per faction from the docs/02 "Buildings" table (`Content/BuildingContentTests` pins them).
  `GameData.Buildings` (dense ids, ordinal order), `FindBuilding`; `ContentHash` covers every field.
  `trainedAt` and `requires` stay unresolved strings.
- **Buildings.** `Rts.Sim.Entities.BuildingStore` (`World.Buildings`): structure of arrays with
  generational handles (`Alive`, `Generation`, `Owner`, `TypeId`, `Cell` = anchor, `Hp` = the type's
  maximum for now), LIFO free list, capacity `SimConfig.BuildingCapacity` (optional, default 256).
  Read-only outside the sim. A building blocks its footprint (`NavFlags.Building` (16) with `Blocked`,
  cost 255) and bumps `NavGrid.Version` once, so to movement it is a wall like a mine. The only way in
  is the dev / test command `Command.SpawnBuilding(player, typeId, position)` (kind 6; reuses `TypeId`
  and `Position`; the cell holding `Position` is the anchor, the footprint's lowest x, y). It is
  dropped at apply, never thrown, for an unknown type, a full store, a footprint that isn't open
  ground (the resource-node rule: every cell in the map, passable, not a ramp, one level; so no cliff,
  border, node or building), or a live unit whose center lies in it. The queued flag on it is
  malformed. `Free` exists as a test seam until destruction (M3-3 / M4).
- **Player totals.** `World.Gold` / `World.Wood` (read-only spans, one int per player) start at
  `rules.json` `startingGold` / `startingWood`; deposits add to them (saturating at `int.MaxValue`).
  Hashed.
- **Gather command.** `Command.Gather(player, unit, position[, queued])` (kind 7, a unit order):
  `position` is any point of a node's footprint; the node is resolved at apply. Dropped (no throw) for
  a dead or foreign handle, a unit whose type isn't in the `worker` slot (queued or not), or a target
  off the map. On a node's cell: that node if it is exposed, else the depleted-node rule from its
  center. On a cell without a node: the nearest exposed node of either kind within `nodeSearchRadius`
  of the target. Nothing found drops the command (queue and Hold stay, like a Move to nowhere).
  Accepted unqueued, it replaces the queue. Shift-queued it is appended like any unit order and
  started by phase 7 when the unit is Idle; a popped Gather keeps the rest of the queue, which runs
  only when the loop ends.
- **Unit fields** (`UnitStore`, reset on Alloc and Free, hashed): `GatherNode` (the node's handle;
  default = no loop), `GatherSite` (the node's footprint center, kept after the node dies: the
  depleted-node search starts there), `GatherProgress` (fraction of the next unit), `Cargo`,
  `CargoKind`. `PrevFacing` (view request 4) is the facing at the start of the tick, set where
  `PrevPosition` is; derived, not hashed.
- **States.** A worker on a loop walks its legs with the Move machinery (`UnitState.Moving`,
  `OrderSystem.Walk`: a fresh order, no same-target shortcut). Standing, it is `Gathering` (on the
  node leg: working when in reach, else waiting) or `Returning` (a full load, out of reach of a
  drop-off, waiting). Between arriving (movement sets Idle, phase 9) and the next phase 4 it is
  briefly Idle with `GatherNode` set. Movement treats every standing state like Idle: a standing
  worker is shoved, anchors its blob and walks back exactly as an Idle unit does (the ten "Idle"
  tests in `MovementSystem` now read "not Moving"; nothing else changes, M1 trajectories identical).
- **Exposure rule (BUG-0075).** A node is *exposed* when at least one passable cell is 4-adjacent to
  its footprint. Only exposed nodes are gathered: an order on an interior tree goes to the nearest
  exposed tree, and a worker whose node stops being exposed takes the depleted-node rule. A forest is
  eaten from the outside, so a felled cell always touches open ground and no hollow can form.
- **Reach and walking.** A worker works or deposits when the distance from its center to the
  footprint rectangle is at most `EconomyConstants.Reach` (1.25 m, engine geometry like `CellSize`).
  It walks to a passable cell 4-adjacent to the footprint, aiming `EconomyConstants.GoalInset`
  ((Reach - ArrivalDistance) / 2 = 0.125 m) outside the shared edge, so a walker that arrives (within
  `ArrivalDistance` of its goal) is in reach. The cell is the one with the least distance from the
  worker to its center plus `CellSize` per other unit standing in it (spatial hash), ties to the lower
  cell index: workers spread round a crowded mine. A worker that arrives or gives up out of reach
  stands and walks again every `EconomyConstants.RetryTicks` (20); that is the queue at a mine's edge.
- **Gathering.** Each tick in reach, `GatherProgress += RulesDef.GoldPerTick` (or `WoodPerTick`);
  whole units are taken with `ResourceStore.Take` into `Cargo`, so the node loses exactly what the
  worker gains. A gold load of 10 takes 286 ticks in reach (14.3 s), wood 334 (16.7 s). At
  `workerCarry` the progress resets and the worker returns.
- **Returning.** The drop-off is the player's live `dropOff` building whose footprint center is
  nearest the worker in a straight line (ties to the lower slot), chosen when the trip starts and on
  every retry; in reach of any own drop-off it deposits: total += cargo, cargo 0, then back to the same
  node if it stands exposed, else the depleted-node rule. No own drop-off: Idle, cargo kept, loop over.
- **Depleted-node rule** (docs/02): the nearest exposed live node of the loop's kind whose center is
  within `nodeSearchRadius` (20 m) of the old node's center, ties to the lower slot; none: Idle, cargo
  kept. A node that dies while the worker walks to it is replaced at once; one that dies while the
  worker carries a load is replaced after the deposit.
- **Cargo rules** (Producer decisions, owner may revisit). A Move, AttackMove, Stop or HoldPosition
  ends the loop and keeps the cargo; a later Gather of the same kind continues from it. A Gather of the
  other kind discards it (AoE II rule). There is no explicit `ReturnCargo` yet (M3-3 / HUD).
- **Cost.** Nothing allocates per tick: stand cells, drop-offs and nodes are found by bounded scans
  (footprint ring of at most 16 cells, the building store, the resource store) in slot or cell order.
  Measured (Debug, this PC): 500 marching units + 50 gathering workers average 0.94 ms a tick; 200
  workers gathering alone 0.26 ms.
- **Replays.** Format stays 3: the new kinds ride in the existing `c` lines. Format 3 has no
  building-capacity line, so `ReplayRecorder` refuses a sim whose `BuildingCapacity` isn't the
  default. The golden was regenerated once for the hash composition and the data hash; trajectories
  unchanged.
- **CLI.** `run --workers N` (0-200 per player; N above 0 needs `--forests` or `--mines` above 0, else exit 1):
  player p plays faction p mod 2; its Town Hall goes on the open 4 x 4 spot (no march unit in it or on
  its ring) nearest its start block's center, ties toward the map centre; N workers spawn one per
  free cell on the rings round it and are ordered to gather once they exist (odd slots the nearest
  mine, even slots the nearest tree). The header ends with `workers N` and the last lines are
  `player P gold G wood W`, only when `--workers` is given (other runs print exactly as before).
- **Not yet:** the Camp as a drop-off in play, `ReturnCargo`, Whirlwind's gather bonus,
  production, population, depletion events for views (construction and placement: M3-3, below). (BUG-0073 and BUG-0077, closing vs opening grid
  changes, and BUG-0074, wood footprints other than 1 x 1 refused, were done in M3-2b: see "Flow
  fields" and "Local movement".)

### Implementation (M3-3)

Players (and later the AI) place any of their faction's ten buildings, and workers build and repair them (the sim
half of the M3 criterion "Building placement (ghost preview, validity), construction with multiple builders,
repair"; the ghost is view work that calls `World.CanPlace`). Closes BUG-0078 by rule: no placement seals ground.

- **Placement rule.** `World.CanPlace(player, typeId, anchorCell, out PlacementError reason)`: public, read-only,
  allocation-free, and the one rule `Build` applies. `reason` is the first rule broken, in order: `UnknownType`;
  `WrongFaction` (player p plays faction `p mod factions`, `World.FactionOf`, until a lobby picks them, M6);
  `Requires` (M3-6, see "Implementation (M3-6)"); `OffMap`; `Blocked` (a footprint cell blocked: cliff, border, node, building; or a ramp, or another level than
  the anchor: `BuildingStore.Fits`); `SealsGround` (below); `UnitInTheWay` (an enemy unit, or an own unit holding
  position, has its center in the footprint; own units that aren't holding are pushed out instead); `CannotAfford`;
  `StoreFull`; else `None`. The "explored by the player" rule waits for fog (M4). The `Build` apply path needs only
  pass / fail, so it runs the cheap rules (UnknownType to StoreFull) first and the seal flood last (M3-H1, BUG-0091:
  100 refused Builds at a long-detour anchor cost one tick 22 ms in Debug before, 0.07 ms now); both answer pass / fail
  identically, `CanPlace` keeps the order above. A worker's own Hold never puts it in the way of its own Build (M3-H1,
  BUG-0092): the Build is accepted, the worker pushed out and its Hold ended like any order's, while `CanPlace`, which
  has no worker, still reports `UnitInTheWay` for that spot.
- **Never seal (BUG-0078).** A footprint may be taken only if every two passable cells that connect now still
  connect without it (`Map.SealCheck`, on `World`, allocation-free). A path through the footprint enters
  and leaves it through passable cells 4-adjacent to it, so it is enough that those cells still reach each other.
  The resource placer's ring-then-flood test: when every cell 8-adjacent to the footprint is passable they form a
  loop round it and the answer is yes at once; otherwise one 4-connected flood from all of them at once, outside the
  footprint, each carrying its side's label: where two floods meet their labels merge (yes once one is left), and a
  label whose cells are all expanded without meeting another is a cut-off region (no). A "no" so costs about the
  pocket it would make, not the map. Unlike
  the placer it doesn't need the whole map connected beforehand. The dev command `SpawnBuilding` applies it too.
  A destroyed or cancelled building that other buildings enclosed would leave a pocket (reachable from nowhere); since
  M3-H1 the grid's pocket rule (BUG-0093, "Navigation grid") keeps its cells blocked as `NavFlags.Pocket` cells
  instead, with nothing published, until an opening beside them joins them to open ground. So every passable cell
  reaches every other at all times, and a tree whose only open side was such a pocket counts as unexposed.
- **Build command.** `Command.Build(player, worker, typeId, anchor[, queued])` (kind 8, a unit order; `TypeId`
  and `Position` as for `SpawnBuilding`: the cell holding `Position` is the anchor). Dropped, never thrown, for a dead
  or foreign handle, a unit not in the `worker` slot (queued or not), or an anchor off the map. At apply: if the
  player's own site of that type is anchored exactly there, the worker joins it as a builder (several selected
  workers share one command this way, and it is how "right-click a site to help" works); anything else on that cell
  drops the command. Otherwise `CanPlace` must say `None`: the cost is paid, the site spawns (a closing change:
  `Version` and `BlockVersion` bump once), and every own unit whose center lies in the footprint is set down (a
  position set, not a walk) on the nearest free cell outside it, slot order: the rings of cells round the footprint
  (ring 1 is the cells 8-adjacent to it), nearest ring first, on the footprint's level, passable and with no other
  unit's center in it; within a ring the cell nearest the unit, ties to the lower cell index. Rings go on outward until
  a free cell turns up, so two pushed units never share a cell (M3-H1, BUG-0092; only a level with no free cell at all
  falls back to the nearest passable cell). Occupants come from the spatial hash, rebuilt once per push-out so it
  matches the units of that moment, each cell's answer kept in the flow-field builder's scratch; a pushed unit's
  `PrevPosition` is set with its `Position`, so the view doesn't draw it sliding through the building. Then the worker
  is given the site. A dropped
  Build changes nothing (totals, store, queue and Hold stay). Queued, a Build is checked when it starts (phase 7) and
  pays then.
- **Construction.** A site is a `BuildingStore` entry with `UnderConstruction` and `Work` (int);
  `WorkNeeded(type) = EconomyConstants.BuildWorkScale (3) x buildTicks`. Each tick n builders in reach add
  n + `BuildWorkBase` (2), so a site takes exactly `ceil(3 t / (n + 2))` ticks (docs/02's `t x 3 / (n + 2)`): a 20 s
  House takes 400, 300, 200 and 120 ticks with 1, 2, 4 and 8 builders. `Hp = max(1, maxHp x Work / WorkNeeded)`
  while building; at `WorkNeeded` the site completes at full hit points and its workers go Idle (`popProvided`
  applies with population, M3-4). Damage to a site is overwritten by the next tick's progress (until combat, M4).
- **Builders.** `UnitStore.BuildTarget` (a building handle, hashed, reset on Alloc / Free) and state
  `UnitState.Building` (standing; movement shoves it like a gatherer). A worker walks to a passable cell 4-adjacent
  to the footprint exactly like a gatherer (`EconomySystem.WalkToFootprint`), works while within
  `EconomyConstants.Reach`, and standing out of reach walks again every `RetryTicks`. A site under construction is
  built, a finished building below full hit points repaired. `ConstructionSystem.Run` (phase 4, after gathering)
  first steps every worker with a target in slot order and counts those in reach per building, then applies work per
  building in slot order, so the order of builders never matters. Move, AttackMove, Stop, HoldPosition, Gather and
  another Build or Repair end the order (cargo kept); the target gone, finished, or whole again idles the worker.
- **Cancel.** `Command.Cancel(player, position)` (kind 9): the player's own site covering the position is freed (an
  opening change: `Version` bumps, `BlockVersion` doesn't; or, walled in, its cells stay blocked as a pocket with no
  bump, M3-H1), `floor(cost x (WorkNeeded - Work) / WorkNeeded)` of each
  resource comes back, and its workers go Idle. Dropped for a finished building, another player's site, or nothing.
- **Repair.** `Command.Repair(player, worker, position[, queued])` (kind 10): the player's own finished building
  covering the position, below full hit points; dropped for anything else (full, a site, an enemy's, none). Each
  repairer in reach restores `maxHp / buildTicks x repair.rateFactor` (0.5) hit points a tick and the owner pays
  `repair.costFactor` (0.25) `x cost x restored / maxHp` of each resource (docs/02: 50% of the build rate, 25% of
  the cost, scaled by damage). Both run through per-building fixed-point accumulators (`EconomyConstants.RepairFixedOne`
  = 2^16, hashed), so whole hit points and whole resources are exact: one worker restores a Keep from 1,200 to 2,400 hp
  in 1,800 ticks for 34 gold and 34 wood (`floor(0.25 x 275 x 0.5)`), two in 900. Repair stops, and its workers go
  Idle, when a payment would take a total below 0, or at once when the player has 0 of a resource the building costs.
  At full hit points the workers go Idle and the accumulators reset.
- **Damage seam.** `BuildingStore.Damage(handle, amount)` (internal): at 0 hit points the building is freed, an
  opening change (or a pocket, as for a Cancel), so flow fields cached before it stay usable. Combat calls it in M4.
- **Data.** `rules.json` gains `"repair": { "rateFactor": 0.5, "costFactor": 0.25 }` (required; each from 2^-16 to
  1, since a smaller factor rounds to 0 in the 2^16 fixed point and the repair would restore or cost nothing, M3-H1,
  BUG-0092; a missing `repair` object is one error). `ContentHash` covers both. A building's missing `cost` object is now
  one error, like a missing `footprint` (BUG-0079).
- **Hash and replays.** `StateHash` adds each building's `UnderConstruction`, `Work` and repair accumulators, each
  unit's `BuildTarget` (with the gather fields, only when one of them isn't default, so units without them hash as
  before) and a queued Build's type id. Replays stay format 3: the new kinds ride in `c` lines. The golden was
  regenerated for the data hash only (the `repair` block); its checkpoints are byte-identical.
- **Cost.** Placement scans the unit store (`UnitInTheWay`); push-out uses the spatial hash (a Keep on 16 own units in a
  blob of 400: the Build's apply 0.3 ms in Debug, 7.3 ms a tick before M3-H1); nothing allocates in apply or per tick. Measured (Debug,
  this PC, `ConstructionPerfTests`): 500 marching units + 50 workers building 10 sites average 0.98 ms a tick (1.02 ms
  with the same units and no sites); one `CanPlace` on the 128 map averages 0.0013 ms over every anchor and 0.05 ms at
  its slowest. The flood borrows the flow-field cache's build queue storage (`FlowFieldCache.BuildScratch`) instead of
  two map-sized arrays of its own (8 MB on a 1024 map), and clears its visited marks before each flood it runs. So
  `CanPlace`, though it changes no sim state, writes that scratch: the view must call it on the sim thread between
  ticks, never while a tick runs (BUG-0092).
- **Not yet:** the placement ghost and HUD (view), the fog "explored" rule (M4), rubble, real damage (M4),
  start-location Town Halls (M6), events for views. (Population, `popProvided`, production and rally points: M3-4, below.)

### Implementation (M3-4)

A finished building trains the units whose `trainedAt` names it (the M3 criterion "Production queues (5 slots), rally
points, population and cap, refunds on cancel"; the HUD's production card and rally marker are view work, M3-V3).

- **Data.** The loader resolves each unit's `trainedAt` to `UnitDef.TrainedAtTypeId`: it must name a building of the
  unit's own faction, else one `DataError` at `units[i].trainedAt` (unknown id, or another faction's building). It is
  skipped when a buildings file itself had errors, so a broken file is reported once. `GameData.Trains` /
  `UnitsTrainedAt(buildingType)` list the unit ids each building type trains, ascending (an `ImmutableArray<int>` per
  building type, built at load; no dictionary). `requires` stays a string list: in M3-4 a unit with any requirement
  (the Sapper, the Zealot: `["age_ii"]`) is not trainable (`TrainError.LockedByRequirement`) until techs exist
  (M3-5 / M3-6; since M3-6 it is locked only while the requirement is unmet). `ContentHash` covers `TrainedAtTypeId`.
- **Commands** (phase 1; dropped, never thrown, for anything below that fails):
  - `Command.Train(player, building, unitTypeId)` (kind 11; `Position` a point of the building, `TypeId` the unit
    type): the player's own **finished** building covering `Position` queues the unit and the full cost is paid now.
    The rule is `World.CanTrain(player, buildingSlot, unitTypeId, out TrainError reason)` (public, read-only,
    allocation-free, for the view's production card), first rule broken in order: `NoBuilding` (none, another
    player's, or a site: sites have no queue), `UnknownType`, `WrongFaction`, `NotTrainedHere` (its `trainedAt` names
    another building type), `LockedByRequirement`, `QueueFull` (`EconomyConstants.ProductionQueueCapacity` = 5),
    `CannotAfford`.
  - `Command.CancelTrain(player, building, slotIndex)` (kind 12; `TypeId` the queue index, 0 = the head): the item
    leaves the queue and its full cost comes back; later items shift down. A started head also loses its progress and
    releases its population reservation. A bad index (negative, 5, past the count) is dropped.
  - `Command.SetRally(player, buildingCell, target)` (kind 13): the building rides in `TypeId` as a nav cell
    (`y * Width + x`, any cell of its footprint, e.g. `BuildingStore.Cell`), so the target keeps `Position`'s full
    precision. Dropped for a target off the map, no building there, another player's, or a site.
  - `Command.ClearRally(player, building)` (kind 14; `Position` a point of the building, like `Cancel`).
- **Production** (`ProductionSystem.Run`, tick phase 3, buildings in slot order). A head item with no progress starts
  when `HalfPop + unit.HalfPop <= HalfPopCap` for its owner: the reservation is taken and the tick counts
  (`Progress` 1); else it waits with no progress (a full cap pauses the queue, the items behind it too). Each later
  tick adds 1; at `trainTicks` (`round(trainTime x 20)`: a 14 s Heavy Infantry takes 280 ticks counting the tick its
  `Train` applied) the unit spawns, the reservation passes to the unit's own count, and the next item starts in the
  same tick, so each takes exactly its train ticks. With no free cell (or a full unit store) the item stays complete
  and tries again next tick; units never stack.
- **Spawn cell.** The push-out's ring search, shared (`FreeCellSearch`): the rings of cells round the footprint, nearest
  ring first, on the footprint's level, passable, no live unit's center in the cell; within a ring the cell nearest the
  rally point (the footprint's center without one), ties to the lower cell index. The ring walk ends once a ring
  encloses the bounding box of the level's cells (`World.LevelBounds`, terrain only, computed at load), so a full small
  plateau costs its own area, not the map's (BUG-0095's lesson; it bounds the construction push-out the same way:
  same answers, a full 6 x 6 plateau no longer scans the map). Occupancy comes from the spatial hash and the
  push-out's per-cell cache, whose level box is cleared on the tick's first spawn attempt there, so units spawned this
  tick count for later spawns; the spatial hash is rebuilt after a tick with a spawn. The unit stands on the cell's
  center, Idle.
- **Rally.** With a rally point the spawned unit gets the Move rule to it (it arrives within `ArrivalDistance`); a
  `worker` rallied onto a cell of a resource node gets `Gather` on it instead (Age of Empires' "rally on a mine";
  the node a `Gather` there resolves to). Without one it stands Idle at the edge. Rally on a building (repair,
  garrison) is out of scope.
- **Population.** Per player, in half-pop: `World.HalfPop` (every live unit's type `pop`, counted at spawn, plus the
  reservations of started heads) and `World.HalfPopCap` (`min(sum of the finished own buildings' popProvided,
  rules.json popCap)`: a Town Hall +10 = 20, a House +8 = 16, never above 100 pop = 200). Both are kept incrementally
  in `PlayerLedger` (with the gold and wood totals), shared by the stores: `UnitStore.TrySpawn` counts a unit and
  `UnitStore.Free` releases exactly what it counted (so M4 deaths release population for free); a building counts
  toward the cap when it spawns finished or completes, and stops when freed. The dev `SpawnUnit` ignores the cap but
  counts, so the numbers past the cap still add up. A building destroyed or cancelled lowers the cap (nothing dies;
  training elsewhere pauses if the cap is now full), every item of its queue is refunded in full and a started head's
  reservation released. A site has no queue and provides nothing.
- **Storage** (`BuildingStore`, flat, no per-building lists): `QueueCount` (per slot), `QueueTypeId` (`Capacity x 5`
  entries, head first, entries past the count 0; read with `QueueTypeAt(slot, i)`, -1 past the count), `Progress`,
  `HasRally`, `RallyPosition`; `TrainTicks(unitType)` and `ReservedHalfPop(slot)` for the view. Freeing a slot clears
  them (Alloc starts from cleared slots).
- **Hash and replays.** Each live building's production rides in the high half of its construction-state word (bit 0
  a queue, bit 1 progress, bit 2 a rally, bit 3 a non-zero rally point, bit 4 + k queue entry k non-zero), followed by
  the flagged words; a building with none of them hashes exactly as before, so the golden's checkpoints did not move
  (it was regenerated for the data hash only, `TrainedAtTypeId`). A started head's reservation is its `Progress`
  being non-zero, so it is hashed there. `HalfPop` and `HalfPopCap` are derived (from units, queues and buildings)
  and not hashed, like `Speed`; the fuzz checks them against a recount every tick. Replays stay format 3: the new
  kinds ride in `c` lines.
- **Cost.** Production is a slot-order scan of the building store per tick plus, per spawn, a ring search and one
  spatial-hash rebuild; nothing allocates per tick or in the four applies. Measured (Debug, this PC,
  `ProductionPerfTests`): 500 marchers + 50 gatherers + 20 Town Halls training non-stop average 0.41 ms a tick over
  2,000 ticks; a waiting spawn on a full 8 x 8 plateau of a 420 x 420 map costs 0.005 ms (3.0 ms without the level-box
  cap). CLI: `run --workers` prints `player P gold G wood W pop U/C` (pop in whole units, `.5` for a half).
- **Not yet:** techs, Age II and Forge upgrades (M3-5, below), `requires` resolution (M3-6, below), AI build orders (M5), the
  production card, queue and rally visuals (view M3-V3), rally on a building, events for views.

### Implementation (M3-5)

Age II research and the Forge upgrades (the M3 criterion "Age II research and unlocks; Forge upgrades"): techs get a
data schema, a building researches them through the M3-4 production queue, each player keeps a tech state, and the
bonuses are a read-only query that M4 combat will apply. The research button and card are view work (M3-V3).

- **Schema** (see "Data format"): `common/techs.json` holds the techs every faction shares, `age_ii` and the six Forge
  upgrades (`melee_weapons_1 / _2`, `ranged_weapons_1 / _2`, `armor_1 / _2`); `factions/<id>/techs.json` holds that
  faction's upgrade (`moranth_supply`, `dryjhnas_prophecy`). Both are required. A tech names the building **slot** that
  researches it (`researchedAt`: `town_hall`, `forge`, ...), so a common tech is researched at each faction's building of
  that slot; a faction without one is an error at the tech's `researchedAt`. Numbers are docs/02 "Tech" and the faction
  pages. The Forge table's "+1 / +2" are totals: each level adds 1. Docs/02's filters map to `appliesTo`: "melee units"
  is `attackType: melee` (workers have a melee attack, so they count), "pierce units and towers" is `attackType: pierce`
  (towers have no attack data until M4), "all non-siege units" is `siege: false` (not the `siege` slot: the Sapper, a
  unique with a siege attack, gets armor). A faction upgrade's `units` filter names its own units (another faction's is
  an error). An `abilityCooldown` effect names the unit whose ability it changes (M4's abilities schema may add an
  ability filter).
- **Loader.** `TechDef` (ids in ordinal order across all techs files, `Faction` -1 for common, `ResearchedAtSlot`,
  `ResearchTicks` = `round(researchTime x 20)`: Age II 60 s = 1,200 ticks, cost, `Requires` strings, `Effects`).
  `TechEffect` is a struct with the filters resolved to ints at load: `Stat` (`TechStat`), `Amount` in sim units (whole
  points for attack / armor / hp, meters for range, **ticks** for ability cooldown: -15 s = -300), `AttackType` (damage
  type id, -1 any), `Tags` (ids into `GameData.UnitTags`, the sorted tags of all units; the unit needs one of them),
  `Units` (unit ids), `Siege` (1 / 0 / -1 any); every set filter must match. `GameData.Techs`, `FindTech`,
  `TechsResearchableAt(buildingType)` (ascending `ImmutableArray<int>` per building type: the techs of its slot that are
  common or its faction's), `AgeTechs` (`DataLimits.AgeTechIds` = `["age_ii"]` resolved; entry k researched = Age
  k + 2; a missing one is an error). `ContentHash` covers every tech and effect field and the buildings' `requires`.
  Every `requires` entry of a tech, a building or a unit must name a tech or building id (one error at the entry); the
  check, like the per-faction slot check and the effects' unit and tag references, runs only when the files it points
  into were read whole (no broken buildings file, every unit id accepted, every techs file read), so a broken file is
  one error. Whether a requirement is met is **M3-6** (Age II's "any two of" rule too: its `requires` ships as `[]`);
  until then `CanResearch` does not look at `requires` and `CanTrain`'s `LockedByRequirement` stays "has any". (Since
  M3-6 both gate on the resolved requirements; see "Implementation (M3-6)".)
- **Commands** (phase 1; dropped, never thrown, when a rule fails):
  - `Command.Research(player, building, techId)` (kind 15; `Position` a point of the building, `TypeId` the tech; not a
    unit order, so the queued flag is refused): the player's own **finished** building there queues the tech and its
    full cost is paid now. The rule is `World.CanResearch(player, buildingSlot, techId, out ResearchError reason)`
    (read-only, allocation-free), first rule broken in order: `NoBuilding` (none, another player's, a site),
    `UnknownTech`, `WrongFaction` (another faction's upgrade), `NotResearchedHere` (not in
    `TechsResearchableAt(type)`), `AlreadyResearched`, `AlreadyQueued` (the player has it queued at any of its
    buildings: a tech is queued once at a time), `QueueFull`, `CannotAfford`.
  - Cancelling: `Command.CancelTrain(player, building, slotIndex)` cancels any queue item, a unit or a tech, with a full
    refund (one cancel for the production card; no new kind).
- **Queue items.** `BuildingStore` keeps a parallel `QueueIsTech` flag per queue entry; `QueueTypeAt` keeps returning
  the type id (a tech id when `QueueIsTechAt(slot, i)`), `ItemTicks(slot, i)` gives an item's train or research ticks,
  and `Progress` counts both. A tech at the head starts at once (no population, `ReservedHalfPop` 0) and on its last
  tick sets the owner's flag and leaves the queue; nothing spawns, and the next item starts in the same tick. A
  destroyed building refunds its queued techs in full; a tech is never half-researched.
- **Tech state** (`Economy/TechState`, reached through `World`): per player one bit per tech (`World.HasTech(player,
  tech)`, `World.Age(player)`: 1 at start, 2 once `age_ii` is researched). Bonuses: at world creation every effect is
  matched against every unit type into a (unit, stat, tech) table; when a flag changes the player's sums per unit type
  and stat are recomputed, so `World.TechBonus(player, unitType, TechStat)` is one array read (allocation-free; 0 for
  out-of-range ids). **Combat does not apply it yet** (M4 adds `TechBonus` to attack, armor, range, hp, and ability
  cooldowns).
- **Hash.** The flags are state: a player with any tech sets the high half of its gold word and its flag words follow;
  a queue entry's tech flag is bit 9 + k of the building's production bits (no extra word); research progress is the
  building's `Progress`, hashed since M3-4. The bonus sums are derived and not hashed. A world with no tech researched
  and no tech queued hashes exactly as before, so the golden's checkpoints did not move; it was regenerated for the data
  hash only (`techs.json`, the buildings' `requires`). Replays stay format 3: `Research` rides in `c` lines.
- **Cost.** Research adds a flag check to the production scan; `CanResearch` scans the player's queues for "already
  queued". Measured (Debug, this PC, `ResearchPerfTests`): the M3-4 criterion-10 scene plus 10 Forges researching
  non-stop averages 0.42 ms a tick over 2,000 ticks; `TechBonus` for 500 units x 2 stats costs 0.007 ms. The applies,
  completion and queries allocate nothing (`AllocationTests`). CLI: `run --workers` prints `... pop U/C age A`.
- **Not yet:** combat and ability use of the bonuses (M4), AI research (M5), the research button / card (view M3-V3),
  events for views. (`requires` gating and Age II's any-two-halls rule: M3-6, below.)

### Implementation (M3-6)

`requires` is real: a unit can't be trained, a building can't be placed and a tech can't be researched until its
requirements are met, with Age II's "any two of Infantry Hall / Ranged Hall / Shock Hall / Forge" (docs/02 "Ages") read
from data (the M3 criterion "Age II research and unlocks"). The view's greying and reason text are M3-V3; it reads the
three gates below.

- **Resolution at load.** Every `requires` entry of a unit, building or tech resolves to a tech id or a building *type*
  id: `RequiresTechs` / `RequiresBuildings` on `UnitDef`, `BuildingDef` and `TechDef`, each an ascending,
  de-duplicated `ImmutableArray<int>` (the strings stay in `Requires` for tools). A tech requirement is met once the
  player has researched it; a building requirement by one of the player's own **finished** buildings of that type (a
  site doesn't count, nor an enemy's). `ContentHash` covers the resolved arrays as well as the strings.
- **`requiresAnyOf`** (optional, techs only): `{ "count": n, "of": [...] }`. Each `of` entry is a building **slot** id,
  so a common tech works for every faction, or, in a faction's own techs file, one of its building ids, which counts as
  that building's slot. It resolves to `TechDef.RequiresAnyOfSlots` (ascending `BuildingSlot` values) and
  `RequiresAnyOfCount`; it is met when at least `count` of the distinct listed slots hold an own finished building (two
  Barracks fill one slot). Validation, one error at the field each: `count` missing or outside 1 to the number of `of`
  entries; `of` missing; an entry that is neither a slot id nor (in a faction file) an own-faction building id; an entry
  naming a slot already named. Shipped only on `age_ii`: `{ "count": 2, "of": ["infantry_hall", "ranged_hall",
  "shock_hall", "forge"] }`; every other tech has none.
- **Gates** (one rule each, read-only and allocation-free, shared by the command's apply step and the view;
  `Economy/Requirements`):
  - `World.CanTrain`: `TrainError.LockedByRequirement` (after `NotTrainedHere`, before `QueueFull`) when a required tech
    is not researched or a required building type has no own finished instance.
  - `World.CanPlace`: new `PlacementError.Requires`, right after `WrongFaction` and before `OffMap` (cheap, so before the
    map rules and the seal flood; the `Build` apply path checks it in the same place). Full order: `UnknownType`,
    `WrongFaction`, `Requires`, `OffMap`, `Blocked`, `SealsGround`, `UnitInTheWay`, `CannotAfford`, `StoreFull`. The
    enum's numbers moved up by one from `OffMap` on; the view keys its text by member name (`requires`).
  - `World.CanResearch`: new `ResearchError.Requires`, after `NotResearchedHere` and before `AlreadyResearched`, for an
    unmet `requires` or `requiresAnyOf`. Full order: `NoBuilding`, `UnknownTech`, `WrongFaction`, `NotResearchedHere`,
    `Requires`, `AlreadyResearched`, `AlreadyQueued`, `QueueFull`, `CannotAfford`.
  A refused command is dropped with nothing changed (totals, queues, store). The dev command `SpawnBuilding` ignores
  requirements, as it ignores cost and faction.
- **Queue-time rule.** Requirements are checked when the command applies (a queued `Build` when it starts, phase 7),
  never again at completion: a unit or tech already queued survives the loss of what unlocked it (Age of Empires'
  rule: a Barracks destroyed mid-training doesn't cancel the item; Age II keeps researching if a hall falls). Age II
  completing in phase 3 of tick T opens the gates for commands applying in phase 1 of tick T + 1; it unlocks nothing
  retroactively (a command already refused stays refused).
- **Finished counts.** `PlayerLedger` keeps each player's finished buildings per type and per slot, updated by
  `BuildingStore` when a building spawns finished, completes or is freed (a site never counts). They are derived from
  the store, so not hashed (like `HalfPopCap`); `RequirementFuzzTests` recounts them against the store every tick, and
  `StateHashTests` shows changing them doesn't move the hash. The gates read them in O(1) per requirement.
- **Loader nits folded in.** BUG-0098: an `appliesTo.units` or `.tags` set to `[]` is one error at the list (it used to
  read as "no filter", every unit). BUG-0099: a `requires` cycle among techs and buildings (a self-requirement too) is
  one error at the entry that closes it (an iterative depth-first search in id order, techs before buildings, entries in
  file order); a tech id equal to a building id is one error at the tech's id. BUG-0008: every data file gets a
  `Utf8JsonReader` pre-pass (linear in the file, load time only) that reports a key repeated in one object, once, at its
  path (`units[2].attack.bonusVs.heavy`); a file that isn't well-formed JSON reports only the parser's error; a UTF-8
  byte order mark is still accepted. BUG-0010: each faction's `units.json` fills the seven unit slots and its
  `buildings.json` the ten building slots, one entry each: no entries is one error at the list, a second entry in a
  slot one error at its `slot`, an empty slot one error at the list; skipped when the file already has an error. A
  buildings file failing it counts as broken, so the checks into it (`trainedAt`, `researchedAt`, `requires`) are
  skipped and it is not reported again through them.
- **Hash and replays.** No new sim state is hashed: the tech flags and queues were already. The golden was regenerated
  for the data hash only (`age_ii.requiresAnyOf` and the resolved arrays in `ContentHash`); its checkpoints are
  byte-identical. Replays stay format 3.
- **Cost.** Measured (Debug, this PC, `RequirementPerfTests`): the M3-5 research scene with every command through its
  gate (the halls' Trains, the Forges' Research, plus 20 locked Trains, 10 locked Researches and 10 refused Builds a
  tick) averages 0.42 ms a tick over 2,000; 5,000 locked Train / Build / Research commands applying in one tick take
  0.73 ms. The gates and the locked applies allocate nothing (`AllocationTests`).
- **Not yet:** the view's greying and reason text (M3-V3), AI use (M5), combat use of the bonuses (M4); `requiresAnyOf`
  edges are not part of the cycle check (an any-of that its own members require could deadlock; none ships).


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
    rules.json               # starting resources, pop cap, gather rates, repair factors
    resources.json           # resource node types: tree, gold mine (M3-1)
    techs.json               # techs every faction shares: Age II, Forge upgrades (M3-5)
  factions/<faction_id>/
    faction.json             # id, displayName, bonus, palette, resource display names
    units.json
    buildings.json           # building types (M3-2 schema; all ten slots per faction since D1)
    techs.json               # the faction upgrade (M3-5)
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

Shipped files: `common/damage_table.json`, `common/rules.json`, `common/resources.json` (M3-1), `common/techs.json`
(M3-5), and `faction.json` + `units.json` + `buildings.json` (M3-2) + `techs.json` (M3-5) for `malazan` and
`whirlwind`. All twelve are required. Everything else in the tree above (`statuses`, `abilities`, `ai`, `maps`, other
factions) arrives with the milestone that consumes it.

| File | Shape |
| --- | --- |
| `damage_table.json` | `armorClasses: [{id, displayName}]`, `damageTypes: [{id, displayName, ignoresArmor?, multipliers: {<armorClass>: x}}]`. Every type must list every class. `ignoresArmor` (default false) is how Magic skips armor |
| `resources.json` | `{ "resources": [ ... ] }`, each `{id, displayName, description, resource, footprint {width, height}}`: `resource` is `gold` or `wood`, footprint sides are cells, 1-4 (`DataLimits.MaxFootprint`). Amounts are not here: a tree holds `rules.json` `treeWood`, a mine `startMines.gold` (M3-1, see "Economy implementation") |
| `rules.json` | docs/02 Economy table: `startingGold`, `startingWood`, `startingWorkers`, `popCap`, `workerCarry`, `gatherRate {gold, wood}` (per second), `startMines` / `expansionMines {count, gold}`, `treeWood`, `nodeSearchRadius`. Pop provided by buildings comes with `buildings.json` (M3); the Age II cost with `techs.json` (M3-5) |
| `faction.json` | `id` (must equal the folder name), `displayName`, `description`, `bonus {displayName, description}`, `resources {gold, wood: {displayName}}`, `palette {primary, secondary, accent}` as `#RRGGBB` |
| `buildings.json` | `{ "buildings": [ ... ] }`, each `{id, displayName, description, slot, footprint {width, height}, hp, armor, cost {gold, wood}, buildTime, popProvided, dropOff}`: `slot` is one of the ten template slots (`DataLimits.BuildingSlotIds`), footprint sides 1-4 cells, `buildTime` in seconds (to ticks), `popProvided` a multiple of 0.5 (to half-pop), `dropOff` a required bool, `requires` an optional list of tech / building ids (M3-5; resolved and gating placement since M3-6). Ids unique across factions (M3-2, see "Economy implementation"); exactly one building per slot per faction (M3-6, BUG-0010) |
| `techs.json` | `{ "techs": [ ... ] }` in `common/` (shared by every faction) and in each faction folder (its own), each `{id, displayName, description, researchedAt, cost {gold, wood}, researchTime, requires?, requiresAnyOf?, effects: [{stat, amount, appliesTo {attackType?, tags?, units?, siege?}}]}`: `researchedAt` is a building slot id (a common tech resolves to each faction's building of it; a faction without one is an error), `researchTime` in seconds (to ticks), `requires` tech / building ids (resolved and gating research since M3-6), `requiresAnyOf` `{count, of: [slot ids, or in a faction file its own building ids]}`: met when `count` of the distinct listed slots hold an own finished building (M3-6; shipped on `age_ii` only), `effects` may be empty. `stat` is one of `attack`, `armor`, `range`, `hp`, `abilityCooldown` (`DataLimits.TechStatIds`); `amount` is non-zero, whole for attack / armor / hp, meters for range, seconds for ability cooldown (to ticks, negative shortens it). `appliesTo` is required (`{}` = every unit); each filter it sets must match: `attackType` a damage type, `tags` any one of the unit's tags (each must be a tag some unit has), `units` unit ids (a faction tech's must be its own), `siege` true / false for the `siege` slot; a `tags` or `units` list set to `[]` is an error (M3-6, BUG-0098). Tech ids are unique across all techs files and may not equal a building id (M3-6); `DataLimits.AgeTechIds` (`age_ii`) must be a common tech (M3-5, see "Implementation (M3-5)") |
| `units.json` | `{ "units": [ ... ] }`, entries as in the example. `attack` also takes optional `minRange`, `splash` (m), and `friendlyFire` (docs/02 "Combat / Stats"); they default to 0 / false. Melee range is written as `0.5` (edge to edge) |

Validation rules: ids are `snake_case` and unique (a unit id is unique across all factions);
`slot` is one of the seven template slots; `armorClass`, `attack.type`, and `bonusVs` keys exist in
the damage table; hp, speed, sight, radius, `attack.cooldown`, `trainTime`, and gather rates are
positive; `radius` is within 0.4-1.0 m; `pop` is a multiple of 0.5; every number is checked
against an upper bound before it is narrowed (`DataLimits`: integers at most 1,000,000, decimals at
most 1,000,000, durations at most 3600 s), so nothing overflows to Infinity or a wrapped int
(BUG-0007); every `requires` and `tags` entry is a `snake_case` id (BUG-0009); unknown JSON fields are errors
(a typo must not silently fall back to a default), and so is a key repeated in one object (M3-6, BUG-0008); each
faction fills the seven unit slots and the ten building slots once each (M3-6, BUG-0010). The only literals in C# are these schema limits,
in `DataLimits`.

Conversions at load: durations (`cooldown`, `windup`, `trainTime`) become ticks,
`round(seconds × 20)` (2.2 s → 44, 0.45 s → 9); speed and gather rates become per-tick values;
`pop` and `popCap` become half-pop integers (1 → 2, 1.5 → 3). String ids become dense ints in
ordinal-sorted order of the id strings (units across all factions, factions, armor classes,
damage types, resource node types), so ids never depend on file order or file-system enumeration. `GameData` holds
`ImmutableArray`s indexed by those ids; `FindUnit` / `FindFaction` map a string id back by binary
search, for load time, tests, and tooling only (`FindResource` too, since M3-1).

`trainedAt` resolves at load (M3-4) to `UnitDef.TrainedAtTypeId`, an own-faction building type (one error at
`units[i].trainedAt` otherwise), and `GameData.UnitsTrainedAt(buildingType)` lists each building's units.
`requires` (units, buildings, techs) resolves at load (M3-6) to `RequiresTechs` / `RequiresBuildings` (tech ids and
building type ids, ascending; the strings are kept): one error at an entry naming neither, one at the entry closing a
`requires` cycle. The gates `CanTrain` / `CanPlace` / `CanResearch` check them (see "Implementation (M3-6)"). Not resolved yet
(kept as plain strings): `model` (M2/M6 asset pipeline) and `projectile`
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
  Hud (CanvasLayer)       # resource bar, minimap (M2-4), selection panel, command card, alerts
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
  `--screenshot <path> --screenshot-after <seconds>` (default 2); later tasks added `--units`,
  `--zoom`, `--no-hud`, `--debug-overlay`, `--forests`, `--mines`, `--mute` (M2-6, mutes the
  master audio bus), `--bench <seconds>` and `--vsync on|off` (M2-7). Bad values log a warning and are ignored. Example: `& $env:GODOT --path game -- --screenshot C:\temp\shot.png --screenshot-after 2`.

### Implementation (M2-2)

Placeholder unit views, selection and right-click move. New nodes in `Match.tscn`:

```
Match
  SimRunner
  World3D
    ...TerrainView
    UnitViews          Node3D (UnitViews.cs): one capsule MeshInstance3D per unit slot
    SelectionRings     MultiMeshInstance3D (SelectionRings.cs): one ring instance per selected unit
  RtsCamera
  SelectionController  Node (SelectionController.cs): picking, selection, move orders
    BoxLayer/Box       CanvasLayer + ColorRect drag box (mouse_filter ignore)
  DebugOverlay         now also "sel N"
  Screenshotter
```

- **Start armies (debug until real start locations):** `Match.Start` enqueues `SpawnUnit` for
  `--units <n>` per player (0-1000, default 100). Player p plays faction p in data order
  (malazan, whirlwind) until the M6 lobby and spawns its roster round-robin. Positions come from
  the pure `ViewApi.StartLayout.Block(NavGrid, count, west, maxRadius)`: open cell centres
  nearest the middle of an inner column 3 cells from the map's centre line (west block for player
  0, east for player 1), scored `dx² + dy²` (a half-disc), ties to the lower cell index, on a
  lattice of `ceil(2·maxRadius / 2 m)` cells so bodies never overlap. Since M2-H2 (BUG-0085) a cell
  is open when it is passable, not a ramp, and none of its 8 neighbours is `Blocked` (no tree, mine,
  cliff edge or border ring next to a unit: docs/02 "Map", start locations are open bases), and a
  block stays in one clearing: the open cells joined to its first cell through open cells on that
  cell's level. The first cell is the best-scoring one whose clearing holds the whole block (else
  the one with the roomiest clearing), so a small terrace by the centre line can't split the army.
  No RNG; fewer positions come back (with a warning) if no clearing on that side has room. The CLI's
  march scenario uses the same blocks. The camera starts on player
  0's block centre. The units appear on the sim's second tick.
- **Unit views:** `UnitViews.Bind` makes one `CapsuleMesh` per unit type (radius from
  `UnitDef.Radius`, height `2·radius + 1 m`, 12 radial segments) and one `StandardMaterial3D` per
  faction whose albedo is the faction's `PrimaryColor` (0xRRGGBB, sRGB). The material is per
  faction, not per owner: with player p = faction p they are the same today, and M6's per-player
  colour replaces it. A slot's node is created the first time the slot is alive and hidden while
  it is dead; a respawn into the slot reuses it (and swaps the mesh if the type changed). After a
  slot's first use nothing is created; the per-frame update of 2,000 units allocates 0 bytes and
  costs about 0.5 ms (`game/tests/UnitViewsTest.tscn`). No physics bodies.
- **Interpolation:** each frame, ground point = lerp(`PrevPosition`, `Position`, alpha) with
  alpha = `SimRunner.Alpha` clamped to [0, 1] (NaN shows the current tick), so a view never runs
  past the sim. The capsule's centre sits `height / 2` above `ViewApi.TerrainHeight.At(Heightmap,
  x, y)`. Facing is the current tick's (no previous facing in the sim yet).
- **Terrain height:** `TerrainHeight.At` returns the drawn surface: flat plateau cells, the ramp
  plane on ramp cells, the point clamped onto the map, and on a cell boundary the higher-index
  cell (floor, like `NavGrid.WorldToCell`). The clamp happens in float before the cell index is
  cast to `int`, so any input (1e10, ±Infinity, `float.MaxValue`, NaN) gives a finite edge-cell
  height instead of throwing (BUG-0052); NaN lands on cell 0 and, on a ramp, on the cell's first
  corner. It and `TerrainMeshBuilder` share
  `TerrainHeight.CellCorners`, and a test checks every cell corner and centre of 20 generated
  maps and the 4x4 hand map against the mesh.
- **Facing convention:** sim `Facing = Atan2(vy, vx)` points along `(cos θ, sin θ)` on the ground,
  which is Godot `(cos θ, 0, sin θ)`. A view's forward is Godot's -Z, so its yaw about +Y is
  `-θ - π/2` (`UnitViews.Yaw`). `UnitViewsTest` checks a unit walking +x plus facings +y, -y and
  two diagonals (+x alone can't tell the yaw's sign), and QA's `QaM22Test` sweeps 64 angles.
- **Selection** (input actions `select` = LMB, `command` = RMB, `select_add` = Shift): on release,
  every own (player 0), live unit in front of the camera is projected at its body centre; its
  pixel radius is `radius · viewportHeight / (2 · depth · tan(fov / 2))`. Less than 4 px of mouse
  travel is a click: the nearest projected centre within max(radius, 12 px) wins, exact ties to the
  lowest slot (`ViewApi.ScreenPicker.PickClick`). 4 px or more is a box: every centre inside,
  edges inclusive, either corner order (`PickBox`). A plain click or box replaces the selection,
  Shift + click toggles the unit, Shift + box adds; a plain click on no own unit clears it, a
  Shift + click on nothing keeps it. Enemies are never candidates. The selection is a
  `ViewApi.SelectionSet` of handles (fixed capacity, selection order kept), pruned of dead or
  recycled handles every frame; `SelectionRings` draws one flat unshaded torus per live selected
  unit (radius × 1.35, 6 cm above the ground).
- **Right-click move:** the camera ray (`ProjectRayOrigin` / `ProjectRayNormal`) goes to
  `ViewApi.GroundPicker.TryPick(Heightmap, origin, direction)`: the ray is clipped to the map's box
  (-1 to 9 m high: 1 m below the lowest level and above the highest), walked cell by cell (grid DDA), and solved exactly against each cell's surface
  plane. A ray that enters a cell already below its surface has hit the wall on that boundary,
  which belongs to the higher cell, so a click on a cliff face orders units to the upper plateau.
  The hit is kept 1 mm inside its cell. Rays that miss the map return false and the click
  enqueues nothing. Otherwise one `Command.Move(0, handle, (hit.x, hit.z))` per selected unit; an
  order that would overflow the command queue is dropped whole with a warning. Measured against
  a 5 mm ray march: worst error 0.02 mm at both zoom limits on ramps, plateaus and high ground.
- **Read-only:** `game/` changes sim state only through `Simulation.Enqueue` (Match, SelectionController)
  and `Tick` (SimRunner). The ViewApi helpers take a `Heightmap`, `NavGrid`, spans or arrays and keep
  no sim reference.
- **Launch flags added:** `--units <n>` (0-1000 per player, default 100) and `--zoom <m>` (start zoom,
  clamped 20-60; for perf runs). Flag parsing never takes a token starting with `--` as a value, so
  `--seed --speed 2` warns about `--seed` and still applies speed 2 (BUG-0041).
- **Tests:** `ViewApi/TerrainHeightTests`, `GroundPickerTests`, `ScreenPickerTests`,
  `SelectionSetTests`, `StartLayoutTests`, `ViewApiAllocationTests` (xUnit); headless scenes
  `res://tests/UnitViewsTest.tscn` ("UNITVIEWS TEST PASS") and `res://tests/SelectionTest.tscn`
  ("SELECTION TEST PASS"), run with `& $env:GODOT --headless --path game res://tests/<name>.tscn`.
  Headless windows are 64 x 64 px, so `SelectionTest` sets the root window to 1152 x 648 first.

### Implementation (M2-3)

Order keys, Shift-queue, type select, control groups and Tab subgroups. No new nodes; everything
is in `SelectionController`, two pure ViewApi helpers, and the input map.

- **Input actions** (`game/project.godot`, all physical keys, each rebindable alone):
  `order_attack_move` A, `order_stop` S, `order_hold` H, `order_cancel` Esc, `order_queue` Shift,
  `select_type` Ctrl, `group_assign` Ctrl, `group_add` Shift, `group_1`..`group_9` digits 1-9,
  `subgroup_next` Tab. `order_queue` and `group_add` share Shift with `select_add`, and
  `group_assign` shares Ctrl with `select_type`: one action per purpose, so rebinding one never
  moves another. Key actions carry no modifiers, so Ctrl + 1 still matches `group_1`; the
  controller reads the held modifier actions with `Input.IsActionPressed`. Echo (key repeat)
  presses are ignored. The controller never marks events handled, so the camera still sees them.
- **One order path:** `SelectionController.Order(CommandKind kind, Vector2? point, bool queued)`.
  Kinds: `Move`, `AttackMove` (need a finite point, else nothing), `Stop`, `HoldPosition` (point
  ignored); anything else throws. It prunes the selection, does nothing if it is empty, and drops
  the whole order with one warning (`DroppedOrders` + 1) if `PendingCommandCount + selected`
  exceeds `CommandCapacity`, for every kind. Otherwise one command per selected unit, with
  `Command.QueuedFlag` when `queued`. `IssuedCount(kind)` counts the commands enqueued per kind
  (dev and test readout). Callers: 3D right-click (Move), A + click (AttackMove), S / H keys,
  and the minimap's right-click (Move, when not targeting). Each passes `queued = Input.IsActionPressed("order_queue")`
  at the moment of the click or key.
- **A (attack-move targeting):** A with a non-empty selection sets `Targeting` (A again keeps
  it; with nothing selected A does nothing). While targeting, a left press is never a selection:
  if the camera ray hits the map it orders `AttackMove` to the picked point and ends targeting; off
  the map it does nothing and targeting stays. The matching release is ignored. Esc
  (`order_cancel`), S, H and any right-click end targeting. A right-click then orders nothing,
  whether it lands on the 3D view or the minimap (`SelectionController.CancelTargeting()`, called
  from the minimap's right-click branch; BUG-0068). A minimap left click jumps the camera and keeps
  targeting (minimap attack-move is not built). Targeting also ends when the selection becomes
  empty (every selected unit died or was freed). This is checked each frame after the prune and
  again on the next left press, so that click selects normally instead of being swallowed
  (BUG-0067). The debug label shows a trailing `A` while targeting; no cursor art yet.
- **S / H:** `Order(Stop | HoldPosition, null, queued)` for the whole selection. Shift + S queues a
  Stop behind the current orders (docs/03 "Orders and unit states": a moving unit keeps moving
  and stops on arrival). There is no "holding" indicator: Hold ends when a queued order starts
  (BUG-0056), so it would lie.
- **Type select:** on the release of a click (not a drag) that picks an own unit, if the press was
  a double-click (`InputEventMouseButton.DoubleClick`) or `select_type` (Ctrl) is held, the
  selection becomes every own, live unit of that unit's type whose projected centre lies in the
  viewport (inclusive edges, in front of the camera: the box picker over the whole screen,
  filtered by type). With `select_add` (Shift) it adds instead. Enemies are never candidates.
  A double-click or Ctrl + click on empty ground or an enemy is an ordinary click (clears, or keeps
  with Shift); Ctrl during a drag is an ordinary box.
- **Control groups:** the pure `ViewApi.ControlGroups` holds nine `SelectionSet`s (keys 1-9 are
  groups 0-8; fixed capacity, no sim reference) and is pruned every frame with the selection. Ctrl +
  digit replaces the group with the selection (an empty selection leaves the group alone); Shift +
  digit adds the selection; Ctrl wins if both are held. A plain digit prunes the group and
  replaces the selection with its live units (an empty group changes nothing), so a dead or
  recycled slot (even one respawned for the enemy) is never recalled. A second plain press of the
  same digit within `ControlGroups.DoubleTapSeconds` (0.3 s of wall clock, `Time.GetTicksMsec`;
  a recall of another group in between breaks the pair; times are rounded to whole milliseconds
  before comparing, so a gap of exactly 300 ms is a double-tap at any clock value, BUG-0067) centres the camera on the group's mean
  position (`TryMean`, current tick positions); the double-tap is consumed, so a third press
  starts a new pair.
- **Tab subgroups:** the pure `ViewApi.Subgroups` lists the selection's distinct unit types in
  ascending type id (one pass over the selection and one over the type ids; no sort, no
  allocation after construction) and keeps an active index. Tab advances and wraps; with an
  empty selection it does nothing. Every selection the player makes (click, box, type select,
  group recall) resets it to the first type. Each frame the list is refreshed from the pruned
  selection without a reset: the active type stays active while any unit of it is selected, else
  the first. The debug label shows `sub <typeId> <n>/<m>` (index + 1 of the count). Nothing else
  reads it until the command card (M3).
- **Tests:** `ViewApi/ControlGroupsTests` (assign / add / recall, recycled slots, prune of all
  nine, full-capacity groups, index range, double-tap boundary and consumption, whole-millisecond
  gaps, mean),
  `ViewApi/SubgroupsTests` (ascending distinct types, wrap, empty, reset, refresh keeps or falls
  back, out-of-range types), a `ViewApiAllocationTests` row (groups and subgroups over 2,000
  units, 0 bytes), QA's `ViewHashTwinQaTests` (all four kinds, queued or not, and group recalls
  between orders: the view-driven sim equals a bare twin every tick), and the headless scene
  `res://tests/OrdersTest.tscn` ("ORDERS TEST PASS"). It disables `SimRunner` and calls `Tick()`
  itself, so tick counts are exact (a command enqueued before tick N applies on tick N + 1, the
  second `Tick()` call). It injects key and mouse events into the real Match scene: double-click,
  Shift + double-click and Ctrl + click type select against the on-screen set; Ctrl + 1, Shift +
  2, recall after a freed slot respawns as an enemy, the double-tap focus; Tab over three types,
  the label, reset and the empty case; S on 10 walkers (all Idle with `GoalCell` -1 two ticks
  later); H (every unit holding, then 10 other units walk through them: holders move 0 m); Shift +
  right-click three points (each unit's goal goes p1, p2, p3 and it comes within 5 m of each in
  turn); A + click (an `AttackMove` per unit, the selection kept, the units arrive within 5 m);
  Esc, right-click, A twice and an off-map click while targeting; Shift + S; and S, H, Shift + S,
  Shift + right-click with 1,000 selected against 3,097 pending commands (all four dropped whole).
  From the queue check on, the scene keeps only five spread-out units on the field, because
  walkers that meet idle, holding or packed friends give up (BUG-0028, sim side). QA's
  `res://tests/QaM23Test.tscn` ("QA M2-3 TEST PASS") pushes events through the viewport's real
  routing. Its targeting steps check that a minimap right-click while A is armed cancels and
  orders nothing (and orders a Move when A is not armed), that a 3D right-click and Esc still
  cancel, and that after every selected unit is freed while A is armed the next click is a plain
  selection (with and without a frame in between).

### Implementation (M2-4)

The minimap, the first HUD element. New nodes in `Match.tscn`:

```
Match
  ...
  Hud                CanvasLayer (hidden by --no-hud)
    Minimap          Control (Minimap.cs): bottom-left, 8 px margin, 220 x 220 px, mouse filter Stop
  DebugOverlay
```

- **Pixels:** the pure `ViewApi.MinimapRaster(Heightmap, NavGrid, ownerRgb[], maxDots)` holds two
  RGBA8 layers with one pixel per map cell, row = map y. The terrain layer is baked once in
  `Match.Start` with the 3D mesh's palette (`TerrainMeshBuilder.LevelColor` / `RampColor` /
  `CliffColor`): ramp cells get the ramp tint, nav-grid cliff cells (the lip above a drop and ramp
  walls) the cliff tint, any other cell its level tint, darkened by `ImpassableShade` (0.6) if it is
  blocked anyway (the map's border ring, sealed pockets). `DrawDots(alive, positions, owners)`
  clears the previous dots (only the 4 x 4 blocks it wrote, so cost scales with units, not map
  area) and draws each live unit as a 2 x 2 block in the owner's colour inside a one-cell rim
  (4 x 4 cells, clipped at the map edge). The 2 x 2 centre is the four cells around the cell corner
  nearest the unit (`MinimapRaster.CentreOf`), so the unit's own cell is always one of them; at the
  shipped 220 px for 128 cells it is at least 3 screen pixels per axis, so a lone unit reads in its
  player's colour, not its rim's (M2-H2, BUG-0069; it was one owner cell in a 3 x 3 rim). The
  pixels are written as 32-bit words and copied to the byte layer once per draw. The rim colour is
  `MinimapRaster.RimFor(colour)`: near-black
  (`DarkRim` 0x141414) around light colours, light grey (`LightRim` 0xE6E6E6) around dark ones
  (Rec. 601 luma under 0.35). The rim is what tells a lone dot from terrain of a similar colour: a
  Whirlwind dot (#C8892E) from a 1-2 cell ramp tick, a Malazan dot (#4B4F55) from a cliff lip or
  the border ring (BUG-0064). All rims are drawn before any centre, so in a crowd no rim covers a
  unit; among centres, later slots overwrite earlier ones, and every unit's own cell is drawn last,
  so a neighbour's centre never hides where a unit stands. Unknown owners and non-finite positions
  draw nothing, positions are clamped onto
  the map (floor, like `NavGrid.WorldToCell`). It allocates nothing after construction. Player p's
  colour is faction p's `PrimaryColor` (the same rule as the unit views until M6).
- **Refresh rates:** the terrain `ImageTexture` is made once. The dot texture is redrawn from the
  current tick's positions (no interpolation) every 4 ticks, 5 Hz at 1x speed, and costs about
  0.13 ms at 2,000 units with the rims (`MinimapTest`, raster + upload). The camera outline is
  redrawn every frame in `_Draw`. Both textures use nearest filtering, so dots stay crisp cells.
- **Maps wider than the control (note for later):** at 220 px, a map over 220 cells per axis has
  more texels than pixels, and nearest filtering skips some rows and columns (about 26% of cells
  on a 256 map). A 4-cell-wide dot keeps at least one sampled texel per axis up to 880 cells and
  its 2-cell centre up to 440, so over 440 cells a unit can show only its rim colour, not its owner
  colour (with the M2-H1 one-cell centre that started over 220 cells, BUG-0070). `Match` only
  runs 128 x 128 today. When bigger maps ship, draw the dots in screen space (one dot per screen
  pixel) or downsample with a min-filter that keeps unit pixels.
- **Transform:** the pure `ViewApi.MinimapTransform(controlPixels, mapMeters)` fits the map into
  the control preserving aspect: scale = min(width / mapW, height / mapH), centred, so a non-square
  map is letterboxed (the bars show a dark backdrop). `ToPixel(map)` and `TryToMap(pixel)`
  convert; `TryToMap` is false outside the map area and for NaN/Inf. Screen-up on the minimap is
  map -y, the same as the 3D camera.
- **Camera outline:** the four viewport corners' camera rays (`ProjectRayOrigin` /
  `ProjectRayNormal`) meet the horizontal plane at the terrain height under the camera focus
  (`MinimapTransform.RayToGround`); a ray that points up or lands more than 200 m away is cut at
  200 m along the ray. Each corner is clamped onto the map before it becomes a pixel, so the white
  outline never leaves the map area. Seen from the camera's side it is a trapezoid, wider at the
  top (far) edge.
- **Input:** the control's mouse filter is Stop, so a click inside it never reaches
  `SelectionController`; events outside its rect are never seen by it. Left press (`select`)
  jumps the camera focus to the clicked map point (`RtsCamera.SetFocus`, clamped as usual) and
  keeps following the cursor while the button is held; a drag past the map area just stops
  moving. Right press (`command`) while A-targeting only cancels targeting and orders nothing,
  as on the 3D view (M2-3, BUG-0068). Otherwise it calls `SelectionController.Order(Move, point,
  queued)` (queued while `order_queue` is held; M2-3), the same path a
  3D right-click takes after its ground pick: prune, nothing if the selection is empty or the
  point non-finite, the whole order dropped with a warning if it would overflow the command
  queue, else one `Command.Move` per selected unit. Pixels in the letterbox do nothing. The wheel
  does not zoom while the mouse is over the minimap.
- **Edge panning** is suppressed while the mouse is over the minimap (`RtsCamera.EdgePanBlocker`),
  since the minimap touches the screen's left and bottom edges.
- **Not shown yet:** fog of war (M4), alerts and pings (Alt + click), attack
  orders, minimap zoom. Buildings will be added as dots or footprints when they exist.
- **Launch flag added:** `--no-hud` hides the HUD (clean screenshots, perf comparisons).
- **Tests:** `ViewApi/MinimapRasterTests` (every cell against the mesh tint on the hand map and
  3 generated maps, dot pixels at corners, centres and the map's last float, dead units, unknown
  owners, the 2 x 2 centre and its rim, the nearest-corner rule, at least 3 screen pixels of centre
  per axis at 220 px / 128 cells, rims clipped at the map's edges and corners, neighbouring
  dots never hiding each other's cell in either slot order, rim contrast for every shipped
  faction colour), `MinimapTransformTests` (round trips, 160x48 / 48x160 letterbox, outside and
  non-finite pixels, rays), a `ViewApiAllocationTests` row (2,000 dots, 0 bytes), and the
  headless scene `res://tests/MinimapTest.tscn` ("MINIMAP TEST PASS"): dot colours at 2,000
  units, refresh cost, left-click, drag, right-click orders and outside clicks pushed through
  the viewport, the outline at both zoom limits and map corners. For looking, not pass/fail:
  `& $env:GODOT --path game res://tests/MinimapDotsShot.tscn -- --units 100 --out <png>` (windowed)
  adds one lone Whirlwind unit on a ramp cell and one lone Malazan unit beside a cliff lip, then
  saves the frame and a 4x nearest crop of the minimap (`<png>` with `-minimap` added). Use
  `--units 999` for the crowded shot, since the two extra units need free slots in the 2,000-slot
  store.

### Implementation (M2-5)

The debug overlay. New nodes in `Match.tscn`:

```
Match
  World3D
    ...SelectionRings
    NavOverlay         MeshInstance3D (NavOverlayView.cs), hidden until the overlay is on
    FlowArrows         MultiMeshInstance3D (FlowArrowsView.cs) + GoalMarker child made at first use
  ...
  DebugOverlay         CanvasLayer (DebugOverlay.cs): Label (always on) + TickGraph Control (TickGraph.cs)
```

- **Toggle:** the input action `debug_overlay` (F12, physical key, rebindable in `game/project.godot`;
  F12 is unassigned in docs/02) flips it in `DebugOverlay._UnhandledInput` (echo presses ignored);
  `--debug-overlay` starts it on. Off is the default. While off, `NavOverlay`, `FlowArrows` and
  `TickGraph` are hidden and nothing calls them: the two 3D layers have no `_Process` at all and
  build nothing until the first frame the overlay is on. `DebugOverlay._Process` (already running
  for the M2-1 label) calls `SyncLayers()` only while on. The overlay reads the sim and never
  enqueues.
- **Nav grid:** the pure `ViewApi.NavOverlayBuilder(Heightmap)` builds one quad per cell, inset
  8 cm from the cell edges (so the terrain shows as thin grid lines) and lifted 5 cm above the drawn
  surface (`TerrainHeight.CellCorners`, so ramp quads tilt with the ramp). `Refresh(NavGrid)` refills
  the vertex colours only when `NavGrid.Version` differs from the last fill, so the mesh is
  uploaded once when the overlay first turns on and once per passability change, never per frame.
  Colours (sRGB, alpha-blended, unshaded): any cell with `Blocked` set is blocked whatever else is
  set, deep crimson if also `Cliff` (0.55 / 0.03 / 0.35 at alpha 0.85, bluish so it stays red
  over every level tint; the first dark red blended to olive on green, BUG-0084), green if also
  `Resource` (a tree or mine, M2-3b), else red (border
  ring, sealed pockets; later buildings); an unblocked `Ramp` cell is orange; other open ground is
  faint white. Unknown flag bits never change the colour.
- **Flow arrows:** goal = `GoalCell` of the lowest-slot live selected unit with `GoalCell >= 0`
  (`ViewApi.FlowArrowLayout.GoalOf`). Every frame `FlowArrowLayout.Refresh` peeks
  `World.FlowFields.PeekCached(goal)` and relists the arrows only when the goal cell, the field's
  presence or `Version`, or the window changes. A field's contents depend only on the grid version
  and its target, so an unchanged key means unchanged arrows; an evicted or stale field peeks null
  and shows nothing. No `FlowField` is kept between frames. The window is the 40 x 40-cell square
  centred on the camera focus's cell, clipped to the map (at zoom 60 the screen shows more than the
  window). One arrow per window cell whose `DirectionAt` isn't `NoDirection`, pointing along the
  sim's own offset table (`FlowField.OffsetX/OffsetY`); a cyan disc marks the field's `TargetCell`
  (the cell it leads to) when it is in the window. Arrows are flat, 1.3 m long, 0.3 m above the
  highest ground under them (`FlowArrowLayout.ArrowGround`: an arrow stays inside its cell, so on a
  flat cell that is the cell's height, and on a ramp, a plane, the highest of the arrow's corners;
  they used to dip up to 9 cm into the steepest ramps, BUG-0084), written into the MultiMesh buffer in one
  call per relist. The arrow's shape constants live in `FlowArrowLayout` too. No
  selection, no goal, or no cached field: no arrows. A new order's field appears once the sim
  builds it (the build cap can delay a new goal while many groups are walking, "Build cap" above).
- **Tick graph:** `SimRunner.TickTimes` is a `ViewApi.TickTimeRing` of the last 120
  `LastTickMs` samples, one per `Tick()` (also when several run in one frame), filled even while the
  overlay is off (one array write per tick) so the graph has history when turned on. `TickGraph`
  draws one bar per sample, oldest left, red above the 4 ms budget, with the budget line; its scale is
  twice the budget or the worst sample. It builds no text per frame.
- **Label:** while on, a second line: `units <live>   moving <n>   fields <count>/<capacity>   tick avg
  <ms>   worst <ms> (<samples>)   arrows <n>`. Counts: `UnitStore.Count`, `ViewApi.DebugCounts.Moving`,
  `FlowFieldCache.Count`. Building the text allocates, so since M2-H2 (BUG-0083) `_Process`
  compares every shown value with the last ones and rebuilds the text only when one changed: each
  sim tick (the tick number), when Godot's FPS counter moves (once a second), and on selection,
  subgroup, targeting or count changes. A frame with nothing changed allocates 0 bytes, label
  included.
- **Cost:** `SyncLayers()` (nav check, goal, peek, relist, counts) at 2,000 units, zoom 60, Debug:
  about 0.09 ms average and 0.2 ms worst per frame with the camera moving a cell every frame (a
  relist of about 1,500 arrows each time), less when steady; 0 bytes per frame panning or steady.
- **Tests:** `ViewApi/NavOverlayBuilderTests` (colour rule incl. unknown bits, every cell's quad and
  colour on the hand map and 3 generated maps, edge rows, inset geometry on the drawn surface,
  winding, one refill per version, 0 bytes per refill), `ViewApi/FlowArrowLayoutTests` (the 8
  directions against the sim's table, every arrow pointing to a cheaper passable neighbour, arrows
  equal to `DirectionAt` in windows at the middle, an edge and all four corners, off-map and
  non-finite focus, no field or no goal, relist only on a change, stale and evicted fields never
  drawn, `GoalOf`), `ViewApi/TickTimeRingTests`, `ViewApi/DebugOverlayHashTwinTests` (the helpers
  three times a tick with the overlay toggling, the selection changing, 48-goal churn and a version
  bump: the hash equals a bare twin every tick), a `ViewApiAllocationTests` row, and the headless
  scene `res://tests/DebugOverlayTest.tscn` ("DEBUG OVERLAY TEST PASS"): `--debug-overlay`, off by
  default with nothing built, F12 through the viewport (release and echo ignored) and the action,
  one nav upload over many frames, arrows after a Move equal to `DirectionAt` (and, run windowed,
  the engine's instance transforms equal to the uploaded buffer; the headless renderer keeps none),
  the corner window, no arrows without a goal, a field evicted by a 200-goal churn stops being
  drawn, 80 churn ticks never stale, counts, cost and 0 bytes at 2,000 units, the 120-sample graph,
  and a bare twin sim fed the same commands hashing equal every tick. For looking:
  `& $env:GODOT --path game res://tests/DebugOverlayShot.tscn -- --out <absolute png> [--units n] [--zoom m]`
  orders player 0's army north-east on seed 1 and saves one frame with the overlay on.

### Implementation (M2-3b)

Trees and gold mines on the match map, drawn as props and marked on the minimap. Completes M2
criterion 3 except rocks: rocks are decoration with no sim footprint and wait for the M6 art pass
(Producer decision). New node in `Match.tscn`:

```
Match
  World3D
    ...TerrainView
    PropsView          Node3D (PropsView.cs): one MultiMeshInstance3D child per resource type, named by type id
    UnitViews ...
```

- **Map:** `SimRunner.Start` builds `MapGenParams.Default with { Forests, GoldMines }`, defaults 12
  and 8 (`LaunchOptions.DefaultForests` / `DefaultMines`, Producer defaults for the 128 map). Launch
  flags `--forests <n>` and `--mines <n>` (0 to `MapGenParams.MaxResourceGroups`, 64) override;
  out-of-range or non-integer values are warned about and ignored like `--units`; `--forests 0
  --mines 0` gives the M2 bare map. The start-up line ends with `forests F trees T mines M` from
  `World.ResourcePlacement` (what was placed; it can fall short of the request). On seed 1 the
  default map has 12 forests, 287 trees and 8 mines.
- **Layout:** the pure `ViewApi.PropLayout(GameData.Resources, capacity)` keeps one transform list
  per resource type. `Refresh(Heightmap, NavGrid, alive, typeId, cell)` takes the resource store's
  spans and relists only when `NavGrid.Version` differs from the last fill: every spawn and every
  node taken to 0 bumps it once, and a partly taken node looks the same, so nothing else needs
  watching. A dead node is **gone** (each list is compacted, in slot order), not collapsed to scale
  0, so the drawn instance count is the live count. Each instance sits at its footprint's centre
  (`(x0 + w/2, y0 + h/2)` cells; for a 2 x 2 mine that is a cell corner) at
  `TerrainHeight.At` there (a footprint is one flat level, M3-1). Rotation is a yaw from a
  multiplicative hash of the anchor cell (`cell * 2654435761`, top bits): a one-cell footprint takes
  one of 256 angles; a larger one turns only in quarter turns (square) or half turns (oblong) with
  exact 0 / +-1 basis entries, so a mesh that fills its footprint never pokes out. Trig is
  `SimMath`. Transforms are Godot's MultiMesh layout (12 floats, the 3 x 4 matrix row by row).
  Cost: a relist of 4,096 nodes is about 0.3 ms (Debug); a steady frame is one int compare, 0 bytes.
- **Meshes** (placeholders built in code, hard-coded tints like the terrain's, M2-1 rule), chosen by
  the type's `ResourceKind` so a new type is data only, sized from `FootprintWidth / Height`, never
  literal cell counts: wood = a 7-sided cone (radius 0.4 x the footprint's smaller side, 0.8 m in a
  2 m cell) from 1 m to 3.5 m on a 6-sided brown trunk (radius 0.15 m, 1 m tall); gold = a dark
  slate box covering the footprint, 1.6 m tall, with a gold box half the footprint's sides and
  0.5 m tall on top. `PropsView` copies a relisted type into that type's own buffer and sets
  `MultiMesh.Buffer` (instance count = the store's capacity, `VisibleInstanceCount` = the live
  count), only when `PropLayout.Refresh` returns true and only for the types whose list changed
  (`PropLayout.Changed(type)`: same slots at the same anchors in the same order means the same
  transforms), so a felled tree re-uploads the trees, not the mines (M2-H2, BUG-0086). Props cast
  shadows; no physics.
- **Minimap:** `MinimapRaster` has a third layer, `Resources`, drawn between terrain and dots.
  `DrawResources(types, version, alive, typeId, cell)` redraws it only when the grid version changed:
  it clears the pixels it painted last time, then paints every footprint cell of each live node
  (`WoodRgb` 0x1E5A1E dark green, `GoldRgb` 0xE6B422 gold, by `ResourceKind`). The minimap calls it
  in its 5 Hz refresh before the dots and uploads the layer's texture only when it was redrawn, so
  a felled tree disappears within one refresh. The terrain bake no longer darkens a cell blocked
  only by a resource node (`Resource` set), so the ground under a felled tree has its true colour.
  Cost: 2,000 dots plus a forced 4,096-node resource redraw is about 0.25 ms (Debug; limit 0.3 ms);
  0 bytes.
- **Read-only:** `PropLayout` and `DrawResources` read spans and keep no reference to the store;
  nothing in `ViewApi` names `ResourceStore`, `Take`, `Spawn` or the flow-field cache (except the
  M2-5 arrow layer's peek).
- **Tests:** `ViewApi/PropLayoutTests` (hand map with a plateau and a ramp, generated maps seeds 1-5
  at 12 / 8 against an oracle from the store and the heightmap, a mine's centre on a cell corner,
  a full take relists once and drops the instance, a partial take relists nothing, 600 ticks of a
  walking army relist nothing, yaw rules),
  `ViewApi/MinimapRasterResourceTests` (colours, every footprint cell, terrain equal to the bare
  map's, dots never touching the layer, a depleted node's cells transparent on the next redraw),
  `ViewApi/PropsHashTwinTests` (props, minimap and unit-view reads three
  times a tick with 2,000 units marching between mines and three trees felled: equal to a bare twin
  every tick for 600 ticks; a ViewApi source scan), `NavOverlayBuilderTests` (green resource
  cells), and the headless scene `res://tests/PropsViewTest.tscn` ("PROPS VIEW TEST PASS": the
  `--forests` / `--mines` rows, then the real Match at 12 / 8, 3 / 2 and 0 / 0: drawn count per
  type equal to the store's and to `ResourcePlacement`, mesh bounds inside footprints, the minimap's
  mine and tree pixels, one upload, steady frames 0 bytes); `ViewApi/PropsMeasureTests` holds the
  cost and allocation rows at a full 4,096-node store. `DebugOverlayTest`'s bare twin now copies the
  match's whole `SimConfig`, map parameters included. For looking:
  `& $env:GODOT --path game res://tests/PropsShot.tscn -- --out <absolute png>` sends player 0's
  100 units (seed 1) through the forest nearest them and saves one frame as they walk round it.

### Audio (M2-6)

Placeholder sounds for selecting and ordering, so the audio plumbing exists from M2 (docs/04
"Placeholder art"). Nothing is downloaded or imported: every clip is synthesized in code at start-up.
New node in `Match.tscn`:

```
Match
  ...Screenshotter
  Sfx                Node (Sfx.cs): 8 AudioStreamPlayer children (Player0-7), the clip table, counters
```

- **Reads nothing from the sim.** `Sfx` has no `Rts.Sim` reference and no `_Process`; callers
  decide that an event happened and call `Sfx.Play(SfxEvent)`. Today the only caller is
  `SelectionController`. Tone synthesis stays in `game/scripts/` (it uses `Math.Sin`, which the
  architecture scan forbids in `Rts.Sim`).
- **Events and clips:** `SfxEvent` (`Select`, `Command`) indexes a table of notes played back to
  back; `_Ready` turns each row into an `AudioStreamWav` (mono, 16-bit PCM, 44.1 kHz) once.
  `Select` is one 1,320 Hz note of 70 ms; `Command` is 660 Hz then 990 Hz, 60 ms each (120 ms).
  Each note is a sine at amplitude 0.8 shaped by a cubic attack (6 ms), an exponential decay (30 ms)
  and a cubic release (10 ms), so a note starts and ends at silence (no clicks; the first and last
  2 ms stay under 0.05) and peaks at about 0.65. Adding an event (an alert ping, M3/M4) is one enum
  member and one table row; `_Ready` throws if the two disagree.
- **When they play** (`SelectionController`): `Select` after a click, box, double-click / Ctrl +
  click or group recall that leaves a non-empty selection different (as a set) from the one before
  the action; so not on an empty-ground click, re-clicking the only selected unit, a recall of the
  group already selected (the double-tap that centres the camera), Tab, Ctrl / Shift + digit.
  `Command` from `Order(...)` once at least one command was enqueued: right-click, Shift +
  right-click, S, H, A + click, minimap right-click; not with nothing selected, not for an order
  dropped whole on a full command queue, not for A itself.
- **Rate limits:** each event plays at most once per process frame and never within 50 ms
  (`Sfx.MinGapMs`, wall clock) of its last play, so 50 right-clicks in one frame are one sound.
  A play takes the next player of the 8-player pool round-robin (a 9th overlapping sound cuts the
  oldest) and allocates nothing.
- **Volume and mute:** every player runs at `Sfx.SfxVolumeDb` (-6 dB), a constant standing in for
  the SFX volume setting (docs/02 "Settings", M6). `--mute` (takes no value) mutes the master bus;
  without it `Match.Start` unmutes it. Counters run either way.
- **Headless:** the dummy audio driver takes playback without logging errors, so headless runs
  play too (the smoke gate stays clean).
- **Quitting with a sound playing** (BUG-0087): Godot's audio server drops a stopped or finished
  playback only on its next mix, on the audio thread, so quitting within about 120 ms of a click or
  order (any scene or the game, not only tests) left the clip and its playback in the server: an
  ObjectDB leak warning at exit. The pool players stop their sounds as they leave the tree, and if a
  sound started within the longest clip plus 100 ms, `Sfx._ExitTree` then waits for one mix that
  started after the stop (`AudioServer.GetTimeSinceLastMix`, then the server lock the driver holds
  while mixing), at most `Sfx.ExitWaitLimitMs` (200 ms); about 5-60 ms headless. The window's close
  request stops the players too. `SfxTest` no longer waits before quitting.
- **Counters for tests:** `Sfx.PlayCount(SfxEvent)` (plays after rate limiting) and
  `Sfx.LastPlayedFrame(SfxEvent)` (process frame, -1 if never). `Play(e, frame, nowUsec)` drives
  the limiter with an explicit clock.
- **Tests:** `res://tests/SfxTest.tscn` ("SFX TEST PASS"): each clip's length (70 / 120 ms +- 5),
  peak <= 0.9, quiet 2 ms edges, no NaN, 16-bit mono 44.1 kHz, the two clips different; the rate
  limit with a hand-driven clock (20 ms apart 1 play, 60 ms apart 2, one per frame, events
  separate, 0 bytes for 20 plays); `--mute` parsing; then the real Match at 100 units with injected
  input for every selection and order row above, and a second Match with `--mute` (bus muted,
  counter still runs); a pool leaving the tree right after a play waits for one mix (under the
  limit), one that never played doesn't wait; it then quits with a sound playing.

### Implementation (M2-7)

The studio's half of the M2 "Playable" criterion (100 units per player at 60 FPS): a scripted,
repeatable benchmark, facing interpolation, and a screenshot set. The owner's playtest confirms it.

- **Facing blend.** `UnitViews.Sync` turns each unit to `UnitViews.BlendFacing(PrevFacing, Facing,
  alpha)`, the same clamped alpha as the position lerp (NaN shows the current tick). The difference
  is wrapped into [-π, π) before the lerp, so 0.9π to -0.9π turns 0.2π through ±π; an exact half
  turn wraps to -π, so it always turns the same way. `PrevFacing` is the sim's derived, unhashed
  start-of-tick copy (M3-2). Pure arithmetic: the 2,000-unit update still allocates 0 bytes.
- **`--bench <seconds>`** (positive, at most `LaunchOptions.MaxBenchSeconds` = 3,600; `0`, negatives,
  non-numbers and longer runs warn and are ignored: BUG-0103, `1e308` used to run forever).
  `Match.Start` adds a `BenchRunner` node. It waits until tick 2 (the armies exist) and any
  `--screenshot` is saved (the screenshotter then doesn't quit), skips 30 warm-up frames (the
  first draws of new unit nodes compile pipelines: a one-time ~85 ms frame on the dev PC, load time
  rather than play), then plays the pure `Rts.Sim.ViewApi.BenchScript` on frame time until the
  duration ends. One 10 s loop (M2-H2 order): camera to the local army at the start zoom (0.25 s),
  box-select the whole screen (0.25 s), A + click (0.25 s), Move across the map (1 s), four minimap
  clicks on the map corners (0.25 s each, all inside the first 3 s), camera back to the army
  (0.25 s), three Shift-queued moves (0.5 s each), zoom 20 m (1.5 s) and 60 m (3.5 s), H (0.25 s),
  S (0.25 s). "Across" (BUG-0101; it used to target the enemy start block about 15 m away) is the
  pure `ViewApi.BenchTarget.TryAcross`: the passable cell nearest (0.85 x width, 0.5 x height) for an
  army whose centre is west of the map's centre line, the mirror point (0.15 x width) for one east
  of it, ties to the lower cell index; with no passable cell within 8 cells of that point, the
  passable cell nearest the map corner opposite the army. A + click comes before the Move and the
  three moves are queued behind it, so the army marches from 0.75 s until H at 9.5 s; on seed 1 the
  target is about 109 m away and the army's centre moves about 22 m in 10 s (`QaM27Test`: target at
  least 100 m, centre at least 20 m). The march runs through the idle enemy block, which roughly
  halves the army's pace after a few seconds (a bare 10 s sim march moves the centre 24 m, 29 m with
  no enemy in the way). Every action goes
  through the real code: `SelectionController.BoxSelect` / `OrderAt` / `BeginAttackMove` +
  `AttackMoveClick` / `Order` (public since M2-7; the mouse and key handlers call the same
  methods), `Minimap.JumpTo` (the camera directly under `--no-hud`), `RtsCamera.SetZoom`. Edge
  panning is off during a bench.
- **Measurement.** Each timed frame's `_Process` delta goes into `Rts.Sim.ViewApi.FrameTimeStats`
  (a 0.01 ms histogram up to 250 ms, so no per-frame allocation; percentiles are bin edges);
  `fps` is the timed frames over the timed seconds (= 1000 / avg; BUG-0102: it was a mean of
  Godot's once-a-second counter, whose first sample is the load second, 7-10 % low on a 10 s run);
  ticks and their mean cost come from `SimRunner.TickTimes.Total` and the new
  `SimRunner.TotalTickMs`. At the end it prints `Bench worst frame <ms> at <s>, after step <step>`
  and then the one result line, and quits with 0 (all three lines in the invariant culture, so a
  comma-decimal PC prints `10.56`, not `10,56`):
  `bench: seconds S frames N avg A ms p50 B ms p99 C ms worst D ms fps F ticks T avgTick U ms`.
  Nothing else starts with `bench:`.
- **`--vsync on|off`** sets `DisplayServer.WindowSetVsyncMode` (skipped headless). Without it the
  project default (vsync on) holds. With vsync on, Godot's `application/run/delta_smoothing` (on
  by default, not overridden here) snaps `_Process` deltas to the refresh period, so the vsync-on
  rows measure pacing, not cost: they read as perfect frames by construction (BUG-0103). The
  vsync-off rows are raw deltas; use them for cost.
- **Figures** (dev PC: i7-13700F, RTX 4070, Debug build, window 1920 x 1061 because the taskbar
  clamps a 1920 x 1080 window (`--resolution 1920x1080`), default 128 map with 12 forests / 8 mines,
  HUD and sound on, 60 s; rerun 2026-10-07 for M2-H2 with the march across the map and `fps` as
  frames / seconds):

  | Run | avg | p50 | p99 | worst | fps | avg tick |
  | --- | --- | --- | --- | --- | --- | --- |
  | 100 / player, vsync off | 0.73 ms | 0.71 ms | 1.39 ms | 9.1 ms | 1,377 | 0.21 ms |
  | 100 / player, vsync on, 120 Hz display | 8.34 ms | 8.34 ms | 8.34 ms | 14.6 ms | 120.0 | 0.22 ms |
  | 1,000 / player, zoom 60, vsync off | 2.62 ms | 2.78 ms | 3.34 ms | 7.9 ms | 382 | 3.32 ms |

  The frame budget at 60 FPS is 16.7 ms; the 100-unit case uses under 5% of it. The vsync-on row
  shows pacing, not cost (delta smoothing, above); its worst frame is the first timed frame. The
  1,000-unit row now has the west army marching through the east one; another run while other
  work shared the PC measured avg 3.57 ms / p99 5.27 ms / tick 4.45 ms. Before M2-H2 (the army
  stopping in the enemy block 15 m away): 0.72 / 1.31 ms at 100 units, 2.26 / 3.34 ms and tick
  3.03 ms at 1,000; a 60 Hz display showed 16.67 ms frames (fps 59.5 by the old counter). At the
  default window size (1152 x 648) the 100-unit vsync-off run averages 0.52 ms.
- **Screenshot set** (windowed, kept out of the repo): the overview (`--zoom 60 --screenshot <png>
  --screenshot-after 3`), the overlay (`--debug-overlay --screenshot <png> --screenshot-after 3`), and
  `res://tests/MarchShot.tscn -- --out-dir <dir>`, which box-selects the army, orders it east
  at 4x, and once 10 own units stand on ramp cells switches to 1x, puts the camera on them at zoom 30
  and saves `ramp-crossing.png` plus `minimap-corner.png` (the bottom-left 240 px of the same frame).
- **Tests:** `Rts.Sim.Tests/ViewApi/BenchScriptTests` (step order, loop, end of run, one step per
  call, bad durations, 0 bytes), `BenchTargetTests` (seeds 1-20 from both start blocks: a passable
  cell on the other side at least 100 m away, equal to a brute-force nearest search; the corner
  fallback; no passable cell; 0 bytes) and `FrameTimeStatsTests` (mean, percentiles, spikes past the
  histogram, 0 bytes); `res://tests/BenchTest.tscn` ("BENCH TEST PASS"): `--bench` / `--vsync`
  parsing (3,600 accepted, 3,600.5 and 1e308 not); the info lines under de-DE; `fps` within 2 % of
  frames / seconds; the game binary started headless as a child process with `--bench 2 --mute` (exit 0,
  exactly one line in the documented shape, under 7 s, no ERROR) and with `--bench 0 | -3 | abc`
  (a WARNING, no line, no ERROR); an in-process `--bench 3` (selection 0 -> the whole army,
  commands enqueued, four minimap jumps to four different camera points); and, windowed only, a
  10 s bench at 100 units per player with vsync off that must average under 16.7 ms with p99 under
  33 ms (headless prints "BENCH TEST SKIP" for that row). `UnitViewsTest` adds the facing-blend rows.

### Implementation (M3-V1)

The first view half of M3: the owner sees the economy run and drives it. The default match gets a Town Hall and workers
per player, the HUD a resource bar, a right-click on a tree or mine sends selected workers gathering, and workers and
buildings show what they are doing.

```
Match.tscn  (new nodes)
  World3D/BuildingViews   Node3D, BuildingViews.cs: one box + bar per building slot
  Hud/ResourceBar         Label, ResourceBar.cs: top right, "<gold name> N  <wood name> N"
```

- **Start bases** (`Match.SpawnBases`, the pure `Rts.Sim.ViewApi.StartBase`): after the armies, per player a finished
  Town Hall (its faction's `town_hall` slot, `World.FactionOf`) through the dev `Command.SpawnBuilding`, then
  `--workers <n>` (0-200, default `rules.json` `startingWorkers` = 5) units of its `worker` slot. The hall's spot is
  the anchor whose footprint fits (`BuildingStore.Fits`) on the start block's level, whose 8-ring has no blocked cell
  (tree, mine, cliff, border, building: an all-passable ring is a loop round the footprint, so the never-seal rule
  always passes), with no army unit or earlier base in footprint or ring, and which stays on its own side of the
  6-cell centre gap between the two blocks (west for player 0, east for player 1); the one whose centre is nearest
  the block's mean wins, ties toward the map centre, then the lower anchor (the CLI `run --workers` order). With no
  army (`--units 0`) a one-unit block stands in, and the camera starts on player 0's hall. Workers stand one per
  passable free cell on the rings round the hall, ring 1 first, within a ring nearest the gold mine closest to the
  hall (else the block), so the first trip is short (seed 1: all five Gathering by tick 23). No spot: one warning per
  player ("no open spot for a Town Hall...") and no hall or workers for that player; the match runs on (seeds
  1-50 at the default 12 / 8 resources, and 1-40 at 64 / 64 with 100 or 1,000 units: no player without a spot). The
  bench's 100 / 1,000 units per player are unchanged; the unit store grows past 2,000 to fit the workers
  (`players x (units + workers)`). The start-up line ends with `town halls H, W workers per player`.
  **`--no-bases`** (dev / tests) skips halls and workers: the armies-only match the M2 test scenes were written for
  (their hash twins replay their own spawns, and they count one nav / minimap / props upload because nothing
  changes the grid after the start, which a hall placed on tick 1 now does).
- **Building views** (`BuildingViews`): polled each frame from `Buildings.Alive` / `Generation` like the props (no
  sim events yet). A slot's nodes (box, bar back, bar fill) are made the first time it holds a building and hidden
  while it is free. The box is the footprint (cells x 2 m) by 3 m, in the owner's faction colour; a construction
  site is slate (lighter than the Malazan grey) and rises from 15% to full height with its progress. The bar over it
  comes from the pure `ViewApi.BuildingBars.Of(buildings, defs, slot, out fill)`: `Progress` (`Work / WorkNeeded`,
  yellow) on a site, `HitPoints` (`Hp / max`, green) on a finished building below full hit points, `None` otherwise;
  its transform is written only when kind or fill change. A freed slot (a Cancel) is hidden on the first frame
  after. Buildings are not selectable yet (M3-V3).
- **Resource bar** (`ResourceBar`, `Hud` top right, docs/02 "HUD layout"): player 0's `World.Gold` / `Wood` with the
  names from its faction's `faction.json` `resources.gold / wood.displayName` (`FactionDef.GoldName` / `WoodName`, so
  no resource name is a C# literal); the text is rebuilt only when a number changes (the M2-H2 overlay rule).
  Population joins it in M3-V3. Hidden with the HUD (`--no-hud`).
- **Right-click Gather** (`SelectionController.ContextOrder`, called by `CommandAt` from a 3D right click): when the
  picked ground point lies on a live resource node's cell (`ViewApi.ResourcePicker`) and the selection holds at least
  one `worker`-slot unit, each selected worker gets `Command.Gather(player, unit, point)` and every other unit a
  `Move` there, Shift queueing both; anywhere else it is the plain `Order(Move)`. One Command sound; the whole order or
  nothing when the command queue is too full (as `Order`). The sim resolves the node at apply (an unexposed tree goes
  to the nearest exposed one). The minimap's right click stays a Move. `ResourcePicker.NodeAt(grid, defs, alive,
  typeId, anchorCell, cell)` (and `NodeAtPoint` for meters) returns the node's slot or -1: a cell without
  `NavFlags.Resource` costs one flag read; otherwise the live nodes are scanned for the footprint that covers it (the
  store has no cell index).
- **Worker feedback** (`UnitViews`): a standing worker's `Gathering` (green), `Returning` (amber) or `Building` (blue)
  state tints its body through a shared translucent overlay material (`MaterialOverlay`); while `Cargo > 0` a 0.35 m
  cube floats above it, gold or wood-brown by `CargoKind`. A slot's marker node is made the first time it carries,
  then reused; overlay and marker are touched only when what they show changes. Walking legs (`Moving`) are not
  tinted; the marker shows the load either way. The F12 overlay's second line adds "workers gathering N,
  returning N, building N" (`ViewApi.DebugCounts.InState`).
- **ViewApi rule.** The new reads take the stores' read-only parts (grid, defs, spans, `BuildingStore`), never the
  `World` (the QA source scan forbids `World`, `Simulation` and `UnitStore` in `ViewApi/`); `StartBase.Plan` takes
  each player's faction from the caller (`World.FactionOf`). No `CanPlace` call (M3-V2's ghost will, on the sim thread
  between ticks).
- **Cost.** 300 idle frames of the resource bar, building views and unit views (cargo markers and tints in use)
  allocate 0 bytes. `--bench 10 --vsync off` at 100 units per player with the halls and workers (dev PC, default
  1152 x 648 window, Debug): avg 0.52 ms, p99 0.92 ms, worst 1.39 ms, avg tick 0.21 ms (the M2-7 figure at this size
  was 0.52 ms).
- **Tests.** xUnit (`Rts.Sim.Tests/ViewApi`): `ResourcePickerTests` (every cell of every node on 50 match maps resolves
  to it and every other cell to -1, against a brute-force oracle; off-map and non-finite input; a felled node stops
  resolving; 0 bytes for the picker, `BuildingBars` and `DebugCounts`), `BuildingBarsTests` (no bar at full hp, the hp
  share when damaged, a site's progress growing with `Work`, none when done; dead and out-of-range slots),
  `StartBaseTests` (seeds 1 / 6 / 31: both halls stand finished at the planned anchor on the block's level, five
  workers each beside it facing the nearest mine; 50 seeds: every planned hall is accepted, ring passable, on its side
  of the gap; no room on a 7 x 7 map; a tree on the ring or a taken cell rules a spot out; worker ring order) and
  `EconomyViewHashTwinTests` (400 ticks of gathering, a build and its cancel, calling every new read each tick: the
  hash equals a bare twin's every tick). Headless scene `res://tests/EconomyViewTest.tscn` ("ECONOMY VIEW TEST
  PASS"): `--workers` parsing; the no-spot warning path (`SpawnBases` with every cell taken: two warnings, nothing
  enqueued); seeds 1 / 6 / 31 boot with both halls (faction, anchor, finished, box size and colour, no bar), five
  workers each and the bar reading "Gold 200  Wood 200" from data; `--workers 0` and `--units 0 --workers 7`; a
  real right-click on seed 1's mine with the five workers selected (all Gathering by tick 23 of 60, gold up at tick
  310 of 1,200), then on a tree (wood up within 1,200), each frame checking the bar against `World.Gold` / `Wood` and
  every unit's tint and cargo marker against the sim; the F12 counts; 5 workers + 5 soldiers: plain ground is 10
  Moves, Shift + right-click on the mine queues a Gather per worker and a Move per soldier, unqueued the workers gather
  and the soldiers walk, soldiers alone get Moves, the minimap path stays a Move; 300 idle frames at 0 bytes; a
  worker's Build: the site box is slate with a progress bar equal to `Work / WorkNeeded` every tick, and gone the
  frame after its Cancel frees it. Windowed with `-- --shots <dir>` it saves the start of each seed, workers
  hauling, and the site. `QaH1Test`'s fuzz now expects the Gather / Move split when its "empty ground" right click
  lands on a tree or mine with workers selected; the M2 scenes that assume an armies-only match pass `--no-bases`.
- **Not yet:** command card, build menu, ghost (M3-V2); production UI, rally marker, population in the bar (M3-V3);
  building selection; a damaged building in a scene test (no public damage path until combat; `BuildingBarsTests`
  covers the hit-point bar's numbers).

### Implementation (M3-V2)

The owner builds a base in the window: a command card with the docs' grid hotkeys, the worker build menus, a placement
ghost the sim's own `CanPlace` colours, building selection with a site's Cancel, and right-click Repair / join.

```
Match.tscn  (new nodes)
  World3D/BuildingOutline Node3D, BuildingOutline.cs: the selected building's flat footprint outline
  World3D/BuildGhost      Node3D, BuildGhost.cs: translucent footprint box + reason label (Label3D)
  Hud/CommandCard         Control, CommandCard.cs: bottom right, 5 x 3 buttons
game/data/common/ui.json  view-only text and menu lists (UiText.cs); the sim's DataLoader never reads it
```

- **`ui.json`** (Producer default; view data, so CLAUDE.md rule 8 holds for the card): `{ "commands": { "<id>":
  { "displayName", "hotkeyHint" } }, "buildMenus": { "basic": [slot ids], "advanced": [slot ids] }, "placement":
  { "<reason>": text } }`. Command ids: `move`, `attack_move`, `stop`, `hold`, `build_basic`, `build_advanced`,
  `cancel`. Placement keys are the snake_case names of every `PlacementError` but `None` (`blocked`, `seals_ground`,
  `unit_in_the_way`, `cannot_afford` ...), so a reason the sim adds (M3-6's `Requires`) is a load error until the file
  has its text. Menus list slot ids (`DataLimits.BuildingSlotIds` spelling): basic = the six Age I slots (House, Camp,
  Infantry Hall, Ranged Hall, Shock Hall, Forge), advanced = Caster Hall, Siege Works, Watch Tower (docs/02
  "Buildings"); at most 15 each. `UiText.Shared` loads it once (`File.ReadAllText` of `res://data/common/ui.json`,
  globalized like the sim data); every missing or empty key, an unknown slot id or bad JSON is one error naming it
  ("ui.json: missing commands.stop.displayName"), logged with `GD.PushError` (so the smoke gate fails), and the match
  runs with the card hidden.
- **Card layout** (`CommandCard`, docs/02 "HUD layout" / "Grid hotkeys"): 5 x 3 buttons (92 x 60 px, 4 px gaps),
  cells 0-14 keyed by input actions `card_0` ... `card_14` = `Q W E R T / A S D F G / Z X C V B`. A button shows its
  name (command `displayName` or building `displayName`), its hotkey hint top left (`hotkeyHint`, or for a menu entry
  the key bound to its `card_<i>` action as the keyboard labels it) and, for a building, its cost "G / W" bottom right;
  the tooltip is the building's `description` and "<gold name> G  <wood name> W" (faction names). Contents follow
  the selection: units: Attack (A) on the A cell, Stop (S), Hold (H) and Move (M) on the rest of the middle row (row 1
  stays free for abilities, Q W E R); when the active Tab subgroup is a `worker` type also Build advanced on V and
  Build basic on B; B / V (actions `build_basic`, `build_advanced`) open a menu whose entries take cells 0, 1, 2 ...
  in list order (Q W E R T A for the basic menu) and own every grid key while it is open (A picks the sixth entry, S
  does nothing); a selected own site: Cancel on the B cell (its grid key); a finished building: nothing (M3-V3); no
  selection: nothing. Every entry is enabled (`requires` greying: M3-6 / M3-V3). A button press makes exactly the call
  its key makes (A / M arm targeting, S / H order, B / V open, Cancel cancels, an entry raises the ghost). Esc or a
  right click closes a menu and its ghost and orders nothing. The texts are written only when the contents change
  (`Layouts`); every string is built in `Init`. The root control ignores the mouse, so only visible buttons take clicks
  (an empty cell lets a click through to the map); buttons take no keyboard focus.
- **M (Move).** New input action `order_move` (M): arms Move targeting like A arms attack-move
  (`SelectionController.TargetKind`); the next left click orders a plain `Move` there (today what a right click on
  ground does; it will ignore enemies once combat exists, M4). `Targeting` is true for either; the F12 label shows "M"
  or "A".
- **Ghost rule** (`BuildGhost`): the anchor is `ViewApi.PlacementGhost.Anchor(grid, def, point)`: the cell under the
  cursor minus half the footprint (integer halves: exact centring for odd footprints, within a cell for even ones),
  clamped so the footprint stays on the map; a cursor off the map hides the box. Green when `World.CanPlace(player,
  type, anchor, out reason)` passes, red otherwise with `ui.json` `placement.<reason>` over it. `CanPlace` writes
  flow-field scratch (M3-3), so it is called only from the ghost's `_Process`, which runs after `SimRunner`'s tick
  step in the same frame (tree order: the runner is the match's first child), on the main thread, never while
  `SimRunner.Ticking`, at most once per frame, and only when the anchor, the type or the sim tick changed since the last
  answer (nothing else changes it). The box moves only with an answer, so the colour drawn is always `CanPlace` of the
  anchor drawn. Left click with a green ghost: `SelectionController.OrderBuild`: one `Command.Build(player, worker,
  type, anchor point)` per selected live worker (the anchor cell's centre; the first to apply places the site, the rest
  join it), one Command sound; the menu and ghost close, unless Shift is held: then the ghost stays, and each later
  placement of the same ghost is queued (`queued` flag) so the workers build them in turn (the first stays unqueued:
  a queued order behind an endless Gather would never start). A red or off-map click does nothing and plays nothing.
  Meshes (one per building type, on first use) and the two materials are kept; the label text is set only when the
  reason changes.
- **Selection rule** (`SelectionController`): a left click that picks no unit selects the own building whose drawn box
  the camera ray meets first (`ViewApi.BuildingPicker.PickRay`: footprint x box height on the terrain at the footprint
  centre, a site only up to its drawn rise) alone: units cleared, targeting ended, Select sound if it changed. Shift or
  double-click make no difference; enemy buildings aren't selectable (own only, like units). A box never selects a
  building (a plain box drops it like the units); a click on empty ground, a unit, a box or a group recall that leaves
  units selected drops it. `SelectedBuilding` is the slot or -1, a view selection (slot + generation): a freed or reused
  slot reads -1 from the next read on. Tab does nothing with a building selected. `BuildingOutline` draws four thin
  flat bars just outside the footprint, moved only when the selection changes. The F12 label says "sel building".
  Cancel: `Command.Cancel(player, footprint centre)` (`CancelSelectedSite`), one Command sound.
- **Right-click Repair / join** (`ContextOrder`, with a worker selected): on a node's cell a Gather (M3-V1); else on an
  own finished building below full hit points (`ViewApi.BuildingPicker.SlotAt`, the store's `SlotAt` in meters) a
  `Repair(player, worker, point)`; on an own site a `Build` of the site's type at its anchor cell's centre (it joins);
  every other selected unit a `Move` there; Shift queues them all. Anything else (no worker, enemy building, own building
  at full hit points, ground) is the plain Move. One sound; nothing if the whole order doesn't fit the command queue.
- **ViewApi additions** (read-only, allocation-free, no `World`): `BuildingPicker` (`SlotAt`, `PickRay`, `BoxRise`),
  `PlacementGhost` (`Anchor`, `AnchorPoint`), `BuildMenu` (`Entries`: the faction's building per listed slot,
  `TryParseSlot`).
- **Cost.** 300 idle frames with the card in a menu, a ghost up and the outline on allocate 0 bytes (card, ghost,
  outline); the ghost following a moving cursor (one `CanPlace` a frame) allocates 0 bytes. `--bench 10 --vsync off`
  (dev PC, default window, Debug): avg 0.80 ms, p99 1.39 ms, avg tick 0.33 ms over two runs, with the unit card on show and another track's test run loading the CPU (the M3-V1 figure, measured on a quiet machine, was 0.52 ms).
- **Tests.** xUnit (`Rts.Sim.Tests/ViewApi`): `BuildingPickerTests` (every cell of three seeds' bases vs a footprint
  scan, off-map / NaN; a straight-down ray over every cell picks the building under it, own filter; oblique rays: the
  nearer box of two, a site only up to its drawn rise, bad rays; `BoxRise` follows `Work`; 0 bytes),
  `PlacementGhostTests` (every cell of the 128 map for six footprints, four points per cell, against the formula with
  the clamp; centring away from the edges; off-map / NaN / too large; `AnchorPoint` round trip; 0 bytes),
  `BuildMenuTests` (both factions' menus in slot order, Malazan by key; span bound, unknown faction, empty list;
  `TryParseSlot`; 0 bytes) and `CommandCardHashTwinTests` (400 ticks of joining Builds, a damaged house's Repair, a
  placed house and its Cancel, calling every new read and the ghost's `CanPlace` each tick: the hash equals a bare
  twin's every tick). Headless scene `res://tests/CommandCardTest.tscn` ("COMMAND CARD TEST PASS"): `ui.json` rows
  (every key present; a missing command, hint, placement text or bad slot id, bad JSON and a missing file each one named
  error); card contents for nothing / soldiers / workers / a mixed selection through Tab; S, H, A + click and M + click
  by key and by button enqueue the same commands, with and without Shift; B / V by key and button; through the
  viewport an empty card cell passes a click to the map and a visible button takes it; both menus' entries (type, name,
  grid key, tooltip description and cost, cost label), Esc and right-click close, A in a menu picks the Forge, S does
  nothing, B with soldiers opens nothing; 200 random cursor points (seed 1, a tick every 10) each checked against the
  test's own `CanPlace` (anchor, colour, reason, `ui.json` text) with at most one call a frame, and two `Sync`s in one
  frame making one call; a green click with 3 live workers + a dead one + a soldier: 3 Builds of the type at the anchor,
  one sound, the ghost closed, the site placed and all three building it; a red "Can't afford" ghost after it (red
  material, regression for a new ghost keeping the last one's green) and a click on the hall: nothing, no sound; Shift:
  the ghost stays and a second click enqueues 3 queued Builds; a site click selects it alone (Cancel on B from
  `ui.json`, outline, "sel building", Tab inert), a box over it doesn't, B enqueues one Cancel at its centre, the box
  and the selection go the frame after; the Cancel button and a slot reused in the same tick clear the selection; the
  hall selected with an empty card, the enemy hall not selectable; right-click: full-hp hall = Moves, damaged hall = 3
  Repairs + the soldier's Move (Shift queued), own site = 3 joining Builds, enemy hall = Moves; 300 idle frames 0 bytes
  and nothing rewritten. Windowed with `-- --shots <dir>` it saves the soldier and worker cards, the B menu, a green and
  a red ghost and a selected site with its Cancel.
- **Not yet:** production card, queue, rally, population (M3-V3); `requires` greying (M3-6); rebinding UI; real art.

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
  - Header: format version (`Replay.CurrentFormatVersion` = 3 since M3-1; 2 was M1-7, without the
    resource lines, 1 was M1-6, without command flags; both are refused with `FormatVersionMismatch`),
    `SimInfo.Version` (informational, not checked on playback), data hash (`GameData.ContentHash()`),
    seed, player count, unit, command and (M3-1) resource capacity (they decide outcomes: the unit and
    resource capacities are in the state hash and a full store drops spawns), checkpoint interval,
    tick count, and every `MapGenParams` field (M3-1 added `map.forests`, `map.forest-min-trees`,
    `map.forest-max-trees`, `map.gold-mines`, `map.mine-spacing`). There is no map id yet: the map,
    resource nodes included, is rebuilt from seed + params.
  - Command log: every command `Simulation.Enqueue` accepted, as stamped (tick, player, sequence,
    kind, type id, position, unit handle, flags), in enqueue order. Commands stamped for a tick after the
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
  `c tick player sequence kind typeId x y unitIndex unitGeneration flags` (10 fields; format 2 added
  `flags`), `checkpoints N` then N lines
  `k tick hash`, `end`, and last `checksum H`: FNV-1a 64 over every byte before that line, so any
  changed byte is caught. Integers are invariant-culture decimal in canonical form (no `+`, no
  leading zeros); hashes are 16 uppercase hex digits; floats are their exact IEEE-754 bit pattern
  as 8 uppercase hex digits (`0.5` is `3F000000`, NaN round-trips). `TryRead` never throws on bad
  bytes: it returns a `ReplayError` code and no replay. Read rules (`Replay.Validate`): command
  ticks run from 1 to the tick count without going backwards, each player's sequences count 0, 1,
  2, ..., players are below the player count, every command is well formed (`Command.IsWellFormed()`:
  a known kind, no flag bit but `Command.QueuedFlag` (M1-7), and that one only on a unit order
  (M1-9), the rule `Enqueue` applies), no tick has more commands than the
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
  It stores the flow-field cache's *contents* too, every used slot's direction bytes and costs with
  its keys, version stamps and LRU stamps (Producer decision 2026-10-07, BUG-0081; owner may revisit
  at M6): since M3-2b a usable-but-stale field was built on the grid as it was then, so rebuilding
  the fields from their keys at load would route units differently from the unsaved run (QA measured
  positions parting at once). So save then load reproduces the unsaved run exactly. Replays need
  nothing of this: they replay from tick 0, and the cache rebuilds itself on the way.
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
  1,500 ticks, checkpoints every 100 (about 16 KB). Last regenerated in M3-1 (format 3, the
  resource store and grid version in the hash, `resources.json` in the data hash; its commands and
  unit trajectories are unchanged). `ReplayGoldenTests` plays it back and fails on
  the first mismatching checkpoint. When a deliberate change alters outcomes (movement, tick order,
  RNG use, any `game/data` edit), run the tests once with the environment variable
  `RTS_REGEN_GOLDEN=1`: the test rewrites the file and then fails with "golden regenerated; rerun
  without the flag". Rerun without it, and explain why in the commit message.
- `DeterminismTests`: two sims with the same seed and commands keep equal hashes every 100 ticks
  over 2,000 ticks of movement; different seeds differ by tick 100. Since M3-1 also on a map with 12
  forests and 8 mines, with scripted `Take`s between ticks that fell trees mid-run.
- Tests whose bounds or preconditions were measured on one pre-M1-6 map pass their old seed
  through `TestSeeds.PreMix`, which inverts `SimRng.MixSeed` so they get exactly the old streams.
  New tests use plain seeds.
- Perf thresholds are generous (they catch 2× regressions, not 5% noise).
- The M1 perf criterion is `PerfCriterionTests.FiveHundredMovingUnits_AverageTickUnder4Ms` (M1-7):
  500 units of every type, two players, on the default 128 map, spawned within 12 path cells of the
  center and sent to the farthest passable cell. It asserts that at least 95% are Moving after every
  measured tick, times 200 ticks one by one after 5 warm-up ticks, prints the average, p99 and
  worst, and fails on an average of 4 ms or more. Measured (Debug, dev machine, run alone): about
  0.7-1.0 ms average, all 500 Moving and stepping on every tick.
- The 2,500-unit crowd rows (M1-9, `CrowdPerfTests`, BUG-0044 / BUG-0047): the one-player tight
  blob (seed 99, 2,500 units within 12 path cells of the central cell, all ordered to it; 5 warm-up
  ticks, a full GC, 300 timed ticks) must average at most 4.5 ms, the M1-4d-3 target. Two report rows
  guard the plug test's cost where two players overlap all the time: the same blob with owners
  alternating (both players contest the point) under 10.5 ms, and 2,500 units to 4 points, one player
  per point, seed 1, 600 ticks, under 3.7 ms: what each cost before the plug answers were cached.
  Wall-clock rows are sensitive to other load on the machine: re-run a failure once alone first.
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
- **Benchmark** (M2-7): `& $env:GODOT --path game -- --bench 60 [--vsync off] [--units n] [--zoom m] [--mute] [--no-hud]`
  plays a scripted 10 s loop of selections, orders, minimap jumps and zooms for that many seconds
  and prints one `bench: ...` line of frame-time figures, then quits 0. Details in "Implementation (M2-7)".
- **Debug overlay** (M2-5; F12, input action `debug_overlay`; launch flag `--debug-overlay` starts it
  on, so `--screenshot` can capture it): the nav grid on the ground, the flow-field arrows of the
  selection's goal around the camera, a tick-time graph of the last 120 ticks with the 4 ms budget
  line, and a second label line with live / Moving units, cached fields and the graph's average and
  worst. Off by default; while off its layers are hidden, have no `_Process` and aren't built.
  Costs (2,000 units, zoom 60, Debug): about 0.09 ms per frame with a relist every frame (camera
  panning). Allocation: the overlay layers 0 bytes per frame; the label text is rebuilt (and
  allocates) only when a shown number changes, at most once a tick plus once a second for the FPS,
  so a frame with nothing changed is 0 bytes on and off (BUG-0083). Details in "Implementation (M2-5)". Deferred: per-unit
  state labels (maybe with M2-7), arrows for more than one goal, a window that follows the visible
  trapezoid instead of a fixed square.
- **Dev console** (backtick): `spawn <unit> <n>`, `give <gold> <wood>`, `reveal`, `speed <x>`,
  `ai <player> <difficulty>`, `win`, `lose`, `hash`.
- **Headless CLI** (M1-8, `tools/Rts.Cli`, references `Rts.Sim` only):
  - `dotnet run --project tools/Rts.Cli -- run --seed <n> --units <n> [--ticks <n>] [--players 1|2] [--checkpoint <ticks>] [--forests <n>] [--mines <n>] [--record <path>] [--data <dir>]`
    builds the default generated map from the seed (since M3-1 with `--forests` forests and `--mines`
    gold mines, 0-64 each, default 0) and runs the march scenario: player 0's army
    (`--units` in total, split ceil/floor with two players) spawns in the west debug start block
    (`ViewApi.StartLayout.Block`, as `Match`), player 1's in the east one, unit types round-robin
    over every type in id order. Once the units exist (tick 2) every unit gets one `Move` to its
    player's goal: the passable cell with the greatest path cost from the middle of its own map
    edge (the passable cell nearest `(1, height / 2)` for player 0, `(width - 2, height / 2)` for
    player 1), ties to the lowest index, so the armies cross the map and each other. Only
    `Command.*` factories build commands, and a `ReplayRecorder` attaches before the first
    enqueue when `--record` is given. Defaults: 1,500 ticks, 1 player, checkpoint every 100 ticks,
    `game/data` next to the nearest `RtsGame.sln` above the working or program directory.
    Limits: units 1-100,000, ticks and checkpoint 1-1,728,000. Output: one header line (ending
    `forests F trees T mines M` since M3-1: what the placer put down),
    `tick <n> hash <16 hex>` per checkpoint (`StateHash()` right after that tick), then
    `ticks N avg A ms p99 P ms worst W ms` (a `Stopwatch` around each `Tick()` in the CLI, never in
    the sim; the CLI's own hashing and printing are outside it, but with `--record` the recorder
    hashes the state inside `Tick()` on checkpoint ticks, so those ticks time about 1 ms longer at
    2,500 units; BUG-0057), and `recorded <path>` after writing the replay. The `--record` path is
    checked before the first tick (M1-9, BUG-0057): a path that isn't valid, names a directory, or
    sits in a directory that doesn't exist fails at once with `error: cannot write replay ...`
    instead of after the whole run. Since M3-H1 (BUG-0072) the file is also created (truncated) and
    held open before the first tick and written at the end, so a name the file system refuses
    (`a<b.replay`, which `Path.GetFullPath` accepts) or a folder the user may not write to fails
    then too (a write that still fails at the end reports the same way).
  - `play <path> [--data <dir>]` reads the replay (`ReplayFormat.TryReadFile`) and plays it
    (`ReplayPlayer.Run`); on success it prints the same `tick <n> hash` lines and
    `ok: K checkpoints matched over N ticks`, or `ok: 0 checkpoints (nothing compared) over N ticks`
    for a replay shorter than one checkpoint interval (valid, but it proves nothing; M1-9).
  - Data that doesn't load prints `error: data in '<dir>' did not load (N error[s]), first: <error>`,
    where an error reads `file: path: message` with an empty file or path left out (M1-9).
  - Exit codes: **0** success; **1** bad usage (one `error: ... usage: ...` line), data that
    doesn't load, a replay file that can't be read, or a replay that can't be written; **2** a replay
    that is malformed, truncated, refused (`DataMismatch`, ...) or fails playback, printed as
    `replay failed: <ReplayError> ...` (a `CheckpointMismatch` names the tick and both hashes).
    Expected failures print exactly one stderr line, never a stack trace.
  - Tests: `CliTests` call `CliRunner.Run(args, stdout, stderr)` in-process (the test project
    references `tools/Rts.Cli`).
- **Logging:** `SimLog` with categories (AI, Path, Combat, Econ) written to `user://logs/`. In
  headless runs it also goes to stdout so Claude can read it.

## Build and export

- Godot export preset "Windows Desktop" (committed in `game/export_presets.cfg`; Godot 4.3+ keeps
  credentials out of it).
- Export templates for 4.7.x .NET must be installed once per machine (SETUP.md).
- `tools/export.ps1`: builds the solution in Release, runs the tests, exports
  `build/RtsGame/RtsGame.exe`, and zips it as `build/RtsGame-<version>.zip`.
- **M6 notes (from the M2 hardening, M2-H2):**
  - **Leave `game/tests/` out of the release.** The test and screenshot scenes (and their scripts)
    are dev tools; some start the game binary as a child process (`OS.Execute`). Exclude them in the
    preset (resources filter `tests/*`) and check the exported `.pck` holds no `tests/` path.
  - **Load `game/data/` in a `.pck`-safe way.** Today `Main` and the test scenes call
    `DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"))`, which reads the JSON from disk:
    fine in the editor, but in an exported game `res://` lives inside the `.pck` and the globalized
    path doesn't exist. Read the files through Godot (`DirAccess` / `FileAccess` on `res://data`)
    and hand their text to a loader overload that takes text, or ship `data/` beside the exe and
    locate it from `OS.GetExecutablePath()`. Either way `.json` is a non-resource file, so the preset
    needs the include filter `data/*.json`. A smoke check runs the exported exe headless and looks
    for the `Rts.Sim <version>` banner.
