# BUG-0330: The tick's death list holds UnitCapacity events; units and buildings dying in one tick can overflow it (IndexOutOfRangeException)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-09-1155, task M4-4b-1 |
| System | combat / world (death events) |
| Fixed by | |

## Repro
1. Un-skip `QA/CusserQaTests.ACusserKillingEveryUnitSlotAndABuilding_InOneTick_DoesNotOverflowTheDeathList`.
2. `dotnet test sim/Rts.Sim.Tests --filter FullyQualifiedName~ACusserKillingEveryUnitSlot`
   (a world with `UnitCapacity: 2`: a 30 hp Sapper throws the Cusser on its own feet beside an enemy Crossbowman and an
   enemy Keep at 100 hp: 3 deaths in one resolve).

## Expected
Every death of the tick is recorded (units and buildings): 3 events.

## Actual
```
System.IndexOutOfRangeException : Index was outside the bounds of the array.
   at Rts.Sim.World.RecordDeath(DeathEvent& e) in sim/Rts.Sim/World.cs:line 174
   at Rts.Sim.Combat.CombatSystem.HitBuilding(...) line 983
   at Rts.Sim.Abilities.AbilitySystem.Resolve(...) line 196
```
The exception escapes `Simulation.Tick()` mid-phase (the sim is left half-ticked).

## Notes
`World._deaths = new DeathEvent[config.UnitCapacity]; // one death per hit at most` counts unit deaths only, but
`HitBuilding` records building deaths in the same list. The bound is `UnitCapacity + BuildingCapacity`.
Pre-existing since buildings could die (M4-1 melee/ranged, M4-2b splash reaches buildings too); the Cusser (units,
own units and buildings in one resolve) makes the shape easier to hit. Reaching it needs more deaths in one tick than
there are unit slots, i.e. essentially every unit slot full and dying together plus a building, so it's far from normal
play at the shipped 200+ capacities: S3, not S1. Fix: size the list `UnitCapacity + BuildingCapacity`.
