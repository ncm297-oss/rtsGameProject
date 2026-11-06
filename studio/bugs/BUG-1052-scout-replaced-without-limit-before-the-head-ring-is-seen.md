# BUG-1052: A scout lost before its head ring is seen is replaced without limit; a seat whose scouts die at the door feeds every worker to the role

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed (M6-SH1, dev; QA to confirm) |
| Found | 2026-11-05-1716, task M6-S7 (QA) |
| System | AI scout (`ScoutManager.Sweep` death branch, `AiPlayer.ReachLifted`), M6-S7 BUG-1048 fix |
| Fixed by | M6-SH1: `rules.json` `ai.scoutReplacements` (default 2) against the own unit losses |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~ReachRuleQaTests.EveryScoutKilledAtOnce"` (remove the
   `Skip`): a generated 2-seat Normal v Normal match (seeds 1-3); every scout seat 0 sends is killed on the tick it is
   sent (`CombatSystem.HitUnit`, as `GatherReachQaTests.ScoutKilledAtOnce` does for the first one), run to tick 6,000.

## Expected
The M6-S7 rule replaces a scout lost before the head ring is seen, so a seat that loses its scout early still looks at
home. It should not turn into a drain: a seat whose scouts keep dying should stop sending workers out after a few (the
row's bound: at most 4), and keep an economy.

## Actual
| Seed | Scouts lost by 6,000 | Workers at 6,000 | Wood at 6,000 | Unharmed run (workers / wood) | ab54ddb (scouts lost / workers / wood) |
| --- | --- | --- | --- | --- | --- |
| 1 | 9 | 0 | 0 | 20 / 770 | 1 / 18 / 0 |
| 2 | 9 | 0 | 0 | 20 / 800 | 1 / 19 / 810 |
| 3 | 9 | 0 | 0 | 20 / 880 | 1 / 19 / 980 |

The death branch sets `ScoutPhase` back to 0 whenever `ReachLifted()` is false, and nothing counts the replacements:
every think after a death `PickBuilder` takes the next worker. The reach never lifts (the ring is never seen), so it
never stops. A realistic camp is far milder: four enemy line units standing on the first head-ring point cost 1 scout on
seeds 1-3 (`ReachRuleQaTests.AnEnemyCampOnTheHeadRing_CostsTheSeatAtMostThreeScouts`, green), because each scout sees
some of the ring before it dies and the replacement skips what was seen. The drain needs scouts that die before they
see anything (an enemy at the hall's door, a tower rush covering the hall's exit).

## Notes
A cap on replacements (e.g. a `rules.json` `ai.scoutReplacements`, or "replace once, then lift the reach") would bound
it; the Producer decides whether the reach should then lift (BUG-1048's original symptom) or hold.

## M6-SH1 (dev)
- A lost scout is sent again only while the player's own unit losses (`PlayerView.OwnUnitLosses`, the saved
  `MatchStats.UnitLosses`) are at most `ai.scoutReplacements` (0-8, default 2, validated, hashed); past it the sweep ends
  (`ScoutPhase` 2) and the reach lifts, as in M6-S5. No new saved state, save format still 5.
- `QA/ReachRuleQaTests.EveryScoutKilledAtOnce_TheSeatDoesNotFeedItsWorkersToTheScoutRole` un-skipped: seeds 1-3 lose 3 scouts
  by tick 6,000 (was 9), 17 workers each (unharmed 20), farthest gather target 16.1 / 31.8 / 21.0 m (seeds 1 / 2 / 3; no
  150 m hold needed), wood 0 / 550 / 650. Seed 1's 0 wood: it knows no forest (its start sees none and every scout dies
  on its first tick), so the row asserts wood only where a forest is known.
- `Ai/ScoutReplacementTests`: n + 1 scouts lost for n = 0 / 1 / 2 / 4; the cap reads the own seat only (enemy losses don't
  engage it, own ones do); a save after the first or the second death loads equal and the cap engages on the same tick
  (`ASaveBetweenTwoScoutDeaths_LoadsEqual_AndTheCapEngagesOnTheSameTick`); the reads allocate nothing.
  `DataValidationTests` / `DataContentHashTests` rows for the knob. The 20 `StandardPresetPinQaTests` and 5
  `SharedVisionFfaPinTests` pins unchanged.
