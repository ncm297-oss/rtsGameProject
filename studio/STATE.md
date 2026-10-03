# Studio state

The dashboard. The Producer rewrites it at the end of every session. **Owner: read "Waiting on
you" first.**

_Last updated: 2026-10-03 (session 2026-10-03-0907, PLAN)._

## Waiting on you

- Nothing blocking. M0 is signed off; the studio is working on M1.
- [ ] Optional M0 item: Godot MCP server for Claude Code (your install; see SETUP.md). Not needed
      before M2.

## Now

| Field | Value |
| --- | --- |
| Milestone | M1 — Core sim, no graphics (started 2026-10-03) |
| Current task | M1-1: sim core (`World`, generational handles, seeded RNG streams, `SimMath`, command queue, 20 Hz tick, state hash) + BUG-0001 doc fix |
| Gate | **GO** |
| Tools on this PC | Godot 4.7.2 .NET, .NET SDK 8.0.425, Git 2.53 + LFS 3.7; `GODOT` user variable set |
| Build | green on main `4fc0120` (0 warnings, 0 errors) |
| Tests | 4/4 green; `tools/qa/smoke.ps1` PASS (`Rts.Sim 0.0.1`, no ERROR lines) |
| Open bugs | 2 (S1: 0, S2: 0, S3: 1, S4: 1) — none blocking |
| Sessions today | 2 / 4 (this one counts) |
| Last session | 2026-10-03-0826 · M0-1 · ACCEPT |

## Milestone progress

| Milestone | Criteria met | Status |
| --- | --- | --- |
| M0 | 7 / 7 required | **Done** 2026-10-03 (optional MCP item open) |
| M1 | 0 / 8 | In progress (M1-1 running) |
| M2-M9 | — | Planned |

## Backlog (outside the current task)

- M1-2 data loader for `game/data/` with validation; M1-3 terraced heightmap + nav grid +
  spatial hash; M1-4 flow fields + steering; M1-5 scenario test; M1-6 replay + determinism;
  M1-7 perf test; M1-8 headless CLI.
- BUG-0002 (S4): `.sln` Release config maps RtsGame to Debug. Fix with `tools/export.ps1` (M6).
- Evaluate a Godot MCP server at M2 (see SETUP.md).
- .NET 8 support ends 2026-11-10: move to the next LTS at M6 (docs/03).

## Recent sessions

| Date | Session | Task | Result |
| --- | --- | --- | --- |
| 2026-10-03 | 2026-10-03-0907 | M1-1 sim core | in progress |
| 2026-10-03 | [2026-10-03-0826](sessions/2026-10-03-0826.md) | M0-1 solution skeleton + toolchain | ACCEPT, M0 signed off |
