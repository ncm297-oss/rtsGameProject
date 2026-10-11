# BUG-0542: PlayerView perf row gates a banded layout only; the interleaved worst case is ~1.9x the 0.3 ms budget

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-11-0314, task M5-1 |
| System | AI `PlayerView`, perf |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~PlayerViewPerfTests.EightPlayers" --logger "console;verbosity=detailed"`
   (alone, twice), and once with `-c Release` in a scratch clone.

## Expected
Brief M5-1 scope 5: "enumerating a `PlayerView` for 8 players at 2,500 units under 0.3 ms a tick". No layout is named.

## Actual
| Layout | Answers | Debug (2 runs) | Release |
| --- | --- | --- | --- |
| Banded (gated) | 5,260 | 0.230 / 0.231 ms avg (worst tick 0.405 / 0.384) | 0.247 ms |
| Interleaved (reported only) | 19,389 | 0.584 / 0.565 ms | 0.384 ms |
| Whole read, banded / interleaved | | 0.47 / 0.98 ms | 0.43 / 0.60 ms |

The gate passes; the worst case the brief's sentence also covers is over budget in both builds. The developer flagged it as
a deviation needing a ruling (docs/01 2026-10-11 row (d), docs/03 "Implementation (M5-1)", Cost). The cost is the answers
(~20-30 ns a record), so a faster loop won't close a 2x gap.

## Notes
Producer ruling wanted: accept the banded gate (an AI thinks every 10-40 ticks, so per-tick cost is a fraction of this; the
docs/03 average tick budget is 4 ms), or add a gated worst-case row with its own number. Not filed S2 because the row the
brief asked for exists and is green, and the layout choice was disclosed.
