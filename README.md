# rtsGameProject

A 3D, single-player real-time strategy game for Windows in the StarCraft / Age of Empires mold,
set in a dark-fantasy world inspired by Steven Erikson's *Malazan Book of the Fallen*. Five
peoples (the Malazan Empire, the Whirlwind, the Teblor, Shadow, and the Tiste Andii) gather,
build, and fight in 20-30 minute skirmishes against AI opponents.

A hobby and learning project, built with Godot 4.7 (.NET) and C#, mostly by Claude Code.

## Status

**Planning complete; M0 (environment & skeleton) is next.** The repo currently holds design docs
only. See the [roadmap](docs/05-roadmap.md).

## Docs

| Doc | What's in it |
| --- | --- |
| [01 Vision](docs/01-vision.md) | Pitch, pillars, tone, scope caps, settled decisions, IP/naming policy |
| [02 Game design](docs/02-game-design.md) | Faction template, economy, buildings, tech, combat, abilities, stealth, map, controls, AI, game flow |
| [Factions](docs/factions/) | [Malazan](docs/factions/malazan.md) · [Whirlwind](docs/factions/whirlwind.md) · [Teblor](docs/factions/teblor.md) · [Shadow](docs/factions/shadow.md) · [Tiste Andii](docs/factions/andii.md) |
| [03 Technical design](docs/03-technical-design.md) | Architecture, tick model, entities, pathfinding, fog, rendering, data format, testing |
| [04 Art pipeline](docs/04-art-pipeline.md) | Style guide, asset sources, naming, import process, licensing log |
| [05 Roadmap](docs/05-roadmap.md) | Milestones M0-M9 with acceptance criteria |
| [06 Division of labor](docs/06-division-of-labor.md) | What Claude builds vs. what needs the owner or an outside service |
| [07 Studio workflow](docs/07-studio-workflow.md) | The AI studio: Producer (PM), game-dev, QA inspector, scheduled sessions, permissions |
| [studio/STATE.md](studio/STATE.md) | Live dashboard: what's next and what's waiting on you |
| [CLAUDE.md](CLAUDE.md) | Rules and conventions every Claude Code session follows |
| [SETUP.md](SETUP.md) | Installing the tools on a new machine |

## How it's built

- **Sim / presentation split:** all game rules live in a pure .NET library (`sim/Rts.Sim`) with
  no Godot dependency, running at a fixed 20 Hz tick. Godot (`game/`) renders, handles input,
  and draws the UI.
- **Deterministic:** seeded RNG and a command queue make replays and replay-based regression
  tests possible.
- **Data-driven:** units, buildings, techs, abilities, and AI build orders are JSON.

## Running it

Nothing to run yet. Once M0 lands:

```powershell
dotnet test sim/Rts.Sim.Tests             # simulation tests
& $env:GODOT --path game                  # run the game
```

Tool installation is in [SETUP.md](SETUP.md).

## Legal

Malazan names and characters belong to Steven Erikson and Ian C. Esslemont and are used here only
as internal codenames in a private, non-commercial project. Any public release will first get a
full rename pass to original names (see [01 Vision](docs/01-vision.md#ip-and-naming-policy)).
Third-party assets are listed with their licenses in the
[licensing log](docs/04-art-pipeline.md#licensing-log).
