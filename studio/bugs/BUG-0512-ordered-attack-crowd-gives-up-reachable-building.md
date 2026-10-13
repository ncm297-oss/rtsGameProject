# BUG-0512: 444 of 500 units give up an ordered Attack on a reachable building once they reach the crowd round it

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-10-2334, task M4-H3 (QA) |
| System | combat (`CombatSystem.ReachableOnFoot`, BUG-0401's reachable-chase rule) |
| Fixed by | 1dfdef3 (M5-H1); `QA/SimHardeningH3QaTests.FiveHundredUnits_OrderedOntoAReachableBuildingThroughARampDetour_NeverGiveUp_ZeroBytes` un-skipped (2x-scale residue: BUG-0651) |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~SimHardeningH3QaTests.FiveHundredUnits"` with the `Skip`
   removed (`QA/SimHardeningH3QaTests.cs`).
2. Scene: 96 x 64 map, two level-1 plateaus across a canyon, 3-wide ramps on their south edges. 500 own units (half Heavy
   Infantry, half Crossbowmen) on the west plateau are given `Attack` on an enemy Tent on the east plateau (route 70 m
   south first, then across and up). The Tent is healed each tick so it outlasts the run; 4,000 ticks.

## Expected
Brief M4-H3 scope 6: "An ordered chase (`CombatMode.Ordered`) whose target is reachable (finite flow-field cost at the
unit's cell) never counts stalled scans". QA focus: at 500 units it must never give up.

## Actual
```
head: gave up 444 (first at 1518); max stall 9; ticks with a blow 1880; attacking 77, moving 18, other 405
base: gave up 500 (first at 39); ticks with a blow 0
```
The detour half is fixed (nobody gives up on the way; the base drops all 500 at tick 39). At the crowd round the Tent, unit
486's last 30 ticks: `Moving st9 stuck1..19`, then `Idle st9 g-1` (movement's stuck stop), then Moving again (combat
re-issues the walk), ... and at tick 1518 a scan lands on the one Idle tick: `ReachableOnFoot` answers false (not Moving, no
goal), `ChaseStall` is already 9 (held there by the rule), so it reaches 10 and the order is given up. Over 4,000 ticks 444
units do the same and stand Idle beside their fighting friends.

## Notes
`ReachableOnFoot` treats "movement stopped this unit" as "no route", but a stuck stop in a crowd of friends is not "no route":
the cell's field cost is finite. Holding the stall at 9 makes the very next non-Moving scan fatal. Options: test the field
cost at the unit's cell whatever its state (and keep the old rule only where the walk ended at the nearest ground, i.e. the
goal cell's field cost is finite but the target is out of reach from it), or treat a stuck stop like the BUG-0143 friend
exception (friends attacking the same target nearby: wait). Not a regression (the base gives everyone up earlier). The
unreachable case still behaves: `AnOrderedAttackOnAnUnreachableBuilding_GivesUp_AndNobodyPaces` (60 units, ramp walled
off by buildings) passes on head and base.

## Producer triage (2026-10-11, ACCEPT of 2026-10-10-2334)
S3 confirmed; not a regression (the base gave all 500 up at tick 39; the detour half of BUG-0401 is fixed and the brief's
regression row holds). Second item of the next sim hardening: test the goal field's cost at the unit's cell whatever its state,
keeping the give-up only where the walk ended at the nearest ground (goal cost finite, target out of reach from there), or treat a
stuck stop beside friends attacking the same target like the BUG-0143 friend exception; un-skip QA's 500-unit row.

## QA re-check (2026-10-13-0527, M5-H1)
**Fixed**, verified by QA on 1dfdef3: the rows named in Fixed by are green, they failed or were skipped on 0155205, and the full non-Perf suite is green. See `studio/qa/2026-10-13-0527-M5-H1.md`.
