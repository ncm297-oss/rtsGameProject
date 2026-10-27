# BUG-1022: The gather shift moves workers 4x as often (1.4 -> 5.6 a minute); about 7.5 % less gathered by 10:00

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open (measured in M5-H4, not shipped: the Producer's tuning call) |
| Found | 2026-10-18-1814, task M5-4g (QA) |
| System | AI economy: `AiPlayer.WantedGoldShare` in `EconomyManager.Gatherers` / `Rebalance` (`ai.reserveShift`) |
| Fixed by |  |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~ReservePairingsStressTests.Pairing" --logger "console;verbosity=detailed"`:
   each `ROW` prints `switches/min` (workers whose AI gather kind flipped gold <-> wood at a think, alive with the same
   handle) and `shareFlips` (thinks at which the wanted share changed). The same file on the base f531dd9.
2. A shift-0 twin (QA scratch, not kept: shipped data with `ai.reserveShift` 0 v 0.25, the 27 pairing runs, gathered gold +
   wood from `World.GatheredGold` / `GatheredWood`).

## Expected
The brief's QA focus: the shift should move workers toward a lacking kind without oscillating.

## Actual
27 pairing runs, 54 sides, Normal v Normal, 20 min:

| | base | M5-4g |
| --- | --- | --- |
| gatherer kind switches a minute | avg 1.4 (0.4-3.2) | avg 5.6 (1.95-9.25) |
| wanted-share changes a match | 0 | avg 22 (max 36) |
| thinks holding a reserve | - | avg 39 % (max 74 %) |

Each change of the wanted share (0.35 / 0.6 / 0.85 as the reserved step is short of wood, both, gold) walks one gatherer a
think to the other kind; each walk drops what the worker carries (BUG-0594 picks the least loaded) and costs the walk.
With the reserve kept and only the shift turned off (`reserveShift` 0 v 0.25, same 27 runs):

| | shift 0 | shift 0.25 |
| --- | --- | --- |
| gathered (gold + wood) by 10:00, avg a side | 4,998 | 4,623 (-7.5 %) |
| by 20:00 | 8,940 | 9,020 (+0.9 %) |
| Age II reached, avg | 9:04 | 8:40 |
| sides defeated / with no wave | 15 / 12 | 11 / 11 |

So the shift does what it is for (Age II ~24 s earlier, fewer defeats) at the price of the early economy and a lot of
walking. Not a correctness bug: a tuning observation for the Producer.

## Notes
- A hysteresis (keep the shifted share until the step is paid or changes, rather than re-deciding from the purse each
  think) or shifting only the idle pass (not `Rebalance`) would likely cut the churn; measure before choosing.
- The wave losses of BUG-1021 come from the reserve, not the shift: with shift 0 the no-wave count is 12 (11 with it).

## Re-check (2026-10-18-1814, fix round 1, 57dfa22)
With the reserve gated, it is held on 21.8 % of thinks (39 % in M5-4g). Gatherer kind switches average **3.93 a minute**
(M5-4g 5.57, base 1.4) and the wanted share changes about **11.8 times a match** (M5-4g 22.4). Still S4, still open.

## Re-check after fix round 2 (c76bc5f)
- 27 pairing runs: gatherer kind switches 4.04 a minute on average (max 7.25), base 1.40 and round 1 3.93. The wanted share changes 10.6 times a match. Still open, S4.

## Measured (M5-H4, session 2026-10-26-2108)
The brief: ship a variant only if gathering by 10:00 recovers without losing Age II or waves. The 27 pairing runs (shipped
data, `ROW` lines now print `gathered10` / `gathered20`), each variant a scratch build, nothing of it kept:

| 54 sides | shipped | shift off | idle pass only | band (no reserve: rebalance only back into goldShare ± shift) |
| --- | --- | --- | --- | --- |
| gathered by 10:00, avg | 5,068 | 5,179 | 5,176 | 5,076 |
| gathered by 20:00, avg | 9,693 | 9,735 | 9,861 | 9,747 |
| switches / min | 4.04 | 1.45 | 3.03 | 2.78 |
| shareFlips | 10.6 | 9.9 | 10.1 | 10.1 |
| alive without Age II | 4 / 49 | 6 / 49 | 2 / 49 | 3 / 49 (2 new) |
| Age II vs shipped, paired | - | +16.8 s | +14.3 s | -10.4 s |
| no wave; Malazan / Whirlwind waves | 3; 81 / 117 | 4; 83 / 116 | 3; 83 / 128 | 3; 85 / 123 |
| defeats M / W | 5 / 0 | 4 / 1 | 5 / 0 | 5 / 0 |

The idle-pass-only variant recovers the 10:00 economy fully and the waves rise, but Age II comes 14 s later on average
(most of the shift's gain); the band keeps Age II but recovers no gathering. Neither passes the rule, so nothing shipped.
A hysteresis across thinks needs a saved field (format change), not tried. Table also in docs/03 "Implementation (M5-4g)".
