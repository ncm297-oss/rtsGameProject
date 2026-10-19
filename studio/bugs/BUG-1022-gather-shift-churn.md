# BUG-1022: The gather shift moves workers 4x as often (1.4 -> 5.6 a minute); about 7.5 % less gathered by 10:00

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-18-1814, task M5-4g (QA) |
| System | AI economy: `AiPlayer.WantedGoldShare` in `EconomyManager.Gatherers` / `Rebalance` (`ai.reserveShift`) |
| Fixed by | |

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
