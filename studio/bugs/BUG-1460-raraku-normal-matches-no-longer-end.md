# BUG-1460: Raraku Normal v Normal seeds 1, 3, 4 and 6 no longer end inside 72,000 ticks (M6-S10's tree)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open (Raraku seed 1 only since M6-SH2; seeds 3 / 4 / 6 end at 25,083) |
| Found | 2026-11-15-0506, task M6-S10 (developer, criterion 11) |
| System | sim: AI end game (a winner that cannot finish), economy (wood on Raraku) |
| Fixed by | |

## Repro
`dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~MatchRowsM6S10Report&DisplayName~raraku" --logger "console;verbosity=detailed"`
(every check of `CriterionMatch` on, 72,000 ticks).

## Expected
The M5-5 Raraku row (seed 1) ends with a winner inside its 36,000 bound (28,231 on c684902).

## Actual
Seeds 1-8: none / 27,936 / none / none / 29,569 / none / 28,547 / 28,547; every check clean. Seed 1 probed (every 6,000
ticks): seat 0 is down to buildings alone from about 27,000 (0 workers, 0 army, 4,058 gold, 18 buildings falling to 11 by
72,000); seat 1 keeps 17 workers, all idle from 36,000 (gold 1,878, wood 14), and 2 soldiers that raze slowly; no hunt.
No held worker causes it (a seat-0 worker is held 5,000-7,300 and 8,600-10,000 by its own army parked at the rally point:
BUG-1461) and no lone unit goes unhunted (BUG-1116 is a different shape). `MatchCriterionTests`' Raraku row moved to seed 2 by
the brief's rule; `MatchCriterionSweepQaStressTests.Known` holds seeds 1, 3, 4 and 6 (silent seats; the shared list makes `FarExpandProbeQaStressTests` (raraku, 1) print its oracles, so that row is green); the two-process end-game row
follows the criterion row.

## M6-SH2
Seeds 1-8 now: none / 56,940 / 25,083 / 25,083 / 31,420 / 25,083 / 28,633 / 28,633 (were none / 27,936 / none / none /
29,569 / none / 28,547 / 28,547). QA: the drop-off rule alone (BUG-1520) ends seeds 3 / 4 / 6; the retrain alone ends
none. Seed 1: the winner (24 workers, 5,638 gold) knows no tree at all (the nearest stands 41.8 m from its drop-offs,
never explored), its scouts are spent and its starved waves died to towers, so the brief's wood rule had nothing to act
on and was not built. **Producer decision (docs/01 2026-11-16): a seat short of a resource its army needs that knows no
node of it sends a worker on the scout's sweep again, past `ai.scoutReplacements`; the next AI task.**
