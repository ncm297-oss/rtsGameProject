# BUG-1124: `AiPerfTests.EightAiSeats_Over2500Units_AThinkUnder1Ms` goes red (1.19-1.33 ms) when it runs after the slice's `EightHardSeats` row in the same process; alone it takes 0.52 ms

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-11-07-0926, task M5-4c-2 (QA re-verify round 1) |
| System | Perf tests (`Ai/AiPerfTests`), AI think cost |
| Fixed by | |

## Repro
On a `git archive` export of 9f7423b (or 947a273), with the machine otherwise idle:
1. `dotnet test sim/Rts.Sim.Tests --no-build --filter "FullyQualifiedName~Rts.Sim.Tests.AiPerfTests" --logger "console;verbosity=detailed"`
2. `dotnet test sim/Rts.Sim.Tests --no-build --filter "FullyQualifiedName~AiPerfTests.EightAiSeats_Over2500Units_AThinkUnder1Ms" --logger "console;verbosity=detailed"`

## Expected
A Perf gate measures the code, not what ran before it in the process. The row passes in its class run as it does alone,
and as the class does on 6f6414b.

## Actual
| Run | Tree | `EightAiSeats_Over2500Units_AThinkUnder1Ms` avg | Result |
| --- | --- | --- | --- |
| whole class (the Hard row runs first), x2 | 9f7423b | 1.189 / 1.283 ms | red |
| whole class, x2 | 947a273 | 1.330 / 1.235 ms | red |
| the Hard row + this row only, x3 | 9f7423b | 1.497 / 1.235 / 1.393 ms | red |
| this row alone, x2 | 9f7423b | 0.532 / 0.525 ms | green |
| this row + the storms row (no Hard row), x2 | 9f7423b | 0.525 / 0.520 ms | green |
| this row alone, x2 | 6f6414b | 0.452 / 0.456 ms | green |
| whole class (no Hard row on this tree) | 6f6414b | 0.598 ms | green |

The AI's behaviour is the same in every case (12,543 commands sent on 9f7423b each time). Neither turning off dynamic
PGO (`DOTNET_TieredPGO=0`) or tiered compilation (`DOTNET_TieredCompilation=0`) nor a 256 MB gen0 budget
(`DOTNET_GCgen0size`) removes the slowdown (1.19-1.36 ms). The cause is not found. The suspects left are state the Hard
row leaves in the process (a retained world or data copy, a larger heap, cache effects).

## Notes
- The product budget holds. Alone, 8 seats with 2,628 units think in 0.52 ms (the row's seats rotate difficulties, so
  some are Hard; 0.45 ms on 6f6414b) and the 8-Hard-seat row in 0.73-0.80 ms with 0 B allocated. This is a test-isolation
  fault, not a blown budget: S3.
- It came with the slice (947a273) when the Hard row was added to the class, not with fix round 1. In this session's
  full Perf runs the machine was in a slow state: 26 red rows on 6f6414b too, mostly untouched systems. So the full run
  alone can't separate this effect from the noise; the class runs above can.
- Fix ideas for the developer: give the Hard row its own `[Collection]` / nested `Serial` class run after the others,
  release its world at the end of the test (`GC.Collect()` + `WaitForPendingFinalizers` before the next row measures),
  or find what it leaves behind.
