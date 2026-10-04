# Studio state

The dashboard. The Producer rewrites it at the end of every session. **Owner: read "Waiting on
you" first.** "For your review" is non-blocking: things the studio decided or finished on its own.

_Last updated: 2026-10-04 14:00 (session 2026-10-04-0120, ACCEPT)._

## Waiting on you

- Nothing blocking. The studio runs on autopilot (chain sessions, self sign-off) until the end
  of the roadmap or a cap/incident stops it.

## For your review

- 2026-10-04 (M1-4b): units move. `Command.Move(player, unit, target)` walks a unit along a cached
  flow field at its JSON speed; 200 units reach a shared point well inside the 60 s limit; 500 units with
  500 different goals cost ~1 ms per tick. Producer decisions, revisit any time: at most one
  flow-field build per tick (units whose field isn't ready wait a tick), and the cache's contents
  count as sim state (to be hashed/saved next task; docs/01 change log). To watch it:
  `dotnet test sim/Rts.Sim.Tests --filter TwoHundredUnits_OneMove --logger "console;verbosity=detailed"`.
- Session note: 2026-10-04-0120 was interrupted twice (sleep, then your stop before QA) and resumed
  by the 10:48 run; one game-dev report was lost. Nothing was lost in the code; see the session log.
- 2026-10-03 (M1-4a): ramps are true corridors, map params can't overflow, spatial hash is in.
  Producer decision: `MaxAttempts` cap is 8.
- 2026-10-03 (M1-3): terraced maps + nav grid; cliffs are blocked cells on the plateau rim.
- 2026-10-03 (M1-2): first real game data in `game/data/`; invented values flagged in docs/03.
- Optional M0 item: Godot MCP server for Claude Code (your install; see SETUP.md). Not needed
  before M2.

## Now

| Field | Value |
| --- | --- |
| Milestone | M1 — Core sim, no graphics (started 2026-10-03) |
| Current task | next: **M1-4c build-cap determinism + fairness (BUG-0021/0022), suite stability first (BUG-0024/0017)**; then M1-4d steering/separation/arrival/shoving |
| Gate | **GO** (next session starts when the conductor is ready) |
| Tools on this PC | Godot 4.7.2 .NET, .NET SDK 8.0.425, Git 2.53 + LFS 3.7; `GODOT` user variable set |
| Build | green on `studio/2026-10-04-0120` @ b707114 (0 warnings, 0 errors), merging to `main` |
| Tests | 844 passed / 10 skipped / 854 (known-bug skips: BUG-0005, 0008 x2, 0010 x2, 0014, 0021, 0022, 0023 x2); `tools/qa/smoke.ps1` PASS. **Full run in one process is flaky** (BUG-0024/0017): check with `--filter "Category!=Perf"` then `--filter Category=Perf -- xUnit.ParallelizeTestCollections=false` |
| Open bugs | 10 (S1: 0, S2: 0, S3: 7, S4: 3) — none block; BUG-0024 + BUG-0017 are the next session's first commit |
| Sessions today | 1 / 10 |
| Last session | 2026-10-04-0120 · M1-4b GameData in sim + flow fields + LRU cache + `Move` · ACCEPT (1 fix round) |

## Milestone progress

| Milestone | Criteria met | Status |
| --- | --- | --- |
| M0 | 7 / 7 required | **Done** 2026-10-03 (optional MCP item open) |
| M1 | 3 / 8 (+ criterion 4 half done: flow fields, cache, Move) | In progress |
| M2-M9 | — | Planned |

## Backlog (outside the current task)

- M1-4c: hash + save flow-field cache metadata (BUG-0021), serve misses oldest order first with a
  small count cap and capacity scaled to `UnitCapacity` (BUG-0022); first commit de-flakes the
  suite (BUG-0024 non-parallel Perf/alloc collection, BUG-0017 re-measure-and-report).
- M1-4d steering, separation (via `Spatial.QueryRadius`), arrival slots, shoving + BUG-0005
  per-player command buckets; closes roadmap criterion 4. Then M1-5 scenario test, M1-6 replay +
  determinism golden (BUG-0014 first), M1-7 perf test (document maps > 256 unsupported, BUG-0023),
  M1-8 headless CLI.
- Spatial hash: queries in one crowded bucket are sort-dominated (2,500 units -> 500 queries 8 ms);
  separation in 4d issues one query per unit per tick: measure first.
- Generator pinned by 15 hashes + QA oracles (`QA/Oracles/PreBug0015*`); `NavGrid.Flood` relies on
  the blocked outer ring (M3 passability changes must keep it).
- Loader: BUG-0008 (S3), BUG-0010 (S4) with the M3 data task; follow-ups for buildings, techs,
  abilities, `ai.json`, maps; cross-field checks.
- BUG-0002 (S4): `.sln` Release config maps RtsGame to Debug; fix with `tools/export.ps1` (M6).
- 1024 World allocates 221 MB (32 preallocated fields): scale cache capacity with map size if big
  maps ever ship.
- Enqueue stamps `TickNumber + 1` (up to 50 ms input latency); revisit at M2-1 if felt.
- Evaluate a Godot MCP server at M2. .NET 8 support ends 2026-11-10: move to next LTS at M6.

## Recent sessions

| Date | Session | Task | Result |
| --- | --- | --- | --- |
| 2026-10-04 | [2026-10-04-0120](sessions/2026-10-04-0120.md) | M1-4b GameData in sim + flow fields + LRU cache + `Move` | ACCEPT after 1 fix round (QA: S2 + 2 S3 fixed in-session, 4 S3 open); interrupted/resumed session |
| 2026-10-03 | [2026-10-03-2220](sessions/2026-10-03-2220.md) | M1-4a ramp walls + param safety + spatial hash | ACCEPT after 1 fix round (QA: S2 + S3 fixed in-session, 1 S3 open) |
| 2026-10-03 | [2026-10-03-1235](sessions/2026-10-03-1235.md) | M1-3 heightmap + nav grid + 2 bug fixes | ACCEPT (QA: 2 S3 + 2 S4 filed) |
| 2026-10-03 | [2026-10-03-1151](sessions/2026-10-03-1151.md) | M1-2 data loader + 3 bug fixes | ACCEPT (QA: 2 S3 + 2 S4 filed) |
| 2026-10-03 | [2026-10-03-0907](sessions/2026-10-03-0907.md) | M1-1 sim core | ACCEPT (QA: 4 S3/S4 bugs filed) |
| 2026-10-03 | [2026-10-03-0826](sessions/2026-10-03-0826.md) | M0-1 solution skeleton + toolchain | ACCEPT, M0 signed off |
