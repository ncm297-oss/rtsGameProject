# BUG-0790: `GatherPocketStressTests` `(MineOneSide, 40)` goes red at `gatherRate` 0.85 / 0.75: a queued worker stands 68-84 ticks out of reach

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-16-1654, task M5-4f (the dev's full run; filed by the Producer, the dev can't write `studio/bugs`) |
| System | economy / movement: gather crowding at a mine face; the QA stress row `Stress/GatherPocketStressTests.Pocket_NoGathererWedgesOutOfReach_ConservationExact` |
| Fixed by | |

## Repro
1. On `studio/2026-10-16-1654-sim` (8ac7b43; `rules.json` `gatherRate` gold 0.85, wood 0.75):
   `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~GatherPocketStressTests.Pocket_NoGathererWedgesOutOfReach_ConservationExact"`.
2. The `(MineOneSide, 40)` row fails (the Producer reproduced it, 45 s):
   `seed 2: unit 15 stood out of reach with nobody between for 68 ticks (to tick 2260); now at <27.147322, 13.756537> state Idle`;
   `seed 8: unit 27 stood out of reach with nobody between for 84 ticks (to tick 1149); now at <24.86384, 10.631518> state Moving`.
   The other seeds: longest wedge 2-14 ticks. The bound is 40 ticks (2 s).
3. At the old rates (0.7 / 0.6, `main` e48cb0e) the row is green (the 0847 merged tree's full suite; the dev's A/B).

## Expected
The row's contract: a gatherer on its node leg never stands still out of reach (`EconomyConstants.Reach`, 1.25 m) with nobody
between it and the node's footprint for more than 40 ticks. Its own comment: "a worker waiting behind others at a crowded node is
queueing, not wedged."

## Actual
With 40 workers on a mine open on one side, the quicker trips crowd the 2-cell face harder; a worker waits 3.4-4.2 s outside
reach while others fill the face. The row's `SomeoneBetween` looks only along the straight line to the nearest footprint point,
so a crowd beside that line reads as "nobody between".

## Notes
Two readings, to be settled by QA in M5-4f's round (the Producer's ruling at the 1654 ACCEPT):
- **A sim fault** if the worker had a free standing spot within reach and stood anyway (replay seeds 2 and 8 to the failing tick
  and look at the face: `MovementSystem.QueuesBehind` / `StandArrival2`, three stand points per cell edge, M4-2a / BUG-0146): then
  the row is skipped with this id (the pattern of BUG-0702's row), S3 stays open for M5-H3, and this file says what the probe saw.
- **The check's geometry** if every spot in reach was taken (the face full = queueing): QA re-stages the check (a crowd at the face
  counts as "someone between") with the reason in the file, and this bug closes as a test fault.
A dev never loosens a QA row (standing rule); the ruling is QA's. Related: BUG-0146 (the seed-21 wood wedge), the `k`-line
re-record of `seed21_playable.replay` in the same task (unrelated to this row, which builds its own map).
