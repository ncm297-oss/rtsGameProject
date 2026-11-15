# BUG-1430: Criterion 7 not met: the economy fuzz has a worker 60+ s without a trip only with the knob on (seed 7 at 0.75, seed 3 at 0.25), and the asserting rows leave those seeds out

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | open |
| Found | 2026-11-14-2156, task M6-S9 (QA) |
| System | sim: movement / economy (gather loop at a Keep's corner), tests (`Movement/GatherOverlapFuzzTests`) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~GatherOverlapQaStressTests.Bug1430" --logger "console;verbosity=detailed"`
   (Stress; runs `GatherOverlapFuzzTests.Run(overlap, seed, tripWindow: true)`, the developer's own fuzz scene).
2. The wider sweep: `--filter "FullyQualifiedName~GatherOverlapQaStressTests.EconomyFuzz_Seeds2To16"` (seeds 2-16 at 0 and at
   each value; fails a seed whose worker passes 60 s only with the knob on).

## Expected
Brief criterion 7: with the knob at 0.25 / 0.5 / 0.75 the economy fuzz keeps every worker delivering (no worker without a
trip for 60 s while its node lives). QA focus: "a wedge that only forms with the knob on".

## Actual
Longest gap without a trip (s), seeds 2-16 (13: the fuzz scene can't place a Keep, skipped):

| Seed | 0 | 0.25 | 0.5 | 0.75 |
| --- | --- | --- | --- | --- |
| 3 | 58.5 | **74.6** | 37.9 | 33.1 |
| 4 | 68.1 | 70.7 | 51.4 | 39.4 |
| 7 | 52.7 | 45.8 | 40.8 | **81.7** |
| others (2, 5, 6, 8-12, 14-16) | 40.2-57.4 | 29.6-51.4 | 28.4-48.1 | 27.5-48.7 |

```
seed 7 at 0.75: worker 35 of player 1 (Moving, at <88.06126, 100.69065>, goal <86.125, 111.875>, cargo 0 Gold, stuck 0)
  without a trip for 1201 ticks at tick 5080, its node at 41,56 alive, its Keep at <84, 104>
seed 3 at 0.25: worker 7 of player 0 (Returning, at <137.316, 169.48497>, goal <137.6, 168.125>, cargo 10 Gold, stuck 0)
  without a trip for 1201 ticks at tick 3048, its node at 69,85 alive, its Keep at <134, 164>
```

0.5 holds on all 14 seeds. 0.75 (the cap, `DataLimits.MaxGatherOverlap`) and 0.25 do not. The developer's
`EconomyFuzz_InvariantsHold_EveryWorkerKeepsDelivering` asserts only seeds 2-5; seeds 6 and 7 are in no row at any value
and the code comment gives no reason for 7 (docs/03 table (c) prints 81.7 s as a "reason against 0.75"). Seed 3 at 0.25 is
left out as the pre-existing drop-off wedge (BUG-1431), but it crosses the 60 s line only with the knob on.

## Notes
- QA's trace of seed 7 at 0.75 (a scratch probe, ticks 3,850-5,100): worker 35, ordered by the fuzz to the mine south of
  its Keep, stands still at (87.83, 99.83), just off the Keep's north-east corner, for about 1,100 ticks. It cycles every
  `RetryTicks`: Moving with velocity about 3e-6 m/tick for ~20 ticks, then Gathering (stand), then a fresh walk; its stuck
  count goes 0 -> 17-19 -> 0, so it never gives up. Every neighbour within 2.5 m is an **Idle** worker of its own player
  with no loop (`IsGatherPair` false: loops ended when their trees ran out), standing round the corner 1.2-2.4 m away and
  never shoved aside. So the scaled pair distance is not in play at the moment of the wedge; the knob changes the scene
  (more trips, trees run out sooner, more loop-ended workers parked at the Keep) and today's corner handling then holds
  the walker. At 0 on the same seed the run diverges early and nobody parks there.
- Seed 3 at 0.25 is BUG-1431's shape (Returning, out of reach at a Keep corner).
- Either the wedge gets a fix, or the Producer re-scopes the criterion (for example: 0.5 only, or the cap lowered to the
  values that hold). Leaving seeds out of the asserting rows hides the failure; the rows should name every left-out seed
  and why.
