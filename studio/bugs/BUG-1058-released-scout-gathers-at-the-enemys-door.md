# BUG-1058: A released scout gathers at the node nearest itself, often inside the enemy base

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-11-05-2148, task M6-SH1 round 1 (QA re-verify) |
| System | AI scout / economy (`ScoutManager.FoundEnoughToGather` / `NearestForestAtAnEnemyDoor`, `EconomyManager.NearestNode`) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~ScoutSentAgainQaTests.AReleasedScout_FirstGathersClearOfTheEnemysBuildings"`
   (the theory is skipped for this bug; remove the `Skip`). It runs 30 rows.
2. The scene: seeds 1-10, Normal v Normal, the generated start with player 0's trees within 90 m taken. One scout is
   killed 0, 200 or 2,000 ticks after it goes out. When a scout that was sent again is released alive, the row records
   the first node that scout is set to gather, and how far that node is from the nearest enemy building.

## Expected
- The door rule (docs/03 "Round 1", BUG-1056) is there because "a scout gathering there, and the gatherers the economy
  sends after it, would work inside the enemy base (measured: such rows lost 2-5 more workers)".
- docs/02 "AI opponent" says the released scout "stands at the far forest it found, so the first wood comes home a walk
  sooner".

## Actual
- 25 of the 30 rows release a scout.
- 13 of the 25 released scouts first gather **gold**, not wood.
- 8 of the 25 first gather within `ai.workerFleeRadius` (16 m) of an enemy building, 7.1-15.8 m off: 6 at a gold mine,
  2 at a forest. 4 of those 8 scouts die by tick 6,000.

| Row | Released scout's first node | Distance from an enemy building | Scout died | Own losses, release on / off (mutant) |
| --- | --- | --- | --- | --- |
| seed 3 @0 | gold | 9.5 m | tick 5,820 | 6 / 3 |
| seed 4 @0 | wood | 15.8 m | no | 2 / 4 |
| seed 4 @200 | wood | 15.8 m | no | 2 / 3 |
| seed 4 @2000 | gold | 9.5 m | no | 2 / 2 |
| seed 7 @0 | gold | 7.1 m | tick 5,870 | 2 / 1 |
| seed 7 @200 | gold | 7.1 m | tick 5,794 | 2 / 1 |
| seed 9 @0 | gold | 8.9 m | no | 3 / 3 |
| seed 9 @200 | gold | 8.9 m | tick 5,441 | 2 / 1 |

Why it happens:
- `NearestForestAtAnEnemyDoor` checks only the known forest **nearest the hall**.
- But the economy hands the released (idle) scout to `EconomyManager.NearestNode`, which picks the node of the wanted
  kind **nearest the worker**. That worker is standing where the sweep left it, often in or next to the enemy base.
- So the scout usually takes the enemy's own mine (gold share) or the forest at the enemy's door, the very case the door
  rule was meant to rule out.

## Notes
- The release is still a net gain over seeds 1-10, so the criterion holds and this is S3, not S2.
  - Wood by 6,000 on the 0 / 200 / 2,000 rows: 10 / 9 / 6 of 10 with the release, 9 / 6 / 4 with the release removed.
  - Own losses summed over the 30 rows: 74 with the release, 72 without.
- The release's cost falls on the rows above: seed 3 @0 loses 3 more workers with no wood gained (first wood at 4,694
  either way).
- The developer's 10-seed figures (9 / 8 / 5) don't match QA's rerun of the same rows on a0b75b7 (10 / 9 / 6). QA counts
  `GatheredWood > 0` at tick 6,000.
- Possible fixes (the developer's call):
  - Send the released scout to the safe forest the rule judged (a `Gather` on that node) instead of leaving it idle for
    `NearestNode`.
  - Or judge the door against the nodes nearest the scout.
  - docs/02's sentence should then match what happens.
