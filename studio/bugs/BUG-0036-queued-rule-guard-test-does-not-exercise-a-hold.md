# BUG-0036: The M1-5 "jammed group still gives up" test never exercises a hold; four safety mutants of the queued rule pass the dev suite

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-05-1234, task M1-5 |
| System | movement tests (LocalMovementTests, queued-walker rule) |
| Fixed by | |

## Repro
In a scratch copy, mutate `sim/Rts.Sim/Movement/MovementSystem.cs` and run the dev's tests
(`dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~LocalMovementTests|FullyQualifiedName~ScenarioTests"`):
1. M2: delete `if (u.StuckTicks[i] == 0) u.StuckTicks[i] = 1;` in `Apply` (a queued unit may sit at 0
   and pass the "made progress" signal on).
2. M3: drop `u.StuckTicks[j] == 0 &&` from the queued check in `Plan` (any walking groupmate ahead
   holds the count, progress or not).
3. M8: replace that floor with `u.StuckTicks[i] = 0;` (a queued tick counts as progress: waiters
   reset each other forever, the failure mode the rule's comment warns about).
4. M7: `remaining[i] = act == ActStuck ? best : after;` (a queued tick may raise `BestRemaining`, so
   the same ground can be "progressed" over again).

## Expected
docs/03 (M1-5) gives `JammedQueueBehindAnEnemyInOneCellCorridor_AllGiveUp` as the proof that "a
jammed group still gives up" under the queued rule; it should fail when either safety condition
of the rule is removed.

## Actual
All four mutants pass every dev test, including `JammedQueueBehindAnEnemyInOneCellCorridor_AllGiveUp`
and `CrowdThroughAThreeCellGap_10Seeds_NoneGiveUp_AllArrive`. In the 1-cell corridor the units ahead
are blocked, so their velocity is zero and they count as standing: no tick is ever queued, and the
test passes with or without the rule's guards.

QA tests kill all four: the new `QA/QueuedGiveUpQaTests.CrowdAtAOneCellGapPluggedByAnEnemy_AllGiveUp_InBoundedTime`
(5 rows; a crowd jammed at a plugged gap must all give up within the walk plus 10 give-up periods)
(the only killer of M7), and, for M2 and M8, `CrossMap_NoMovingUnitShowsZeroStuckWithoutANewBest`
(the "0 means progress" invariant, every tick); M2, M3 and M8 also fail the older `QA/ShoveQaTests`
crossing rows and the `Stress/LocalMovementStressTests` crowd rows.

## Notes
No product change needed; the dev test's doc comment (and the docs/03 sentence citing it) overstate
what it proves. Either reword them or point at the QA tests.
