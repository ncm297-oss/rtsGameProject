# BUG-1116: Losing seats rebuild houses they don't need; Pale Hills 4-seat FFA seed 2 no longer ends by 144,000 ticks

| Field | Value |
| --- | --- |
| Severity | S1 (red build: a stress row that was green at the diff base is red; the slice's goal is "finish the match") |
| Status | open |
| Found | 2026-11-07-0236, task M5-4d-2 |
| System | AI (BuildOrderExecutor.ReopenLost), match end |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~HuntStressTests.FourAiSeats_OnPaleHills_EndWithTheOracleOnEveryHunt"`
2. Row `(seed: 2, teams: False)` fails: `seed 2: no result by 144,000 ticks`. It passed at the diff base 052c39e.

## Expected
The row ends within 144,000 ticks as at the base (criterion 1: non-Perf green). The brief: "a row that no longer ends is a
finding, not a bound change". docs/02 D15 C's intent: a house that adds nothing is not built.

## Actual
QA probe (same match, per-seat Build commands and reopened steps; 8155ee4):

| tick | p1 Whirlwind `raider_rush` | p2 Malazan `cadre_rush` |
| --- | --- | --- |
| 24,000 | 26 workers, 39 army, 10 tents; 0 reopens | 30 workers, 28 army, 10 billets; 0 reopens |
| 48,000 | 3 workers, 0 army, gold 4; **91 steps reopened; 96 + 19 tent Builds, 9 more raider camps** | defeated; **75 reopened; 90 billet Builds, 10 crossbow ranges** (hall lost at 38,240) |
| 144,000 | unchanged: 3 workers, 12 tents, 2 raider camps, wood 4,413 | — |

p0 (13 army, gold 6) and p3 (0 army) never reach a wave again; p1's rebuilt buildings stay known, so nobody hunts its 3
workers. About 90 % of the rebuild Builds are **houses** (115 tents, 90 billets) for seats whose population is a small fraction
of their cap. `ReopenLost` reopens a House step whenever the seat holds fewer houses than done House steps, unless it is at
the hard cap. A seat with 3 workers and 10 tents has far more cap than it needs, so a lost tent adds nothing back, yet
each loss draws a new Build (one every ~12 s under siege) and keeps a fresh enemy target on the map.

## Notes
- The dev's report says the same: the BUG-1068 seed-2 stall (gold runs out, wave thresholds can't be reached) moved here
  by rebuilding.
- Proposed cheap fix, the Producer's call (a rule change, a few lines): reopen a House step only when the economy's house
  rule would want a house (`HalfPopCap - HalfPop <= ai.houseHalfPopMargin`, the condition `EconomyManager.House` already
  uses), on top of the hard-cap test. That keeps D15 C's intent and removes most of the churn. Then re-run this row. If it
  still stalls, the remaining cause is BUG-1068's gold starvation, and marking the row a known case is a loosening that needs
  the Producer's (or the owner's) explicit ruling.
- Not the look: 0 thinks on a look point for every seat over the whole match.
