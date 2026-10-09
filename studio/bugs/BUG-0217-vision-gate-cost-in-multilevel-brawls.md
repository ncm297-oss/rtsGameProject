# BUG-0217: The vision gate costs ~25 % of the tick in big brawls on 3-level maps (no perf row covers it)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-08-1435, task M4-3a |
| System | combat targeting (fog gate) / perf |
| Fixed by | 34931b8 (`CombatSystem.PickTarget` asks `VisionSystem.UnitSeesUnit` only of a would-be winner); perf rows `Vision/VisionGatePerfTests`; QA order regressions `QA/VisionGateOrderQaTests` |

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

## QA re-check (2026-10-08-1435, round 1)
- **Same pick, by argument:** before, a candidate replaced the best iff (seen AND better than best); now iff (better
  than best AND seen). `UnitSeesUnit` / `FogStore.SeesUnit` / `LevelAt` are pure reads and best / bestTier / bestD2
  change only on a replacement, so the two conjunctions accept the same candidates in the same order. Visibility
  can't change mid-scan (the fog only changes in phase 12). `PickBuilding` is untouched.
- **Same pick, by experiment:** scratch clones of 34931b8 with and without the reorder (6ed39a0's `CombatSystem.cs`):
  per-tick `StateHash` of the hostile fog fuzz on seeds 11-22 (1,200 ticks each, ranged, Catapults, holds, towers on
  every level) and `MapBrawl` 250 v 250 on seeds 3, 5, 7, 9 (600 ticks): 16,816 hashes, byte-identical files.
- **Edges (new permanent rows, `QA/VisionGateOrderQaTests`, 5 cases):** an unseen plateau enemy scanned before / after
  a farther seen one; an exact distance tie with the unseen one in the lower slot and scanned first; an unseen tier-0
  last attacker scanned first against a seen tier-1; seen far, unseen near, seen middle in that scan order. Green on
  both the old and the new gate; mutation-checked: a scan that moves best distance / tier before the vision check
  fails 4 / 5, a scan with no gate fails 5 / 5.
- **Perf (Debug, alone, interleaved old / new, two runs):** 1,000 v 1,000 seed 3: 29.48 / 29.45 -> 23.38 / 23.40 ms
  (dd5b5b9 without fog measured 24.4-24.9 in round 0); 250 v 250 seed 3: 3.59 / 3.58 -> 3.08 / 3.09 ms (budget 4).
  Worktree runs: 3.11 / 3.09 and 23.51 / 23.42 ms. Verified fixed.
