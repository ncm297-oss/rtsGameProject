# BUG-1558: Rally point edge cases: a shift to 6 m from the hall's centre (1 m off its face), a fall-back onto a building cell, and every-think flapping while the target flickers

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | items 2 and 3 fixed (M6-S14 item E, session 2026-11-26-1847; QA verified, accepted 2026-11-26-1847); item 1 measured, open |
| Found | 2026-11-16-2036, task M6-12 (M6-S12), QA full |
| System | AI military (`MilitaryManager.RallyPoint` / `OffThePassage`) |
| Fixed by | M6-S14 (E, items 2-3) |

## Repro
`dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~M6S12RallySurveyQaStressTests"` (Stress, about 45 min): every
rally change of every seat is printed. 120 seat-runs (Raraku, Pale Hills, Vathar Crossing, generated maps of 2, 3 and 4 seats,
seeds 1-8, 12,000 ticks each), the same file run on the base (95186a7) for the before column.

## Expected
The brief (item 2): the point steps back toward the hall to the nearest acceptable point, the nominal point if none is
acceptable, stable between thinks. QA focus: no point on a building, none so near the hall that the parked army blocks its
workers or drop-offs.

## Actual
The rule does what the brief says and, measured, helps a lot (held worker-ticks 30,603 -> 4,118; idle-army unit-ticks
within 1.5 m of an own drop-off 96,918 -> 62,829; nominal points on a building cell 145 -> 80; no shifted point ever on
closed ground; no back-and-forth while the target holds). Three edges remain:
1. **6 m from the hall.** Generated seed 4 (three and four seats), p0: v3 at tick 1,560 is (41.06, 112.78), 6.0 m from the
   hall's centre (36, 116) along (5.06, -3.22): about 1 m outside the 4 x 4 hall's east face. The full step back
   (`ai.rallyReach`) puts the army against the hall. No harm was measured in these runs (that seat's held worker-ticks fell
   19,339 -> 397), but nothing in "acceptable" keeps the point off the hall's own drop-off ring.
2. **Fall-back onto a building.** Same seat, tick 2,920 / 3,060: the shifted point stopped being acceptable (a building went
   up by it) and no other was, so the point went back to the nominal one, which is on a building cell (cell (23, 54)).
   Vathar Crossing seed 7 / 8, p0, tick 10,560: the same (back to (128.6, 132.3), a building cell). The brief's own fall-back
   ("the nominal point if none is acceptable") lands there; the previous acceptable point would have been better.
3. **Flapping with a flickering target.** Generated, three seats, seed 6, p0, ticks 10,340-10,580: 12 changes in 240 ticks,
   one a think, between (51.79, 112.58) and (51.99, 110.50); each re-sends the parked army. The trigger is not the passage rule:
   player 2's construction site at 1 hp (slot 5, cell 7089) is seen on one think and gone the next with no ghost, so
   `KnownEnemyBuildings` goes 2, 1, 2, 1 and the nominal target moves (`Stress/M6S12QaFlapProbe`-style trace in the QA report).
   The base's nominal rule follows a flickering target the same way; the search adds no hysteresis.

## Notes
- (3) also suggests the site itself: a 1 hp enemy site that appears and vanishes every 40 ticks next to p0's army may be a
  placement-and-raze loop costing player 2 its gold each time; worth one trace (not traced here).
- Cheap cures if wanted: keep the current point while it is still acceptable; never step back past `ai.rallyDistance` minus
  the hall's half-size plus a cell; with none acceptable keep the last acceptable point rather than the nominal one.

## Fix (M6-S14)
The point in use is kept while acceptable and within `ai.rallyReach` of the nominal one; with none acceptable the last acceptable one is kept. Test: `Ai/RallyKeepTests`. Item 1 measured (QA's survey, base -> M6-S14): the point nearest a hall 6.0 m -> 4.5 m, the 4.5 m a kept point laid on the line of a hall that moved (generated, four seats, seed 2, p3, tick 8,160); flaps 61 -> 5, changes 630 -> 315 (docs/03 "Implementation (M6-S14)").
