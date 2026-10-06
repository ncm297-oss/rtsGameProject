# BUG-0046: Walker positions still depend on the neighbors' spawn (slot) order

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-05-1609, task M1-4d-3 (pre-existing on base 7f741f1) |
| System | movement (`Constrain` sequential clips; possibly the chain BFS) |
| Fixed by | |

## Repro
1. Remove the `Skip` from the skipped cases of `QA/CrowdRoutingQaTests.DetourPastAMixedEnemyCluster_AnySpawnPermutation_BitEqualWalkerPath`
   and `ManyWalkersDetouringAnEnemyCluster_AnySpawnPermutation_BitEqualPositions` and run them.
2. Scenario: a walker (or 16 walkers of one player) crossing a cluster of 10-12 Idle enemy units of
   mixed radii; the same spawns applied in different orders (so the same units get different slots).
   Positions are compared by unit identity, tick by tick.

## Expected
docs/03 (M1-4d-3) and the brief's QA focus: the detour's side choice doesn't depend on slot order;
the order-free fixed-point sums were added so positions are bit-equal in any spawn order.

## Actual
- One walker, 6 seeds x 7 orders: bit-equal on 5 seeds; seed 6 differs from tick 126
  (`<40.941322, 42.289536>` vs `<40.940617, 42.307663>`). Base: also 5 of 6 (seed 3 differs).
- 16 walkers, 3 seeds: all differ from about tick 133 (base: tick 122), by a few millimeters at first.
- The detour side never flipped in these runs; the drift starts while walkers slide along the
  cluster, consistent with `Constrain` clipping a step against walls one at a time in slot order
  (documented as "the slot-order clips" in BUG-0038).

Same-seed determinism is not affected (two sims with the same spawns hash equal every tick in every
scenario tried). This is order-independence only: a save/load or replay is fine, but "no result
depends on slot order" is not true of movement as a whole.

## Notes
Low priority while replays rebuild the same slots. Worth a docs/03 sentence so nobody relies on full
order independence (for example in a future "re-spawn from snapshot" feature).

## Re-check M1-9 (2026-10-06-0905, commit 6d1cbfd): still open, a documented known limit
The developer built and measured a slot-free wall-clip order (bit-equal on the four permutation rows) but did not land it: it re-rolled three fitted crowd bounds. docs/03 "Known limits (M1)" now lists the slot dependence. The four QA rows stay skipped under this bug; landing it with re-fitted bounds is the Producer's call. BUG-0071 adds one more slot-order dependence (shove-pass plug cache).

- Producer (ACCEPT 2026-10-06-0905): not landed, kept as a known limit (docs/03 "Known limits (M1)", docs/01 change log). Reason: the sort buys spawn-order independence nothing relies on (replays keep slots) and re-fitting three crowd bounds on wider sweeps is a session of measurement; revisit together with the crowd-cost work after M4, which re-rolls every crowd outcome anyway. The 4 QA rows stay skipped.
