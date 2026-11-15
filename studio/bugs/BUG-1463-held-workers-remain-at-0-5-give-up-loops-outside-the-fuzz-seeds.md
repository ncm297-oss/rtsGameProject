# BUG-1463: At the shipped gatherOverlap 0.5, workers are still held (a give-up loop against standing mates or parked workers) outside the fuzz's seeds 2-56; two shapes are held only since the cure

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-11-15-0506, task M6-S10 (QA full) |
| System | sim: movement (footprint walk give-ups, `MovementSystem.QueuesBehind`), economy retry (`EconomySystem.Wait`) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~KeepCornerCureQaStressTests.HardShapes|FullyQualifiedName~KeepCornerCureQaStressTests.FuzzSeeds57To120|FullyQualifiedName~KeepCornerCureQaStressTests.Seed72" --logger "console;verbosity=detailed"`
2. Shapes (6,000 ticks each; "held" is the brief's word, checked on every tick past the 1,200-tick clock).

## Expected
The brief's goal: no held worker at the shipped 0.5 (criterion 4 gates only the fuzz's seeds 2-56, which pass). docs/02 now
says a queued worker stops only once it is in reach of the mine or drop-off.

## Actual
Runs (of 8 spawn-order shuffles) with a held worker, the M6-S10 tree against the diff base c684902 (base "0.5" = the same
test copy with the field written):

| Shape | 0, base | 0, M6-S10 | 0.5, base | 0.5, M6-S10 (shipped) |
| --- | --- | --- | --- | --- |
| 2 x 2 depot, a 2 x 2 mine flush on its east face, 12 Teblor Gatherers (`depot22_20`) | 8 | 8 | **0** | **8** |
| same, 16 Laborers | 4 | 6 | 0 | 0 |
| same depot flush against a two-deep tree line, 12 Gatherers | 8 | 8 | 0 | 0 |
| Keep on the map's west edge, mine below it against the edge (`edge1_15`), 16 mixed | 8 | 5 | 8 | 4 |
| Keep flush against a cliff, mine at its corner on the cliff (`cliff28_25`), 16 mixed | 8 | 8 | 8 | 1 |

Fuzz seeds 57-120 (scene, orders, window unchanged; `HeldWorkerProbe`): knob off none held (longest 59.2 s, seed 89);
**at 0.5 seed 72 holds one** (72.5 s; the base at 0.5 and at 0: none, 42.6 / 41.6 s). Seed 72's worker (p0 Laborer, node leg,
empty) stands 0.39 m from its Keep's footprint, 6.98 m from its node; every walk gives up after 19 stuck ticks (goal cell
dropped), `Wait` re-walks 20 ticks later into the same spot, 15 times in 600 ticks; within 3 m: four of its own **Idle,
loop-less** Laborers (1.10-2.84 m). `depot22_20` at 0.5: the held Gatherers stand Returning with 15 gold 1.4-1.6 m from the
depot's corner stand point, below the mine, goal cell dropped (a give-up), the same spot for minutes; one never delivers in
5 minutes.

## Notes
Not the crowded-arrival stop BUG-1431 cured: these walks end in a give-up, not a stop on a touching mate. It is the family of
BUG-1461 (own army parked on the path) and BUG-1430 (2) (seed 7 at 0.75, parked Idle workers): a walker pressing on standing
units that do not yield gives up and re-walks into the same spot. The cure makes such walkers press where they used to stop,
which helps most shapes (above: every edge / cliff / corner / 40-worker shape improves) but turns `depot22_20` with
Gatherers at 0.5 from 0 of 8 held to 8 of 8, and seed 72 at 0.5 from none to one. Relative to the game that ships today
(the base, knob off) `depot22_20` is unchanged (8 of 8) and seed 72 is new. A generic fix probably belongs with BUG-1461.
