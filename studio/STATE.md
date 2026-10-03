# Studio state

The dashboard. The Producer rewrites it at the end of every session. **Owner: read "Waiting on
you" first.** "For your review" is non-blocking: things the studio decided or finished on its own.

_Last updated: 2026-10-03 (session 2026-10-03-1235, ACCEPT)._

## Waiting on you

- Nothing blocking. The studio runs on autopilot (chain sessions, self sign-off) until the end
  of the roadmap or a cap/incident stops it.

## For your review

- 2026-10-03 (M1-3): the sim now generates terraced maps (levels 0-2, rectangular plateaus,
  3-cell-wide ramps) and a nav grid. Producer decisions, revisit any time: cliffs are blocked
  *cells* on the plateau rim (not per-edge rules); the 30° slope rule stays, so ramp sides will
  be walled off in the next task (BUG-0011). To see a map: `dotnet test sim/Rts.Sim.Tests
  --filter DefaultMap_HasRampsAndCliffs --logger "console;verbosity=detailed"` prints an ASCII
  render (`#` cliff, `x` other blocked, `/` ramp, otherwise the level digit 0-2).
- 2026-10-03 (M1-2): first real game data is in `game/data/` (Malazan + Whirlwind units,
  factions, damage table, economy rules). Invented values are flagged in docs/03 "What ships
  today": collision radii 0.4 / 0.7 / 0.9, melee range 0.5, windups 0.3-0.45 s, placeholder
  `requires: ["age_ii"]`, faction palettes. Retune in JSON any time.
- Optional M0 item: Godot MCP server for Claude Code (your install; see SETUP.md). Not needed
  before M2.

## Now

| Field | Value |
| --- | --- |
| Milestone | M1 — Core sim, no graphics (started 2026-10-03) |
| Current task | next: **M1-4a spatial hash** (first commit fixes BUG-0011/0012/0013) |
| Gate | **GO** (next session starts when the conductor is ready) |
| Tools on this PC | Godot 4.7.2 .NET, .NET SDK 8.0.425, Git 2.53 + LFS 3.7; `GODOT` user variable set |
| Build | green on `studio/2026-10-03-1235` @ 4fc5e23 (0 warnings, 0 errors), merging to `main` |
| Tests | 372 passed, 0 failed, 9 skipped (all known-bug tests: BUG-0005, 0008 x2, 0010 x2, 0011, 0012 x2, 0014); `tools/qa/smoke.ps1` PASS |
| Open bugs | 8 (S1: 0, S2: 0, S3: 4, S4: 4) — none blocking |
| Sessions today | 4 / 10 |
| Last session | 2026-10-03-1235 · M1-3 heightmap + nav grid · ACCEPT |

## Milestone progress

| Milestone | Criteria met | Status |
| --- | --- | --- |
| M0 | 7 / 7 required | **Done** 2026-10-03 (optional MCP item open) |
| M1 | 2 / 8 (+ criterion 3 half done: heightmap + nav grid in, spatial hash pending) | In progress |
| M2-M9 | — | Planned |

## Backlog (outside the current task)

- M1-4a spatial hash (+ BUG-0011/0012/0013 first); M1-4b flow fields + LRU cache + steering,
  separation, arrival, shoving (+ BUG-0005); M1-5 scenario test; M1-6 replay + determinism
  golden (+ BUG-0014 first, before any golden hash exists); M1-7 perf test; M1-8 headless CLI.
- Map bugs: BUG-0011 (S3, ramp sides walkable: block the flanking cells), BUG-0012 (S3, int
  overflow in `MapGenParams.Validate` / `Heightmap` size), BUG-0013 (S4, worst-case params ~35 s).
- BUG-0014 (S4): `SimRng` seed `ulong.MaxValue` collides with seed 0 on stream 0; mix the seed
  at M1-6 before golden replays.
- Loader: BUG-0008 (S3, duplicate JSON keys) and BUG-0010 (S4, faction slot check) with the M3
  data task (buildings/techs), which also resolves `trainedAt`, `requires`, `projectile`.
- Loader follow-ups (M3/M4/M5): buildings, techs, abilities, statuses, `ai.json`, maps; cross-field
  checks (`minRange <= range`, `windup <= cooldown`); warn on durations not a multiple of 50 ms.
- BUG-0005 (S3) O(n^2) command sort: fix with per-player buckets at M1-4b.
- BUG-0002 (S4): `.sln` Release config maps RtsGame to Debug. Fix with `tools/export.ps1` (M6).
- M1-6 decides whether terrain feeds `StateHash` directly (today only via the MapGen RNG state).
- Enqueue always stamps `TickNumber + 1` (up to 50 ms extra input latency); revisit at M2-1 if felt.
- Evaluate a Godot MCP server at M2 (see SETUP.md).
- .NET 8 support ends 2026-11-10: move to the next LTS at M6 (docs/03).

## Recent sessions

| Date | Session | Task | Result |
| --- | --- | --- | --- |
| 2026-10-03 | [2026-10-03-1235](sessions/2026-10-03-1235.md) | M1-3 heightmap + nav grid + 2 bug fixes | ACCEPT (QA: 2 S3 + 2 S4 filed) |
| 2026-10-03 | [2026-10-03-1151](sessions/2026-10-03-1151.md) | M1-2 data loader + 3 bug fixes | ACCEPT (QA: 2 S3 + 2 S4 filed) |
| 2026-10-03 | [2026-10-03-0907](sessions/2026-10-03-0907.md) | M1-1 sim core | ACCEPT (QA: 4 S3/S4 bugs filed) |
| 2026-10-03 | [2026-10-03-0826](sessions/2026-10-03-0826.md) | M0-1 solution skeleton + toolchain | ACCEPT, M0 signed off |
