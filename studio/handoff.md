# Handoff: next session brief

Written by the Producer at the end of each session for the next one. The next session's
Producer starts from this, verifies it against the repo, and then plans.

_Written: 2026-10-03 (session 2026-10-03-0907, PLAN)._

## Where we are

M0 is **Done** (owner sign-off 2026-10-03). Main `4fc0120` is green: build 0/0, 4/4 tests,
`tools/qa/smoke.ps1` PASS. M1 started; the sim assembly still contains only `SimInfo`.

## Current session plan

TASK_ID: M1-1
TASK_TITLE: Build the sim core: World, generational handles, seeded RNG streams, SimMath, command queue, fixed tick

Goal: Lay the deterministic foundation every later M1 system builds on: a `World` of struct
arrays addressed by generational handles, seeded RNG streams, deterministic trig, a command
queue applied at the start of each 20 Hz tick, and a 64-bit state hash. Also close BUG-0001 by
making `tools/qa/smoke.ps1` the smoke gate in CLAUDE.md.

Scope:
- `sim/Rts.Sim`: `SimConstants` (TickMs = 50, TicksPerSecond = 20), `EntityHandle`,
  `UnitStore` (SoA arrays + free list + generations), `SimRng` (PCG32 or xorshift64*, struct),
  `RngStream` ids (MapGen, Combat, Ai per player), `SimMath` (Sin, Cos, Atan2, Sqrt wrapper,
  deterministic), `Command` + `CommandKind` (`Noop`, `SpawnUnit` for tests/dev), `CommandQueue`,
  `World`, `Simulation` (`Enqueue`, `Tick`, `TickNumber`, `StateHash()`), `SimConfig` (seed,
  player count, unit capacity).
- Tests in `sim/Rts.Sim.Tests` (see list below). Add `TreatWarningsAsErrors` to the test csproj.
- CLAUDE.md: smoke command -> `powershell -File tools/qa/smoke.ps1`; definition-of-done item 2
  says the script must print PASS; "Current milestone" line -> M1.
- docs/03: record the chosen RNG algorithm and SimMath approach in the Determinism section.
- OUT: data loader/JSON, terrain, nav grid, spatial hash, movement, Move command, any Godot
  changes, replay file format, perf benchmarks.

Acceptance criteria:
1. `EntityHandle(int Index, int Generation)`; `UnitStore.Alloc()` returns handles, `Free(h)`
   bumps the slot generation; `IsAlive(stale)` is false after reuse; capacity comes from
   `SimConfig`, never a literal in the store. Test proves alloc/free/reuse/stale.
2. `SimRng`: same seed -> identical sequence; different stream ids from the same seed ->
   different sequences; drawing from one stream leaves the others unchanged. Tests prove all
   three. `NextInt(minInclusive, maxExclusive)` and `NextFloat()` in [0,1) exist.
3. `SimMath.Sin/Cos/Atan2` are table or polynomial based (no `Math.Sin`/`Math.Cos`/`Math.Atan2`
   in sim source; extend `ArchitectureTests` forbidden list with `Math.Sin|Math.Cos|Math.Atan2|
   Math.Atan|MathF.Sin|MathF.Cos|MathF.Atan2|MathF.Atan`). Tests: max abs error vs `Math.*`
   <= 1e-3 over [-4pi, 4pi] for Sin/Cos and <= 1e-3 rad for Atan2 over a 64x64 grid of inputs
   incl. axes and zero; results are bit-identical across two calls.
4. `Simulation.Enqueue(cmd)` stamps the command for `TickNumber + 1` with an increasing per-player
   sequence; `Tick()` applies commands for the current tick sorted by (player, sequence) and then
   increments `TickNumber`. Tests: commands enqueued during tick N apply in tick N+1; two players
   enqueued interleaved apply player 0 first; `SpawnUnit` order determines handle indices.
5. `Simulation.StateHash()` returns a 64-bit hash (FNV-1a or similar, implemented in sim) over
   tick number, every live unit field, RNG states and pending commands. Test: two simulations with
   the same seed and command list give equal hashes after 1000 ticks; a different seed or one
   extra command gives a different hash.
6. `Tick()` with no commands and with 100 queued `SpawnUnit` commands allocates 0 bytes
   (`GC.GetAllocatedBytesForCurrentThread()` delta in a test, after a warm-up tick).
7. `dotnet build RtsGame.sln` 0 warnings / 0 errors; `dotnet test` green; `tools/qa/smoke.ps1`
   PASS; `Rts.Sim.Tests.csproj` has `TreatWarningsAsErrors` true.
8. CLAUDE.md Commands block shows `powershell -File tools/qa/smoke.ps1` as the smoke run and the
   milestone line says M1; docs/03 Determinism names the RNG and trig implementation.

Design references: docs/03 Tick model (phase order, commands sorted by player+sequence, hash as
phase 14), Determinism (RNG streams, no forbidden APIs, floats OK, SimMath), Entity model
(`EntityHandle`, SoA `UnitStore` fields; include at least Position, PrevPosition, Velocity,
Facing, Owner, TypeId, Alive now). CLAUDE.md rules 2, 4, 5.

Tests required: `UnitStoreTests`, `SimRngTests`, `SimMathTests`, `SimulationTests` (tick
counter, command order, next-tick apply), `StateHashTests` (determinism), `AllocationTests`
(zero bytes per tick); existing `ArchitectureTests` extended and green.

Constraints: no `Dictionary`/`HashSet` iteration in tick code; no LINQ, closures or `new` in
`Tick()` or command apply (pre-size arrays from `SimConfig`, insertion-sort commands in place);
`Rts.Sim` keeps zero package references; no `System.Random`, `HashCode`, `DateTime`; keep the
whole change under ~800 lines.

QA focus: handle churn (1M alloc/free cycles, assert no stale handle ever resolves and the free
list never double-frees); RNG stream independence under interleaved draws and seed 0 / -1 /
int.MaxValue; SimMath accuracy at huge angles (1e6 rad) and exact axis inputs for Atan2
(0,0 / 0,-1 / -0f); command flood (10 000 commands in one tick, all players) ordering and
allocation; hash sensitivity (flip one float in one unit -> different hash); run the
determinism test 20x in a loop; confirm `tools/qa/smoke.ps1` still passes.

## Next task candidates (after M1-1)

- M1-2 data loader for `game/data/` with validation and a load-all test (System.Text.Json
  source generation; seconds -> ticks; ids -> ints).
- M1-3 terraced heightmap generator + nav grid + spatial hash.
- M1-4 flow fields + steering; then scenario test, replay/determinism, perf, headless CLI.

## Watch out for

- Headless Godot does not compile C# and exits 0 even when `Main.cs` fails to load; use
  `tools/qa/smoke.ps1` (BUG-0001, being fixed in M1-1 docs).
- `$env:GODOT` can be empty in the non-interactive shell; fall back to
  `[Environment]::GetEnvironmentVariable('GODOT','User')`.
- `ArchitectureTests.SimSource_UsesNoForbiddenApis` greps sim source; new sim code must adjust
  the test only by adding patterns, never by weakening it.
- `.sln` Release config builds the game project in Debug (BUG-0002, S4): irrelevant until M6.
