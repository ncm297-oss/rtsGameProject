# Handoff: next session brief

Written by the Producer at the end of each session for the next one. The next session's
Producer starts from this, verifies it against the repo, and then plans.

_Written: 2026-10-03 (session 2026-10-03-0826, ACCEPT)._

## Where we are

M0 is functionally complete: `RtsGame.sln`, `sim/Rts.Sim` (pure net8.0, warnings-as-errors),
`sim/Rts.Sim.Tests` (xUnit, 4 tests incl. architecture guards), and `game/` (Godot 4.7.2 C#)
build, test, import and boot green; the headless smoke prints `Rts.Sim 0.0.1`. Gate is **HOLD**
until the owner writes `M0 accepted` in `studio/inbox.md`. Verified toolchain: .NET SDK 8.0.425,
Godot 4.7.2.stable.mono, Git 2.53, Git LFS 3.7.1.

## Next session

1. Check the inbox for `M0 accepted`. Without it, STOP (milestone HOLD); do not start M1.
2. On sign-off: mark M0 **Done** in `docs/05-roadmap.md`, finalize the retro, set M1 to **Next**,
   and plan **M1-1**.

## Next task candidates (M1, in order)

- **M1-1 — Sim core:** `World`, entity stores with generational handles, seeded RNG streams,
  `SimMath` (deterministic trig), command queue, `Simulation.Tick()` at 20 Hz. Tests: handle
  reuse/generation, RNG reproducibility by seed and stream, tick counter, command apply order.
  Fold in BUG-0001's doc fix (CLAUDE.md smoke command -> `tools/qa/smoke.ps1`) and consider
  `TreatWarningsAsErrors` in the test project. Keep under ~800 lines: no data loader, no terrain.
- **M1-2 — Data loader** for `game/data/` with validation and a load-all test.
- **M1-3 — Terraced heightmap + nav grid + spatial hash.**
- Then flow fields/steering, scenario test, replay/determinism, perf, headless CLI.

## Watch out for

- Headless Godot does not compile C# and exits 0 even when `Main.cs` fails to load; always
  `dotnet build` first and check the log (or run `tools/qa/smoke.ps1`). BUG-0001.
- `$env:GODOT` can be empty in the non-interactive shell; fall back to
  `[Environment]::GetEnvironmentVariable('GODOT','User')`.
- `ArchitectureTests.SimSource_UsesNoForbiddenApis` greps sim source for `Godot.`, `System.Random`,
  `DateTime`, `Stopwatch`, `Guid.NewGuid`, `HashCode.`, `Parallel.`, `Vector<`. New sim code
  that legitimately needs a name that matches (unlikely) must adjust the test, not bypass it.
- `.sln` Release config builds the game project in Debug (BUG-0002, S4): irrelevant until M6.
- Open bugs: BUG-0001 (S3), BUG-0002 (S4). No S1/S2.
