# BUG-1556: A blind seat whose scouts die at its door re-scouts every 15 s with no cap: with the retrain rule it spends all its gold on workers that walk out and die

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed (M6-S14 item D, session 2026-11-26-1847; QA verified, accepted 2026-11-26-1847) |
| Found | 2026-11-16-2036, task M6-12 (M6-S12), QA full |
| System | AI scouting (`ScoutManager.Rescout` / `BlindRescoutDue`) with the retrain (`BuildOrderExecutor.Retrain`, BUG-1465) |
| Fixed by | M6-S14 (D) |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~M6S12AiRulesQaStressTests.EveryScoutKilledAtOnce_TheBlindSeatsDrain_20Minutes_Report"`.
2. The blind scene (`M6S12AiRulesQaTests.BlindScene(keepMines: false)`: no node of either kind known, five idle workers,
   1,000 gold, past the scout cap, sweep ended); each scout is killed the tick it is sent (an enemy camped at the door, as
   `ReachRuleQaTests.EveryScoutKilledAtOnce_*` stages it). 20 minutes (24,000 ticks).

## Expected
QA focus (the brief): "the re-scout as a drain (a camping enemy killing each scout: workers lost in 20 minutes, bounded?)".
docs/02's new sentence: "a worker at work is never taken for it, so an enemy camped at the door does not drain the economy".

## Actual
```
20 min: re-scouts 25, scouts killed 25, own units lost 25, trained 20, workers alive 0 (none from tick 1201),
gold 1000 -> 0, most re-scouts in one window 1; at 5 min: killed 20, workers 1, gold 200
```
One worker every `ai.scoutAfter` window walks out and dies; when none is left the retrain rule (a seat with no worker trains
one first thing) buys another, which is idle (no node known) and goes next window. The seat ends with no worker and no gold
by about 6 minutes and stays so. On the base (95186a7) the same seat keeps its five workers and 1,000 gold (no code path there leaves phase 2, so no worker is sent). The only bound is
the seat's gold; `ai.scoutReplacements` (the cap that exists for exactly this) is bypassed by design.

## Notes
- Taking only idle workers keeps the income (none is lost here: there is none), so docs/02's sentence is true of gatherers,
  but the seat's whole stock goes. A cap would do: e.g. no re-scout while the seat's own losses since the last re-scout went
  out exceed `ai.scoutReplacements`, or one per `ai.scoutAfter` x (re-scouts lost + 1).
- The rule is one window per re-scout at most (asserted), so the drain is linear, not a burst.

## Fix (M6-S14)
The seat counts its blind re-scouts lost (`Blackboard.BlindScoutsLost`, saved in `AIST`) and stops at `ai.scoutReplacements`. The 20-minute drain row: 2 lost, 4 workers and 950 gold at 20:00 (was 25 lost, none, 0). `ReachRuleQaTests.EveryScoutKilledAtOnce_*` bound raised by the cap (seed 1 measured 5), with this id. `ScoutCapQaTests` (the 20-minute row), `ScoutReplacementTests` and `Stress/MapSlice2StressTests.EveryScoutKilledAtTheDoor_OnANewMap_*` (all 20 rows measured 5 a seat: 2 x cap + 1) raised the same way.
