# BUG-1930: A seat blind to gold re-scouts on the coarse grid and misses a mine 65 m from its own drop-off for the whole match

| Field | Value |
| --- | --- |
| Severity | S3 (a seat with 26 workers sits on wood with gold 65 m away; a measure-and-report row) |
| Status | open (triaged at the 2026-11-27-1857 ACCEPT: the sim's next AI clean-up, not blocking) |
| Found | 2026-11-27-1857, task M6-S15 (B, BUG-1891's trace; filed from the developer's patch, confirmed by QA) |
| System | AI scouting (`ScoutManager.Rescout`), far expansion |
| Fixed by | |

## Repro
`dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~GoldLeftTraceTests"` (Category Stress) on d26338d3: Pale
Hills four-seat FFA seed 4, seat 0 from 48,000 to 72,000.

## Expected
A seat short of gold for its army, with no gold node known, finds the mine 65 m from its drop-off.

## Actual
The mine's cell (167, 49) is never explored by seat 0; its blind re-scout is out from before 48,000 to about 70,000 on
the scout grid (51 m cells, 14 m sight) and never passes within sight; the far expansion (armed, phase 1) looks only at
start locations. 26 workers on wood, 11,155 wood and 52 gold at 72,000.

## Notes
- Cure options: the re-scout prefers unexplored ground near home, or the map's mine spots; new behaviour (a pin move).
- The match itself ends since M6-S15 C (p0 at 47,612).
- docs/03 "Implementation (M6-S15)" B.
