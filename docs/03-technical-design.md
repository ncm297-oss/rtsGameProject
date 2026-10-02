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

1. **Apply commands** queued for this tick, sorted by (player, sequence number).
2. **AI think:** each AI player runs on its own cadence (default every 10 ticks, staggered by
   player index) and enqueues commands for the *next* tick, the same as a human.
3. **Production:** training and research timers, spawning finished units at rally points.
4. **Construction and economy:** building progress, gathering, drop-offs.
5. **Status effects and zones:** expire, tick DoTs and regen, apply zone effects.
6. **Abilities:** cast timers, effect resolution.
7. **Orders and targeting:** order queue advance, target acquisition (staggered scans).
8. **Pathfinding requests:** build or fetch flow fields for new move targets.
9. **Movement:** flow-field direction + steering + collision against the nav grid.
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

The sim runs on the main thread in v1. If profiling demands it later, move `Tick()` to a worker
thread with double-buffered snapshots. The sim's design (no Godot calls, explicit snapshot
output) already allows this.

## Determinism

Goal: the same seed and the same command list produce the same state hash, on the same machine,
every run. Cross-machine lockstep (fixed-point math) is **not** a goal, since there's no multiplayer.

Rules:

- One seeded RNG (`SimRng`, xorshift/PCG) owned by `World`, with separate streams for map gen,
  combat, and each AI player, so adding a random call in one system doesn't shift the others.
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

### Spatial hash

A uniform grid of 4 m buckets (2×2 cells), rebuilt every tick with a counting sort into flat
arrays (no allocation). Queries: units within radius, nearest enemy within radius, units in a
rectangle (box select support for tests/AI). Buildings are also indexed in the nav grid.

## Pathfinding

### Navigation grid

128 × 128 cells, 2 m each. Per cell: passability flags (terrain slope, building footprint, tree,
water), a movement cost byte (1 normal, 255 blocked), and an elevation `Level` (0-2) used by the
high-ground vision rule. The map generator builds terraced terrain: flat plateaus at multiples of
4 m joined by ramps, with steep (impassable) plateau edges. A grid `Version` counter increments
whenever passability changes (building placed or destroyed, tree depleted).

### Flow fields

On a move order, the group's target cell gets a flow field:

1. **Integration field:** Dijkstra from the target over 8-connected cells (diagonals cost 1.41,
   no corner cutting past blocked cells). 16K cells, well under 1 ms.
2. **Direction field:** each cell points at its lowest-cost neighbor.

Fields are cached by target cell in an LRU cache (default 32 entries) and tagged with the grid
version. Any passability change invalidates the cache (simple; refine to region versions only if
profiling shows rebuild cost). All units heading to the same target share one field. Units whose
collision radius is at most half a cell share one size class, which is why the design caps
collision radius at 1 m.

If the target cell is blocked (a building, a forest), the field targets the nearest reachable
cell. Unreachable targets (an island) send units to the closest reachable point.

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

Example unit definition:

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
