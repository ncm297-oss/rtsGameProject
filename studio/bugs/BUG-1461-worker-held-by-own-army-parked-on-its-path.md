# BUG-1461: A gatherer is held for minutes by its own army parked across its path (rally point), each retry giving up in the same spot

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
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
