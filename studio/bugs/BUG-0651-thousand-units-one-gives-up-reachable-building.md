# BUG-0651: At 2x the unit budget, one of 1,000 units gives up an ordered Attack on a reachable building

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | closed: no longer reproduces (Producer, 2026-10-16-0847 ACCEPT; the guard row un-skipped in M5-H2) |
| Found | 2026-10-13-0527, task M5-H1 |
| System | combat (ordered chase give-up, `CombatSystem.ReachableOnFoot`) |
| Fixed by | not by a named change: `QA/SimHardeningH3QaTests.ThousandUnits_OrderedOntoAReachableBuildingThroughARampDetour_NeverGiveUp` un-skipped in M5-H2 (47ca4a2) passes on 8c39c08 and on head (0 of 1,000 give up, max stall 9); it stays in the suite as the guard |

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

## Producer triage (2026-10-13-0527 ACCEPT)
S3 confirmed; at 2x the supported crowd, so not a blocker and not an M5-4b item. Direction (the dev's and QA's reading agree): in `CombatSystem.ReachableOnFoot` a stopped chaser whose chase cell's field is not cached should hold the count (as the `Moving` branch does), not read "unreachable"; un-skip `ThousandUnits_OrderedOntoAReachableBuildingThroughARampDetour_NeverGiveUp`. Next sim hardening, or folded into an M5-4b task that touches the chase code if it is a few lines.

## QA note (2026-10-16-0847, M5-H2)
The dev un-skipped `ThousandUnits_OrderedOntoAReachableBuildingThroughARampDetour_NeverGiveUp` as a guard. QA ran the row against base 8c39c08 code too: it passes there as well (0 of 1,000 give up, max stall 9), so it had stopped reproducing before M5-H2 and nothing in M5-H2 fixed it. Status is the Producer's call (cannot reproduce vs fixed by an earlier change).

## Producer close (2026-10-16-0847 ACCEPT)
Closed as "no longer reproduces": the repro at 1dfdef3 was real, and the chase code has moved three times since (M5-P1's surround rule and BUG-0731's any-mode `ReachableOnFoot` for a walk to a claimed spot are the likely reason: the stopped-chaser branch is no longer where a crowded chaser lands). The suspected cause in Notes (a stopped chaser whose field isn't cached reads "unreachable") is not disproved; if the row goes red again, that is the first place to look. The row runs every session now.
