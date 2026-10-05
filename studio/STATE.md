# Studio state

The dashboard. The Producer rewrites it at the end of every session. **Owner: read "Waiting on
you" first.** "For your review" is non-blocking: things the studio decided or finished on its own.

_Last updated: 2026-10-04 (session 2026-10-04-2056, ACCEPT)._

## Waiting on you

- Nothing blocking. The studio runs on autopilot (chain sessions, self sign-off) until the end
  of the roadmap or a cap/incident stops it.

## For your review

- 2026-10-04 (M1-4c): the flow-field build cap is now deterministic and fair: the cache's
  bookkeeping is part of the state hash, move orders are served oldest first at 2 builds per tick,
  and the cache scales with the unit cap (64 fields at 512 units). The whole test suite is green in
  one `dotnet test` run again (Perf/allocation tests run serially). Producer decisions, revisit any
  time (docs/01 change log): cap of 2; cache size formula; same-tick ties break by goal cell
  (BUG-0026, S4) and over-capacity eviction is plain LRU (BUG-0025, S3), both fixed together after
  M1-4d. To watch it: `dotnet test sim/Rts.Sim.Tests --filter FieldBuildOrderTests --logger "console;verbosity=detailed"`.
- 2026-10-04 (M1-4b): units move along cached flow fields at their JSON speed; 200 units reach a
  shared point well inside the limit. `--filter TwoHundredUnits_OneMove` shows it.
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
| Current task | next: **M1-4d steering, separation, arrival slots, shoving + BUG-0005** (closes roadmap criterion 4); then BUG-0025/0026 follow-up, M1-5 scenario test |
| Gate | **GO** (next session starts when the conductor is ready) |
| Tools on this PC | Godot 4.7.2 .NET, .NET SDK 8.0.425, Git 2.53 + LFS 3.7; `GODOT` user variable set |
| Build | green on `studio/2026-10-04-2056` @ f753e4f + ACCEPT commit (0 warnings, 0 errors), merging to `main` |
| Tests | 905 passed / 9 skipped / 914 in one process (~1m50s; known-bug skips: BUG-0005, 0008 x2, 0010 x2, 0014, 0023 x2, 0025); `tools/qa/smoke.ps1` PASS. Quick loop: `--filter "Category!=Perf&Category!=Soak"` |
| Open bugs | 8 (S1: 0, S2: 0, S3: 5, S4: 3) — none block |
| Sessions today | 2 / 10 |
| Last session | 2026-10-04-2056 · M1-4c build-cap determinism + fairness, suite de-flaked · ACCEPT (0 fix rounds) |

## Milestone progress

| Milestone | Criteria met | Status |
| --- | --- | --- |
| M0 | 7 / 7 required | **Done** 2026-10-03 (optional MCP item open) |
| M1 | 3 / 8 (+ criterion 4: flow fields, cache, Move, deterministic build cap done; steering/separation/arrival/shoving open) | In progress |
| M2-M9 | — | Planned |

## Backlog (outside the current task)

- M1-4d steering, separation (via `Spatial.QueryRadius`), arrival slots, shoving + BUG-0005
  per-player command buckets; closes roadmap criterion 4. Measure query cost first (one crowded
  bucket: 2,500 units -> 500 queries 8 ms; separation issues one query per unit per tick).
- BUG-0025 (S3) + BUG-0026 (S4): over-capacity eviction by newest order, rotated same-tick
  tie-break; one small task after 4d, before M5.
- Then M1-5 scenario test (200 units, obstacles), M1-6 replay + determinism golden (BUG-0014
  first), M1-7 perf test (document maps > 256 unsupported, BUG-0023), M1-8 headless CLI.
- `NavGrid.Version` is not hashed: fine while the grid is immutable; must join the hash when M3
  tree depletion changes passability.
- Generator pinned by 15 hashes + QA oracles (`QA/Oracles/PreBug0015*`); `NavGrid.Flood` relies on
  the blocked outer ring (M3 passability changes must keep it).
- Loader: BUG-0008 (S3), BUG-0010 (S4) with the M3 data task; follow-ups for buildings, techs,
  abilities, `ai.json`, maps; cross-field checks.
- BUG-0002 (S4): `.sln` Release config maps RtsGame to Debug; fix with `tools/export.ps1` (M6).
- 1024 World allocates 226 MB (32 fields + cell index): fine unless big maps ship.
- Enqueue stamps `TickNumber + 1` (up to 50 ms input latency); revisit at M2-1 if felt.
- Evaluate a Godot MCP server at M2. .NET 8 support ends 2026-11-10: move to next LTS at M6.

## Recent sessions

| Date | Session | Task | Result |
| --- | --- | --- | --- |
| 2026-10-04 | [2026-10-04-2056](sessions/2026-10-04-2056.md) | M1-4c build-cap determinism + fairness; suite de-flaked | ACCEPT, 0 fix rounds (QA: 1 S3 + 1 S4 filed; 4 bugs closed) |
| 2026-10-04 | [2026-10-04-0120](sessions/2026-10-04-0120.md) | M1-4b GameData in sim + flow fields + LRU cache + `Move` | ACCEPT after 1 fix round (QA: S2 + 2 S3 fixed in-session, 4 S3 open); interrupted/resumed session |
| 2026-10-03 | [2026-10-03-2220](sessions/2026-10-03-2220.md) | M1-4a ramp walls + param safety + spatial hash | ACCEPT after 1 fix round (QA: S2 + S3 fixed in-session, 1 S3 open) |
| 2026-10-03 | [2026-10-03-1235](sessions/2026-10-03-1235.md) | M1-3 heightmap + nav grid + 2 bug fixes | ACCEPT (QA: 2 S3 + 2 S4 filed) |
| 2026-10-03 | [2026-10-03-1151](sessions/2026-10-03-1151.md) | M1-2 data loader + 3 bug fixes | ACCEPT (QA: 2 S3 + 2 S4 filed) |
| 2026-10-03 | [2026-10-03-0907](sessions/2026-10-03-0907.md) | M1-1 sim core | ACCEPT (QA: 4 S3/S4 bugs filed) |
| 2026-10-03 | [2026-10-03-0826](sessions/2026-10-03-0826.md) | M0-1 solution skeleton + toolchain | ACCEPT, M0 signed off |
