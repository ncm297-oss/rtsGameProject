# Studio state

The dashboard. The Producer rewrites it at the end of every session. **Owner: read "Waiting on
you" first.**

_Last updated: 2026-10-03 (session 2026-10-03-0907, ACCEPT)._

## Waiting on you

- Nothing blocking. M1 is 1/8; the studio continues with M1-2 (data loader).
- [ ] Optional M0 item: Godot MCP server for Claude Code (your install; see SETUP.md). Not needed
      before M2.

## Now

| Field | Value |
| --- | --- |
| Milestone | M1 — Core sim, no graphics (started 2026-10-03) |
| Current task | next: M1-2 data loader for `game/data/` (+ BUG-0003/0004/0006 quick fixes) |
| Gate | **GO** |
| Tools on this PC | Godot 4.7.2 .NET, .NET SDK 8.0.425, Git 2.53 + LFS 3.7; `GODOT` user variable set |
| Build | green on `studio/2026-10-03-0907` @ ab41554 (0 warnings, 0 errors); conductor merges to main |
| Tests | 83 passed, 0 failed, 4 skipped (known-bug regression tests); `tools/qa/smoke.ps1` PASS |
| Open bugs | 5 (S1: 0, S2: 0, S3: 3, S4: 2) — none blocking |
| Sessions today | 2 / 4 (this one counts) |
| Last session | 2026-10-03-0907 · M1-1 sim core · ACCEPT |

## Milestone progress

| Milestone | Criteria met | Status |
| --- | --- | --- |
| M0 | 7 / 7 required | **Done** 2026-10-03 (optional MCP item open) |
| M1 | 1 / 8 | In progress (M1-1 accepted; M1-2 next) |
| M2-M9 | — | Planned |

## Backlog (outside the current task)

- M1-3 terraced heightmap + nav grid + spatial hash; M1-4 flow fields + steering (fix BUG-0005
  here); M1-5 scenario test; M1-6 replay + determinism golden; M1-7 perf test; M1-8 headless CLI.
- BUG-0003 (S3) Sin/Cos out of range above ~1e8 rad; BUG-0004 (S3) failed Enqueue bumps
  sequence; BUG-0006 (S4) SpawnUnit accepts NaN/Inf. All few-line fixes with ready QA tests:
  first commit of M1-2.
- BUG-0005 (S3) O(n^2) command sort: fix with per-player buckets at M1-4.
- BUG-0002 (S4): `.sln` Release config maps RtsGame to Debug. Fix with `tools/export.ps1` (M6).
- Enqueue always stamps `TickNumber + 1` (up to 50 ms extra input latency); revisit at M2-1 if felt.
- Evaluate a Godot MCP server at M2 (see SETUP.md).
- .NET 8 support ends 2026-11-10: move to the next LTS at M6 (docs/03).

## Recent sessions

| Date | Session | Task | Result |
| --- | --- | --- | --- |
| 2026-10-03 | [2026-10-03-0907](sessions/2026-10-03-0907.md) | M1-1 sim core | ACCEPT (QA: 4 S3/S4 bugs filed) |
| 2026-10-03 | [2026-10-03-0826](sessions/2026-10-03-0826.md) | M0-1 solution skeleton + toolchain | ACCEPT, M0 signed off |
