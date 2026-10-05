# BUG-0033: A friendly unit parked by a Move never steps aside; it blocks a 1-cell corridor for its own army

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | open |
| Found | 2026-10-05-1013, task M1-4d-2 |
| System | movement (shoving: `MovementSystem.ShoveDirection`) |
| Fixed by | |

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
