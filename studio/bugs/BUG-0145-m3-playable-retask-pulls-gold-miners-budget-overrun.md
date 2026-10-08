# BUG-0145: M3PlayableTest's idle re-task pulls working gold miners off the mine; seed 1 overran the 16,000-tick budget in 1 of 4 runs

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | fixed (QA re-check 2026-10-07-2315 round 1) |
| Found | 2026-10-07-2014, task M3-V4 (QA) |
| System | M3 Playable proof scene (`game/tests/M3PlayableTest.cs`, view track) |
| Fixed by | ec28ee4 "M3-V4: fix QA findings (round 1)" |

## Repro
1. `& $env:GODOT --headless --path game res://tests/M3PlayableTest.tscn` (both seeds, about 100-160 s), four times.
2. QA run 1 (another track's test host busy at the same time):
   `M3 PLAYABLE TEST FAIL 15. V, W: Engineers' Yard (seed 1, tick 16001): Engineers' Yard finished: not by tick 16000;
   work 0 / 3300; builder Moving ...; workers Gathering/Gold/q0 x1, Gathering/Wood/q0 x5, Moving/none/q0 x1, Moving/Wood/q0 x4`.
   Runs 2-4 passed (seed 1: 11,871, 12,646, 13,493 ticks; seed 6: 11,146, 11,037, 11,034).
3. The failing run's replay (`m3playable-seed1-fail-tick16001.replay`, saved by the scene in Godot's user data folder)
   replays bit-exact. Its command log shows the scene's own re-task clicks moving gold miners to wood:
   ```
   t5074 Gather unit 3 pos <97, 153>   (unit 3 was mining the gold mine, node 4, at t5000)
   t5475 Gather unit 2 pos <97, 153>   (mining node 4)
   t5674 Gather unit 1 pos <103, 149>  (mining node 4)
   ```
   Gold miners fell from 5 (tick 3,859) to 2 (the builder and one more) for the rest of the match. Age II waited 5,700
   ticks for its gold (step 11 done at tick 9,568), and the Engineers' Yard site went down at tick 15,896.

## Expected
Criterion 1 of M3-V4: the scene PASSes headless on seeds 1 and 6 within the fixed tick budget it states. The script
re-tasks "a laborer standing Idle (its rally tree was felled before it was born)", as a player would.

## Actual
`RetaskIdle` takes any own laborer with `State == Idle && QueueCount == 0` other than the builder. A gatherer on the
loop is `Idle` for single ticks all the time: it is between legs, waiting at the mine's edge, or retrying (`Wait`,
`EconomyConstants.RetryTicks`). In the seed 1 replay, the five gold miners were `Idle` with `GatherNode` = the mine on
dozens of ticks between ticks 200 and 9,000 (for example unit 4 every 21 ticks from 204 to 435, and unit 3 every 21 ticks
from 1,805 to 1,973). The scene checks every 100 ticks, so whether it catches a gold miner depends on frame timing.
When it does, it sends that miner to wood for good. A player would not re-task a worker that is still on its mining loop
(its `GatherNode` is the live mine). The pass depends on luck: the dev's 8 runs ranged 10,506-13,430 ticks, QA's 4 runs 11,034-16,000+ (seed 1 alone 11,871-16,000+).

## Notes
- Fix idea (test code only): re-task only a laborer whose `GatherNode` is no longer alive (or `!OnLoop`), the case the
  scene's comment describes. Then measure the tick spread again over 10 or more runs, under load too (the frame clock
  makes command ticks drift).
- Seed 21 (not an acceptance seed) also fails on budget, for a different reason: the sim wedge in BUG-0146. The scene
  keeps sending idle laborers to the nearest tree, which is the wedged one.
- The `--break` check works: `-- --seed 6 --break 4` prints
  `M3 PLAYABLE TEST FAIL 4. Q three times (seed 6, tick 504): deliberately broken by --break`.
- **Re-check (QA 2026-10-07-2315, still open, no fix yet).** `RetaskIdle` on the branch still selects on
  `State == Idle && QueueCount == 0` only (no `GatherNode` / on-loop check). Five two-seed runs while the full sim suite
  ran in parallel: 5 / 5 PASS, but seed 1 took 15,055 / 11,872 / 13,285 / 14,591 / 12,504 ticks (3-10 re-tasks; the
  slowest finished with 945 ticks of budget left), against a steady seed 6 at 11,032-11,306. The seed-1 spread is the
  timing luck this bug describes. For the fix: 10 runs per seed under load, all inside the budget, as the Producer's
  focus asks.
- **Re-check round 1 (QA 2026-10-07-2315): fixed.** `RetaskIdle` now also requires `!W.Resources.IsAlive(U.GatherNode[i])`
  (`OffTheLoop`). A laborer on a live node's loop is never picked, and a default handle (no gather order) or a dead node's
  handle still counts as idle, so the "rally tree felled before birth" case the scene describes is still exercised.
  Ten two-seed runs on ec28ee4, all PASS, exit 0. Runs 1-5 ran while a full `dotnet test sim/Rts.Sim.Tests` was running
  (two back-to-back suite runs), and runs 6-10 mostly alone. Seed 1: 11,986 / 11,872 / 11,873 / 11,872 / 11,873 / 11,990 /
  11,871 / 11,872 / 11,985 / 11,984 ticks. Seed 6: 11,034 / 11,592 / 11,523 / 11,034 / 11,416 / 11,207 / 11,349 / 11,083 /
  11,357 / 11,109 ticks. Every run re-tasked exactly 2 laborers per seed, and the gold census holds (seed 1 run 1: 5 on
  gold before and after the two re-tasks). The worst run is 4,010 ticks inside the 16,000 budget. Wall time 146-161 s per run.
