# BUG-1582: `RampWallQaTests.EveryField_AtExtremes_*` fails "generation hung" under load (a 10 s wall clock in the default loop)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed (M7-H3) |
| Found | 2026-11-17-0816, task M8-2 (QA re-check, fix round 1) |
| System | tests (QA-owned): `sim/Rts.Sim.Tests/QA/RampWallQaTests.cs` |
| Fixed by | M7-H3 |

## Repro
1. Run the full loop on a busy machine: `dotnet test sim/Rts.Sim.Tests --filter "Category!=Perf"` (Stress included),
   ideally beside another suite.
2. Some `EveryField_AtExtremes_ValidateThrowsOrMapIsValid` rows can fail with `<field>=<value>: generation hung`.

## Expected
A default-loop (non-Perf) row is not decided by wall-clock time. Generating one default map takes about 0.3 s.

## Actual
- M8-2 fix round 1 (developer's full loop, 2 h 17 m): `(MineSpacing, 1.4E-45)` failed "generation hung" (19 s). Rerun
  alone it passed with the other 177 rows.
- 2026-11-14-2156 (M7-V5 QA): 12 rows failed the same way while three suites ran at once. All passed rerun alone.
- QA re-check today, full loop (4 h 46 m): it passed, but `(GoldMines, 1)` took **24 min 23 s** of recorded duration.
  Alone, all 178 rows take 0.27 s in total (three runs).

`MineSpacing` is irrelevant here: `MapGenParams.Default` places no mines. Line 118 starts the generation with
`Task.Run`, then line 123 races it against `Task.Delay(10 s)`. When the Stress rows saturate the thread pool, the
generation task can wait for a worker longer than the timer, so the row is red although nothing hung.

## Notes
- Not caused by M8-2 (no map-generator change). Rule: never loosen a threshold. The options are the Producer's call:
  (a) run the generation synchronously on the test thread and keep the hang guard as an iteration cap or a
  `[Trait("Category","Perf")]` twin; (b) start the stopwatch only once the task is running; or (c) put the row in Stress.
- Related: BUG-0012 (the reason the row exists), BUG-0015 (map-gen worst case).

## Fix (M7-H3, 2026-11-25; ruling docs/01 2026-11-25 (2): a hang guard, not a speed limit)
`EveryField_AtExtremes_ValidateThrowsOrMapIsValid` runs the generation through `HangGuard.Run`: a thread of its own (a
background thread, never the thread pool) joined with `RampWallQaTests.GenerationLimit` = 120 s (about 400 times the
0.3 s a default map takes alone). A real hang still fails the row ("generation hung (still running after 120 s on its own
thread)") instead of hanging the run: `AGenerationThatSpins_FailsItsRow_InsideTheLimit` (a generator that spins, limit
0.5 s) and `HangGuardTests`. The 178 rows passed 3 of 3 runs beside a full default loop (see the M7-H3 report).

Verified by M7-H3 QA (2026-11-25-0924): the extremes rows (191 with `HangGuardTests`) passed 3 of 3 beside a full default loop and two 30,000-tick digest runs; a generator forced to spin in the real row (scratch mutation, limit 3 s) fails each row "generation hung (still running after 3 s on its own thread)" and the run finishes.
