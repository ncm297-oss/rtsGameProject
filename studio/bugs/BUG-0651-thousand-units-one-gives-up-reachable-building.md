# BUG-0651: At 2x the unit budget, one of 1,000 units gives up an ordered Attack on a reachable building

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-13-0527, task M5-H1 |
| System | combat (ordered chase give-up, `CombatSystem.ReachableOnFoot`) |
| Fixed by | |

## Repro
1. Un-skip `QA/SimHardeningH3QaTests.ThousandUnits_OrderedOntoAReachableBuildingThroughARampDetour_NeverGiveUp` and run
   `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~ThousandUnits_OrderedOnto" --logger "console;verbosity=detailed"`
   (about 35 s). The scene is the 500-unit row's (`ChaseScene`, the BigCanyon ramp detour onto a Tent) with `count` 1,000.

## Expected
BUG-0401's rule as M5-H1 fixed it for BUG-0512: no unit gives up an ordered Attack on a reachable building. At 500 units
none does.

## Actual
At 1dfdef3, 1 of 1,000 gives up, at tick 2270. The unit is slot 503, a Heavy Infantry. It is `Moving`, stuck in the crowd at
the Tent on the east plateau (around (125.6, 23.8) m), and its `ChaseStall` climbs 0 to 9 between ticks 2230 and 2266. The
chase moves its goal from cell 1214 to 1118 at 2250. Movement's stuck stop leaves it `Idle` with goal -1 at 2269, and the
scan at 2270 gives the order up:

```
[2266 Moving st9 g1118 stuck17] [2267 Moving st9 g1118 stuck18] [2268 Moving st9 g1118 stuck19] [2269 Idle st9 g-1 stuck0]
```

The 500-unit row also peaks at stall 9 of 10 (`max stall 9`), one scan short of a give-up.

## Notes
- Suspected cause (not proven): the new stopped-chaser branch judges a dropped-goal stop by `ChaseCell`'s field through
  `PeekCached`, and a field that isn't cached returns "unreachable" for a unit that isn't `Moving`. With 1,000 chasers there
  are many goal cells, so the chase cell's field may have been evicted or never built. The `Moving` branch holds the count
  when the field is missing; the stopped branch does not.
- Scale is 2x the 500-unit budget (docs/03). Deterministic, so it fails the same way on every run.
