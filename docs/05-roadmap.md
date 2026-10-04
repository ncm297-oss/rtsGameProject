# 05 — Roadmap

Each milestone ends with a playable (or runnable) build, green tests, and a short retro before
the next one starts. Acceptance criteria are checkboxes so progress is visible in the doc itself.

Status legend: **Next** = start here, **Planned** = not started, **Done** = accepted.

| # | Milestone | Status | One-line goal |
| --- | --- | --- | --- |
| M0 | Environment & skeleton | **Done** (2026-10-03) | Tools installed, empty projects build, tests and headless boot pass |
| M1 | Core sim, no graphics | **Next** | 200 units path across the map deterministically, fast |
| M2 | Presentation | Planned | Move an army around a 3D map |
| M3 | Economy & buildings | Planned | Build a Malazan base |
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
- [ ] Flow fields with LRU cache; steering, separation, arrival, shoving.
      _(flow fields + LRU cache + `Move` command + build cap: session 2026-10-04-0120, task M1-4b;
      steering, separation, arrival, shoving still open)_
- [ ] Scenario test: 200 units ordered across a 128×128 map with obstacles all arrive within a
      time limit, none stuck, none inside blocked cells.
- [ ] Replay format (seed + commands + checkpoint hashes); determinism test (same run twice →
      same hash) and one golden replay.
- [ ] Perf test: 500 moving units, average tick < 4 ms on the dev machine.
- [ ] A tiny CLI in `tools/` runs a scenario headless and prints hashes and timings.

## M2 — Presentation

**Done when:**

- [ ] `SimRunner` with accumulator and interpolation; game speed setting.
- [ ] RTS camera: 55° pitch, edge pan, arrow keys, middle-drag, zoom, clamped to the map.
- [ ] Heightmap terrain mesh with biome vertex colors; trees and rocks as MultiMesh.
- [ ] Placeholder unit views (primitive meshes, team colors), pooled, interpolated.
- [ ] Selection: click, box, shift-add, double-click type, control groups, Tab subgroups.
- [ ] Right-click move, A attack-move (moves only for now), S stop, H hold, shift-queue.
- [ ] Minimap with click-to-move-camera and right-click orders.
- [ ] `--screenshot` debug flag; debug overlay (nav grid, flow arrows, tick time).
- [ ] Placeholder audio for select and command.
- [ ] Playable: the owner moves an army of 100 placeholder units around a generated map at 60 FPS.

## M3 — Economy & buildings

**Done when:**

- [ ] Gold mines and trees as resource entities; tree depletion updates the nav grid.
- [ ] Worker gather/return loop with automatic drop-off choice.
- [ ] Building placement (ghost preview, validity), construction with multiple builders, repair.
- [ ] Production queues (5 slots), rally points, population and cap, refunds on cancel.
- [ ] Age II research and unlocks; Forge upgrades.
- [ ] Malazan and Whirlwind factions fully defined in data (units, buildings, techs).
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
