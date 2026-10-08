# BUG-0141: A retaliation whose anchor cell becomes blocked never ends (mode stays `Retaliate` / `AttackMove` for good)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-07-2014, task M4-1 |
| System | combat settle (`CombatSystem.Settle`) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~AnchorCellBlockedByANewBuilding_EngagementStillEnds"`
2. An Idle Heavy Infantry at cell (20, 20) takes on an enemy holder 12 m away (anchor = its own position). While it
   fights, its owner places a 2 x 2 Billet over (19-20, 19-20). After the kill it walks back as near as it can and
   stands there: after 600 more ticks `Idle`, mode still `Retaliate`, goal <43, 41>, anchor <41, 41>.

## Expected
docs/03 "Modes": "standing Idle with no target ends the mode".

## Actual
`Settle` ends the mode only when the unit stands with `Goal == AnchorPosition`. With the anchor's cell blocked,
`MoveTo` resolves to the nearest passable cell, so the goal never equals the anchor; standing there, every phase 7
calls `MoveTo` again, which the Move rule treats as "already there" and leaves alone. The mode never clears. The same
holds for an attack-move whose destination is covered by a building after the order.

## Notes
Consequences: the unit is hashed with combat state for good; a stuck `AttackMove` mode means its next engagement
(`Engage` sets a leash only from mode `None`) chases without a leash and then walks back to the stale destination; a
stuck `Retaliate` keeps the old anchor for the next fight. Fix idea: compare the goal with the anchor's resolved goal
(`ResolveTarget`), or end the mode whenever the unit stands Idle after a walk that `Settle` started.
