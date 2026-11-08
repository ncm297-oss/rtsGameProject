# BUG-1124: `AiPerfTests.EightAiSeats_Over2500Units_AThinkUnder1Ms` goes red (1.19-1.33 ms) when it runs after the slice's `EightHardSeats` row in the same process; alone it takes 0.52 ms

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open as the record of **rule 42 (Producer, 2026-11-07-0926 ACCEPT)**: on this PC a Perf failure counts only when the row fails **alone in its own process after at least 30 s idle**; the Producer and QA run the `AiPerfTests` class row by row; no code change; BUG-0764's 0.6x gates stay. Closes when the owner changes the power plan (STATE "Waiting on you" item 2) or a reference-workload ratio gate is built in a hardening session |
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

## Re-verify (2026-11-07-0926, round 2, HEAD f823ec4): the diagnosis is confirmed, and 6f6414b shows it too
QA ran a scratch test class (`QaSpin`, not committed) in fresh `git archive` exports of 6f6414b and f823ec4 (Debug, as the
suite runs). It calls `AiPerfTests.EightAiSeats_Over2500Units_AThinkUnder1Ms` directly, one process per method:

| Method | 6f6414b (no slice code) | f823ec4 |
| --- | --- | --- |
| The row 4 times in one process | 0.455, then **1.277 / 1.171 / 1.061** ms | 0.526, then **1.224 / 1.084 / 1.264** ms |
| A 4 s `Math.Sqrt` spin, then the row | **1.074** ms | **1.110** ms |
| The row alone, after 45 s idle, 3 processes back to back | 1.6 s each, all green | |
| The row 4 times, after 45 s idle | green, then 1.316 / 1.196 / 1.315 ms | |

- The base reproduces it with no Hard row in the process, and a spin that touches no sim code reproduces it. Short
  separate processes after an idle gap stay green. This is not state left by a test.
- The counter `\Processor Information(_Total)\% Processor Performance` (typeperf, 1 s samples) during the spin + row
  reads 130-205 % for the first seconds, **81-120 %** through the spin and the timed thinks, and 145-168 % once idle
  again. The core runs at about half its boost clock after a few seconds of sustained load. That matches the 2x
  slowdown (0.5 to about 1.1-1.3 ms).
- On f823ec4, run alone after the machine was idle: plain 0.509 ms, storms 0.359 ms, Hard 0.726 ms (p95 1.62, allocation
  assert green). The class run together still fails the plain row at 1.321 ms, the expected result under this diagnosis.
- No code fix is called for. What is left is a rule for Perf runs on this machine, for the Producer / owner. Options: a
  cool-down or warm-up before each timed row, a power plan that holds the clock, or gating on a ratio to a reference
  workload measured in the same process.
