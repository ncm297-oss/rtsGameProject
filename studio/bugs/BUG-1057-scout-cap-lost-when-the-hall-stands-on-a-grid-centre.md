# BUG-1057: The scout cap is lost when the hall's centre is exactly a scout grid centre

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed (M6-S8; QA confirmed 2026-11-06-0326) |
| Found | 2026-11-05-2148, task M6-SH1 round 1 (QA re-verify) |
| System | AI scout (`ScoutManager.SentAgain`, the death branch of `Sweep`) |
| Fixed by | M6-S8: `ScoutManager.Mark` lays the mark a quarter cell (0.5 m) off the hall's centre when that is the grid point's own centre, and `SentAgain` reads the same mark (no saved byte). Tests: `QA/ScoutSentAgainQaTests.EveryScoutKilledAtTheDoor_TheCapHolds_WhenTheHallStandsOnAGridCentre` (un-skipped: 3 scouts, was 9), `Ai/ScoutReplacementTests.AScoutSentAgain_FromAHallOnAGridCentre_IsMarkedAQuarterCellOff`, `EveryScoutOfAHallOnAGridCentre_Killed_TheSeatLosesThree` (30 and the control 29), `ASaveWhileAHallOnAGridCentreHasAScoutSentAgainOut_LoadsMarked_AndPlaysOnHashEqual`, `SentAgain_OnAHallOnAGridCentre_AllocatesNothing` |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~ScoutSentAgainQaTests.EveryScoutKilledAtTheDoor_TheCapHolds_WhenTheHallStandsOnAGridCentre"`
   (the row is skipped for this bug; remove the `Skip`).
2. The scene: a flat 64 x 64 cell (128 m) map, not explored, 2 Normal AI seats. Player 0's 4 x 4 Town Hall is anchored
   at cell (30, 30), so its centre is (64, 64) m. With the shipped `ai.scoutGrid` of 56 m the scout grid is 3 x 3, and
   its middle centre is also (64, 64). Every scout is killed on its first tick out. The match runs to tick 6,000.

## Expected
docs/02 "AI opponent" and the BUG-1052 fix: a scout is sent again only while the own unit losses are at most
`ai.scoutReplacements` (2), so a seat whose scouts die at the door loses at most 3 of them.

## Actual
| Hall anchor | Hall centre | Scouts killed by 6,000 | Marked "sent again" | Phase at 6,000 |
| --- | --- | --- | --- | --- |
| (29, 30), the control | (62, 64) | 3 | 2 | 2, the reach lifted |
| (30, 30) | (64, 64) | **9** | **0** | 0, still sending |

Round 1 applies the cap only to a scout that is "sent again" (`capped = losses > cap && (cap == 0 || SentAgain)`).
`SentAgain` finds that mark only when the grid point nearest the hall differs from its normal cell centre. When the hall
stands on a grid centre, every replacement reads as a first scout. The cap then never applies, and the seat keeps
feeding workers to the door with no limit: BUG-1052 comes back. The developer noted this edge ("hall exactly on a grid
cell centre reads false") but not what it costs.

## Notes
- Shipped content can't reach it today. QA probed 1,620 generated halls (seeds 1-60, 2 / 3 / 4 seats, map sizes 128 /
  192 / 256) and none stood on a grid centre. Raraku's starts don't either.
- The edge is still easy to reach:
  - With a 56 m grid, the hall centres that can land on a grid point are (128, 128) on a 128 map, (192, 192) on a 192
    map, and (128 | 384, 128 | 384) on a 256 map. On a 256 map those four are the quarter points, a natural place for a
    hand-made 4-seat map file to put its starts.
  - The same can happen through a rebuilt hall, or through the first-building fallback in `TryHallPoint`.
- Possible fixes (the developer's call): make the mark a point that can never be a grid centre, e.g. offset it by a
  quarter cell when the hall's centre is a grid centre. Or count a scout as "sent again" from another saved fact.

## QA (2026-11-06-0326, M6-S8)
- Confirmed: `QA/ScoutSentAgainQaTests.EveryScoutKilledAtTheDoor_TheCapHolds_WhenTheHallStandsOnAGridCentre` is un-skipped
  and green (3 scouts); the save rows and the `ai.scoutReplacements` rows are green in the full suite.
- On a 256 map (`QA/MapSlice2QaTests`): halls centred on all four quarter points (128 / 384 m), every scout killed at the
  door to 8,000: 3 killed each, sweep ended, reach lifted (the control one cell off: 3). The quarter-point hall destroyed
  and rebuilt on the same cell: 3. No hall, only a Billet on the grid centre (`TryHallPoint`'s first-building fallback): 3.
  `SentAgain` on the 256 quarter-point hall allocates nothing.
- On the shipped maps, seeds 1-10, both seats (`Stress/MapSlice2StressTests`): 3 scouts lost a seat on all 40 seat-runs.
