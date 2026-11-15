# BUG-1431: A worker returning with a load stands out of reach at a Keep's corner for about a minute (today's rules, knob off)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed (M6-S10, 2026-11-15-0506) |
| Found | 2026-11-14-2156, task M6-S9 (found by the game-dev while writing the gather-overlap fuzz; id given and re-measured by QA) |
| System | sim: economy (`EconomySystem.StartReturn` / `WalkToFootprint` stand points), movement (BUG-0146's footprint walk) |
| Fixed by | M6-S10: `MovementSystem.QueuesBehind` + `EconomyConstants.QueueArrival` |

## Repro
1. `sim/Rts.Sim.Tests/Movement/GatherOverlapFuzzTests.Run(0, 4, tripWindow: true)` with the overlap **off** (the shipped
   data with `gatherOverlap` removed: movement bit-identical to `main`). Generated map, seed 4, 12 forests, 8 mines, no start
   locations; two players, a Keep each beside a mine (`EconomyScenario.Setup`), Laborers and Teblor Gatherers sent to nodes
   within a 30 m walk, seeded Gather re-orders.
2. QA's sweep prints every seed: `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~GatherOverlapQaStressTests.EconomyFuzz_Seeds2To16" --logger "console;verbosity=detailed"`.

## Expected
A worker on its loop with a live node delivers at least once a minute (a trip in these scenes is well under 40 s).

## Actual
Developer's capture, knob off:
```
seed 4 at 0: worker 25 of player 1 (Returning, at <113.84873, 188.28807>, goal <112.125, 188.125>, cargo 10 Gold, stuck 17)
  without a trip for 1201 ticks at tick 5465, its node at 57,94 alive, its Keep at <108, 184>   [footprint 104-112 x 180-188]
```
The worker stands 1.3-1.5 m from the Keep's south-east corner (reach 1.25 m), its stand point 1.2-1.4 m away, and retries
every `RetryTicks` without getting in. QA's re-measure (longest gap, s): seed 4: 68.1 at 0, 70.7 at 0.25, 51.4 at 0.5,
39.4 at 0.75; seed 3: 58.5 at 0, 74.6 at 0.25.

## Notes
- Not caused by M6-S9: it reproduces with the field absent.
- Likely the BUG-0146 / BUG-0790 family: a corner stand point behind returning traffic; each retry picks the same point
  and stops short of it. BUG-1430's seed-7 wedge (a walker held at a Keep's north-east corner by parked Idle workers, its
  stuck count reset by every retry) may share the cause.

## Re-check 2026-11-14-2156, fix round 1: still open, and more common than it looked
QA's wider sweep (`Stress/GatherOverlapWedgeQaStressTests.WiderSeedSweep_17To56_BreachesByValue`, the developer's fuzz
scene on seeds 17-56): **with the knob off, 5 of 40 seeds pass 60 s without a trip.** Seed 22: 67.3 s, a 0.75 m Gatherer
Returning 1.47 m from the footprint. Seed 27: 89.8 s, a Laborer held at the Keep on its way out (cargo 0, 0.63 m). Seeds 35
(67.4 s) and 44 (66.5 s): Laborers Returning 1.47-1.57 m from the footprint. Seed 56: 73.2 s, a Gatherer 6.3 m out. With
seeds 2-16 that makes 6 of 54 with the knob off. This is the root of every criterion-7 breach in BUG-1430; the knob only
changes which seeds hit it (5 / 54 at 0.25, 2 / 54 at 0.5, 1 / 54 at 0.75). Severity stays S3 (shipped behaviour, slower
income, no hang), but it now blocks M6-S9's criterion 7 as written. It should be scheduled before or with the slice that
ships the knob.

## Fix (M6-S10, developer)
Diagnosis (docs/03 "Implementation (M6-S10)", stage 0; `Stress/KeepCornerWedgeDiagnosisTests`): every retry (`Wait` ->
`StartReturn` / `WalkToNode` -> `WalkToFootprint` -> `StandPoint`) picked the same cell and free point and the walk ended the
tick it started, by crowded arrival on a touching mate standing with the walker's goal cell and nearer the point (a mate that
deposited in reach of its node and kept the Keep's stand cell as its goal; at seed 27 one cell is a stand cell of both the
mine and the Keep), 1.3-2 m out of reach. Cure: a footprint walker stops on such a mate only within `QueueArrival`
(Reach - GoalInset x sqrt 2 = 1.073 m) of its point, where a stop is in reach. Proof: `GatherOverlapFuzzTests.Bug1431_*`
(seeds 4 / 22 / 27 / 35 / 44 at 0, red on c684902, green after); `Stress/HeldWorkerSweepStressTests`: no held worker on
seeds 2-56 at 0 / 0.25 / 0.5 / 0.75 (base: 5 / 4 / 2 / 1 seeds held).
