# BUG-1111: A sighting mid-leg resumes the perimeter leg at the next ring point, so the edge stretch before that point is approached from inland

| Field | Value |
| --- | --- |
| Severity | S3 (the leg's "traces every edge once a pass" does not hold when a sighting interrupts it; the stretch is walked on the next pass's leg) |
| Status | fixed (M5-4d slice 2, 8155ee4: `StartSweep` resumes at `RingPrevious(HuntRing)`, the leg's last point reached; the `(-1)` row un-skipped and green, 0 / 252 border cells missed; verified by the Producer at the 2026-11-07-0236 ACCEPT) |
| Found | 2026-11-06-0926, task M5-4d (QA re-check round 2, fix 1eccd06) |
| System | AI military manager, the hunt's perimeter leg (`MilitaryManager.StartSweep` / `AfterReached`, `Blackboard.HuntRing`) |
| Fixed by | 8155ee4 (`MilitaryManager.RingPrevious`), proof `HuntSweepRound2LegQaTests.ARingLeg_InterruptedBySightings_StillSeesTheWholeBorder(-1)` |

## Repro
1. Un-skip the row `HuntSweepRound2LegQaTests.ARingLeg_InterruptedBySightings_StillSeesTheWholeBorder(interrupts: -1)`
   in `sim/Rts.Sim.Tests/Stress/HuntSweepRound2QaStressTests.cs`, then run
   `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~ARingLeg_Interrupted"` (under 1 s per row).
2. The scene: flat 128 m map, combat off, player 0 hunting with four Heavy Infantry. The leg starts on the west edge.
   Player 1 is one Raider parked at (40, 90) m. When the leg turns to its next east-edge ring point, the Raider steps into
   the sight of player 0's hall on the west side.

## Expected
docs/03 "Implementation (M5-4d)" (5): the leg "traces every edge once a pass". If a sighting interrupts the leg, the leg
keeps going after it.

## Actual
The leg is kept, but it resumes at `HuntRing`, the ring point it was heading for. The army was cut off at (125, 36) on the
east edge, 15 m past ring point (127, 21.3), heading for (127, 64). It crossed the map to the sighting at (28, 71), then
came back to (127, 64) from the west. The edge between where it stopped and that point was never walked, so border cell
(63, 26) at (127, 53) m was never visible to player 0 during the leg. The leg still completed (`HuntRing` -1, every ring
point marked). The two control rows pass: 0 interruptions and 3 nearby interruptions both see 0 of 252 border cells
missed.

```
t1321 sweep 21 ring 21 left 9 target <127, 64> army (125,15)
t1461 sweep -1 ring 21 left 9 target <28.5, 71.1> army (125,36)   <- sighting at home
t2181 sweep 21 ring 21 left 9 target <127, 64> army (32,71)       <- resumes at the next ring point, from the west
interrupts 1; leg 41 -> 4341 (215 s); border cells never visible in the leg: 1 / 252: (63, 26)
```

On the 128 m test map the gap is one cell, because ring points are 42.7 m apart. On the 512 m maps they are 51.2 m apart
and sight is 14 m. A cut early in a segment, followed by a return from far inland, can leave up to about 23 m of edge
unwalked for the rest of that pass. A unit hiding there survives until the next pass's leg. On Pale Hills one pass
takes more than 40 minutes of the 60-minute match: in QA round 2's new-hide row (AI seat 0), the hunt began at 12:21 and
was still on its first pass when the match ended at 54:02.

## Notes
- Suggested direction (the developer's call): on resume, go back to the ring point before `HuntRing` (the leg's last
  point reached) and walk that segment again. Or resume at the leg's point nearest the army, then continue clockwise.
  Either way, add a regression row like the one above.
- Related: BUG-1107 (the leg is its round-2 fix).
