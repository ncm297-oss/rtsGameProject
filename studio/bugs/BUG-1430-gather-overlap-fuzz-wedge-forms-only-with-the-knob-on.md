# BUG-1430: Criterion 7 not met: the economy fuzz has a worker 60+ s without a trip only with the knob on (seed 7 at 0.75, seed 3 at 0.25), and the asserting rows leave those seeds out

| Field | Value |
| --- | --- |
| Severity | S3 (filed S2 by QA against criterion 7 as written; re-triaged by the Producer at the 2026-11-14-2156 ACCEPT, see "Producer ruling") |
| Status | fixed (M6-S10, 2026-11-15-0506; through BUG-1431's cure) |
| Found | 2026-11-14-2156, task M6-S9 (QA) |
| System | sim: movement / economy (gather loop at a Keep's corner), tests (`Movement/GatherOverlapFuzzTests`) |
| Fixed by | M6-S10 (BUG-1431's rule) |

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

## Re-check 2026-11-14-2156, fix round 1 (4377d68): still open, S2 (criterion 7 not met), now a Producer decision
The round changed no production code. The developer's fuzz now has 56 rows (seeds 2-16 without 13, at 0 / 0.25 / 0.5 /
0.75). Four rows **measure the trip window instead of asserting it**, listed in `GatherOverlapFuzzTests.KnownWedges`:
0 / 4, 0.25 / 3, 0.25 / 4 and 0.75 / 7. The default loop is green because of those exclusions, not because the criterion
holds: **0.25 and 0.75 still fail criterion 7 on the developer's own seeds.**

What QA checked (`sim/Rts.Sim.Tests/Stress/GatherOverlapWedgeQaStressTests.cs`):
- **The exclusions are honest.** `KnownWedges_EveryBreachInAnExcludedRow_IsTheNamedKeepWedge` (4 rows, green): every worker
  past 60 s in an excluded row stands at its own Keep (0.06-1.87 m from the footprint). At 0.75 / 7 the only breach is the
  0.75 m Gatherer (unit 35). Three rows hide more than one breach, all at the Keep: seed 4 at 0 (units 25 and 23), seed 4 at
  0.25 (unit 22 Returning; unit 25 Gathering with cargo 0, 1.09 m from the footprint) and seed 3 at 0.25 (unit 7 twice).
  Every other invariant is still asserted on those rows, and every 0.5 row asserts the window (14 of 14 green).
- **"Today's rules, not the scaled distance" is true of the hold, not of how the run gets into it.** Seed 7 at 0.75 was run
  with the knob switched mid-wedge (the World's two flags set by reflection, test-only;
  `Seed7AtTheCap_RealRun_KnobOffOrNudgedMidWedge_Report`). With the knob off from 20 s or 40 s into the stretch, unit 35
  still goes 84.0 / 80.9 s without a trip, so once it is in the corner today's rules hold it. With the knob off from 0 s or
  10 s it gets free (longest 40.2 s). With the knob nudged to 0.74 from any point it is held longer (107.0 s): the Laborer
  that frees it at 57 s is a gather pair and presses in at the scaled distance. So the knob decides whether a run falls into
  the wedge and how it leaves; the wedge itself is today's corner handling. The developer's evidence row reproduces
  (nearest standing gather pair 8.62 m, reach 3.31 m, passers 46 ticks).
- **The breaches are chaotic.** The same scene with the knob switched on one tick late (at tick 2) gives seed 7 at 0.75 a
  longest gap of 32.9 s (no breach), seed 3 at 0.25 67.3 s with a different worker, and seed 4 at 0.25 60.2 s
  (`KnobSwitchedOffMidWedge_Report`). Which seed breaches at which value is a lottery over today's wedges.
- **The knob makes breaches rarer, but 0.5 isn't clean either.** `WiderSeedSweep_17To56_BreachesByValue` ran 40 more seeds,
  all buildable. Seeds past 60 s: off 5 (22, 27, 35, 44, 56), 0.25 3 (23, 27, 31), **0.5 2 (23: 77.7 s; 27: 90.8 s)**,
  0.75 0. Adding seeds 2-16 gives: off 6 / 54, 0.25 5, 0.5 2, 0.75 1. Breaches only with the knob on: **seed 23 at 0.5
  (77.7 s; 42.1 s off)** and at 0.25 (83.8 s), and seed 31 at 0.25 (65.5 s; 59.4 off). Seed 23's are BUG-1431's shape:
  Returning with a load, 1.41-1.72 m from the Keep's footprint, so out of reach. Seed 27 at 0 is an outbound worker held at
  the Keep **with the knob off** (Gathering, cargo 0, 0.63 m from the footprint, 89.8 s), the same class as seed 7's walker.
  Seed 31 at 0.25 is a slow Gatherer 6-12 m out with a full load, a long trip rather than a wedge.
- **docs/03 is wrong.** It says "criterion 7 holds at 0.5 on every seed" and "0.5 is the only value whose fuzz gaps all
  stay under 60 s". Both are true only of seeds 2-16. The developer's rows assert that seed set and stay green; QA's sweep
  row is the expected red.

For the Producer: **this can't be fixed inside M6-S9's scope.** Every breach found comes from today's Keep-corner handling:
BUG-1431's drop-off shape, or an outbound walker held at the corner. That happens with the knob off on 6 of 54 seeds, and
curing it moves the shipped hash, which the brief rules out. The knob only changes which seeds hit it (fewer overall). The
call: amend criterion 7 (for example "no more 60 s breaches than off over N seeds, and none away from a Keep corner") and
schedule BUG-1431, or hold the shipping slice until BUG-1431 is fixed. Shipping 0.5 as planned leaves 2 breaches in 54
seeds, against 6 with the knob off.

## Producer ruling (2026-11-14-2156 ACCEPT)
- Criterion 7's "no worker without a trip for 60 s" was the Producer's error at the PLAN (rule 48, now rule 66): it
  does not hold with the knob **off** either (6 of 54 fuzz seeds, QA's sweep), and the brief forbade any change to
  knob-off behaviour, so the task could not move it. It is re-read as "measure and report". What was measured: seeds
  past 60 s over seeds 2-56: off 6, 0.25 5, 0.5 2, 0.75 1; every breach is BUG-1431's Keep-corner shape; one
  knob-only breach at 0.5 (seed 23, 77.7 s against 42.1 s off).
- S3, not S2: the shipped data has no `gatherOverlap`, so no match on `main` can show it, and with the knob on the
  wedge is rarer than today's. It is still **open and gates the value**: the session that ships a non-zero
  `gatherOverlap` fixes BUG-1431 first (or in the same hash move) and must turn QA's five expected-red Stress rows
  green (`EconomyFuzz_Seeds2To16_NoWedgeOnlyTheKnobForms` at 0.25 and 0.75, `Bug1430_*` x2,
  `WiderSeedSweep_17To56_BreachesByValue`) or re-state them as "no more breaches than off, none away from a Keep
  corner" with the figures. They reach `main` red by this ruling (rule 57; Stress only, not in the default loop).

## Fix (M6-S10, developer)
The five Stress rows red by ruling are green as written on the M6-S10 tree (`GatherOverlapQaStressTests.EconomyFuzz_Seeds2To16_*`
at 0.25 / 0.75, `Bug1430_*` x2, `GatherOverlapWedgeQaStressTests.WiderSeedSweep_17To56_BreachesByValue`). Seeds past 60 s over
2-56: off 1 (seed 14, 63.6 s, a worker walking: not held), 0.25 / 0.5 / 0.75 none. QA's `KnownWedges_*` (4) and
`Seed7AtTheCap_RealRun_*` rows lost their subject (no breach left): re-stated as "no breach", old asserts in comments.
