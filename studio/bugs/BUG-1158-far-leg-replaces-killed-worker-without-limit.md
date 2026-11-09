# BUG-1158: The far expansion's leg replaces a killed worker without limit: a broke seat feeds every worker to a guarded free start

| Field | Value |
| --- | --- |
| Severity | S3 (real AI bug outside the criteria; scripted, not seen in the measured matches: 0-1 leg deaths a seat on Pale Hills) |
| Status | open |
| Found | 2026-11-09-0426, task M5-9b (QA inspection, QA focus "the scout dying on the leg") |
| System | AI: the far expansion's leg (`Ai/FarExpansion.cs`, `Leg`) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~FarExpandQaTests.ALegWorker_KilledAtAGuardedStart_IsNotReplacedWithoutLimit"`
2. The scene: the far scene of `FarExpandTests` (flat 64 map, four start locations, every gold mine emptied, a Malazan
   Normal seat with 5 workers, 0 gold), combat on, no far mine, and an enemy Whirlwind Lookout Tower (18 m range) on each
   of the two free starts.

## Expected
A leg worker that dies is replaced, but not without limit: the scout's sweep stops after `ai.scoutReplacements` (2,
BUG-1052) so a seat doesn't feed its workers to whatever killed the scout. A broke seat (it arms only when it can't pay
for anything and has no gold) cannot train new workers, so every worker it loses is gone for good.

## Actual
```
leg workers sent 5; workers left 0 (of 5 at the start, plus any trained), phase 1, looked 4, start 3
```
Each think after a death, `Leg` picks the next worker (`PickBuilder`) and walks it to the same look point
("another worker takes the leg from the point it was walking to"). In 12,000 ticks all five workers die at the towers;
the seat has no economy left. Nothing in the rule notices the deaths, the seen tower, or that the point was never reached.

On the measured matches it stays small: Pale Hills Normal v Normal seeds 1 / 2 / 5 and Hard v Normal seeds 2 / 3 lose
0-1 leg workers a seat (QA's `FarExpandProbeQaStressTests.PaleHills_LegLosses_Measured`). Where it can bite: a free start
taken by the enemy's far expansion (its depot, army, towers), or a defeated seat's start that the winner now holds.

## Notes
- Candidate fixes (developer's call): a replacement budget per start (as `ai.scoutReplacements`), or marking the look
  point looked when its worker dies on it, or skipping a start where a known enemy building stands (the view's buildings
  and ghosts, which `Candidate` already reads).
- The worker is `IsScout` while on the leg, so the defense's flee does not pull it back either.
