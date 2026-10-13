# BUG-0590: A melee unit whose body overlaps a building's footprint never strikes it (a negative gap reads as inside `minRange` 0)

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | open |
| Found | 2026-10-12-1930, task M5-3 (pre-existing since M4-2b `b8cc072`; M5-3's AI waves walk into it) |
| System | combat (`CombatSystem.InReach` / `InReachOfTarget`, building targets) |
| Fixed by | |

## Repro
1. Remove the `Skip` on `QA/AiMilitaryQaTests.AMeleeUnitOverlappingAFootprintEdge_StrikesTheBuilding`, then
   `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~AMeleeUnitOverlappingAFootprintEdge"`.
2. Scene: `CombatScenes.Flat(48)`, player 1's `malazan_garrison_keep` at cell (20, 20) (footprint y from 40 m), player 0's
   `whirlwind_raider` (radius 0.4, melee range 0.5) at (46.97, 39.987): its centre 0.013 m outside the footprint's top edge,
   its body overlapping it. `Attack(raider, keep, isBuilding: true)`, 200 ticks.

## Expected
docs/03 "Combat": a unit strikes a building when the gap (the footprint's nearest point to the unit's centre, less its
radius) is within `range` and not inside `minRange`. `minRange` is the Catapult's 6 m rule; with `minRange` 0, a unit
touching or overlapping a footprint is in reach.

## Actual
`raider Idle at <46.97, 39.987>; keep hp 2400 -> 2400`. The gap is 0.013 - 0.4 = -0.387 m, and
`InReach(attack, gap) => gap <= attack.Range && !(gap < attack.MinRange)` reads `-0.387 < 0` as inside the minimum range.
The order is kept (target set, mode `Ordered`) but the unit never swings and never steps back. The same scene with the centre
0.40 m or 0.45 m off the edge (gap 0 or above) hits (2400 -> 2393 in 200 ticks); at 0.30 m off (gap -0.1) it never hits.

How it happens in play: crowd separation and shoves push melee units against a building's edge. In
`Stress/AiWaveStressTests` seed 6 a Whirlwind raider stands at cell (43, 87) beside the Malazan keep (anchor (40, 88)), its
centre 0.013 m above the footprint. The AI sends it `Attack` 278 times over 5,500 ticks and it never strikes (BUG-0591). A
human's units would stand idle the same way.

## Notes
- A fix such as `!(attack.MinRange > 0 && gap < attack.MinRange)` (or clamping the gap at 0 for the min-range test) keeps the
  Catapult's rule. Check unit targets too: two overlapping unit bodies also give a negative gap.
- Pre-existing (the combat code is unchanged in M5-3's diff). Severity is S2 as a wrong game rule; the Producer decides
  whether it blocks M5-3 or goes in its own task.
