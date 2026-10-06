# BUG-0075: Felling a forest's interior tree first leaves an open cell nobody can reach; an order onto it does nothing

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-06-1255, task M3-1 |
| System | navigation grid / economy (depletion), docs |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~OrderIntoAFelledGroveMiddle_Report" --logger "console;verbosity=detailed"`
   (`QA/ResourceQaTests.cs`): a 3 x 3 grove of trees on a flat 24 x 16 map. Its middle tree is taken
   to 0, then a unit at (3, 7) is ordered to the middle cell (11, 7).
2. `Stress/ResourcePlacementStressTests` (`Placement_IndependentOracle_ManySeeds`) fells every node
   of generated maps in random order: on 30 of 30 checked seeds (the first 5 of each of 6 parameter
   sets), some fell leaves an open cell that a flood from the main region can't reach.
3. The assertion version `OrderIntoAFelledGroveMiddle_StillWalksToTheGrove` is skipped with this bug's id.

## Expected
docs/03 "Navigation grid": "the placer keeps every passable cell reachable ... so the build-time
pocket seal still holds". docs/03 "Flow fields": "Unreachable targets can't happen yet: the nav
grid seals every pocket, so all passable cells connect." Brief: "depletion only opens cells, so no
pocket can ever appear later."

## Actual
```
grove intact:  unit ends at <22.13, 11.08> (goal cell 131 = (11, 5), Idle); every open cell reachable: True
middle felled: unit ends at <7, 15> (goal cell -1, Idle); every open cell reachable: False
```
Opening a cell whose 4 neighbours are all still trees creates a passable cell with no way in. It
isn't a trap, since no unit can be inside it. But it breaks the "every passable cell connects" rule
the docs and the order code rely on:
- `ResolveTarget` sees a passable target and no longer snaps to the nearest reachable cell.
- The flow field from the hollow reaches nobody.
- The unit abandons its order where it stands. Before the fall, the same click walked it to the
  grove's edge.

Not reachable in M3-1 (nothing calls `Take` in game). Whether M3-2 can reach it depends on the
gather rule: gathering only 4-adjacent trees from reachable ground never creates a hollow (QA's
reachable-order felling over 246 seeds kept every cell reachable). Gathering diagonally, or
splash/ability tree removal later, does create one.

## Producer triage (2026-10-06-1255 ACCEPT)
S3 agreed; not blocking M3-1 (nothing fells trees in game yet). Planned into the M3-2 brief: a worker
gathers only from a node cell adjacent to the passable cell it stands on, so interior trees fall last;
docs/03's two sentences get the condition ("as long as nodes are only removed from reachable ground");
the skipped QA row becomes the regression test. Abilities that remove trees (M4+) must re-check this.

## Notes
Options: make M3-2's gather rule 4-adjacent from reachable ground and say so in docs/03; or treat
an unreachable target like a blocked one (snap to the nearest cell *reachable* from the unit, which
needs a reachability label per region); or re-label hollows as blocked on open. In any case fix the
two docs/03 sentences above.
