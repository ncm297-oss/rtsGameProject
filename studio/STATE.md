# Studio state

The dashboard. The Producer rewrites it at the end of every session. **Owner: read "Waiting on
you" first.** "For your review" is non-blocking: things the studio decided or finished on its own.

_Last updated: 2026-10-03 (session 2026-10-03-2220, ACCEPT)._

## Waiting on you

- Nothing blocking. The studio runs on autopilot (chain sessions, self sign-off) until the end
  of the roadmap or a cap/incident stops it.

## For your review

- 2026-10-03 (M1-4a): ramps are now true corridors (their sides are cliffs, so nothing walks off
  a ramp sideways), map params can't overflow, and the worst legal map generates in ~1 s. The
  spatial hash is in: `world.Spatial.QueryRadius / QueryRect / NearestEnemy`. Producer decision,
  revisit any time: the map generator's `MaxAttempts` cap is 8 (its default), not the 16 I first
  briefed; it only bounds generation time. To see a map with ramp walls: `dotnet test
  sim/Rts.Sim.Tests --filter DefaultMap_HasRampsAndCliffs --logger "console;verbosity=detailed"`.
- 2026-10-03 (M1-3): terraced maps (levels 0-2, rectangular plateaus, 3-cell ramps) and a nav
  grid. Producer decisions: cliffs are blocked *cells* on the plateau rim; the 30° slope rule stays.
- 2026-10-03 (M1-2): first real game data in `game/data/` (Malazan + Whirlwind units, factions,
  damage table, economy). Invented values flagged in docs/03 "What ships today"; retune in JSON.
- Optional M0 item: Godot MCP server for Claude Code (your install; see SETUP.md). Not needed
  before M2.

## Now

| Field | Value |
| --- | --- |
| Milestone | M1 — Core sim, no graphics (started 2026-10-03) |
| Current task | next: **M1-4b flow fields + LRU cache + `Move`** (steering/separation likely split into M1-4c) |
| Gate | **GO** (next session starts when the conductor is ready) |
| Tools on this PC | Godot 4.7.2 .NET, .NET SDK 8.0.425, Git 2.53 + LFS 3.7; `GODOT` user variable set |
| Build | green on `studio/2026-10-03-2220` @ f282aaf (0 warnings, 0 errors), merging to `main` |
| Tests | 717 passed, 0 failed, 6 skipped (known-bug tests: BUG-0005, 0008 x2, 0010, 0014 + 1 perf-only skip); `tools/qa/smoke.ps1` PASS |
| Open bugs | 6 (S1: 0, S2: 0, S3: 3, S4: 3) — none blocking; BUG-0017 is a flaky QA test, watch it |
| Sessions today | 5 / 10 |
| Last session | 2026-10-03-2220 · M1-4a ramp walls + param safety + spatial hash · ACCEPT (1 fix round) |

## Milestone progress

| Milestone | Criteria met | Status |
| --- | --- | --- |
| M0 | 7 / 7 required | **Done** 2026-10-03 (optional MCP item open) |
| M1 | 3 / 8 (sim core, data loader, terrain + nav grid + spatial hash) | In progress |
| M2-M9 | — | Planned |

## Backlog (outside the current task)

- M1-4b flow fields + LRU cache + `Move` command; M1-4c steering, separation, arrival, shoving
  (+ BUG-0005 per-player command buckets); M1-5 scenario test; M1-6 replay + determinism golden
  (+ BUG-0014 first, before any golden hash exists); M1-7 perf test; M1-8 headless CLI.
- BUG-0017 (S3): flaky `Flood_10000Commands_OneTick_AllocatesNothing` (1 fail in 11+ runs). If it
  fails again on main, harden it per the bug file (re-measure once, report both deltas).
- Spatial hash follow-ups: query cost in one crowded bucket is dominated by sorting (2,500 units
  in one bucket -> 500 queries 8 ms); revisit if M1-4c separation makes it show in the tick budget.
  Rebuild cost scales with bucket count (0.65 ms on a 1024 map), fine for design-size maps.
- Generator is pinned by 15 hashes + QA oracles (`QA/Oracles/PreBug0015*`): a deliberate generator
  change retires/regenerates them in the same commit. `NavGrid.Flood` relies on the blocked outer
  ring; M3 passability changes must keep ring cells blocked.
- BUG-0014 (S4): `SimRng` seed `ulong.MaxValue` collides with seed 0 on stream 0; mix the seed
  at M1-6 before golden replays.
- Loader: BUG-0008 (S3, duplicate JSON keys) and BUG-0010 (S4, faction slot check) with the M3
  data task (buildings/techs), which also resolves `trainedAt`, `requires`, `projectile`.
- Loader follow-ups (M3/M4/M5): buildings, techs, abilities, statuses, `ai.json`, maps; cross-field
  checks (`minRange <= range`, `windup <= cooldown`); warn on durations not a multiple of 50 ms.
- BUG-0002 (S4): `.sln` Release config maps RtsGame to Debug. Fix with `tools/export.ps1` (M6).
- M1-6 decides whether terrain feeds `StateHash` directly (today only via the MapGen RNG state).
- Enqueue always stamps `TickNumber + 1` (up to 50 ms extra input latency); revisit at M2-1 if felt.
- Evaluate a Godot MCP server at M2 (see SETUP.md).
- .NET 8 support ends 2026-11-10: move to the next LTS at M6 (docs/03).

## Recent sessions

| Date | Session | Task | Result |
| --- | --- | --- | --- |
| 2026-10-03 | [2026-10-03-2220](sessions/2026-10-03-2220.md) | M1-4a ramp walls + param safety + spatial hash | ACCEPT after 1 fix round (QA: S2 + S3 fixed in-session, 1 S3 open) |
| 2026-10-03 | [2026-10-03-1235](sessions/2026-10-03-1235.md) | M1-3 heightmap + nav grid + 2 bug fixes | ACCEPT (QA: 2 S3 + 2 S4 filed) |
| 2026-10-03 | [2026-10-03-1151](sessions/2026-10-03-1151.md) | M1-2 data loader + 3 bug fixes | ACCEPT (QA: 2 S3 + 2 S4 filed) |
| 2026-10-03 | [2026-10-03-0907](sessions/2026-10-03-0907.md) | M1-1 sim core | ACCEPT (QA: 4 S3/S4 bugs filed) |
| 2026-10-03 | [2026-10-03-0826](sessions/2026-10-03-0826.md) | M0-1 solution skeleton + toolchain | ACCEPT, M0 signed off |
