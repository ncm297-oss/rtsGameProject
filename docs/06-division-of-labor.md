# 06 — Division of Labor

Who does what. Claude Code writes the code, content files, and docs; the owner makes taste
calls, plays the game, and does anything that needs an account, a purchase, or a click in a
GUI installer or download page.

Inside "Claude", three agents split the work (see [07-studio-workflow.md](07-studio-workflow.md)):
the **Producer** (Fable) plans, accepts, and keeps the studio's memory; **game-dev** builds;
the **QA inspector** stress-tests. Scheduled sessions run them without the owner present.

## Claude Code builds entirely

- **Simulation:** economy, units, buildings, tech tree, combat, pathfinding, fog of war,
  abilities, statuses, stealth, AI opponent, save/load, replays.
- **Godot content:** scenes (`.tscn`), C# scripts, resources (`.tres`), shaders, materials, UI,
  input maps, project settings, export presets. All text formats, so no editor GUI is required.
- **Data:** unit/building/tech/ability/faction JSON and AI build orders; balance tables.
- **Placeholder art and audio:** procedural primitive meshes, terrain, effects, generated tones,
  enough for a fully playable game before any real asset exists.
- **Asset pipeline tooling:** manifest-driven import scripts (copy, rename, convert, scale,
  material renames), Blender Python scripts for batch touch-ups the owner runs.
- **Maps:** the procedural generator and the three shipped maps; later, a map editor.
- **Tests:** xUnit for the sim, scenario and determinism tests, replay regression tests, perf
  benchmarks, headless Godot smoke tests.
- **Build and export:** scripts that build, test, export, and zip a Windows release.
- **Debugging loop:** build, run headless, read logs, take screenshots (Godot MCP server or the
  game's `--screenshot` flag) and inspect them.
- **Documentation:** everything in `docs/`, `CLAUDE.md`, `SETUP.md`, `README.md`.

## The owner, or an outside service

| Need | Who / what | Claude's role |
| --- | --- | --- |
| Install Godot, .NET SDK, Git LFS, Blender | Owner (or Claude via `winget` with approval) | Gives exact commands, runs them on request, verifies the install |
| GitHub repo, pushing from a new machine | Owner signs in (Git Credential Manager browser prompt) | Handles all git operations after sign-in |
| Download asset packs | Owner clicks download (itch.io "name your price" pages need a click; kenney.nl, quaternius.com) | Finds the exact packs and links, imports and wires them up |
| AI 3D model generation | Meshy or Tripo account (owner) | Writes prompts; with an API key in a local `.env`, scripts bulk generation and import |
| Character animations | KayKit Character Animations (CC0); Mixamo (Adobe account) for extras | Retargets and wires animations into Godot |
| Music | Free packs (Kevin MacLeod: CC BY with attribution; Kenney: CC0) or Suno/Udio (paid for ownership) | Integrates, loops, and mixes |
| Final SFX | Kenney audio packs (CC0), freesound.org (CC0 filter) | Generates placeholders; integrates real ones |
| "Is it fun?", visual taste | Owner, playtesting | Implements feedback; inspects screenshots too |
| Open design decisions ([OPEN] items) | Owner | Lays out options and trade-offs, recommends a default |
| Publishing | itch.io account (free); Steam ($100 fee) if ever | Builds the export, writes the store page copy, runs the rename pass |

## Ground rules

- Claude asks before installing software, creating accounts, spending money, or anything that
  publishes outside the private repo.
- API keys and tokens live in a local, gitignored `.env`, never in committed files or chat.
- Claude doesn't download from untrusted sources. Asset packs come from the authors' own pages
  listed in [04-art-pipeline.md](04-art-pipeline.md).
- Taste calls (art direction, what feels fun, faction identity) are the owner's. Claude proposes;
  the owner decides.
