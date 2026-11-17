# BUG-1463: At the shipped gatherOverlap 0.5, workers are still held (a give-up loop against standing mates or parked workers) outside the fuzz's seeds 2-56; two shapes are held only since the cure

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open (S3, a movement task's: the five rows are skipped as known-bug rows since M6-S12, bodies and bounds unchanged, cause traced per shape; no row is red) |
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

## M6-SH2
Raraku seed 1, seat 0's worker 1 held from 5,131: it stands in the east mouth of a one-cell passage where the AI's rally
point is, eight soldiers parked round it; a shove moves a unit along the contact normal, into the wall (BUG-0033's
rule). Reproduced in `Movement/ParkedArmyGatherTests` (`Bug1461_*` report rows). A generic shove change freed 1 of 3
passage shapes and not Raraku's: reverted. Still red at 0.5: `HardShapes` cliff28_25 / depot22_20 / edge1_15,
`TwoByTwoDepot` (22, 20) Gatherers, `KeepOnTheMapEdge` (1, 15); `FuzzSeeds57To120` is green. **Producer decision
(docs/01 2026-11-16): the AI's rally point keeps off one-cell passages (an AI rule); the five synthetic shapes get one
more bounded trace in the next AI task and are then either cured or re-stated as known-bug rows: no fourth red session.**

## M6-S12 (2026-11-16-2036, the developer's note, applied by the Producer)
Traced on the base 95186a7 from the rows' own HELD lines. Red at 0.5 were `HardShapes` cliff28_25 (1 of 8 shuffles),
depot22_20 (8 of 8), edge1_15 (4 of 8), `TwoByTwoDepot_MineAtItsCorner` (22, 20) Gatherers and `KeepOnTheMapEdge`
(1, 15); `KeepAgainstACliff` (28, 25) at 0.5 was green on the base (the PLAN's list named it by mistake).
- depot22_20 (Gatherers, r 0.75): the mine flush on the depot's east face leaves stand points only round the mine's two
  corners; mates working the mine hold them (Gathering does not yield); loaded Gatherers give up (goal cell -1) and
  `Wait` re-walks into the same spot.
- edge1_15 / KeepOnTheMapEdge (1, 15): a loaded 0.75 m worker frozen in the one-cell gap between the Keep's south face
  and the mine's north face, closed by the map edge, pressed by a mate working the mine; never delivers in 300 s.
- cliff28_25: the same one-cell gap closed by the cliff, a Laborer frozen beside a mate working the mine.
One family (a one-cell lane or corner shared by workers at their node and loaded mates); no army, no AI. The only
generic cure is a movement yield / shove change (M6-SH2's reverted family), out of M6-S12's scope. The five rows are
`Skip = "BUG-1463: <cause>"`, bodies and bounds unchanged; QA un-skipped them on a scratch clone and all five are
still red with the traced shapes. docs/03 "Implementation (M6-S12)" item 1.
