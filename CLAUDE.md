# CLAUDE.md

Malazan-inspired 3D RTS. Godot 4.7 (.NET build) + C#, Windows desktop, single-player vs AI.
Hobby/learning project: a codebase the owner understands beats a clever one.

**Current milestone: M1 (core sim, no graphics).** See [docs/05-roadmap.md](docs/05-roadmap.md).
Live status is in [studio/STATE.md](studio/STATE.md). Don't write gameplay code ahead of the roadmap.

An AI studio works on this repo between the owner's sessions: `producer` (Fable) plans and
accepts, `game-dev` builds, `qa-inspector` stress-tests. See
[docs/07-studio-workflow.md](docs/07-studio-workflow.md). Skills: `/studio-session`, `/qa`, `/handoff`.

## Read before working

| Touching | Read first |
| --- | --- |
| `sim/` or `game/scripts/` | [docs/03-technical-design.md](docs/03-technical-design.md) |
| Gameplay rules or `game/data/` | [docs/02-game-design.md](docs/02-game-design.md) + `docs/factions/<id>.md` |
| `game/assets/`, `tools/` import scripts | [docs/04-art-pipeline.md](docs/04-art-pipeline.md) |
| Scope or priorities | [docs/01-vision.md](docs/01-vision.md), [docs/05-roadmap.md](docs/05-roadmap.md) |

## Architecture rules (non-negotiable)

1. **Sim/presentation split.** All game rules live in `sim/Rts.Sim`, a plain .NET class library.
   It never references Godot: no `using Godot;`, no GodotSharp package, no Godot types.
   Godot (`game/`) only renders, reads input, plays audio, and shows UI.
2. **Fixed tick.** The sim runs at 20 Hz (50 ms per tick) and advances only through `Simulation.Tick()`.
   No wall clock, no frame delta, no `Time`/`DateTime` inside the sim. Rendering interpolates
   between the previous and current tick.
3. **Commands, not mutation.** Player input and the AI never write sim state. They enqueue
   `Command`s, which apply at the start of the next tick. The AI uses the same command API as
   the player and reads the world only through its own fog-filtered view.
4. **Deterministic on one machine.** Same seed + same commands = same state hash, every run.
   - All randomness comes from the sim's seeded RNG. No `System.Random`, `Guid.NewGuid()`, `HashCode`.
   - Never iterate a `Dictionary`/`HashSet` where order affects results; use arrays or sorted keys.
   - Trig goes through `SimMath` (deterministic), not `Math.Sin`/`Atan2`. No `Vector<T>` or
     parallel loops in the sim. No Godot physics anywhere near gameplay.
5. **Data-oriented entities.** Entities are generational handles into struct arrays. No
   class-per-unit inheritance trees, no Godot nodes in the sim. Spatial queries go through the
   spatial hash grid. Per-tick code must not allocate (no LINQ, no closures, no `new` in hot loops).
6. **Data-driven.** Unit, building, tech, ability, faction, and AI build-order data lives in JSON
   under `game/data/`. Never hard-code a stat in C#. A new faction is data + models, not new
   systems. If a design needs a new mechanic, build a generic system with data hooks.
7. **Dumb views.** One lightweight view node per entity (mesh + AnimationPlayer, no physics body),
   created/destroyed from sim events, positioned from sim snapshots. Views hold no gameplay state.
8. **Player-facing text comes from data** (`displayName`, `description` fields), never from C#
   literals. Malazan names are internal codenames; the pre-release rename pass must be data-only.

## Repository map

```
CLAUDE.md  README.md  SETUP.md
docs/                  design docs (source of truth for decisions)
  factions/            one page per faction
game/                  Godot project (from M0): project.godot, RtsGame.csproj
  scenes/ scripts/ shaders/ ui/ assets/ data/
  tests/               Godot-side test scenes (QA)
sim/                   (from M0)
  Rts.Sim/             pure .NET 8 library: the whole game simulation + AI
  Rts.Sim.Tests/       xUnit tests; Stress/ and QA/ are the QA inspector's suites
tools/                 asset import, map generator CLI, build/export scripts; qa/ = QA tooling
studio/                studio memory: STATE, inbox, autopilot, handoff, sessions, bugs, qa
.claude/               agents/, skills/, settings.json (committed); worktrees/ (gitignored)
asset-sources/         raw downloaded packs (gitignored, never committed)
RtsGame.sln            (from M0) all C# projects
```

## Commands

Available once M0 is complete. `$env:GODOT` points at the Godot *console* exe (see SETUP.md).

```powershell
dotnet build RtsGame.sln                                   # build everything
dotnet test sim/Rts.Sim.Tests                              # sim tests, run after every sim change
dotnet test sim/Rts.Sim.Tests --filter Category!=Perf      # skip benchmarks for a quick loop
& $env:GODOT --headless --path game --import               # (re)import assets after adding files
powershell -File tools/qa/smoke.ps1                        # build + import + headless boot gate, must print PASS
& $env:GODOT --path game                                   # run the game windowed
powershell -File tools/export.ps1                           # Windows release build -> build/ (not yet written)
```

Why the script and not a bare `& $env:GODOT --headless --path game --quit-after 600`: headless
Godot does **not** compile C# (it loads a stale or missing `RtsGame.dll`), and it exits 0 even when
a script fails to load. `tools/qa/smoke.ps1` builds first, then fails unless the log contains
`Rts.Sim <version>` and no `ERROR` line. It reads `GODOT` from the user environment itself if
`$env:GODOT` is empty in a non-interactive shell
(`[Environment]::GetEnvironmentVariable('GODOT','User')`).

## Coding conventions

- C# 12 / .NET 8, nullable enabled. `Rts.Sim` builds with warnings as errors.
- File-scoped namespaces (`Rts.Sim.*`, `Rts.Game.*`), one public type per file, file named after it.
- PascalCase types/methods/properties, `_camelCase` private fields, `camelCase` locals.
- Prefer plain structs and static systems in the sim (`MovementSystem.Run(World w)`), classes in the view.
- Units in code: meters, seconds in data, ticks inside the sim. Loaders convert seconds to ticks.
- Data ids are `snake_case` strings in JSON (`malazan_heavy_infantry`), mapped to ints at load.
- Godot scripts are C# only (no GDScript). Scenes and resources stay in text formats (`.tscn`, `.tres`).
- Comments explain *why*, not *what*. Public sim APIs get a one-line `///` summary.
- No new NuGet packages in `Rts.Sim` without asking. Tests use xUnit and its built-in asserts.

## Testing rules

- Every sim system gets xUnit tests. Every bug fix gets a regression test that failed first.
- Data changes run the data-validation test (catches bad ids and missing references).
- Changes that can affect determinism (tick order, RNG use, movement, combat) re-run the replay
  golden tests. If golden hashes change on purpose, regenerate them in the same commit and say why.
- Perf benchmarks (`[Trait("Category","Perf")]`) guard tick cost. Don't loosen a threshold
  without explaining the regression.

## Definition of done (any task)

1. `dotnet build` clean, `dotnet test` green.
2. If `game/` changed: `powershell -File tools/qa/smoke.ps1` prints PASS (it exits non-zero on a
   missing `Rts.Sim <version>` banner or any `ERROR` line, which Godot's own exit code hides).
3. If visuals changed: run windowed, take a screenshot (Godot MCP or the `--screenshot` debug
   flag once it exists), and look at it before claiming it works.
4. Docs updated in the same commit when behavior or a decision changed.

## Workflow

- Two machines and the studio share this repo through GitHub. `git pull` before starting,
  commit + push when stopping. The studio works in `.claude/worktrees/studio` (sim track),
  `.claude/worktrees/studio-view` (view track) and `.claude/worktrees/studio-data` (data track);
  never edit files there from an interactive session.
- At the end of interactive work, run `/handoff` so the Producer records it for the next session.
- Owner requests for the studio go in `studio/inbox.md`; only the Producer (or the conductor, when recording an incident) edits `studio/STATE.md`
  and `studio/handoff.md`.
- Small commits, imperative subject, milestone prefix: `M1: cache flow fields by target cell`.
- Docs are the source of truth. Items tagged **[OPEN]** are the owner's call: ask, don't silently resolve.
  When a settled decision changes, update the table in `docs/01-vision.md` with the date.
- Never commit raw asset packs. Follow the art pipeline, and add every third-party asset to the
  licensing log in `docs/04-art-pipeline.md` in the same commit that imports it.
- Binary file types must be in `.gitattributes` (Git LFS) before the first commit that adds one.
- Installing tools, creating accounts, and anything that costs money are the owner's call; ask first.
