# BUG-1149: In a 2v2 the defense sends AttackMove onto an ally's building edge (BUG-1120's case, through the defense)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-11-07-2306, task M5-H5 (QA full; pre-existing: red at the diff base `cdbe6ca` too) |
| System | AI defense (`DefenseManager`, the defenders' `AttackMove`), QA team oracle |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~M5H5HuntAfterDefeatQaTests.A2v2_TheDefenseToo"` after removing
   its `Skip` (`sim/Rts.Sim.Tests/Stress/M5H5HuntAfterDefeatQaTests.cs`): Pale Hills, four Normal AI seats, teams 1/2/1/2
   (seats 0 and 2 allied), seed 2, the team soak's oracle (no AttackMove or cast within 0.5 m of an allied building's
   footprint) on every command every seat sends.
2. The other 2v2 rows of the same class count these orders and print the first ("defense AttackMoves onto an ally's
   building (BUG-1149)") instead of failing.

## Expected
The team soak's rule (M6-S4; BUG-1120's exemption removed in M5-H5): no seat orders an AttackMove onto an ally's building.
M5-H5 pushed the hunt's sighting point off allied footprints; the defense answers the same kind of sighting.

## Actual
At `cd211f2` and at `cdbe6ca` (QA ran the same row on a clone of the base, same tick, same point):
```
tick 12220: seat 0 AttackMove onto an ally's building at <105.28219, 158.25337>; unit 1 type malazan_heavy_infantry
  at <135.7284, 141.38951>; wave False hunting False defending True sweep -1 target 14/1 point <384, 376>
  enemyUnit <103.06438, 155.55675>@12220
```
Seat 0 is defending (no wave, no hunt); its defender is sent to an enemy unit standing by its ally's (seat 2's) building.

## Notes
- Not caused by M5-H5 (no hunt, no defeat in that match by then). Found only because QA's new 2v2 rows run Pale Hills to
  72,000; `TeamAiSoakStressTests` stops at 12,000 on a generated map.
- Likely fix: the same `MilitaryManager.SightingPoint` push for the defense's point (it already has the ally test and the
  members' side), or the defense aims at the enemy unit (`Attack`) rather than its position.
- Harm is small (the defenders walk to the wall and fight the enemy that is there), so S3, as BUG-1120 was.
