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

## QA ruling (2026-10-16-2036, M5-4f round): a sim crowding fault; the row is skipped, the bug stays open (S3) for M5-H3

The positions in the failure message are the workers' positions at the end of the run (tick 3,000), not at the wedge. QA
replayed the row's exact scene (same map, spawns, seeds, wedge rule) with `QA/Bug0790FaceProbeQaTests` and, at every tick a
worker counted as wedged, searched the face for a free standing spot: centre within `EconomyConstants.Reach` (1.25 m) of the
footprint, the body (radius 0.4 m) clear of every blocked cell and of every other unit, on a 0.05 m grid.

| Seed | Wedge (unit, ticks) | Stuck worker at | Ticks past the bound with a free spot in reach | Free spot in a 4-adjacent ring cell (the cells `WalkToFootprint` offers) | Free spot nearest the worker |
| --- | --- | --- | --- | --- | --- |
| 2 | unit 15, 68 ticks to 2,260 | (28.80, 11.55), state Gathering / Idle at each 20-tick retry, moving only at tick 2,257 | 28 / 28 | 28 / 28: (24.05, 11.20), 0.80 m clear (room for a whole worker) | 0.51 m away, (28.55, 11.10), corner cell (14, 5) |
| 8 | unit 27, 84 ticks to 1,149 | (29.04, 11.40), Gathering / Idle at each retry, never moving | 44 / 44 | 44 / 44: (28.00, 10.60), 0.17 m clear, **1.3 m from the stuck worker, open all 84 ticks** | 0.51 m away, (28.80, 10.95), corner cell (14, 5) |

So the face was **not** full: a worker stood still out of reach next to a free spot in reach, through three or four of its
own retries (each retry's `WalkToNode` ends at once: Idle the next tick, then Gathering-out-of-reach again). The row's
straight-line `SomeoneBetween` geometry is not what made it red. Two shapes of the fault: (a) the retry's stand point is one of
the six fixed points on the two ring cells' edges and the crowd-arrival rule stops the walker short of a gap that isn't on a
stand point; (b) the face's diagonal corner cells ((11, 5) and (14, 5)) hold spots in reach that no stand point ever offers.
The quicker gathering (0.85 / 0.75) only makes the face churn faster; the control at the old rates (0.7 / 0.6, the same
scene, seeds 1-8) is green: longest wedges 21, 12, 24, 12, 19, 14, 18, 9 ticks (bound 40).

**Done by QA:** `GatherPocketStressTests.Pocket_NoGathererWedgesOutOfReach_ConservationExact(MineOneSide, 40)` is skipped
with this id (no bound changed; the other 19 rows run). Kept live: `QA/Bug0790FaceProbeQaTests.Control_MineOneSide40_AtTheOldGatherRates_NoWedgePastTheBound`
(the same scene and bound at 0.7 / 0.6, green) and the regression row
`MineOneSide40_NoWorkerWedgedPastTheBound_WhileARingSpotInReachIsFree` (seeds 2 and 8, skipped with this id; it turns
green when the fault is fixed). The fix is M5-H3's (`EconomySystem.StandPoint` / `WalkToFootprint` or the crowd-arrival rule
in `MovementSystem`); un-skip both rows with it.
