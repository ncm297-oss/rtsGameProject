# BUG-0270: A tower shooting down from high ground is not revealed to its victim's owner

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-09-0125, task M4-3b (builder's known limit) |
| System | combat / vision (towers, high-ground reveal) |
| Fixed by | |

## Repro
1. `FogMaps.TwoLevel()`: a Watchtower of player 0 on the plateau (`TowerTests.PlaceBuilding(sim, 0, Watchtower, 22, 30)`),
   a Crossbowman of player 1 below at `FogMaps.Cell(14, 31)`, no other unit of player 0.
2. Run until the tower has hit the Crossbowman.

## Expected
docs/02 "High ground": "Attacking from high ground reveals the attacker to the target's owner for 2 s". A unit shooting
down is revealed (M4-3a); a tower is an attacker too.

## Actual
`World.Fog.CanSeeBuilding(1, tower)` stays false and the Crossbowman never targets the tower: the reveal store
(`FogStore._revealUntil` / `_revealGeneration`) is per unit slot, and `CombatSystem.HitUnit` / `HitBuilding` skip
`VisionSystem.OnHit` for a building attacker (`PendingHit.AttackerIsBuilding`), so a tower on high ground kills low
units unanswered.

## Notes
Fix sketch: a per (building slot, player) reveal pair like the units', read by `FogStore.CanSeeBuilding` /
`SeesBuildingCells` (and so by the ghost list and `UnitSeesBuilding`), hashed. Memory: 2 ints x 256 slots x players.
Left out of M4-3b to keep the slice to its brief; recorded in docs/03 "Implementation (M4-3b)" and docs/01's M4-3b row (d).
