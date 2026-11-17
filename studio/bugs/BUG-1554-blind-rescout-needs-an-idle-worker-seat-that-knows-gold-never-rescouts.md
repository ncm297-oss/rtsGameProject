# BUG-1554: The blind re-scout fires only for a seat with an idle worker, so a seat that knows gold but never saw a tree never re-scouts

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open (Producer ruling 2026-11-16-2036, docs/01 2026-11-17: idle-only stands for now; cured together with BUG-1556 by a saved count of blind re-scouts lost, capped by `ai.scoutReplacements`, in the sim's next AI task (a save-format change and a pin move)) |
| Found | 2026-11-16-2036, task M6-12 (M6-S12), QA full |
| System | AI scouting (`ScoutManager.Rescout` / `BlindRescoutDue`, `AiPlayer.PickBuilder(idleOnly)`) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~M6S12AiRulesQaTests.KnowsGold_NeverSawATree_ShortOfWood_NoIdleWorker_NeverRescouts_Bug1554"`.
2. The scene: the flat AI map, unexplored, no tree anywhere, player 0's two home mines standing, gold 1,000, wood 0, three
   units lost (past `ai.scoutReplacements`), the sweep ended (phase 2). Its army mix costs wood.

## Expected
docs/01 2026-11-16 item (3): "a side short of a resource it has never seen a node of sends a worker scouting again, past the
scout cap". The handoff's candidate line says "sends one idle worker", which did not foresee what "idle" means after the
economy has run.

## Actual
From tick 1,200 to 7,200 the seat is blind to wood (`ScoutManager.BlindTo(Wood)` true) on **16 of 20** `ai.scoutAfter`
windows and sends **0** re-scouts: every worker is on the gold mines (5 of 5 on a gather loop), and the economy (which runs
first) sends every idle worker to the known kind when the wanted kind has no node (`EconomyManager.Gatherers`' fallback), so no
worker is ever idle after the economy. In practice the rule fires only when the seat knows **no usable node of either kind**
(Raraku seed 1's base case: its gold mined out first). The developer reports the same (docs/03 "Implementation (M6-S12)"
Q4). QA's survey (`Stress/M6S12RallySurveyQaStressTests`, 120 seat-runs of 12,000 ticks on the three maps and generated maps
of 2-4 seats) saw 4 re-scouts in all, every one on generated seed 5's seat 0 (1 with two seats, 3 with three), and docs/03 says
the rule moves no criterion row.

## Notes
- Why it was narrowed: taking a gatherer each window lost 16 scouts against QA's `ReachRuleQaTests.EveryScoutKilledAtOnce_*`
  bound of 4. A middle way for the Producer: take a gatherer only of the kind the seat has enough of, at most once per N
  windows, or only while the seat's own losses since the last re-scout are 0.
- docs/02's sentence says "has a worker with nothing to gather", which is accurate but does not say that this means "knows no
  node of either kind".
- The row pins today's behaviour (`Assert.Equal(0, rescouts)`); it flips when the Producer rules the decided rule in.
