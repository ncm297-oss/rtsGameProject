# BUG-0028: Groups sent to nearby points give up en masse (84-87%); walkers give up against units that are only waiting

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open (part fixed) |
| Found | 2026-10-05-0742, task M1-4d-1 |
| System | movement (give-up rule, standing units as walls) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~LocalMovementStressTests.Crowd" --logger "console;verbosity=detailed"`
2. Read the `(500, 4, 3000)` row: 500 units, 4 points 3 cells (6 m) apart, round-robin.

## Expected
Most units of each group reach their own point's blob (QA focus "Crowds", 4 points 3 cells apart).
Giving up is for units that "can't make progress", i.e. the exception.

## Actual
```
500 units to 4 point(s): 0 moving after 601 ticks; arrived 78, gave up 422
  given-up distance to own point: <2 m 4, <4 m 7, <8 m 51, <16 m 257, more 103
2500 units to 4 point(s): arrived 319, gave up 2180
```
Each group walks straight into the other groups' arrived blobs (hard walls since standing units
are never pushed, and the flow field ignores units) and gives up after 1 s, mostly 8-16 m short.
Given-up units then wall in the rest. The same mechanism explains the loosened assertions in
`QA/FieldBuildCapQaTests.BuildCap_500UnitsWith500DistinctGoals_EveryUnitArrives_NoDeadlock`
(59/500 give up; the walls there include units that are merely *waiting* for a field under the
build cap: Moving at velocity 0, about to walk, yet treated as walls that make others give up)
and `MovementSystemTests.MoreGoalsThanCacheSlots_...` (41/128).

## Notes
Known limit per the developer and docs/03 until shoving (M1-4d-2). Filed so it is tracked:
M1-4d-2 should (a) bring these give-up rates down and re-tighten the two loosened assertions
(the QA test's name still says "EveryUnitArrives"; its 15% bound has ~3 points of headroom),
and (b) consider not counting a stuck tick against a Moving unit that is only waiting for a field.

## Re-check (2026-10-05-1013, M1-4d-2 accepted)
Partly addressed by shoving: 500 to 4 points now arrives 174 (was 78), 2,500 to 4 points 891 (was
329); the two loosened assertions were re-tightened to the measurements (7% / 22%). Still open:
the rates are far from "most arrive" (BUG-0032 has the current table) and (b) is not done. Both go
to task M1-4d-3 (crowd routing); close this bug there together with BUG-0032.

## Update (QA verified at 2026-10-05-1609 (M1-4d-3, commit 0a71412))
- **Waiting units: fixed.** A walker blocked by a unit that is Moving but waiting for its field now holds
  its count and follows: `CrowdRoutingTests.WalkerBehindAUnitWaitingForItsField_Queues_ThenFollowsAndArrives` passes.
  Side effect: under more live goals than cache slots this hold can last forever (BUG-0048, S1, 1 map in 160).
- **Groups to nearby points: still open.** QA sweep, one player per point (the setup is legitimate: the
  old `slot % 4` split did send both players to every point, `QA/CrowdRoutingQaTests.OldCrowdSplit_*`):
  500 to 4 points over seeds 1-40 arrived mean 256 (51%), min 206 (base 7f741f1: 241, min 164); 2,500
  to 4 points over seeds 11-40 mean 844 (34%), min 621 (base 732).
