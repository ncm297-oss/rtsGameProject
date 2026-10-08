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
   unit that arrives or gives up in phase 9 starts its next leg on the next tick; a unit with a
   combat target waits (M4-1). Then (M4-1) `CombatSystem.Acquire`: dead targets dropped, finished
   engagements ended, the leash applied, and every unit due (slot s on ticks where
   `(tick + s) % 4 == 0`) scans; see "Implementation (M4-1)".)
8. **Pathfinding requests:** build or fetch flow fields for new move targets. (M1-4c: the first
   step of `MovementSystem.Run`: Moving units are sorted by (goal cell, slot), every goal that
   needs a field and has it cached is touched, and the missing ones are built oldest order first,
   without allocating, at most `MovementConstants.MaxFieldBuildsPerTick` (2) per tick; see "Build cap".)
9. **Movement:** flow-field direction + steering + collision against the nav grid
   (`MovementSystem.Run`, units in (goal cell, slot) order; since M1-4d-1 every unit plans from
   start-of-tick state before any unit moves, and since M1-4d-2 the Idle units they shoved move
   last; since M1-4d-3 a unit a shove cut off its blob walks back once, see "Local movement").
10. **Combat:** attack wind-ups and cooldowns, projectile flight and impact, splash. (M4-1:
    `CombatSystem.Attack`: cooldowns count down, units with a target stand and swing in
    reach, chase out of it, and land wind-ups as queued hits. M4-2b: first every projectile in flight moves one step
    (`ProjectileSystem.Fly`); a wind-up of an attack with a projectile ends in a shot instead of a queued hit.)
11. **Damage and death:** apply queued damage, kill entities, emit death events. (M4-1:
    `CombatSystem.Resolve`: hits in attacker slot order, the dead freed at once with their
    population, kills and losses counted, death events recorded, stale targets cleared. M4-2b: after the queued hits,
    the projectiles that arrived this tick land in slot order, with their splash, before the stale targets are cleared.)
12. **Vision and detection:** recompute fog every 4 ticks (5 Hz) per player.
13. **Cleanup:** free dead handles, finalize the tick's event list, bump `TickNumber`. (M4-1: the
    dead are freed in phase 11 already; the tick's death events stay readable in `World.Deaths`
    until the next tick starts, which empties the buffer first thing.)
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
  `CargoKind`), flagged by bit 16 of the order word and added only when one isn't default. Since M4-1
  likewise each live unit's combat fields (`Hp` when not the type's, `Target`, `TargetIsBuilding`,
  `CooldownTicks`, `WindupTicks`, `LastAttacker`, `AnchorPosition`, `Mode`), flagged by bit 17, and
  each player's kills and losses, flagged by bit 33 of its gold word; see "Implementation (M4-1)".
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
Since M4-1 `QueryEnemies(center, radius, player, results)` returns the other owners' slots within the
radius in bucket order (not sorted; the combat scan breaks ties by slot itself), skipping buckets that
hold only the player's units (each bucket's single owner, empty or mixed, kept in the rebuild's
per-bucket fill-position array once its positions are spent, so it costs no memory) and returning at
once when no other owner has a live unit (a count per owner).

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
  Time-sliced builds are BUG-0023. At a realistic rate (a House every 2 s, 32 groups of 512 walkers) no walker waits
  more than 1 s for a usable field (`APlacementEveryTwoSeconds_32MarchingGroups_LongestFieldWait_Report`, 16 ticks).
  Since M4-1 that row runs with combat off (BUG-0135): its two owners' groups would meet and fight, and a chaser
  re-aiming under the churn waits up to 34 ticks; chasers' waits are BUG-0144 (M4-2), kept as a skipped combat-on row
  (`ChasersUnderPlacementChurn_LongestFieldWait_AtMostOneSecond`).
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
  `Move` and since M4-1 also scans and fights on the way (see "Implementation (M4-1)"). `Stop`
  clears the queue and Hold and leaves the unit Idle with no goal (`GoalCell = -1`), velocity 0,
  `StuckTicks` 0, `BestRemaining` infinity and no walk-back: goal-less, so friendly walkers shove it
  freely and it anchors nothing. `HoldPosition` is `Stop` and then sets `UnitStore.Hold`.
- **Holding** units are never shoved (single, chain, parked-line yield) and never walk back, and
  since M1-9 (BUG-0055, Producer decision 2026-10-05) they are hard walls to everyone, their own army
  included: a walker never goes deeper into one, also when pressed between it and something else, a
  holder can be (part of) a plug, a chain shove stops at a unit touching it, and no shove presses a
  unit past the pack limit into it. A unit told to hold a choke lets nobody through (QA's 1-cell
  corridor row: 0 of 30 own walkers pass, 10 before). Since M4-1 a holder scans for enemies within
  its reach and fights them without moving (an Attacking unit is planted the same way). Any later unqueued
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
Since M4-1 units fight (melee only): see "Implementation (M4-1)". Any unit order that is taken
(unqueued, or popped from the queue) ends the unit's engagement first, except that re-issuing a fight never throws
the swing away (BUG-0152, M4-2a): an unqueued `Attack` on the target the unit already has, or an unqueued `AttackMove`
on the leg it walks or by a unit fighting in reach or mid-swing, keeps the target, swing and cooldown; an `AttackMove`
to any point but the leg's own then re-picks by priority at once, so it can still pull a unit off a building onto an
enemy (BUG-0154; see "Implementation (M4-2a)"). Since M4-2a there is `Attack` (16, a unit
order, Shift-queueable): `Command.Attack(player, unit, target, isBuilding[, queued])` carries the target in
`Command.Target` / `Command.TargetIsBuilding` (default on every other kind, else the command is malformed), and a
queued Attack keeps its handle in the flat queue arrays it already has: an Attack has no target point, so the entry's
`QueuePosition` holds the target's slot (x) and generation (y) as whole numbers (exact in a float below 2^24; a slot's
generation counts its frees, which a 24-hour match can't take that far) and its `QueueTypeId` is 1 for a building
(`UnitStore.QueuedTarget(entry)` reads it back). So it is hashed with the entry as before, and the queue costs no more
memory: a new per-entry handle array (256 KB at 4,096 slots) took the 1,024-cell world past its 228 MB bound
(`FieldBuildFairnessQaTests.World_1024Map_CacheStays32_MemoryBounded`: 228.2 MB). See "Implementation (M4-2a)". Not
built yet: `Patrol`, formations and group moves.

Unit states: `Idle`, `Moving`, `Chasing`, `Attacking` (wind-up / cooldown), `Gathering`,
`Returning`, `Building`, `Casting`, `Dead`.
(Since M4-1 `Attacking` is a real `UnitState`; chasing is `Moving` with a live `UnitStore.Target`,
so movement walks a chaser unchanged; a dead unit is freed the tick it dies, so there is no `Dead`
state; `Casting` comes with abilities.)

Target acquisition: idle, attack-moving, holding, and patrolling units scan for enemies within
sight every 4 ticks (staggered by index). Priority: enemies attacking me > units that can attack
> other units > buildings; then nearest. Units retaliate when hit while idle.

## Combat implementation

- An attack starts when a target is in range and the cooldown is ready. At the wind-up point,
  melee damage is queued, or a projectile spawns. Damage applies in phase 11, so tick order never
  decides who "shot first" inside one tick.
- Projectiles store position, impact point, a per-tick step, flight ticks left, and the attacker's unit type (its
  damage, splash and friendly fire). Hit/miss is resolved on arrival (see [02 Projectiles](02-game-design.md#projectiles)
  and "Implementation (M4-2b)").
- The damage formula lives in one function (`DamageCalc.Compute`) with exhaustive unit tests,
  including the worked example from 02.

### Implementation (M4-1)

Melee combat: `Rts.Sim.Combat` (`CombatSystem`, `DamageCalc`, `CombatMode`, `DeathEvent`,
`CombatConstants`). Projectiles, misses, splash, friendly fire, minimum range and the
`Attack(target)` order are M4-2; fog M4-3. Until M4-2b only attacks **without a projectile** fought
(`CombatSystem.CanFight`): ranged, caster, Catapult and Sapper units neither scanned nor swung, but they
were targets like any other unit. Since M4-2b every attack with a value fights ("Implementation (M4-2b)").

**Damage.** `DamageCalc.Compute(table, damageType, armorClass, attack, bonusVs, armor)`:
`raw = attack x table[type][class] x bonusVs`, then `max(1, round(raw) - armor)`, or `max(1, round(raw))`
for a type with `ignoresArmor` (Magic). `round` is half up, `floor(raw + 0.5)` in float (so 2.5 gives 3,
and the worked example's `9 x 0.6 x 1.3 = 7.02` gives 7). The attack's `bonusVs` for the class is 1 where
the data gives none. Attack and armor include the owners' `TechState.Bonus` (`TechStat.Attack` /
`Armor`, rounded half up to whole points). Buildings take damage as the `structure` armor class
(`CombatConstants.StructureClassKey`) with their data armor; there are no building tech bonuses. The hp
and range tech stats are not applied yet.

**Per-unit state** (`UnitStore`, reset by Alloc and Free): `Hp` (the type's `hp` at spawn), `Target`
(a unit handle, or a building handle when `TargetIsBuilding`), `CooldownTicks`, `WindupTicks`,
`LastAttacker` (the last enemy unit that hit it), `AnchorPosition` and `Mode` (`CombatMode`). State
`Attacking` (5) is real; **Chasing is not a state of its own**: a chasing unit is `Moving` with a live
`Target`, so movement walks it unchanged. `UnitStore.IsPlanted(i)` (holding, or Attacking; written out inline in
movement's hot loops) is what movement and the placement rule used to ask of `Hold`: an Attacking unit is never shoved or walked
back, is a hard wall to everyone, and is never pushed out of a new footprint.

**Modes.** `None`: an Idle unit scans; a plain `Move`, a worker loop never scan. `AttackMove`: the
anchor is the leg's goal once the Move rule has applied; the unit scans while walking, chasing and fighting, and
walks the leg again (`OrderSystem.MoveTo` to the anchor) whenever it drops its target; standing Idle
with no target (arrived, or gave up) ends the mode, after which it is an ordinary Idle unit and its
queue may advance. `Retaliate`: an Idle unit that took a target (by scan, or because it was hit);
the anchor is where it stood. When its target dies or leaves its sight it walks back to the anchor,
still scanning; standing Idle with no target ends the mode. **Leash** (Producer default, M4-1): a
retaliating unit farther than its own sight radius from its anchor drops its target and walks back as
`Returning`, which does not scan (so it can't be kited back and forth), and is `None` again once it
stands. "At the anchor" means its goal is the anchor, or the point the Move rule resolves the anchor to (a
building placed over the anchor's cell since: the nearest passable cell's center; BUG-0141), so a blocked anchor
still ends the mode. Holding units keep `None`: they scan, take targets within reach only, and never move. Since M4-2a
`Ordered` is the explicit `Attack` order's mode (no anchor, no leash, no scan; "Implementation (M4-2a)").
Workers on a gather, build or repair loop (`Gathering`, `Returning`, `Building`, or walking a leg of
it) never scan and never retaliate; outside a loop a worker scans only on an attack-move leg ("Workers never fight
on their own", below). Any unit order that is taken (unqueued, or popped) clears the
target, the swing and the mode first (`CombatSystem.ClearForOrder`); the cooldown keeps counting. A
unit with a target does not pop its queue.

**Acquisition (phase 7, after the queue).** Every live unit, slot order: a dead `LastAttacker` or
target is dropped; a finished engagement ends; the leash is checked; then a unit that scans and is
due (`(tick + slot) % CombatConstants.ScanInterval == 0`, interval 4) and is neither mid-swing nor
already in reach of a live target picks one (a swing is never thrown away for a better target). The pick is one pass over `SpatialHash.QueryEnemies` (other owners within
sight, or within reach for a holder): tier 0 enemies whose `Target` is this unit or that are its
`LastAttacker`, tier 1 units that can attack (`attack.value > 0`, not the worker slot), tier 2 other
units; then nearest (squared center distance), then lowest slot. Only when no unit qualifies are
buildings looked at: this tick's list of live building slots (collected once a tick, at most
`BuildingCapacity`, skipped outright when no other owner has one), within sight of the footprint
rectangle, nearest then lowest slot. No target found drops the current one (out of sight). A new
target of a unit that may move starts a chase unless it is in reach.

**Chase.** `CombatSystem.Chase` resolves the target's position (a building: the footprint point
nearest the unit, pushed out by the unit's radius + `CombatConstants.BuildingStandOff` (0.25 m), a
standing point inside melee reach) like a Move target and gives a fresh walk (`OrderSystem.Walk`)
through the normal movement path, unless the unit already walks to that cell. A walking chaser's goal
is refreshed only on its scan ticks, so a fleeing target changes the goal at most every 4 ticks; a
chaser standing Idle (it reached where the target was, gave up, or stood down) walks again at its
next phase 7. **Every walk combat starts (a chase, an attack-mover's leg again, a walk home) starts in
phase 7**, before movement, as every order does: phases 10 and 11 only stand units still, so no unit
turns Moving after it moved in a tick (QA's movement invariants read "Moving" as "walked its step").
When a unit loses its target (dead, gone, out of sight) phase 7 settles it (`CombatSystem.Settle`): an
attack-mover heads for the end of its leg again and a retaliator for its anchor, at once, also when it
is still walking a chase; one standing after it walked there (its goal is the anchor: arrived, or
gave up on the way) ends the engagement.
**Giving up a chase** (BUG-0137, M4-1 fix). Target choice is by straight-line distance, so a target can be in
sight and out of reach (another plateau, a sealed pocket) or reachable only by a detour that leads away. Each scan
tick a unit with a target that is not holding and not mid-swing compares its gap with its best this chase
(`UnitStore.ChaseBest`, set when it takes the target or stands in reach): closer by more than
`CombatConstants.ChaseProgress` (0.1 m) is progress, anything else counts a stalled scan (`ChaseStall`). At
`CombatConstants.GiveUpScans` (10 scans, 2 s) stalled scans in a row it **gives the target up**, unless the target is a
unit and another unit of the chaser's owner stands `Attacking` within the chaser's own reach of it
(`CombatSystem.FriendFightsTarget`, one small spatial query, only when a chase stalls; BUG-0143, M4-1 fix round 2).
Then a standing point in reach exists on foot, the chaser is only queued behind its own side (a brawl's back ranks
behind their planted front rank), and the fight there frees the place within a few hits; its count starts again and it
keeps the target (its scan still re-picks the best target every 4 ticks). Without that exception the back ranks of
every brawl gave up three crowd-blocked chases, went reach-only and stood Idle 2-9 m from the enemy (500 v 500: 213 v
190 still alive after 5 min). A friend only standing at the target (a ranged unit that can't swing in this slice, a
worker) does not count: it may never move, and chasers pressing behind it never stopped (a 200-unit cross-map scene).
Nor does a building target, which can take minutes to fall; chasers that find no room at a building give it up as
before. A target on another plateau, behind a wall, or outrunning the chaser has no friend of the chaser fighting it,
so those chases still end. Switching from one target to another keeps the stall count (`Engage` resets it only for a
unit that had no target): a unit whose scans took two targets in turn (one drifting in and out of its sight) restarted
the count every scan and chased forever. So does a retaliator
the leash pulls back, and a chaser that loses sight of its target while not gaining on it, whether or not its scan
then finds another target (BUG-0150, M4-2a fix round: before, a switch to another target in sight did not give the
lost one up, and a caster at the sight edge reached by a path leading out of sight and a nearer worker took turns
forever, each switch a fresh chase whose first scan showed progress). Giving up remembers the
target (`UnitStore.Ignored`, a unit or a building): the unit's scans take it again only when it is in reach, and an
attack by that unit does not start a retaliation (a building remembered with the same slot and generation as the
attacker is not it, BUG-0142; a melee attacker is in reach, so the next scan takes it anyway). The unit
then settles as for a lost target (an attack-mover resumes its leg, a retaliator walks home). It remembers one target
only, so it also counts its give-ups (`GiveUps`); at `CombatConstants.MaxGiveUps` (3) its scans take only targets in
reach, like a holder's, so two unreachable enemies can't take turns forever. Any order taken
(`ClearForOrder`) forgets both; landing a hit resets the count. A chase that stalls because the target runs as
fast as the chaser ends the same way, which is intended (the attack-mover goes on, the retaliator goes home).
Known limits of the friend exception: a friend fighting an enemy the chaser can't reach (both on a plateau the chaser
can't climb) keeps the chaser pressing below for as long as that fight lasts; a target that never dies would keep its
queued chasers queued. Brawl finish ticks after the fix (QA's scenes; f2879b9, which had no give-up at all, in
brackets): 500 v 500 lines 2,278 (2,373), columns 3,586 (3,406), 200 v 200 map seed 1 2,834 (2,371), seed 2 2,073
(2,008), 40 v 40 ten ranks 1,418 (979), five ranks 1,037 (1,285), 100 v 100 1,885 (1,934); a few units give a chase up,
none reaches `MaxGiveUps`.
A chaser counts as arrived only within 0.05 m of its goal (`MovementConstants.ChaseArrival2`), not
the usual `ArrivalDistance` (1 m), so it never stops short of its reach; combat plants it once the
target is in reach. **No straight steering:** the M4-1 plan allowed chasers to steer straight at targets
within 8 m, without flow fields, if chasing thrashed the field cache. Measured (CombatPerfTests
`Chase250After250_Report`, 250 Heavy Infantry attack-moving after 250 faster Crossbowmen fleeing to 50
points, 300 ticks): 0.29 field builds a tick and chasers
standing still while Moving (waiting for a field) on 1.9% of chaser-ticks, 0.65 ms a tick in Debug: the cache does
not thrash, because a walking chaser's goal changes only when its target's cell does, at most every
scan. Straight steering was built and measured (1.0% waiting) and then left out: in the brawl below it
made the lines engage more slowly (120 vs 214 units swinging after 200 ticks) for the same cost.

**Attack (phase 10).** Every live unit, slot order: the cooldown counts down; a unit with a target
mid-swing stands, faces the target and counts its wind-up down; at 0 the hit is queued if the target
is within reach + `CombatConstants.WindupGrace` (0.5 m, Producer default), else the swing is lost.
Otherwise in reach (`gap <= attack range`, gap = center distance less both radii, or for a building
the distance to its footprint less the unit's radius) the unit plants (`Attacking`, no goal, no
velocity, no walk-back), faces the target, and with the cooldown at 0 starts a swing: cooldown =
`attack.cooldown`, wind-up = `attack.windup` (a wind-up of 0 hits at once). So the first hit lands
`windup` ticks after the swing starts and then every `cooldown` ticks. Out of reach an Attacking unit
stands Idle again (a holder waits; anyone else chases from the next phase 7), and a swing that ends
with the target out of reach stands the unit down that same tick, so an Attacking unit is always in
reach or mid-swing, and a scan never re-targets one (fuzz-checked: a unit Attacking at the end of two
ticks running never moved in between).

**Damage and death (phase 11).** Hits apply in attacker slot order; a hit's damage is worked out when
it is queued. A unit killed earlier in the phase takes no more hits, but its own queued hit still
lands, so equal fighters that swing together die together. A unit at 0 hp is freed at once (its handle
is stale before the tick ends, its population goes back through the ledger, its queue and loops go, a
worker's cargo is lost with it, and the slot reads `Idle` with no velocity, so a scan over slots never counts the dead
as walking);
a building at 0 goes through `BuildingStore.Damage` (freed, cells by the pocket rule, queue refunded).
Each death appends a `DeathEvent` (victim handle, building or not, type, owner, killer's owner,
position: a building's footprint center) to a buffer sized to the unit capacity (a death per hit at most), and counts one kill for
the killer's owner and one loss for the victim's (`World.Kills` / `Losses`). A unit hit by an enemy
records it as `LastAttacker` and, if it scans and has no target, takes it on at once (retaliation).
After any death one pass clears every stale target (the attacker stands down; phase 7 settles it)
and last attacker, and ends the build or repair order of every worker whose building died. `World.Deaths` holds one tick's events: the next tick empties it first.

**Hashing.** The combat fields go in only when one isn't at its spawn value (hp below or above the
type's, a target, cooldown, wind-up, last attacker, anchor, mode, or the chase memory `ChaseBest`, `ChaseStall`,
`Ignored` / `IgnoredIsBuilding`, `GiveUps`), flagged by bit 17 of the unit's
order word, so a unit that never fought hashes exactly as before and the golden replay did not move.
Kills and losses go in only for a player with either non-zero, flagged by bit 33 of its gold word.
The death buffer, the hit queue and the building list are scratch, not hashed.

**Combat switch** (`SimConfig.Combat`, default true; BUG-0135, Producer decision 2026-10-07, owner may revisit). A
test and tooling switch, not a game option: false skips `Acquire`, `Attack` and `Resolve` and makes `AttackMove` set no
combat mode, so a world of two owners plays exactly as before M4: enemies are walls and never fight. It exists for the
pre-M4 movement, economy, production and view scenes whose assertions are about a world without fights (arrivals,
alive counts, money oracles, "a holder is Idle"); they opt out through `TestSim.ConfigNoCombat` or their own config
helper, changing the config only, never an assertion. The command fuzzes (the door fuzz, the order stress) stay on
combat with the invariant re-baselined: a holder is Idle *or Attacking* and never moves (a Stop leaving no target and
the state Idle on the tick it applies is `CombatTests`' row). The economy conservation fuzz runs with combat off: on
combat its "the fuzz gathered and delivered" precondition misses on one seed (attack-moved workers fight instead of
delivering), and a precondition is not re-baselined. Since fix round 2 (BUG-0143) brawls are fought to the end, so the
two-owner movement termination rows whose limits are walking bounds (`CrossMap_TwoOwners_Report`,
`TwoHundred_TwoOwners_Seeds1To50_Terminate_Report`, `Crowd_ToFourPoints_BothPlayersAtEveryPoint_Report`, the
`TwoFriendlyGroups_Swapping...` shove row's two-player case) and `RandomOrderMixes_...` (on combat seed 2's queues never
fill: a precondition) run combat off too; their combat-on termination is `CombatTerminationTests` (the same scenes on
combat come to rest, nobody Moving for 200 ticks, within 3x the walking limit; measured at most 1.23x). The
placement-churn field-wait row runs combat off as well (see BUG-0080 above). Since M4-2a four more two-owner movement
rows went combat off once the ram stopped fighting units and a retaliator ping-ponged between two targets (BUG-0150);
its fix round put two back on combat (`ShoveQaTests.BuildCap_500UnitsTo500RandomGoals...(players: 2)`,
`GridChangeFuzzStressTests`, whose seed 3 found the loop). Two stay off (config only):
`CrowdRowSweepStressTests.MoreGoalsThanCacheSlots_Seeds81To140` and `GridChangeQaTests.FourGroups_AClosingChangeEveryTick...`
(walking bound 3,000 ticks). On combat they are not a loop but a slow, one-sided brawl: the scenes end only when one
side's few melee fighters have cut down the other side's units that cannot fight back in this slice (ranged and
casters), which they do by themselves at tick 3,759 (seed 110, the only one of the 60 seeds over the bound) and
5,057-6,618 (the four-groups scene with closings every 3 / 2 / 1 ticks). Matches, the CLI and replays always
fight. Since M4-2a replay format 4 records the switch (`combat` in the header) and `ReplayPlayer.Run(replay, data)` plays
back with it; `Run(replay, data, combat)` overrides it (format 3 test replays recorded off).

**Workers never fight on their own** (Producer decision 2026-10-07, owner may revisit; from the view's BUG-0147,
where player 1's Idle laborers walked to player 0's Depot and then killed player 0's laborers at the Playable scene's
start). A unit of the `worker` slot does not scan while Idle or holding and does not retaliate when hit
(`CombatSystem.Scans`); it scans and swings only on an attack-move leg (and, from M4-2, under an explicit `Attack`).
The data hook is the unit's `slot`, never an id. It still counts as a target (tier 2 in the priority).

**The ram** (BUG-0139): fixed in M4-2a by the `attack.targets` field (below, "Implementation (M4-2a)"); the Battering
Ram's data says `"buildings"`.

**Cost.** The scan's spatial query skips buckets holding only the scanner's own units (a per-bucket
owner code) and returns at once when no other owner has a live unit (per-owner counts), so a
one-player crowd's units skip their scans altogether (each tick works out, per player, whether any
other owner has a unit or building at all). Measured in Debug, same machine, against the M4-1 base
(three alternating runs): the one-player 2,500 tight blob 4.36 -> 4.42 ms, the M1 500-moving-unit row
0.633 -> 0.647 ms; the combat phases themselves cost 0.03 ms a tick there. A 500 v 500 melee brawl
(`CombatPerfTests`, two battle lines 10 ranks deep and 50 m wide, 8 m apart) averages 3.0 ms over its
first 200 ticks and 3.4 ms over the next 400; the same armies as columns 25 ranks deep and 20 m wide
(reported, not asserted) 4.3 / 5.6 ms, most of it movement: the units queued behind their own planted
front rank. Re-measured after the BUG-0143 fix (back ranks keep chasing rather than standing): 3.06 / 3.47 ms and
4.32 / 5.54 ms. Scanning a crowd allocates nothing, nor does a brawl tick (`AllocationTests`). The world on
a 1,024-cell map with 4,096 unit slots grows 0.4 MB (227.5 -> 227.9 MB).

**The tight-blob budget (BUG-0140, Producer decision 2026-10-07, owner may revisit).** QA's 13 paired runs put the
one-player 2,500 blob at +0.08 ms over the base (4.374 -> 4.454 ms), which left the row 0.05 ms under its 4.5 ms budget
and failing about 1 run in 6. The cost is spread over per-unit checks, not the scan: phase 7's loop over every unit,
a walker's `Target` read for the chase arrival distance, and the `Attacking` test beside every `Hold` test in the
hard-wall, shove and chain rules. The fix round tried to win it back (phases 7 and 10 skipping their loops until the
first enemy or attack-move; reading a neighbor's state once in the shove rules); neither moved the measured average
(4.43-4.52 ms over 5 alone runs either way), so both were left out for simpler code, and the budget is 4.6 ms (approved
in advance). The scan itself stays near free: one enemy at the far corner costs well under the 0.25 ms QA allows
(`CombatScaleQaTests`).

### Implementation (M4-2a)

**BUG-0146: gatherers wedged out of reach** (fixed first; M3 sign-off). Root cause, from the seed 21 Playable replay: a
footprint walk (a gather, drop-off, build or repair leg, `EconomySystem.WalkToFootprint`) stopped either within
`ArrivalDistance` (1 m) of its goal point, so the front worker stood about 1 m off the edge, or on touching an arrived
groupmate (crowded arrival), so the worker behind it stood about 1.8 m off, out of `Reach` (1.25 m): only one worker
worked a node open on one side. When the front one left with a load, the next one was the front with the queue
touching it from behind, and every 20-tick retry "arrived" at once against that queue, on the same spot, for the rest
of the match (unit 10 for 14,575 ticks). Three changes, all for footprint walkers only (a unit on a gather, build or
repair loop: `GatherNode` or `BuildTarget` set; every other walk is unchanged, so the golden's `k` lines did not move):
- **Queue rule** (`MovementSystem.QueuesBehind`): a touched arrived groupmate stops a footprint walker only if it stands
  nearer the walker's goal than the walker does. The front of a queue always walks in; the queue still forms behind it.
- **Stand arrival:** while it makes progress, a footprint walker walks on until it is within `Reach - GoalInset - 2 x
  radius` of its stand point (0.325 m for a 0.4 m worker; `MovementSystem.StandArrival2`), not `ArrivalDistance`: the
  front worker stands within about 0.45 m of the edge, so one queued touching it from behind (0.8 m further) is in
  reach too. Once blocked (a stuck or queued tick) within `ArrivalDistance` it stops where it is, as before, which is
  still in reach (a stand point projects onto the edge, so 1 m from it is at most 1.125 m from the footprint).
- **Stand points** (`EconomyConstants.StandPointsPerCell` = 3, `StandPointSpacing` = 1.5 walker radii): the cell is
  picked exactly as before (distance to its center plus `CellSize` per other unit in it, ties to the lower cell), so
  workers spread round a mine as they did; in that cell the worker takes one of three points along the shared edge (its
  middle and 0.6 m to either side for a 0.4 m worker, all `GoalInset` outside the edge): the one with the fewest other
  units on it (center within the walker's radius), then the nearest, then the middle. So four workers on a 1 x 1 tree
  open on one side all work it (three on the points, one behind), and a worker that waited out of reach takes a free
  point on its next retry. The "queue at a mine's edge" stays: with every point taken, a walker stops behind the front
  row and retries every 20 ticks.
- **Cost** (Debug, this PC, alone, three alternating runs against the base): `GatherPerfTests.TwoHundredGatheringWorkers
  Alone` 0.23 -> 0.32-0.33 ms a tick (its budget is 1 ms), because that scene (50 workers on one tree, 50 on one mine,
  per base) now works more: gold 1,120 -> 1,310 and wood 440 -> 500 by the end of the row (+17% / +14%), with 39% more
  walkers a tick in denser crowds round the nodes (stand points fill, so more workers stand close). Per resource
  delivered it is about 20% dearer. The 500 marching + 50 gathering row is unchanged (0.90 ms), and so is the 2,500
  tight blob (4.38-4.54 ms against 4.40-4.49 ms; it has no footprint walkers). The brief's 10% bound on the gather row is
  not met; reported for the Producer (BUG-0151). The fix round profiled it (Debug, alone; base 0.226-0.231, head
  0.314-0.327 ms): the cost is not the new code but the crowd it makes. Per tick over ticks 400-800 the scene has 57
  workers walking against 41 (most of the extra within 4 m of their stand points) and twice the neighbors within 1.5 m
  of each walker (465 against 228), so movement's per-neighbor work doubles. Switching parts off one at a time: the
  stand points' three spatial queries off (middle point only) made it slower (0.38 ms: more workers pile on one point);
  the queue rule off 0.29 ms (but that is the BUG-0146 fix itself); the stand arrival off 0.26 ms with gold 1,010 (below
  the base's 1,120). Three cheaper variants (a stuck walker queuing on any touching groupmate, also on a same-node
  worker ahead in another cell, a wider point-crowd radius) each broke a `GatherPocketStressTests` wedge row, or cut
  little (0.31 ms with gold 1,220 for a walker stuck 10 ticks queuing on any groupmate). None was kept.

Regression rows: `GatherWedgeTests` (the bug's map by hand: a 1 x 1 tree with trees north, west and east and a Depot to
the south-west; four workers each deliver within 600 ticks, and a column of four ordered one after another never has a
worker standing out of reach with nobody between it and the tree for more than 40 ticks; both fail on the old code,
the column for 1,644 ticks) and `QA/GatherWedgeQaTests` (the seed 21 replay un-skipped: longest out-of-reach stand
343 ticks, a worker queued behind a working one, against 14,575; player 0's wood 1,360 against 100). That replay's
header is the pre-D4 data hash; the row accepts the shipped hashes whose changes cannot touch that match (D4's text,
M4-2a's ram field) and checks it still plays its recorded checkpoints up to where the fix first changes a walk (ticks
1-19: on tick 20 its first gather walks walk on to their stand points, so a longer prefix cannot hold). The seed 6 two-cell corridor
oscillation in the bug file is a builder walking out against two standing gatherers, its velocity flipping each tick:
not this arrival rule (the walker never arrives), and not reproduced; left to its own bug.

**`Attack(target)`** (`CommandKind.Attack` = 16; M4 criterion 1). Applied in phase 1 (`OrderSystem.Apply`):
- **Dropped** (queued or not, nothing changes, like a bad Gather) when `CombatSystem.MayAttack` says no: combat is off
  (`SimConfig.Combat` false), the attacker's type cannot fight (`CanFight`: until M4-2b ranged, caster and
  siege-with-projectile units; since M4-2b every attack with a value fights), the target is dead or recycled (generation), or the
  attacker's own (no allies yet), or a kind its `attack.targets` forbids. A queued Attack is checked again when it is
  popped and dropped then if its target died meanwhile; the next queued order starts on a later tick.
- **Started** (unqueued, or popped): the queue is replaced (unqueued only), Hold, any gather / build loop and the old
  engagement end (`ClearForOrder`), the unit stops where it stands, and `CombatSystem.StartAttack` sets the target, a
  fresh chase memory and `CombatMode.Ordered` (no anchor). Phase 7 of the same tick chases it.
- **Rule:** the explicit target outranks the scan: an `Ordered` unit does not scan (`Scans` is false), so no nearer or
  higher-priority enemy re-picks it, and a hit by another enemy does not retaliate (it has a target). A walking ordered
  chaser is re-aimed on the ticks it would have scanned (`(tick + slot) % 4 == 0`). The retaliation leash does not
  apply (it is `Retaliate`-only): an ordered unit chases as far as its target goes (Producer decision (a)). The give-up
  memory does apply: ten stalled scans (BUG-0137's rule, the friend-fighting exception included) give the target up
  (`Ignored`), and the unit then stands Idle where it is, not walking home. A worker obeys an explicit Attack (decision
  (b)); "workers never fight on their own" still holds for scans and retaliation. When the target dies (or is given
  up) the unit stands Idle with no mode (`Settle`, which stops a walking one in place); its queue advances from the
  next phase 7, else it is an ordinary Idle unit and scans as one (a worker does not). A building target works the same
  way (`TargetIsBuilding`; the chase aims at the footprint's nearest point as in M4-1).
- **`attack.targets`** (`AttackDef.Targets`, `AttackTargets`: `all` / `units` / `buildings`, JSON `attack.targets`,
  default `all` when absent, decision (d); any other value is a `DataError` at the field; in `ContentHash`). Respected by
  the scan and holders (`PickTarget` looks only at the allowed kinds: a buildings-only unit goes straight to the building
  list), retaliation (a buildings-only unit never takes the unit that hit it) and the explicit order (dropped). The
  Battering Ram ships `"buildings"` (docs/factions/whirlwind.md: "attacks buildings only"; BUG-0139 closed). The data
  hook is the field, never an id check.
- **Hashing:** a pending command's target goes in after its unit handle, flagged by bit 63 of its kind word; queue
  targets as above (in the entry's hashed position and type id); `CombatMode.Ordered` (4) is in the combat block. No new
  per-unit or per-entry array.
- **Replay format 4** (see "Save/load and replays"): the header's `combat` line and the target on every command line.
  The golden was regenerated once for it: `rts-replay 4`, the `combat 1` line, the new data hash (the ram's field),
  13-field command lines; every `k` line identical (the fix to BUG-0146 changes no walk on its move-only path).
- **Cost:** nothing allocates; the ordered unit costs what a chaser does. The queue rule and the stand arrival read a
  walker's loop handles only when it touches an arrived groupmate or stands within 1 m of its goal, so a crowd of plain
  walkers pays nothing new (the gather cost is under BUG-0146 above).
- **Re-issuing a fight** (BUG-0152, fix round): an unqueued Attack used to end the engagement first and start afresh,
  which zeroed a wind-up in progress while the cooldown it set kept counting, so a Heavy Infantry (6-tick wind-up)
  re-ordered every 5 ticks or faster never landed a hit (an AttackMove to the same point likewise, since M4-1). Now an
  unqueued Attack on the target the unit already has (same handle and building flag) only replaces the queue and
  clears Hold: the swing, cooldown, state and, under `Ordered`, the chase memory stay (`CombatSystem.ReaffirmAttack`); a
  unit that took that target itself (scan, retaliation, attack-move) now holds it as `Ordered`, with a fresh chase and
  give-up memory as a new Attack gets. An unqueued AttackMove keeps the unit's target, swing and chase when it is on the
  leg the unit already walks (the leg's end resolves to the same cell; the give-up memory kept) or when the unit fights
  in reach or mid-swing (any leg; the give-up memory reset as for any new order); the mode becomes the new leg, walked
  once the fight ends. Unengaged on the same leg, the Move rule's same-target case applies as before.
  **Redirecting** (BUG-0154, fix round 2): round 1 stopped there, so an attack-move anywhere left a unit fighting in reach
  on its target for good (the scan never re-picks in reach): a unit hitting a Tent could not be attack-moved onto the
  Raider killing it. Now a kept fight re-picks unless the order is the leg's own point (within `ArrivalDistance`, 1 m, of
  the leg's resolved end: the Move rule's "already there"): the order sets `UnitStore.Repick`, and that tick's phase 7
  scans the unit at once, due or not and in reach or not, by the usual priority. The same pick keeps the swing (`Engage`
  with the target it has changes nothing), so spam to the same point, or to a new point by a unit fighting in reach,
  still lands every hit; a better pick (an attacker, a unit over a building) is taken at once. A chaser still out of
  reach given a new point does lose its target until its next scan (BUG-0157, S3: jittered A-click spam every 1-3
  ticks costs a brawl 10-26 % of its damage; same-point spam and spam 5+ ticks apart cost nothing). Another point in the leg's own cell counts as a new point here
  (QA's repro attack-moved onto a Raider standing in that cell) but keeps the give-up memory. The flag is cleared in the
  same phase 7 (and by `ClearForOrder`), so it is never set between ticks; it is hashed above the mode's byte (`Simulation.AddCombatToHash`), which leaves every hash between ticks, and the goldens, as they were. Rows:
  `AttackOrderTests.ReissuedAttack_...`, `ReissuedAttackMove_...`, `AttackMove_ToANewLeg_MidSwing_...`,
  `AttackMove_WhileHittingABuilding_ANewPointRepicks...`, `QA/AttackOrderQaTests.ReissuingTheSameAttack_EveryNTicks_...`,
  `QA/FightReissueQaTests.AttackMoveOntoAnAttacker_WhileHittingABuilding_TakesTheAttacker`.
- **Not yet:** projectiles, misses, splash, friendly fire, minimum range (M4-2b, below), fog (M4-3), the view's F-key /
  cursor path.

### Implementation (M4-2b)

Projectiles, misses, splash, friendly fire and the minimum range (M4 criterion 3); `Rts.Sim.Combat.ProjectileSystem`,
`Rts.Sim.Entities.ProjectileStore`, `ProjectileImpact`. With it every shipped unit fights: `CombatSystem.CanFight` is
"an attack with a value" (the M4-2a rule that dropped an Attack for a unit with a projectile is gone).

**Data** (`game/data/common/projectiles.json`, sim-owned, required): `{ "projectiles": [ {id, kind, speed, hitTolerance?} ] }`.
`kind` is `aimed` or `lob` (`DataLimits.ProjectileKindIds`); `speed` in m/s (positive, stored per tick: 25 m/s is 1.25 m
a tick); `hitTolerance` in meters, aimed only, 0-2 m (`DataLimits.MaxHitTolerance`), default 0.3
(`DataLimits.DefaultHitTolerance`, docs/02); on a lob it is an error. The shipped five are exactly the ids `units.json`
names: `arrow`, `bolt`, `magic_bolt` (aimed, 25 m/s, 0.3 m; docs/02 gives no caster speed, so the magic bolt flies as an
arrow does), `catapult_stone` and `sharper` (lobs, 12 m/s; docs/02 "thrown munitions"). A unit's `attack.projectile`
resolves at load to `AttackDef.ProjectileTypeId` (the string is kept); an unknown id is one error at
`units[i].attack.projectile`, and a broken projectiles file is reported once, not again per unit. An attack whose
projectile is a lob must have a splash above 0 (a lob's only effect is its explosion). `GameData.Projectiles` /
`FindProjectile`; `ContentHash` covers every field and the resolved id. The Battering Ram has no projectile: it is a
melee siege unit (`attack.targets` `buildings`).

**The store** (`ProjectileStore`, flat structure of arrays, `SimConfig.ProjectileSlots` slots): a projectile is
fire-and-forget, so there are no handles; a shot takes the lowest free slot, and a shot fired while the store is full is
lost. `SimConfig.ProjectileCapacity` sets the size; the default (0) is as many shooters as the players' population caps
allow, `players x rules.popCap / the smallest pop of a unit with a projectile` (200 for two players), at most
`UnitCapacity`: every shipped shot lands before its shooter's next one (flight shorter than the cooldown,
`ProjectileTests.EveryShippedShooter_HasAtMostOneShotInTheAir`), so a shooter has at most one in the air. Dev spawns
past the caps can outnumber it (the 500 v 500 Perf scene sets 1,000). Sizing it to the unit slots instead was measured:
on the 1,024-cell map with 4,096 unit slots the world grew 0.42 MB, past `FieldBuildFairnessQaTests`' 228 MB bound
(which had 33 KB left); the default costs 17 KB there. Per slot: position, last position, a per-tick step, the impact point, ticks left, the
projectile type, the owner, the attacker's unit type and handle, and the target (unit or building handle).

**Firing (phase 10).** A unit whose attack has a projectile swings exactly as a melee unit does (plant in reach,
cooldown, wind-up); at the wind-up end (`CombatSystem.Strike`) it fires instead of queuing a hit: a projectile from
the attacker's center to where its target is *now* (a unit's center; a building's footprint point nearest the
attacker), in a straight line at the type's speed (the view draws any arc). It lands `max(1, ceil(distance / step))`
ticks later: every tick, at the start of phase 10 (`ProjectileSystem.Fly`, before the swings, so a shot fired this tick
first moves on the next), each projectile moves one step, the last one exactly onto its impact point. A Crossbowman 8 m
from its target: `ceil(8 / 1.25)` = 7 ticks.

**Landing (phase 11)**, after the queued melee hits, in slot order (`ProjectileSystem.Land`): an **aimed** shot hits its
target if the target is alive and its center is within its collision radius + `hitTolerance` of the impact point (a
building never moves, so a shot at a live one always hits); the target takes the full damage
(`DamageCalc.Compute` at landing, both owners' techs) and, if the attack splashes, the splash lands round the impact
point too. Otherwise it **misses and lands harmlessly**: no damage, no death, no `LastAttacker`, no retaliation (a target
killed earlier in the phase is a miss as well). A **lob** always explodes: its splash is all of its damage, the target
included if it is still there. Either way the landing is recorded in `World.Impacts` and the slot is freed. Projectiles
outlive their shooter. Hits and landings are one phase, so a unit killed by a projectile still lands its own queued
hit that tick, and the reverse.

**Splash** (`ProjectileSystem.Splash`, an attack with `splash` > 0; a lob's, an aimed hit's, and a melee hit's should a
melee attack ever carry splash): every unit whose center is within the radius of the impact point, and every building
whose footprint is, except the target the aimed or melee hit already struck. The factor (`ProjectileSystem.Falloff`):
1 within 40 % of the radius (`CombatConstants.SplashFullFraction`), then linear to 0.5 at the edge
(`SplashEdgeFactor`). Other owners' units always; **own units only with `friendlyFire`** (the Catapult and the Sapper),
times 0.5 more (`FriendlyFireFactor`), the shooter itself included; other owners' buildings take structure damage
(their `structure` class and armor); **own buildings never** (docs/02: friendly fire never damages buildings). Each
victim's damage is `DamageCalc.Compute` with its own class and armor, times the factor, rounded half up, at least 1.
Units in slot order (the spatial hash's radius query into `World.Neighbors`, movement's scratch, widened by a step and
1 m since the hash holds the phase-1 positions), then buildings in slot order. A melee hit with splash reads the
attacker's type and position from its slot (a unit freed earlier in the phase keeps both), so `PendingHit` is unchanged. A friendly-fire hit is not an attack: no `LastAttacker`, no retaliation; a
friendly-fire kill counts as a kill for the shooter's owner and a loss for the same player (`DeathEvent.KillerOwner` =
`VictimOwner`), so kills and losses still balance.

**Minimum range** (`attack.minRange`, edge to edge like `range`; the Catapult's 6 m). A target inside it cannot be fired
at: no swing starts, a wind-up that ends with the target inside it is lost, a planted unit stands Idle and a chasing one
stops where it is (`CombatSystem.InReach` is "within range and not inside the minimum range"). The scan never takes a
unit or building inside it, so an Idle unit, a retaliator or an attack-mover whose target comes too near drops it at its
next scan and takes one outside it (a nearer enemy of equal priority wins only if outside), and walks its leg on if
there is none; a unit hit by an enemy inside its minimum range does not retaliate on it. An **explicit Attack** on a
too-near target keeps the target and waits where it stands, Idle (the give-up count treats it as in reach, so it never
gives up); it fires as soon as the target is outside the minimum range again. There is no step back or kiting in this
slice (Producer decision).

**BUG-0156** (S3, folded in): a unit hitting a *building* in reach now re-picks between swings when an enemy unit has
hit it (`LastAttacker` set): the scan runs, and the attacker (tier 0) wins; a unit target in reach, or any swing in
progress, is still never thrown away.

**For the view (M4-V3).** `World.Projectiles` (`ProjectileStore`): `Capacity`, `Count`, and read-only spans per slot:
`Alive`, `Position`, `PrevPosition` (the launch point on the tick of the shot; interpolate between the two as for units),
`Target` (the impact point), `ProjectileTypeId` (`GameData.Projectiles`), `Owner`. `World.Impacts`: this tick's landings
(`ProjectileImpact`: position, projectile type, owner, `Hit`: an aimed hit or any lob; false for an aimed miss), slot
order, emptied at the start of the next tick like `World.Deaths`. Splash victims show through hit points and
`World.Deaths` as before.

**Hashing.** The store goes into `StateHash` after the buildings only while it holds a projectile: its count, then every
live slot's index and fields (free slots hold their never-used values, and the slot a shot takes follows from the live
ones), so a match without a shot hashes exactly as before and the golden replay's `k` lines did not move; its
`data-hash` moved once for `projectiles.json`. `World.Impacts` is output, not hashed. Reflection audit: `StateHashTests.EveryProjectileStoreArray_IsHashed`. No replay format change
(no new command; `ProjectileCapacity` is not in the header, a replay plays with the default).

**Hit rates** (`ProjectileTests.HitRateByDistance_Report`, docs/02's numbers: 25 m/s, radius + 0.3 m). A walker is
missed once it moves more than radius + 0.3 m during the flight, so the cut is the flight time: a walking Heavy
Infantry (0.15 m a tick, 0.7 m) is hit every time while the flight is at most 4 ticks (up to 4 m, and 5 m walking
away), about a third of the time crossing at 5 m, and never from 6 m on, whichever way it walks (toward the shooter
too: the bolt flies to where it was); a galloping Horse Raider (0.33 m a tick, 1.0 m) is hit only now and then within
5 m and never from 6 m on. So "slow units almost never dodge" (docs/02)
holds only at short range: ranged units hit standing and fighting units, and miss walking ones beyond a few meters.
Reported for the Producer and the data track (`hitTolerance` is per projectile, Producer decision); not tuned here.

**Cost** (Debug, this PC, each row alone, first run of a fresh process). Nothing allocates
(`AllocationTests.MixedBrawlTicks_WithProjectilesAndSplash_AllocateNothing`, and the 1,000-bolt row); a tick with an empty
store pays one compare in `Fly` and one in `Land`. `CombatPerfTests.MixedBrawl500v500_First200Ticks_AverageUnder4Ms`
(500 v 500 mixed armies, `CombatScenes.MixedBrawl`): 1.73 ms over ticks 5-205, 1.23 ms over 205-605, up to 85
projectiles in flight (cheaper than the melee brawl: shooters stand, and fewer units press into the front).
`ThousandProjectilesInFlight_UnderPoint3MsATick_AndAllocateNothing`: 0.026 ms a tick in flight; the tick all 1,000 land
on one Raider 2.0 ms (reported). Unchanged rows: the melee brawl 3.09 / 3.39 ms (M4-1: 3.06 / 3.47), the one-player
2,500 tight blob 4.34 ms (budget 4.6), `GatherPerfTests.TwoHundredGatheringWorkersAlone` 0.318 ms (M4-2a: 0.32).

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
  (Since M4-2a, BUG-0146: three stand points per cell, the stand arrival and the queue rule; see "Implementation
  (M4-2a)".)
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
  has no worker, still reports `UnitInTheWay` for that spot. Since M4-1 an own unit standing `Attacking` is planted like a
  holder, and the same holds for it (BUG-0142 item 1, kept as the BUG-0092 shape): only an attack-moved worker can be
  Attacking, and its Build is accepted while the ghost reads `UnitInTheWay`.
- **Never seal (BUG-0078).** A footprint may be taken only if every two passable cells that connect now still
  connect without it (`Map.SealCheck`, on `World`, allocation-free). A path through the footprint enters
  and leaves it through passable cells 4-adjacent to it, so it is enough that those cells still reach each other.
  The resource placer's ring-then-flood test: when every cell 8-adjacent to the footprint is passable they form a
  loop round it and the answer is yes at once; otherwise one 4-connected flood from all of them at once, outside the
  footprint, each carrying its side's label: where two floods meet their labels merge (yes once one is left), and a
  label whose cells are all expanded without meeting another is a cut-off region (no). A "no" so costs about the
  pocket it would make, not the map. Unlike
  the placer it doesn't need the whole map connected beforehand. The dev command `SpawnBuilding` applies it too.
  The last flood's answer is kept with its footprint (anchor and size) and `NavGrid.Version` (M3-H2, BUG-0096): the
  answer depends only on passability, and every passability change, opening or closing, bumps `Version` (keying on
  `BlockVersion` would miss an opening that unseals the spot). So 100 workers' Builds refused at one sealing spot in
  one tick flood once: 33 ms before, 0.41 ms now (Debug, the first flood included). Derived scratch, not hashed.
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
  a free cell turns up, so two pushed units never share a cell (M3-H1, BUG-0092), but only over the footprint's
  **plateau** (M3-H2, BUG-0097, see "Implementation (M3-4)": never onto another plateau of the same level). Once the
  plateau has no free cell (found once, not once per unit), the rest, the leftovers, spread over its passable cells by
  the same ring order, one leftover per cell before any cell takes a second, each pass set off the cell's center in its
  own direction (M3-H2, BUG-0095: they used to stack on one point), so no two units share a point and two leftovers
  share a cell only when there are more of them than cells; only a plateau with no passable cell left outside the
  footprint falls back to the nearest passable cell. Occupants come from the spatial hash, rebuilt once per push-out so it
  matches the units of that moment, each cell's answer kept in the flow-field builder's scratch; a pushed unit's
  `PrevPosition` is set with its `Position`, so the view doesn't draw it sliding through the building. Then the worker
  is given the site. A dropped
  Build changes nothing (totals, store, queue and Hold stay). Queued, a Build is checked when it starts (phase 7) and
  pays then.
- **Construction.** A site is a `BuildingStore` entry with `UnderConstruction` and `Work` (int);
  `WorkNeeded(type) = EconomyConstants.BuildWorkScale (3) x buildTicks`. Each tick n builders in reach add
  n + `BuildWorkBase` (2), so a site takes exactly `ceil(3 t / (n + 2))` ticks (docs/02's `t x 3 / (n + 2)`): a 20 s
  House takes 400, 300, 200 and 120 ticks with 1, 2, 4 and 8 builders. Each tick of work **adds** the hit points
  it grows, `max(1, maxHp x Work / WorkNeeded)` less the same at the old work (full `maxHp` at `WorkNeeded`), capped
  at the maximum: an undamaged site reads exactly `max(1, maxHp x Work / WorkNeeded)` and completes at full hit
  points, and damage a site took stays taken (since the M4-1 fix of BUG-0138; before, every work tick recomputed the
  hit points and a builder made a site immune). At `WorkNeeded` the site completes and its workers go Idle
  (`popProvided` applies with population, M3-4).
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
  ring first, on the footprint's **plateau**, passable, no live unit's center in the cell; within a ring the cell
  nearest the rally point (the footprint's center without one), ties to the lower cell index. A plateau
  (`Map/Plateaus`, M3-H2, BUG-0097) is a 4-connected piece of passable ground of one level as the terrain stands at load
  (a ramp reports its lower level, so it belongs to the ground at its foot); each has its own bounding box, and the
  ring walk ends once a ring encloses it, so a full small plateau costs its own area, not the map's (BUG-0095). Until
  M3-H2 the bound was the whole *level* (`World.LevelBounds`), so a spawn on a full plateau landed on another plateau
  of that level, possibly by the enemy's base; now it waits. Plateau ids are terrain only (nodes and buildings don't
  change them; levels never change after load), derived and not hashed. Occupancy: on the tick's first spawn attempt
  on a plateau its box of the push-out's per-cell cache is filled from one pass over the unit store (the spatial hash's
  answer, without a query per cell), and units spawned this tick are marked there for later spawns; a plateau found
  with no free cell is remembered for the rest of the tick (no cell frees during production), so further heads waiting
  on it cost nothing. The spatial hash is rebuilt after a tick with a spawn. The unit stands on the cell's center, Idle.
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
  cap); 20 heads waiting on a packed 120 x 24 map cost 0.09 ms a tick (1.7-1.9 ms before the per-plateau memo, M3-H2). CLI: `run --workers` prints `player P gold G wood W pop U/C` (pop in whole units, `.5` for a half).
- **Not yet:** AI build orders (M5), rally on a building, events for views; a spawn waits rather than walking out a
  full plateau's ramp. (Techs, Age II and Forge upgrades: M3-5; `requires`: M3-6; the production card and rally
  visuals: view M3-V3.)

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
- **Unmeetable requirements (M3-H2, BUG-0100).** A `requires` entry naming another faction's building or tech is one
  error at the entry (a building requirement is an *own* finished building, and nobody places or researches another
  faction's); a common tech, which every faction researches, may name only common techs. On otherwise clean data a
  reachability pass then runs per faction: a fixpoint from "nothing built, nothing researched" in which a building of the
  faction, or a common or own tech, becomes reachable once everything its resolved `requires` names is, and (a tech) at
  least `count` of its any-of slots hold a reachable own building. A `requiresAnyOf` some faction can never fill is one
  error at the field, naming the factions. So an any-of whose members need its own tech first (Age II over three halls
  that each need Age II) is caught; cycles through plain `requires` stay BUG-0099's error. Also (BUG-0099 item 2): a tech
  effect whose filters no unit meets (any faction's for a common tech, its own faction's for a faction upgrade) is one
  error at its `appliesTo`, since the tech would be paid for and change nothing. Shipped data and D3's load with 0
  errors.
- **Hash and replays.** No new sim state is hashed: the tech flags and queues were already. The golden was regenerated
  for the data hash only (`age_ii.requiresAnyOf` and the resolved arrays in `ContentHash`); its checkpoints are
  byte-identical. Replays stay format 3.
- **Cost.** Measured (Debug, this PC, `RequirementPerfTests`): the M3-5 research scene with every command through its
  gate (the halls' Trains, the Forges' Research, plus 20 locked Trains, 10 locked Researches and 10 refused Builds a
  tick) averages 0.42 ms a tick over 2,000; 5,000 locked Train / Build / Research commands applying in one tick take
  0.73 ms. The gates and the locked applies allocate nothing (`AllocationTests`).
- **Not yet:** the view's greying and reason text (M3-V3), AI use (M5), combat use of the bonuses (M4). (An any-of
  that its own members require is a load error since M3-H2, above.)


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
    projectiles.json         # projectile types: aimed / lob, speed, hit tolerance (M4-2b)
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
(M3-5), `common/projectiles.json` (M4-2b), and `faction.json` + `units.json` + `buildings.json` (M3-2) + `techs.json`
(M3-5) for `malazan` and `whirlwind`. All thirteen are required. Everything else in the tree above (`statuses`, `abilities`, `ai`, `maps`, other
factions) arrives with the milestone that consumes it.

| File | Shape |
| --- | --- |
| `damage_table.json` | `armorClasses: [{id, displayName}]`, `damageTypes: [{id, displayName, ignoresArmor?, multipliers: {<armorClass>: x}}]`. Every type must list every class. `ignoresArmor` (default false) is how Magic skips armor |
| `resources.json` | `{ "resources": [ ... ] }`, each `{id, displayName, description, resource, footprint {width, height}}`: `resource` is `gold` or `wood`, footprint sides are cells, 1-4 (`DataLimits.MaxFootprint`). Amounts are not here: a tree holds `rules.json` `treeWood`, a mine `startMines.gold` (M3-1, see "Economy implementation") |
| `rules.json` | docs/02 Economy table: `startingGold`, `startingWood`, `startingWorkers`, `popCap`, `workerCarry`, `gatherRate {gold, wood}` (per second), `startMines` / `expansionMines {count, gold}`, `treeWood`, `nodeSearchRadius`. Pop provided by buildings comes with `buildings.json` (M3); the Age II cost with `techs.json` (M3-5) |
| `faction.json` | `id` (must equal the folder name), `displayName`, `description`, `bonus {displayName, description}`, `resources {gold, wood: {displayName}}`, `palette {primary, secondary, accent}` as `#RRGGBB` |
| `buildings.json` | `{ "buildings": [ ... ] }`, each `{id, displayName, description, slot, footprint {width, height}, hp, armor, cost {gold, wood}, buildTime, popProvided, dropOff}`: `slot` is one of the ten template slots (`DataLimits.BuildingSlotIds`), footprint sides 1-4 cells, `buildTime` in seconds (to ticks), `popProvided` a multiple of 0.5 (to half-pop), `dropOff` a required bool, `requires` an optional list of tech / building ids (M3-5; resolved and gating placement since M3-6). Ids unique across factions (M3-2, see "Economy implementation"); exactly one building per slot per faction (M3-6, BUG-0010) |
| `techs.json` | `{ "techs": [ ... ] }` in `common/` (shared by every faction) and in each faction folder (its own), each `{id, displayName, description, researchedAt, cost {gold, wood}, researchTime, requires?, requiresAnyOf?, effects: [{stat, amount, appliesTo {attackType?, tags?, units?, siege?}}]}`: `researchedAt` is a building slot id (a common tech resolves to each faction's building of it; a faction without one is an error), `researchTime` in seconds (to ticks), `requires` tech / building ids (resolved and gating research since M3-6), `requiresAnyOf` `{count, of: [slot ids, or in a faction file its own building ids]}`: met when `count` of the distinct listed slots hold an own finished building (M3-6; shipped on `age_ii` only), `effects` may be empty. `stat` is one of `attack`, `armor`, `range`, `hp`, `abilityCooldown` (`DataLimits.TechStatIds`); `amount` is non-zero, whole for attack / armor / hp, meters for range, seconds for ability cooldown (to ticks, negative shortens it). `appliesTo` is required (`{}` = every unit); each filter it sets must match: `attackType` a damage type, `tags` any one of the unit's tags (each must be a tag some unit has), `units` unit ids (a faction tech's must be its own), `siege` true / false for the `siege` slot; a `tags` or `units` list set to `[]` is an error (M3-6, BUG-0098). Tech ids are unique across all techs files and may not equal a building id (M3-6); `DataLimits.AgeTechIds` (`age_ii`) must be a common tech (M3-5, see "Implementation (M3-5)") |
| `projectiles.json` | `{ "projectiles": [ ... ] }`, each `{id, kind, speed, hitTolerance?}`: `kind` `aimed` or `lob`, `speed` m/s (positive), `hitTolerance` m (aimed only, 0-2, default 0.3; an error on a lob). Ids unique; every unit's `attack.projectile` must name one (M4-2b, see "Implementation (M4-2b)") |
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
`requires` cycle. The gates `CanTrain` / `CanPlace` / `CanResearch` check them (see "Implementation (M3-6)").
`attack.projectile` resolves at load (M4-2b) to `AttackDef.ProjectileTypeId` (one error at the field for an unknown id).
Not resolved yet (kept as a plain string): `model` (M2/M6 asset pipeline). Unit passives, abilities, detection, and faction modifiers (e.g. Whirlwind's gather
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
  `DrawResources(types, version, alive, typeId, cell)` redraws it only when `version` changed. The
  minimap passes the resource store's `FreeCount`, which rises with each node felled or mined out and
  nothing else (nodes spawn only at map load); it used to pass `NavGrid.Version`, which every building
  spawn, site and cancel also bumps (BUG-0107). A redraw clears the pixels it painted last time,
  then paints every footprint cell of each live node (`WoodRgb` 0x1E5A1E dark green, `GoldRgb`
  0xE6B422 gold, by `ResourceKind`). The minimap calls it
  in its 5 Hz refresh before the dots and uploads the layer's texture only when it was redrawn, so
  a felled tree disappears within one refresh. The terrain bake no longer darkens a cell blocked
  only by a resource node (`Resource` set), so the ground under a felled tree has its true colour.
  Cost (Debug, alone on the dev PC, 2026-10-07): 2,000 dots about 0.17 ms (limit 0.25 ms, QA's dot
  budget) and a forced 4,096-node resource redraw about 0.13 ms (limit 0.2 ms), timed apart since
  BUG-0105 (together they were 0.28-0.29 ms against one 0.3 ms limit); the redraw runs only on a
  refresh after a fell. 0 bytes.
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
  while mixing), at most `Sfx.ExitWaitLimitMs` (200 ms); about 20-30 ms headless alone, up to about
  100 ms under suite load (BUG-0105). The window's close request stops the players too. `SfxTest` no longer waits before quitting.
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
  no enemy in the way). How far the centre gets is a property of the seed's terrain: 19.1-26.2 m over
  seeds 1, 6, 7, 21, 23, 31 and 43 (seed 21 least). So the 20 m bound is seed 1's; `QaH2Test` holds
  any other seed to 15 m, which proves the army marches rather than a pace (BUG-0104). Every action goes
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
  site is a dusty mauve (`BuildingViews.SiteColor`, a hue far from both factions' building colours;
  it was a slate lighter than the Malazan grey, BUG-0107) and rises from 15% to full height with its progress. The bar over it
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
  cube floats above it, gold or wood-brown by `CargoKind`; above 30 m zoom the shared cube meshes grow with the zoom
  (0.7 m at 60 m), so the marker keeps its screen size (BUG-0107: it was 3-4 px at 60 m). A slot's marker node is made the first time it carries,
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
  worker's Build: the site box is in the site colour with a progress bar equal to `Work / WorkNeeded` every tick, and gone the
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
  the key bound to its `card_<i>` action as the keyboard labels it) and, for a building, its cost "G / W" bottom right.
  The name is 12 px, word-wrapped onto at most two lines; a name with a word wider than the label shrinks, a pixel at a
  time down to 9 px, until every word fits and it takes two lines at most (`CommandCard.FitNameSize`, measured once per
  name in `Init`; BUG-0122: "Quartermaster's Depot" broke at the apostrophe, now 10 px);
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
  flow-field scratch (M3-3), so it is called with a map anchor only from the ghost's `_Process` (M3-V4's build-menu
  greying asks it at the off-map `BuildMenu.NoAnchor`, which returns before the flood), which runs after `SimRunner`'s tick
  step and after `RtsCamera`'s pan in the same frame (`ProcessPriority` 1, after every default-priority node; in tree
  order it came before the camera and trailed a pan by a frame, BUG-0122), on the main thread, never while
  `SimRunner.Ticking`, at most once per frame, and only when the anchor, the type or the sim tick changed since the last
  answer (nothing else changes it). The box moves only with an answer, so the colour drawn is always `CanPlace` of the
  anchor drawn. Left click with a green ghost: `SelectionController.OrderBuild`: one `Command.Build(player, worker,
  type, anchor point)` per selected live worker (the anchor cell's centre; the first to apply places the site, the rest
  join it), one Command sound; the menu and ghost close, unless Shift is held: then the ghost stays, and each later
  placement of the same ghost is queued (`queued` flag) so the workers build them in turn (the first stays unqueued:
  a queued order behind an endless Gather would never start); a Shift-click on the anchor this ghost placed last is
  skipped (BUG-0122: a flood of clicks queued copies that only joined the same site). A red or off-map click does
  nothing and plays nothing. Meshes (one per building type, on first use) and the two materials are kept; the label
  text is set only when the reason changes. The red is a deep (0.95, 0.02, 0.04) at 62 % (it read orange over grass at
  (1, 0.12, 0.1) and 55 %); the reason label's `PixelSize` is `BuildGhost.ReasonPixelPerZoom` x zoom, so it is about
  25-35 px per em at every zoom (it was about 9 px at 30 m), set only when the zoom changes.
- **Selection rule** (`SelectionController`): a left click that picks no unit selects the own building whose drawn box
  the camera ray meets first (`ViewApi.BuildingPicker.PickRay`: footprint x box height on the terrain at the footprint
  centre, a site only up to its drawn rise; since M3-V3b terrain occludes: a box the ray enters only after meeting the
  ground outside that box's footprint is behind a ridge or cliff and isn't picked) alone: units cleared, targeting ended, Select sound if it changed. Shift or
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
- **Not yet:** production card, queue, rally, population (M3-V3, below); `requires` greying (M3-6); rebinding UI; real art.

### Implementation (M3-V3)

The owner runs a whole base in the window: a selection panel, a production card on a selected finished building (train
and research buttons greyed by the sim's own rules, a queue strip with progress and cancel), a rally marker set by
right-click, and population in the resource bar. Three M3-V2 bugs are folded in (BUG-0108, 0109, 0110).

```
Match.tscn  (new nodes)
  World3D/RallyMarker   Node3D, RallyMarker.cs: cone at the selected building's rally point + line from its edge
  Hud/SelectionPanel    Control, SelectionPanel.cs: bottom centre (right of the minimap), unit stats / portrait grid / building
  Hud/QueueStrip        Control, ProductionQueueStrip.cs: above the command card, up to 5 queue items
  Hud/ResourceBar       (M3-V1) + children Pop ("Pop a / b") and AgeFlash
```

- **`ui.json` additions** (view data; the sim's `DataLoader` still never reads it): `train` and `research` sections keyed by
  the snake_case name of every `TrainError` / `ResearchError` member but `None` (`no_building`, `queue_full`,
  `cannot_afford`, `already_queued`, `locked_by_requirement` ...), `states` keyed by every `UnitState` (`idle`, `moving`,
  `gathering`, `returning`, `building`; plus `attacking`, shipped ahead of M4-1's `UnitState.Attacking` so the merged
  view boots, an extra key until then: BUG-0136 / BUG-0147), `hud` keyed by the view's `HudText` enum (`pop`, `hp`, `attack`, `armor`, `range`,
  `speed`, `needs`). A member without its key is one named load error, as before. Each reason section (`placement`,
  `train`, `research`) must also carry `requires` (`UiText.ForwardKey`), the reason M3-6's gating adds: a key with no enum
  member yet is accepted (any extra key is), so the file loads against `main` with or without M3-6, and once the sim has
  the member it is read by the same loop (never reported twice). **BUG-0110:** a root that is not an object (`[]`,
  `null`, a number, a string) is now one error ("ui.json: the root must be an object"), not a `UiText` with blank texts.
  With any error the match logs them and hides the card and the panel.
- **Selection panel** (`SelectionPanel`, docs/02 "HUD layout"; a translucent backdrop, hidden with nothing selected).
  One unit: a placeholder portrait (a square in the type's colour, `SelectionPanel.TypeColor`: hue = type id x 0.618,
  with the `displayName`'s initial), the `displayName`, and rows HP / Attack / Armor / Range / Speed (labels `hud.*`):
  values from the `UnitDef` (attack value, flat armor, attack range in meters, speed in m/s = `SpeedPerTick` x 20), and
  `World.TechBonus(player, type, stat)` for hp, attack, armor and range as a green "+N" beside the value, hidden at 0.
  **The bonus is shown only: combat does not apply it until M4.** Units have no hit points in the sim before combat, so
  hp reads max / max (with the hp bonus beside it). The state line is `states.<state>`. Several units: up to
  `PortraitGrid.MaxPortraits` = 24 portraits (8 x 3, 40 px) in selection order, coloured by type with the initial, a "+N"
  label for the rest; the cells of the active Tab subgroup's type are outlined; a click on one is
  `SelectionController.SelectOnly` (that unit alone, Select sound). A building: its `displayName` and hp now / max. Hp
  is two labels, "now" (right-aligned) and "/ max": hp changes every tick under repair, so "now" comes from a table of
  int strings up to the largest hp in the data, built once in `Init`, and a repaired building's panel allocates nothing
  (BUG-0123; it was one small string per tick). Every node is made in `_Ready`; a text is rebuilt only when its value
  changes (`Rebuilds`).
- **Production card** (`CommandCard`, mode `Production` while an own **finished** building is selected; a site keeps its
  Cancel). Entries from the pure `ViewApi.ProductionMenu.Entries(data, buildingType, span)`: `UnitsTrainedAt(type)` in
  order, then `TechsResearchableAt(type)` with the common techs first and the faction's own after them (an Armory: Armor,
  Armor II, Melee Weapons, Melee Weapons II, Ranged Weapons, Ranged Weapons II, then Moranth Supply; a Town Hall:
  Laborer, then Age II). They take cells 0, 1, 2 ... with the cell's grid key as hint, the `displayName`, the cost
  "G / W" bottom right and a tooltip of `description`, "<gold name> G  <wood name> W" and, when the def has `requires`,
  "<hud.needs> A, B" with the ids mapped to display names (`ProductionMenu.RequirementName`). Every string is built in
  `Init`. Each frame the card asks `World.CanTrain` / `CanResearch` for every button (at most 15 allocation-free calls)
  and, only when a button's reason changed, sets `Disabled`, replaces the cost line with `train.<reason>` /
  `research.<reason>` in red (back to the cost when it clears) and dims the name and hotkey labels to
  `CommandCard.DimAlpha` (50 %) through `Modulate` (BUG-0123: Godot's disabled style doesn't reach child labels, so a
  greyed button looked enabled); the reason line stays at full strength. A bare Town Hall's Age II reads
  `research.requires` ("Locked") until two finished halls of distinct slots stand. A press (button, its grid key, or `Press`) is
  `SelectionController.Produce`: it asks the same rule again, then enqueues one `Command.Train` / `Command.Research` at the
  footprint centre with one Command sound; a refused press enqueues nothing and plays nothing. A production card owns the
  whole grid (docs/02 "Grid hotkeys"): an empty cell's key does nothing.
- **Queue strip** (`ProductionQueueStrip`): the selected own finished building's queue, up to `QueueStrip.MaxItems` = 5
  items (64 x 44 px, 4 px gaps), head first: a unit is a square in its type's colour with its initial, a tech a slate
  square with its `displayName`. The head carries a yellow bar of `QueueStrip.HeadFill` = `Progress / ItemTicks(slot, 0)`
  clamped to [0, 1]. A click on item k is `SelectionController.CancelQueueItem(k)`: `Command.CancelTrain(player, footprint
  centre, k)`, a full refund, one Command sound. Items are rewritten only when their type changes, the bar only when the
  fill does.
- **Age flash:** when `World.Age(player)` rises, the resource bar's `AgeFlash` label shows the age tech's `displayName`
  (`GameData.AgeTechs[age - 2]`, "Age II") for 4 s of view time, pulsing.
- **Population** (`ResourceBar`): a `Pop` child label right of "Gold N  Wood N": "<hud.pop> a / b" from `World.HalfPop` /
  `HalfPopCap` through `ViewApi.PopText.Format` (half-pop / 2, ".5" for odd: 11 -> "5.5"), red when
  `PopText.AtCap` (`HalfPop >= HalfPopCap`). Rebuilt only when one of the two numbers changes (`PopBuilds`). The match
  start reads "Pop 5 / 10" (5 workers, one Town Hall).
- **Rally** (`SelectionController.RallyOrder`, `RallyMarker`): with an own finished building selected, a right click on
  the 3D view (`CommandAt`) enqueues one `Command.SetRally(player, buildingStore.Cell[slot], point)` at the clicked point
  (ground, a resource node, another building's centre), or one `Command.ClearRally(player, footprint centre)` when the click
  hits the selected building itself; a selected site gets nothing. The minimap's right click with a building selected is
  `RallyOrder` at the map point (otherwise a Move, as before). One Command sound. The marker is shown while the selected
  own finished building `HasRally`: a 1.6 m yellow cone at `RallyPosition` on the terrain and a thin box line from
  `ViewApi.RallyGeometry.EdgePoint` (where the centre-to-target segment leaves the footprint) to the point, absent when the
  point is inside the footprint; nodes move only when the building or the point changes.
- **BUG-0108** (right click on a box top): `SelectionController.ContextTarget(screen)` first ray-picks the drawn building
  boxes of **any** owner (`BuildingPicker.PickRay`, owner -1) and uses the hit building's footprint centre as the
  context point; only without a hit does it ground-pick. So the visible top of a damaged own building means Repair, an own
  site join, and an enemy or full-hp building a Move to its centre, instead of whatever lies on the ground 2 m behind the
  box. Since M3-V3b resource nodes are ray-picked the same way: `ResourcePicker.PickRay(grid, defs, alive, typeId, cell,
  map, origin, direction, ..., out entry)` treated each live node as a box over its footprint up to its kind's prop
  height (since M3-V4 it tests the drawn trunk and cone or the mine's two blocks, `PropsView.Shape`, BUG-0125: see
  "Implementation (M3-V4)"), with the same terrain occlusion; `BuildingPicker.PickRay` has an overload with the same
  `out entry` (the ray parameter of the hit). Whichever
  of the building and the node the ray enters first wins; a node gives its footprint centre, so a right click on a
  tree's canopy, whose ground point is behind the tree, gathers that tree.
- **BUG-0109** (the placement click): `CommandCard.GhostClick(shift, screen)` takes the click's own position;
  `BuildGhost.ResolveClick` recomputes the anchor there. The drawn anchor uses the drawn answer; another anchor is asked of
  `CanPlace` as that frame's one call (it then draws that answer, so the next `Sync` asks nothing), unless the frame's call
  is already spent, a tick is running or this is not the main thread: then the click is ignored (`ClicksIgnored`). So a
  Build always goes under the click, never to a stale anchor, and `CanPlace` stays at most once per frame.
- **ViewApi additions** (read-only, allocation-free, no `World`): `PopText` (`Format`, `AtCap`), `QueueStrip` (`Count`,
  `HeadFill`, `ItemX`, `Width`, `ItemAt`, `MaxItems`), `RallyGeometry` (`EdgePoint`, `Line`), `ProductionMenu`
  (`Entries`, `RequirementName`) with `ProductionEntry`, `PortraitGrid` (`Shown`, `Overflow`). `PopText.Format` and
  `RequirementName` allocate their string (called on change / at init only).
- **Cost.** 300 idle frames with the panel, a production card, a 2-item queue strip, a rally flag and the bar allocate 0
  bytes and rewrite nothing (also with one unit and with the 26-unit grid). `--bench 10 --vsync off` (dev PC, default
  window, Debug): avg 0.57 ms, p99 0.95 / 1.06 ms, worst 1.39 / 1.97 ms, avg tick 0.21 ms over two runs (the panel
  shows the bench's selections; the M3-V2 figure, measured under another track's test load, was 0.80 ms).
- **Tests.** xUnit (`Rts.Sim.Tests/ViewApi`): `ProductionHudTests` (pop text for 0, odd, even, negative and extreme
  half-pop, `AtCap`; strip layout round trip, gaps and bounds; `Count` / `HeadFill` against `Progress / ItemTicks` every
  tick of a `[laborer, age_ii, laborer]` run to Age II; edge point on all four sides, a corner, inside, NaN and a 360
  degree sweep on the boundary and the line; production entries of the Town Hall, the Barracks, both Forges and every
  building (units before techs, nothing dropped, truncation); requirement names; portrait grid; 0 bytes) and
  `ProductionHudHashTwinTests` (400 ticks of trains, Age II research, a cancel, two SetRallys and a ClearRally, calling every
  new read, `CanTrain` / `CanResearch` for every card entry of every building, `TechBonus` for every type and stat, `Age`
  and the any-owner box pick each tick: the hash equals a bare twin's every tick). Headless scene
  `res://tests/ProductionHudTest.tscn` ("PRODUCTION HUD TEST PASS"): `ui.json` rows (every new key present; a missing
  `train.queue_full`, `research.already_queued`, `placement.requires`, `research.requires`, `states.idle`, `hud.pop` each
  one named error; an extra key accepted; `states.attacking` shipped, and removing it accepted until the sim has
  `UnitState.Attacking`, one named error after; five non-object roots one error each: BUG-0110); "Pop 5 / 10" at the start, red
  "Pop 10 / 10" at the cap, "Pop 5.5 / 10" after a pop-0.5 unit (a data copy); one laborer's name and stats from data, a
  "+1" attack after Melee Weapons, its state "Gathering"; 26 units: 24 cells in selection order and colours, "+2", the
  outline following Tab three times, a portrait click selecting that unit alone with a Select sound; the Barracks, Town
  Hall, Armory and Engineers' Yard cards in order with names, hints and tooltips (needs); 200 random states (money, queue
  ops, cancels, five buildings) with every button's reason, `Disabled` and line equal to `CanTrain` / `CanResearch`
  (791 cells; None, CannotAfford, QueueFull, LockedByRequirement, AlreadyResearched, AlreadyQueued all seen); button and
  key presses enqueue one command at the footprint centre with one sound, a greyed key, signal or `Press` nothing and no
  sound; the strip equal to the queue every frame through `[laborer, age_ii, laborer]` (839 frames, the Age II flash
  with the tech's name), item clicks sending `CancelTrain(2)` then `(0)`; rally: right-click ground = one SetRally at the
  picked point and the cone there within a frame, on a mine the same, on the hall one ClearRally and the flag gone, the
  minimap one SetRally, a selected site nothing; BUG-0108 (the far edge of a damaged Barracks' top: 3 Repairs at its
  centre); BUG-0109 (a click on the red hall while a green ghost is drawn elsewhere: nothing, one CanPlace; a click on
  another green anchor: 3 Builds there; a second differing click in the same frame ignored with no CanPlace); 300 idle
  frames 0 bytes. `QaV2Test`'s three `Known` rows are plain checks now; `QaH1Test`'s fuzz expects a rally order (not a
  Move) for a right click while a building is selected; `CommandCardTest`'s hall card expects the production entries.
  Windowed with `-- --shots <dir>` it saves the one-unit panel, the mixed grid, the hall card with its queue and the rally
  flag.
- **Not yet:** fog (M4); real portraits; rebinding; `requires` greying beyond the text keys (it shows by itself once M3-6's
  reasons reach `main`); rally on a building (units walk to its centre); unit hit points in the panel (M4).

### Implementation (M3-V4)

M3's last criterion ("Playable: the owner builds a full Malazan base and reaches Age II") proven by a script that plays
the owner's playtest through the real HUD, plus BUG-0125 (the node pick) and BUG-0126 items 1-2 (the wording of locked
and researched buttons).

- **The Playable scene** (`res://tests/M3PlayableTest.tscn`, "M3 PLAYABLE TEST PASS" / "M3 PLAYABLE TEST FAIL <step>
  (seed, tick): <what>"): a real `Match` (`--units 0 --mute --speed 8`, shipped data) on seeds 1 and 6 (`-- --seed N` for
  one), the sim ticked by its own `SimRunner` from frame time at 8x. Every action is input pushed into the viewport
  (`Viewport.PushInput`): left / right clicks and box drags on the 3D view, grid keys, clicks on the queue strip, and
  left clicks on the minimap to move the camera; no sim command and no controller call. Headless Godot has no pointer,
  so the viewport's mouse position doesn't follow a pushed motion event; the scene then gives the ghost the same point
  through `BuildGhost.ScreenOverride` (said in the log). Each step is checked against the sim once the next ticks have
  run and the views have drawn them, and prints the tick it completed with the money and a census of the workers. The
  steps: box-select the five workers; right click the nearest mine (each worker's gather node is the mine, all five
  seen `Gathering`); click the Town Hall (panel name and "hp / max"; card: Laborer on Q, Age II on W greyed "Locked",
  the sim saying `Requires`); Q three times (three queued, 150 gold paid, three squares, a head bar); click the third
  square (two left, 50 gold back); right click a tree with the hall selected (rally at the tree's centre, the flag
  shown; two more laborers queued; a laborer born after the rally stands `Gathering` wood); one start worker (the
  builder, clicked alone; the other four keep mining) builds with B then E (Legion Barracks), B, W (a Quartermaster's
  Depot by the forest, and one by the mine when the mine is over 35 m from the hall: seed 6), B, A (Armory), B, Q
  (Billet: the bar's cap 10 -> 18; two more laborers), each on a spot the test's `CanPlace` accepts and where the camera
  ray at its pixel maps back to it (not hidden behind a rise), the ghost green there, built, and the builder
  right-clicked back to the mine; at the hall Age II is live and W researches it; meanwhile V shows the Cadre Tower and
  the Engineers' Yard greyed "Locked", W raises the Yard's ghost red "Locked" and a click on it sends nothing, Esc
  closes; Age 2 and the "Age II" flash; at the Armory Melee Weapons (on E: the card lists the common upgrades in id
  order, Armor Q, Armor II W, Melee Weapons E, Melee Weapons II R) is live and Melee Weapons II "Locked", E researches
  it; V, W places the Engineers' Yard (now live) and it is built; a Heavy Infantry from the Barracks and a Sapper from
  the Yard (their grid keys); the Heavy Infantry clicked: its panel's attack reads the green "+1" (`TechBonus` 1; the label's `font_color` is checked green: BUG-0148 item 2). What a
  player would also do: while waiting for money, a laborer standing Idle (its rally tree was felled before it was born:
  a rally onto a node's cell is a Gather only while the node lives, M3-4) and off its gather loop (its `GatherNode` not
  a live node: BUG-0145, a miner on its loop reads Idle for single ticks between legs and must not be pulled) is clicked and right-clicked onto the nearest
  tree, and the selection clicked back; a site whose work stands still for 800 ticks gets another worker (the laborer
  nearest the site, clicked and right-clicked onto it: a joining Build), which builds from then on. That last one is a
  workaround for a sim movement deadlock seen in about one two-seed run in five (seed 6: the builder, leaving the mine,
  wedges against two gatherers standing in the two-cell corridor between the mine's Depot and a cliff, its velocity
  flipping each tick); the scene logs it and saves the replay (`m3playable-seed<N>-stall-tick<T>.replay` in Godot's user
  data folder) for the sim track. Money comes only from gathering (no dev spawns: the sim has no
  dev resource command, and none was needed). **Tick budget** 16,000 per seed (`M3PlayableTest.TickBudget`, 13 min 20 s
  of game time); after the BUG-0145 fix, ten runs (five while the full sim suite ran): seed 1 11,871-11,997, seed 6
  11,069-11,528, two re-tasks a seed every run, so at least 4,000 ticks (25 %) of margin; about two and a half minutes
  of wall time for both. (Before the fix seed 1 swung 11,871-16,000+, because the re-task caught gold miners between
  legs.)
  The commands land on whichever tick the frame clock reaches, so two runs differ by a few hundred ticks. On any failure
  the replay so far is saved the same way (`-fail-`, or `-exception-` for an exception that is not a step's check:
  BUG-0148 item 4), a repro for `Rts.Cli play`. **Hash twin:**
  `SimRunner.RecordCheckpointInterval` (0 by default) attaches a `ReplayRecorder` to the new sim before the match
  enqueues anything; at the end the scene replays the recorded command stream into a bare sim (`ReplayPlayer.Run`) with
  a checkpoint every tick: every hash equal (seed 1: 45 commands, 11,937 checkpoints). `-- --break <n>` fails step n on
  purpose, to show a failure names its step.
- **Node pick = the drawn shape** (BUG-0125, `ResourcePicker.PickRay(..., in PropShape shape, out entry)`): M3-V3b's pick
  treated a node as the box over its footprint up to the prop's height, so open ground up to about 2.5 m north of a tree
  or mine took the right click (339 of 3,000 QA rays). The caller now passes `PropsView.Shape` (a `ViewApi.PropShape`:
  trunk height and radius, tree height, canopy fill, mine and gold heights, gold fill) and the pick tests what
  `PropsView` draws: a tree is a vertical cylinder of the trunk radius up to the trunk height plus a cone from the
  canopy radius (`CanopyFill` x half the footprint's smaller side) at the trunk's top to a point at the tree height,
  each solved as a quadratic in the ray parameter (below the tip only the cone's lower nappe, a convex solid, so the
  entry is the slab's start or a root); a mine is its footprint block up to `MineHeight` plus the centred `GoldFill`
  block `GoldHeight` tall (two slab tests). Both stand on the terrain at the footprint centre, as drawn; terrain occludes
  as before (ground met inside the node's own footprint doesn't hide it). The circles contain the 7- and 6-sided meshes
  (a facet's middle is at most 8 cm inside). A tree first gets one box test round its drawn shape. Allocation-free.
- **Locked build-menu entries** (BUG-0126 item 1): `ui.json` `placement.requires` is "Locked" (was "Needs more", which
  read like money). While a B / V menu is open the card asks `World.CanPlace(player, type, BuildMenu.NoAnchor = -1)` per
  entry each frame: the sim checks the type, the faction and `requires` before any map rule (M3-6), so an off-map anchor
  answers `Requires` exactly when the building is locked and `OffMap` otherwise, without the never-seal flood or its
  scratch (so this call is outside the ghost's once-a-frame rule, which exists for the flood).
  `BuildMenu.ShownPlaceReason` maps `OffMap` to live. A locked entry gets the production card's look (the reason line in
  red instead of the cost, name and hotkey at `DimAlpha`) but **stays pressable** (Producer default for the owner's
  script: V, W before Age II raises the ghost red "Locked", so the player sees why); a production button is `Disabled`
  because its press would do nothing. `ReasonAt` reports a Place cell's shown `PlacementError` too.
- **Researched / queued over Locked** (BUG-0126 item 2): a research button shows
  `ProductionMenu.ShownResearchReason(simReason, HasTech, IsTechQueued)`: `AlreadyResearched` ("Researched") when the
  player has the tech, else `AlreadyQueued` ("In a queue") when one of its live buildings has it queued
  (`ProductionMenu.IsTechQueued(buildings, player, tech)`, a pass over the queues), else the sim's own first reason. The
  sim answers `Requires` first (M3-6), so a queued or researched Age II read "Locked" after a hall was lost; now it reads
  "In a queue" / "Researched". The button is greyed either way and a press still asks the sim (`Produce`).
- **ViewApi additions** (read-only, allocation-free, no `World`): `PropShape`; `ResourcePicker.PickRay`'s shape overload
  (replaces the two heights); `BuildMenu.NoAnchor`, `ShownPlaceReason`; `ProductionMenu.ShownResearchReason`,
  `IsTechQueued`. Game side: `PropsView.Shape`, `SimRunner.RecordCheckpointInterval` / `Recorder`, `CommandCard`'s
  `GreyPlaces`.
- **Tests.** xUnit: `ResourcePickerTests.PickRay_SeesTheDrawnProp_...` (a camera ray to open ground 2.5 m north of a tree
  that crosses the old column misses; on the axis it hits; beside the trunk under the canopy misses; the cone's slope at
  2 m, 0.45 m in hits and 0.52 m misses; a mine above its block outside the gold block misses); `PickRayQaTests` against
  a sampled reference of the drawn solids (2 mm steps, a ray within 1 cm of a surface skipped as grazing): the brute-force
  march equality on seeds 1 and 21 (0 wrong of about 1,470 rays each), every tree pick touches the drawn tree (0 of
  1,048; the column missed 77.6 %), and the right-click intent: open ground taken for a node 0, a drawn node taken for
  open ground 0, wrong 0 of 1,239 rays with a node involved (the column was wrong in 68.1 %, the ground-point pick of
  M3-V3 in 27.1 %); `ProductionHudTests`: the precedence for every `ResearchError`, Age II queued then the Armory
  destroyed (the sim `Requires` and the button `AlreadyQueued` for 1,198 ticks; `IsTechQueued` equal to the store's own
  rule for every tech and player every tick; researched: `AlreadyResearched`), every building type for both players at
  `NoAnchor` against an oracle at the start, with a Barracks and after Age II (Cadre Tower and Engineers' Yard `Requires`
  until Age II, the Wickan Corral until a Barracks), 0 bytes. Scenes: `QaV3bTest`'s NOTE ("Locked" 10 of 10 frames
  after a hall is lost) is a check now ("Researched" 10 of 10, "In a queue" every frame while queued);
  `ProductionHudTest`, `QaV3Test` and `QaV3bTest` compare research cells with the precedence applied.
- **Not yet:** BUG-0126 items 3-6; M4 views (hp bars, deaths).

### Implementation (M4-V1)

The view half of M4 criterion 4: the owner sees the fighting M4-1 put in the rules. Hp bars over hurt units, a hit flash,
corpses and rubble where things died, kill / loss counts, and live hp in the selection panel. First, BUG-0147: the M2
scenes whose start armies now fight run with combat off.

```
Match.tscn  (new node)
  World3D/CombatViews   Node3D, CombatViews.cs: hp bar MultiMeshes (back, fill) + corpse and rubble MultiMeshes
```

- **BUG-0147** (`--no-combat`, see "Debug tooling"): DebugOverlay, Orders, QaH1, QaH2 and QaM27 pass `--no-combat` on the
  matches whose armies used to stand or march through each other (their hash twins copy the match's `SimConfig`, so the
  twin is off too). No expectation changed. QaM24 passes without it.
- **Hp bars** (`CombatViews`, pure `ViewApi.UnitHpBars`): each frame `UnitHpBars.Collect(Alive, TypeId, Hp, defs, slots)` lists
  the live units below their type's `hp` (as the building bars, M3-V1; hp tech bonuses are not applied by combat yet, so
  the type's `hp` is the maximum), in slot order. Each gets a dark back and a fill of `Hp / max` over the body
  (`UnitViews.BodyHeight` + 0.35 m) at its interpolated position, coloured green (1) -> yellow (0.5) -> red (0)
  (`UnitHpBars.Color`, linear between). Two `MultiMesh`es with one instance per bar, written densely with
  `VisibleInstanceCount` = the count, so a unit that dies or heals simply isn't listed the next frame. 1.2 m x 0.16 m at
  zoom up to 30 m, then grown with the zoom like the cargo cube (M3-V1). The colour written per instance is kept too, since
  the headless dummy renderer keeps no instance data to read back.
- **Hit flash** (`UnitViews`, pure `ViewApi.HitFlash`): no sim event. Each `UnitViews.Sync` compares every slot's `Hp`
  and `Generation` with what the last call saw: the same unit with less hp is lit for `HitFlash.DefaultSeconds` (0.15 s of
  view time, `_Process`'s delta), and a later hit starts the time again, so a unit under repeated hits stays lit. A new
  unit in the slot, a dead slot or a heal never lights. While lit, the body's overlay is a translucent white
  (`UnitViews.FlashMaterial`) in place of the worker tint. `Sync(world, alpha)` without a delta still works (the flash
  doesn't fade), so older scenes are unchanged.
- **Deaths, corpses and rubble** (`CombatViews`, pure `ViewApi.DeathMarkers`). A dead unit's or building's view goes the
  first frame after its death (the slot reads free, as before). `World.Deaths` holds one tick's events and the next tick
  empties it, and at fast speeds one frame runs several ticks, so the deaths are taken in two places: `SimRunner.Ticked`
  (a new C# event raised after each tick it runs) and the frame loop (for scenes that tick the sim themselves).
  `DeathMarkers.Collect(deaths, tick, added)` takes a tick's deaths once, whichever comes first. Each `DeathEvent` adds a
  marker at its position on the terrain: a unit leaves a flat disc of its radius, 0.08 m high, in its owner's colour at
  35 % (unshaded), for `DeathMarkers.UnitLifetimeTicks` (200 ticks = 10 s of game time, docs/02 "Death"); a building
  leaves a 0.6 m grey box over 95 % of its footprint for `BuildingLifetimeTicks` (400 = 20 s). Lifetimes count sim ticks,
  so game speed scales them and a paused game keeps them. The pool is a fixed ring of `DeathMarkers.DefaultCapacity`
  (2,000, Producer default): when it is full the next death replaces the oldest marker. A marker is a position, what it
  was, its owner and an expiry tick; nothing else. Two `MultiMesh`es (corpses with per-instance colour, rubble) hold one
  instance per pool slot; an unused instance has a zero transform. Transforms are written only when a marker is added or
  expires, so a steady frame writes none. `CombatViews.AddDeaths` feeds a synthetic list (the death-storm test).
- **Kills and losses**: `ResourceBar.KillsLabel`, a child under "Pop", shows "<hud.kills> n / <hud.losses> n" (`ui.json`
  `hud.kills` = "K", `hud.losses` = "L"; new `HudText` members) from `World.Kills` / `Losses` of the local player, rebuilt
  only when a number changes. The F12 overlay's second line adds "p0 K n / L n  p1 K n / L n" with the same labels.
- **Selection panel**: one unit's hp is live, the store's `Hp` over the type's `hp`, through the existing int-string table
  (BUG-0123), so a unit losing hp every few ticks allocates nothing.
- **ViewApi additions** (read-only, allocation-free after construction, no `World`): `UnitHpBars` (`Shows`, `Color`,
  `Collect`), `HitFlash` (per-slot last hp, generation and timer; `Update`, `IsLit`, `WasHit`, `Left`), `DeathMarkers` (the ring:
  `Add`, `Collect`, `Expire`, spans of the marker fields). `HitFlash` and `DeathMarkers` hold view state only.
- **Cost.** Measured in `CombatViewTest`: 300 frames of a 20 v 20 brawl after warm-up (combat views, unit views with the
  flash, the panel with a hurt unit) 0 bytes; 120 frames after a 2,500-death storm 0 bytes.
- **Tests.** xUnit (`Rts.Sim.Tests/ViewApi/CombatViewsTests`, scene in `CombatViewScene`: 20 Heavy Infantry v 20 Raiders on
  a flat map, a Billet and a Tent behind the lines, all attack-moved to the far building, so the winners knock the other
  building down): the bar rule's edge rows (full, above max, 0, negative, max 0), the colour ends and its monotony, and
  `Collect` equal to a brute-force list every tick of the brawl; the flash on a hit, fading after 0.15 s, restarting on
  a repeat hit, never on a heal, a new unit or a dead slot, NaN / negative time, and every tick of the brawl (hit exactly
  the units whose hp fell, lit exactly for the hit frame and the two after at 0.06 s a frame); the markers' 200 / 400 tick
  lifetimes, `Collect` once per tick, six 500-death storms (cap 2,000, 1,000 replaced, the newest kept), and every
  death of the brawl giving one marker at its position (rubble for the one building) that expires on its tick; the hash
  twin (4,000 ticks of every new read: equal to a bare twin every tick); 0 bytes for bars + flash + a 500-death collect at
  2,000 units. Headless scene `res://tests/CombatViewTest.tscn` ("COMBAT VIEW TEST PASS", seeds 1 and 6, `-- --seed N` for
  one): `--no-combat` parsing and the sim's switch; per seed a real Match (`--units 0 --no-bases --debug-overlay`)
  with 20 v 20 and a building each beside the central cell, attack-moved through commands; after each tick the unit
  views are synced three times at a fixed 0.04 s and every unit's flash and overlay checked against the test's own hp
  oracle; after two frames: every death's view hidden, a marker at the death position (corpse / rubble kind, drawn on
  the terrain), the marker count equal to the oracle's live deaths and every unused instance drawing nothing, a bar for
  exactly each hurt live unit with its fill and colour, the bar's and overlay's counts and texts equal to
  `World.Kills` / `Losses`, the selected unit's panel hp equal to the store; 300 steady brawl frames 0 bytes; a 5 x 500
  death storm (cap kept, 500 replaced, 0 bytes for 120 frames after, all gone after 20 s); the hash twin (seed 1: 82
  commands, 5,596 checkpoints equal). Then a run at 8x from frame time (up to 5 ticks a frame): one marker per death.
  `ProductionHudTest` extends BUG-0123's row: a Heavy Infantry selected while a Raider fights it, 300 ticks, the panel
  live and 0 bytes. `EconomyViewTest`'s per-frame worker check now expects the flash overlay on a lit unit and the tint
  otherwise (its armies fight since M4-1). Windowed with `-- --shots <dir>` it saves the brawl, corpses and rubble.
- **Layout:** the age flash (M3-V3) moved down one line, below the kills / losses label.
- **Not yet:** the Attack-target cursor and F key (after M4-2a), projectile visuals (M4-2b), the fog shader (M4-3), death
  animations, real corpse models and hit / death sounds (M6).

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
  - Header: format version (`Replay.CurrentFormatVersion` = 4 since M4-2a, which added the `combat` line and an
    attack target on every command line; 3 (M3-1) still reads and plays, `Replay.OldestFormatVersion`; 2 was M1-7,
    without the resource lines, 1 was M1-6, without command flags; both are refused with `FormatVersionMismatch`),
    `SimInfo.Version` (informational, not checked on playback), data hash (`GameData.ContentHash()`),
    seed, player count, unit, command and (M3-1) resource capacity (they decide outcomes: the unit and
    resource capacities are in the state hash and a full store drops spawns), checkpoint interval,
    tick count, (format 4) `combat 0 / 1` (`SimConfig.Combat` when recorded, `Replay.Combat`; a format 3 file has no
    such line and was recorded with combat on), and every `MapGenParams` field (M3-1 added `map.forests`,
    `map.forest-min-trees`, `map.forest-max-trees`, `map.gold-mines`, `map.mine-spacing`). There is no map id yet:
    the map, resource nodes included, is rebuilt from seed + params.
  - Command log: every command `Simulation.Enqueue` accepted, as stamped (tick, player, sequence,
    kind, type id, position, unit handle, (format 4) attack target handle and building flag, flags), in enqueue order. Commands stamped for a tick after the
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
  line per header field in a fixed order (a replay is written in its own `FormatVersion`, so a format 3 file read
  back writes back byte for byte; the recorder makes format 4), `commands N` then N lines
  `c tick player sequence kind typeId x y unitIndex unitGeneration targetIndex targetGeneration targetIsBuilding flags`
  (13 fields since format 4, the three target fields `0 0 0` on every kind but `Attack`, flags kept last; format 3
  lines have 10, without the target; format 2 added `flags`), `checkpoints N` then N lines
  `k tick hash`, `end`, and last `checksum H`: FNV-1a 64 over every byte before that line, so any
  changed byte is caught. Integers are invariant-culture decimal in canonical form (no `+`, no
  leading zeros); hashes are 16 uppercase hex digits; floats are their exact IEEE-754 bit pattern
  as 8 uppercase hex digits (`0.5` is `3F000000`, NaN round-trips). `TryRead` never throws on bad
  bytes: it returns a `ReplayError` code and no replay. Read rules (`Replay.Validate`): command
  ticks run from 1 to the tick count without going backwards, each player's sequences count 0, 1,
  2, ..., players are below the player count, every command is well formed (`Command.IsWellFormed()`:
  a known kind, no flag bit but `Command.QueuedFlag` (M1-7), and that one only on a unit order
  (M1-9), and an attack target only on an `Attack` (M4-2a), the rule `Enqueue` applies; a format 3 replay can
  hold no `Attack` and no combat-off header), no tick has more commands than the
  command capacity, and the checkpoints are exactly the interval's multiples. Format limits: at
  most 16 players, capacities up to 1,000,000, and (M1-4d-3, BUG-0040) a tick count and checkpoint
  interval of at most 1,728,000 (24 h at 20 Hz), so a small file can't declare years of playback.
  The interval may exceed the tick count: the recorder writes replays shorter than one interval.
- **Playback** (`ReplayPlayer.Run(replay, data)`): refuses before any tick with
  `FormatVersionMismatch`, another validation code, or `DataMismatch` when the data hash differs.
  Otherwise it builds the `SimConfig` (since format 4 with the header's `combat`; `Run(replay, data, combat)` overrides
  it, for format 3 test replays recorded with combat off, which can't say so), enqueues each command when `TickNumber == command.Tick - 1`
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
  1,500 ticks, checkpoints every 100 (about 16 KB). Regenerated in M3-1 (format 3, the
  resource store and grid version in the hash, `resources.json` in the data hash; its commands and
  unit trajectories are unchanged); last in M4-2a (format 4: the `combat` line and 13-field command lines; the data
  hash for the ram's `attack.targets`; every `k` line identical). `ReplayGoldenTests` plays it back and fails on
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
  ticks, a full GC, 300 timed ticks) must average at most 4.6 ms (the M1-4d-3 target was 4.5 ms; widened in M4-1 for
  the combat checks' +0.08 ms, BUG-0140, see "Implementation (M4-1)"). Two report rows
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
- **No-combat flag** (M4-V1, BUG-0147): `& $env:GODOT --path game -- --no-combat` (takes no value) starts the match with
  `SimConfig.Combat = false` (`LaunchOptions.NoCombat` -> `SimRunner.Combat`): units never scan, swing or die and enemies
  are only walls, as before M4. A **dev and test flag, never a game option**: it exists for the M2 scenes whose assertions
  are about a world without fights (marches across the enemy block, selections that must not lose units, overlay goals);
  they pass it instead of changing an expectation. The start-up line ends with ", combat off". `--no-bases` (M3-V1) is
  the other half of the armies-only M2 setup. A replay recorded from such a match records `combat 0` in its header
  (format 4, M4-2a), so `ReplayPlayer.Run(replay, data)` plays it back off by itself; the `combat:` override parameter
  stays for format-3 files recorded with combat off.
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
