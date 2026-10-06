# BUG-0058: A unit that gives up while backing off can stop out of reach of its goal and keep its goal cell (stray anchor)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-06-0905, task M1-9 (by the developer, while measuring item 6) |
| System | local movement (`MovementSystem.Apply` give-up rule, anchors) |
| Fixed by | 6d1cbfd (M1-9): `SettleBackedOff`; regression `CrowdRoutingTests.GivingUpWhileBackingOff_OutOfReach_DropsTheGoal` (fails on base 1f533aa, QA-verified) |

## Repro
1. A unit at its goal but too crowded to stop backs off; one that reaches `GiveUpTicks` while backing
   off stops where it is and keeps its goal cell (M1-4d-1 rule: it was at its goal or touching its blob).
2. The back-off step itself can carry it 1.06-1.17 m from its goal with nobody of its group in touch.
   Seen in QA's 2,500-unit one-owner row once a movement change re-rolled the trajectories: a stray
   blob formed 10 m from its point, packed against that one anchor.

## Expected
No unit keeps a goal cell away from its point (the anchor rule, docs/03 "Implementation (M1-4d-2)").

## Actual
Before M1-9: the given-up unit kept its goal cell and later arrivals packed against it.

## Fix
`Apply` lists units that gave up while backing off; after the shoves, `SettleBackedOff` applies the
arrival rule at their end-of-tick positions (in the goal cell within `ArrivalDistance`, or touching an
Idle groupmate holding the goal cell through a legal step); the others drop the goal and may walk back
once. Local, not the whole-group re-check (that cost the 2,500-unit tight blob 16%). Golden replay
byte-identical; changes a few outcomes in big crowds (QA 1,000-unit cross-map seed 4: 989 -> 983
arrived). Recorded by the Producer at ACCEPT; the developer suggested the number.
