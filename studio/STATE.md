# Studio state

The dashboard. The Producer rewrites it at the end of every session. **Owner: read "Waiting on
you" first.**

_Last updated: 2026-10-03 (session 2026-10-03-0826, ACCEPT)._

## Waiting on you

- [ ] **Sign off M0.** All required M0 criteria are met and the studio is on HOLD. To check it:
      `git pull`, then `dotnet build RtsGame.sln`, `dotnet test sim/Rts.Sim.Tests`, and
      `powershell -File tools/qa/smoke.ps1` (expect `PASS`). Optionally `& $env:GODOT --path game`
      opens an empty Forward+ window; the console prints `Rts.Sim 0.0.1`. Feedback wanted: any
      toolchain friction on your machine, anything in CLAUDE.md's Commands that doesn't work for
      you. When happy, add `M0 accepted` under **New** in `studio/inbox.md` and push.
- [ ] Optional M0 item: Godot MCP server for Claude Code (your install; see SETUP.md). Not needed
      before M2.
- [ ] Still open from setup: confirm the scheduled **RTS studio session** uses the **auto**
      permission mode (see [docs/07](../docs/07-studio-workflow.md#permissions-and-access)).

## Now

| Field | Value |
| --- | --- |
| Milestone | M0 — Environment & skeleton (criteria met, awaiting sign-off) |
| Next task | M1-1: `World`, generational handles, seeded RNG, `SimMath`, command queue, fixed tick (after sign-off) |
| Gate | **HOLD** (milestone end, `stop_at_milestone_end` = yes) |
| Tools on this PC | Godot 4.7.2 .NET, .NET SDK 8.0.425, Git 2.53 + LFS 3.7; `GODOT` user variable set |
| Build | green (0 warnings, 0 errors) |
| Tests | 4/4 green; smoke pass (`Rts.Sim 0.0.1`, no ERROR lines) |
| Open bugs | 2 (S1: 0, S2: 0, S3: 1, S4: 1) — none blocking |
| Sessions today | 1 / 4 |
| Last session | 2026-10-03-0826 · M0-1 · ACCEPT |

## Milestone progress

| Milestone | Criteria met | Status |
| --- | --- | --- |
| M0 | 7 / 7 required (optional MCP item open) | Awaiting owner sign-off |
| M1 | 0 / 8 | Next after sign-off |
| M2-M9 | — | Planned |

## Backlog (outside the current milestone)

- BUG-0001 (S3): make `tools/qa/smoke.ps1` the smoke gate in CLAUDE.md and the game-dev
  definition of done. Do at the start of M1.
- BUG-0002 (S4): `.sln` Release config maps RtsGame to Debug. Fix with `tools/export.ps1` (M6).
- Consider `TreatWarningsAsErrors` in `Rts.Sim.Tests` (M1-1).
- Evaluate a Godot MCP server at M2 (see SETUP.md).
- .NET 8 support ends 2026-11-10: move to the next LTS at M6 (docs/03).

## Recent sessions

| Date | Session | Task | Result |
| --- | --- | --- | --- |
| 2026-10-03 | [2026-10-03-0826](sessions/2026-10-03-0826.md) | M0-1 solution skeleton + toolchain | ACCEPT, M0 criteria met |
