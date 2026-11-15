# BUG-1431: A worker returning with a load stands out of reach at a Keep's corner for about a minute (today's rules, knob off)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-11-14-2156, task M6-S9 (found by the game-dev while writing the gather-overlap fuzz; id given and re-measured by QA) |
| System | sim: economy (`EconomySystem.StartReturn` / `WalkToFootprint` stand points), movement (BUG-0146's footprint walk) |
| Fixed by | |

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
