# BUG-1461: A gatherer is held for minutes by its own army parked across its path (rally point), each retry giving up in the same spot

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed (M6-S12, 2026-11-16-2036: the AI's rally point keeps off one-cell passages, `Ai/RallyPassageTests`; held worker-ticks on Raraku seed 1 1,022 -> 261, over QA's 120 seat-runs 30,603 -> 4,118; the movement shape itself stays a report row; edges: BUG-1555, BUG-1558) |
| Found | 2026-11-15-0506, task M6-S10 (developer, probing BUG-1460) |
| System | sim: movement (footprint walk give-ups against own standing non-gatherers), economy retry |
| Fixed by | M6-S12 (efa7b31): `Ai/RallyPassageTests`, QA's `Stress/M6S12RallySurveyQaStressTests` |

## Repro
Raraku, Normal v Normal, seed 1, M6-S10 tree: seat 0's unit 1, after a delivery at (48, 124), walks back toward its mine
(goal (87.9, 118.1)) and stands at (55.25, 124.01) among 5-7 of its own Idle soldiers (types 2 and 3) from tick 5,140 to
7,250 and again from 8,560: every walk gives up after 11-19 stuck ticks (goal cell dropped), `Wait` re-walks 20 ticks later
into the same spot. It is freed when the army leaves on a wave (tick 7,250). The same "held" shape on the base: the c684902
Raraku seed-1 probe holds two seat-0 workers at its end (ticks 27,193 / 27,500). BUG-1430 (2) (seed 7 at 0.75: loop-ended
Idle workers parked at a corner) is the same family.

## Expected
A worker on its loop gets past its own parked units (they yield to a push after `PushAfterStuckTicks`, BUG-0033) or goes
round.

## Note
Not the Keep-corner wedge (BUG-1431, fixed) and not caused by the gather overlap (no gather pair involved).

## M6-SH2
Raraku seed 1, seat 0's worker 1 held from 5,131: it stands in the east mouth of a one-cell passage where the AI's rally
point is, eight soldiers parked round it; a shove moves a unit along the contact normal, into the wall (BUG-0033's
rule). Reproduced in `Movement/ParkedArmyGatherTests` (`Bug1461_*` report rows). A generic shove change freed 1 of 3
passage shapes and not Raraku's: reverted. Still red at 0.5: `HardShapes` cliff28_25 / depot22_20 / edge1_15,
`TwoByTwoDepot` (22, 20) Gatherers, `KeepOnTheMapEdge` (1, 15); `FuzzSeeds57To120` is green. **Producer decision
(docs/01 2026-11-16): the AI's rally point keeps off one-cell passages (an AI rule); the five synthetic shapes get one
more bounded trace in the next AI task and are then either cured or re-stated as known-bug rows: no fourth red session.**

## M6-S12 (2026-11-16-2036, the developer's note, applied by the Producer)
On Raraku seed 1 the "one-cell passage" is not terrain: it is a lane between seat 0's own buildings (cells x 25-27
open only at row 62), and the nominal rally point (55.98, 127.25) stood in the lower building's footprint at the lane's
east mouth. Rule built (`MilitaryManager.OffThePassage`): a nominal point with a one-cell passage cell within
`ai.rallyReach` moves to the nearest open, passage-free point on the same line within `ai.rallyReach`, back toward
the hall first, then out. On Raraku it steps out to (61.96, 126.88) from tick 3,020. Held worker-ticks in the first
12,000 ticks: base 1,022, now 261. `Movement/ParkedArmyGatherTests.Bug1461_*` stay report rows (the movement shape is
unchanged by design; the AI no longer parks there). QA's survey over 120 seat-runs: held worker-ticks 30,603 -> 4,118,
222 shifted points, none on closed ground, no flap while the target holds. Edges left: BUG-1555, BUG-1558.
