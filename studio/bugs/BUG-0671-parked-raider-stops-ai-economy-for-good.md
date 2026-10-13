# BUG-0671: An enemy unit parked in the base stops the AI's economy for good: fled workers stay idle while it stays

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | open |
| Found | 2026-10-13-0916, task M5-4b |
| System | AI (DefenseManager, EconomyManager) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~DefenseQaTests.AParkedRaider_DoesNotStopTheEconomyForGood"`
   (flat AI scene, combat off, no army: one player 1 Raider holds 14 m from player 0's hall by the forest; checked 100 s later).
2. `AiWaveStressTests.TwoWavingAiSeats_*` seeds 1 / 6 / 11 (combat on, three raiders in each base).

## Expected
docs/02 "AI opponent" and the faction pages: workers flee to the hall while the base is attacked. Once they are safe, the
AI keeps an economy. A single unit it can't remove must not end its income for the rest of the match (in repro 1 the two
gold mines on the far side of the hall are out of the raider's reach).

## Actual
1. `4 of the 4 fled workers stand idle 100 s later: the economy leaves them alone while the raider stays` (`Defending`
   true throughout).
2. Both seats: `Gathers 0` in 6,000 ticks (5 minutes), gold 0, every worker `Idle` beside its hall from about tick 100 on.

## Notes
The rule follows the brief's letter ("the economy manager leaves them alone until the threat ends"). But the threat only
ends `defendFor` after the last trigger, and an enemy non-worker that stays within `defendRadius` (or keeps hitting a
building) re-triggers it at every think, so the workers are never released. They also flee to the nearest drop-off even
when that is the building under attack (repro 2: the raiders stand at the hall). Possible fixes (Producer's call):
release fled workers `defendFor` after their flee rather than after the last trigger; let them gather at nodes farther
than `workerFleeRadius` from the point; or flee to the drop-off farthest from the point. With BUG-0670 this deadlocks
AI-vs-AI play (see there). The developer reported this ("fled workers stay idle while the threat holds") and left it for
QA to judge.
