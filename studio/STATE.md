# Studio state

The dashboard. The Producer rewrites it at the end of every session. **Owner: read "Waiting on
you" first.** "For your review" is non-blocking: things the studio decided or finished on its own.

_Last updated: 2026-10-03 (session 2026-10-03-1151, PLAN)._

## Waiting on you

- Nothing blocking. The studio runs on autopilot (chain sessions, self sign-off) until the end
  of the roadmap or a cap/incident stops it.

## For your review

- Optional M0 item: Godot MCP server for Claude Code (your install; see SETUP.md). Not needed
  before M2.
- 2026-10-03: STATE.md restructured per your note (this section added).

## Now

| Field | Value |
| --- | --- |
| Milestone | M1 — Core sim, no graphics (started 2026-10-03) |
| Current task | **M1-2: data loader for `game/data/`** (first commit fixes BUG-0003/0004/0006) |
| Gate | **GO** (session 2026-10-03-1151 in progress) |
| Tools on this PC | Godot 4.7.2 .NET, .NET SDK 8.0.425, Git 2.53 + LFS 3.7; `GODOT` user variable set |
| Build | green on `main` @ 8df3e66 (0 warnings, 0 errors) |
| Tests | 78 passed, 0 failed, 3 skipped (non-Perf filter; 4th skip is a Perf test); `tools/qa/smoke.ps1` PASS |
| Open bugs | 5 (S1: 0, S2: 0, S3: 3, S4: 2) — none blocking |
| Sessions today | 3 / 10 (this one counts) |
| Last session | 2026-10-03-0907 · M1-1 sim core · ACCEPT |

## Milestone progress

| Milestone | Criteria met | Status |
| --- | --- | --- |
| M0 | 7 / 7 required | **Done** 2026-10-03 (optional MCP item open) |
| M1 | 1 / 8 | In progress (M1-1 accepted; M1-2 running) |
| M2-M9 | — | Planned |

## Backlog (outside the current task)

- M1-3 terraced heightmap + nav grid + spatial hash; M1-4 flow fields + steering (fix BUG-0005
  here); M1-5 scenario test; M1-6 replay + determinism golden; M1-7 perf test; M1-8 headless CLI.
- Data loader follow-ups (M3/M4/M5): buildings, techs, abilities, statuses, `ai.json`, maps.
- BUG-0005 (S3) O(n^2) command sort: fix with per-player buckets at M1-4.
- BUG-0002 (S4): `.sln` Release config maps RtsGame to Debug. Fix with `tools/export.ps1` (M6).
- Enqueue always stamps `TickNumber + 1` (up to 50 ms extra input latency); revisit at M2-1 if felt.
- Evaluate a Godot MCP server at M2 (see SETUP.md).
- .NET 8 support ends 2026-11-10: move to the next LTS at M6 (docs/03).

## Recent sessions

| Date | Session | Task | Result |
| --- | --- | --- | --- |
| 2026-10-03 | 2026-10-03-1151 | M1-2 data loader | in progress |
| 2026-10-03 | [2026-10-03-0907](sessions/2026-10-03-0907.md) | M1-1 sim core | ACCEPT (QA: 4 S3/S4 bugs filed) |
| 2026-10-03 | [2026-10-03-0826](sessions/2026-10-03-0826.md) | M0-1 solution skeleton + toolchain | ACCEPT, M0 signed off |
