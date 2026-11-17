# BUG-1461: A gatherer is held for minutes by its own army parked across its path (rally point), each retry giving up in the same spot

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open (cause traced in M6-SH2; five `KeepCornerCureQaStressTests` rows still red by ruling, a third session) |
| Found | 2026-11-15-0506, task M6-S10 (developer, probing BUG-1460) |
| System | sim: movement (footprint walk give-ups against own standing non-gatherers), economy retry |
| Fixed by | |

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
