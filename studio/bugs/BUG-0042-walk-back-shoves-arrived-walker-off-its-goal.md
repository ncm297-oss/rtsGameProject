# BUG-0042: Walk-back undoes the corridor-pair fix: the pair walks home and shoves the arrived walker back off its goal

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | fixed |
| Found | 2026-10-05-1609, task M1-4d-3 |
| System | movement (walk-back, chain shove, lone-anchor yield) |
| Fixed by | M1-4d-3 fix round 1 (6abd200): a walking-back unit never pushes; no detour and only room-side sidesteps in 1-cell passages. `QA/CrowdRoutingQaTests.WalkerPastAParkedPair_StillAtItsGoalOnceTheWalkBacksSettle` (5 rows) and `ParkedPairInACorridor_VariedSeeds_WalkerArrives` (20 seeds) un-skipped, pass |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~CrowdRoutingQaTests.WalkerPastAParkedPair_StillAtItsGoalOnceTheWalkBacksSettle"`
   (remove the `Skip` first). This is the BUG-0033 repro
   (`QA/ShoveQaTests.WalkerInOneCellCorridor_PastAParkedFriendlyPair_Arrives`: radius-0.9 units, a pair
   parked at cell 8 of a 1-cell corridor, a walker from cell 2 to cell 14), but ticked until nothing
   moves and no walk-back is pending, not just until the walker first goes Idle.
2. Also `CrowdRoutingQaTests.ParkedPairInACorridor_VariedSeeds_WalkerArrives` (seeds 1-20: radii,
   parking spot, start and goal vary).

## Expected
Brief M1-4d-3 target row: "Parked friendly pair in a 1-cell corridor: walker arrives". Once everything
has settled, the walker is still at its goal (within `ArrivalDistance`) and keeps its goal cell.

## Actual
The walker pushes the pair along the corridor and arrives (0.95-0.98 m from its goal). The pair, cut
off its point, waits `WalkBackDelayTicks` and walks back. Walking home, the pair is blocked by the
arrived walker, which stands alone on its point, so it yields to them (lone-anchor/chain rule) and
gets shoved all the way back. The walker loses its goal, uses its own walk-back, gets stuck behind
the re-parked pair and gives up. Final state for goal x 12 / 14 / 16 / 18 / 20:

```
goal x 14: walker first Idle at tick 257, 0.98 m from its goal; settled after 491 ticks:
  walker 11.96 m from its goal (goal cell -1), pair back home (goal cells 56 / 56); walk-backs 3
goal x 12: 7.98 m short    goal x 16: 15.90 m    goal x 18: 20.04 m    goal x 20: 23.88 m
```

That is the same final outcome as before M1-4d-3 (walker without its goal about 12 m short), plus
20 s of churn. In the game: order a unit down a corridor past two parked friends; it gets there,
then they march back and push it all the way back to where it started, and it stands there with no order.

Varied seeds 1-20, final state: the walker is at its goal on 6 of 20 (base 7f741f1: 1 of 20). The
other 14 give up 9-18 m short, either like the above (radius-0.9 pairs, 3 walk-backs) or because the
walker's sidestep puts it off the corridor's centerline, it wedges against the parked unit at a
diagonal, and a shove "along the push" drives the pair into the corridor wall instead of along it
(the brief's "sideways where there is room" yield wasn't built; docs/03 says so).

## Notes
- The dev test `CrowdRoutingTests.ParkedPairInACorridor_YieldsAsAChain_ToABlockedWalker` and the QA
  repro both stop at the walker's first Idle tick, so they can't see this; walk-back made "Idle" a
  non-final state.
- A unit walking back is a walker blocked by a lone parked unit, so it gets the BUG-0033 push. Possible
  directions: a walking-back unit never shoves an arrived unit off its point, or the walk-back target
  becomes "near the point" when the point is now held by another group's arrived unit.
- BUG-0033 stays open with this as its remaining repro.

## Re-check round 1 (2026-10-05-1609, fix commit 6abd200): fixed
Verified: the exact repro settles with the walker 0.89-0.97 m from its goal for goal x 12-20 (it keeps
its goal cell); on the 20 varied seeds the walker ends at its goal on 20 of 20 (0.90-1.00 m, two
walk-backs each). The price, by design: the pushed pair walks back, can't push the walker, and ends
goal-less beyond it. Oscillation hunts (corridor swaps, gap swaps, walk-back waves) all terminate with
the per-tick checks green and at most one walk-back per order.
