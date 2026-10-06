# BUG-0033: A friendly unit parked by a Move never steps aside; it blocks a 1-cell corridor for its own army

| Field | Value |
| --- | --- |
| Severity | S3 (filed S2; Producer re-triage 2026-10-05, see below) |
| Status | fixed |
| Found | 2026-10-05-1013, task M1-4d-2 |
| System | movement (shoving: `MovementSystem.ShoveDirection`) |
| Fixed by | M1-4d-3 chain shove (0a71412) + fix round 1 (6abd200, BUG-0042); `QA/ShoveQaTests.WalkerInOneCellCorridor_PastAParkedFriendlyPair_Arrives`, `QA/CrowdRoutingQaTests.WalkerPastAParkedPair_StillAtItsGoalOnceTheWalkBacksSettle` (5 rows), `ParkedPairInACorridor_VariedSeeds_WalkerArrives` (20 seeds) |

## Repro
1. Remove the `Skip` from `QA/ShoveQaTests.WalkerInOneCellCorridor_PastAFriendlyUnitParkedThereByAMove_Arrives`
   (2 rows) and run
   `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~PastAFriendlyUnitParkedThereByAMove" --logger "console;verbosity=detailed"`.
2. Scenario: the 1-cell corridor of criterion 1 (row y 4..6 m). A radius-0.9 unit of player 0 is
   ordered to cell (8, 2)'s center and arrives (Idle, keeps its goal cell). Another radius-0.9
   unit of player 0 is then ordered from cell (2, 2) to cell (14, 2), past it.

## Expected
Criterion 1 / the brief's goal: "standing Idle friendly units step aside for walkers" and "a
corridor walker past a friendly Idle unit arrives". The brief's anchor rule ("a shoved unit keeps
GoalCell only if still arrived, else -1") assumes arrived units can be shoved.

## Actual
```
parked 0 m off its point:   walker gave up after 115 ticks at <15.50, 4.01> (goal cell -1); parked unit at <17, 5>, goal cell 56
parked 0.9 m off its point: walker gave up after 128 ticks at <16.40, 4.01> (goal cell -1); parked unit at <17.9, 5>, goal cell 56
```
The parked unit never moves, and the walker gives up in front of it.

## Notes
- Cause: the developer narrowed the shove rule (a stated deviation). `ShoveDirection` returns zero
  for an Idle unit within `ArrivalDistance` of its own point, and an arrived unit further out only
  moves toward its point. So every unit that obeyed a Move and stands near its point is as hard
  as a wall to its own player. The criterion-1 test covers only units with no goal (never
  ordered), which is why it passes.
- In play: any friendly unit parked on a ramp, bridge or gap blocks the army behind it until it
  is moved by hand. Also a large part of BUG-0032 (walkers meet other groups' blob centers).
- The developer measured that shoving arrived units straight away un-anchored whole blobs (114 /
  238 arrived vs 161 / 499). A fix needs a rule that lets parked units yield without wrecking
  blobs (for example yield sideways out of a corridor and drop the goal, as the brief allows).

## Re-check round 1 (2026-10-05-1013, fix 16d72e8)
Partly fixed. A *lone* parked unit is now pushed once the walker has been stuck for
`PushAfterStuckTicks` (10) ticks: both rows of `ShoveQaTests.WalkerInOneCellCorridor_PastAFriendlyUnitParkedThereByAMove_Arrives`
pass (walker arrives after 256 / 259 ticks; the parked unit is pushed 12.8 m along the corridor and drops its goal).
Mutation-checked: dropping the lone-anchor push or the stuck-count hold fails it.

Still open: a parked *group* still never yields. Repro: un-skip
`ShoveQaTests.WalkerInOneCellCorridor_PastAParkedFriendlyPair_Arrives`. Two radius-0.9 units of player 0
ordered to the same point in the 1-cell corridor (both arrived), a third walks past:
```
walker gave up after 115 ticks at <15.50, 4.01>; pair at <17, 5> / <18.69, 5.07>, goal cells 56 / 56
```
The developer calls this a deliberate deviation (shoving units on their point always "halves the crowd
rows"). Criterion 1 does not limit itself to lone units; the Producer decides whether a parked group
blocking its own army through a choke is accepted for M1.

## Producer triage (2026-10-05-1013, ACCEPT)
Lone case fixed by 16d72e8 (`PushAfterStuckTicks` rule; QA theory green x2, dev test
`LoneParkedUnit_IsPushedOnlyAfterTheWalkerWasStuckPushAfterStuckTicks` pins the delay). The parked
*group* remainder goes S2 → S3 and stays open: a workaround exists (move the group), no player
exists yet, and the dev showed that freely shoving blob members halves the crowd rows, so the fix
needs a real rule (chain shoves, or a stuck walker making a parked group yield sideways out of a
choke and drop its goal) rather than a tweak. Scheduled with BUG-0032 in **M1-4d-3 (crowd
routing)**. Proof of fix: un-skip `ShoveQaTests.WalkerInOneCellCorridor_PastAParkedFriendlyPair_Arrives`.

## Update (QA verified at 2026-10-05-1609 (M1-4d-3, commit 0a71412)): not fixed
The un-skipped repro passes only because it stops at the walker's first Idle tick. Ticked until the
walk-backs settle, the same scenario ends with the walker 8-24 m short and goal-less for every goal x
from 12 to 20: the pushed pair walks back and shoves the arrived walker home (BUG-0042, S2). Over 20
varied seeds (radii, spots, goals) the walker ends at its goal on 6 (base 7f741f1: 1).
Proof of fix now: un-skip `QA/CrowdRoutingQaTests.WalkerPastAParkedPair_StillAtItsGoalOnceTheWalkBacksSettle`.

## Re-check round 1 (2026-10-05-1609, 6abd200): fixed
With BUG-0042 fixed the walker stays at its goal once everything settles: exact repro and goal x 12-20,
and 20 of 20 varied seeds (radii 0.4/0.7/0.9, parking spot, start, goal). The pushed pair ends goal-less
beyond the walker (it walks back once, may not push, gives up).
