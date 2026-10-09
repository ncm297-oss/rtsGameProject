# BUG-0217: The vision gate costs ~25 % of the tick in big brawls on 3-level maps (no perf row covers it)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-08-1435, task M4-3a |
| System | combat targeting (fog gate) / perf |
| Fixed by | |

## Repro
Ad-hoc timing (scratch clones of dd5b5b9 and 977db1d, not committed): `CombatScenes.MapBrawl(seed, perSide)` on the
generated 3-level map, 10 warm-up ticks then 300 timed ticks, Debug, alone, two runs each:

| Scene | dd5b5b9 avg | 977db1d avg | `UnitSeesUnit` calls / tick |
| --- | --- | --- | --- |
| seed 3, 1,000 v 1,000 | 24.4 / 24.9 ms | 30.9 / 32.4 ms | 257,563 |
| seed 5, 500 v 500 | 5.0 / 5.4 ms | 6.1 / 6.1 ms | 49,053 |
| seed 3, 250 v 250 | 3.17 / 3.18 ms | 3.63 / 3.67 ms | 21,954 |
| seed 5, 250 v 250 | 1.64 / 1.85 ms | 1.95 / 2.18 ms | 11,700 |

(The fights also play a little differently, so not every ms is the gate, but the call count times ~25 ns accounts for
most of it.) The fog update itself is cheap (0.09 ms amortized, `Fog1000v1000`).

## Expected
The brief budgets the fog update (<= 0.4 ms at 1,000 v 1,000) but nothing measures the targeting gate, and docs/03's
4 ms design budget is for 500 units: the 250 v 250 seed-3 brawl is now at 3.67 ms.

## Actual
On a multi-level map `PickTarget` calls `VisionSystem.UnitSeesUnit` for **every** candidate in the scan radius (a type
lookup, two `LevelAt` with divisions and clamps), before the best-candidate compare. `PickBuilding` already checks only
a candidate that would win.

## Notes
Cheap fixes: test visibility only when the candidate would beat the current best (as `PickBuilding` does), and hoist
the scanner's sight / level out of the loop (a same-or-lower-level candidate within sight is seen). Add a Perf row:
a 500-unit fight on a generated 3-level map.
