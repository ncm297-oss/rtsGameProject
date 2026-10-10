# BUG-0241: A chaser whose scan takes two or three unreachable targets in turn never gives up: every switch re-takes the progress mark

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-08-2144, task M4-H1 (QA hostile variant of BUG-0149) |
| System | combat give-up (`CombatSystem.Engage` / `Acquire`: `ChaseBest`, `ChaseStall`, `ChasePrev`) |
| Fixed by | 5c2de85 (M4-H2): un-skipped `QA/ChasePrevQaTests.UnreachableTargetsTakenInTurn_TheChaseStillEnds` (6 rows); the `Bug0241Pin_*` row deleted |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~ChasePrevQaTests"`. The skipped theory
   `UnreachableTargetsTakenInTurn_TheChaseStillEnds` (2 and 3 targets, rotation every 1/2/3 scans) is this bug. The pin
   `Bug0241Pin_UnreachableTargetsTakenInTurn_NeverEnd` is green today.
2. Scene: the give-up rows' 40 x 40 map with the sealed level-1 plateau (x 20-35). A Heavy Infantry at cell (18, 20).
   Two or three holding Laborers on the cliff top at (22, 17), (22, 20), (22, 23), spotted (`CombatScenes.Spot`). Every
   `scansPerTurn x ScanInterval` ticks, a different Laborer gets `Target = chaser`, which makes it the tier-0 pick. This
   is the store seam the dev's `CombatTests.SwitchingBackToTheTargetHeldJustBefore_KeepsTheStallCount` uses. In a match,
   cliff-top enemies whose own targets change would do the same thing.

## Expected
docs/03 (give-up): "a unit whose scans took two targets in turn (one drifting in and out of its sight) restarted the count
every scan and chased forever". The fix keeps the stall count on a switch back, so the chase of unreachable targets
should end within a bound. I used 3 x 3 x `GiveUpScans` scans + 400 ticks = 760 ticks.

## Actual
On this branch (267c719) and on its base 4c1f168 alike, the chase never ends:
```
2 targets, every 1 scan:  ended at -1, switches 190, max stall 1, give-ups 0
2 targets, every 3 scans: ended at -1, switches 64,  max stall 1, give-ups 0
3 targets, every 1 scan:  ended at -1, switches 190, max stall 0 (base: 1), give-ups 0
3 targets, every 3 scans: ended at -1, switches 64,  max stall 2 (base: 3), give-ups 0
```
The chaser shuttles along the cliff foot (y 39-42 m) for the whole run.

## Notes
- This was there before M4-H1; the fix didn't cause it. Keeping `ChaseStall` on a switch back (BUG-0143, BUG-0149)
  doesn't help, because `Engage` re-takes `ChaseBest` from the new target's gap on every switch. The chaser then walks
  along the cliff toward the new target, the gap drops by more than `ChaseProgress` (0.1 m), and that counts as progress,
  which zeroes the stall count. So the count can't climb whatever `Engage` does with it.
- docs/03 now lists "three or more targets taken in a cycle are each a fresh chase" as a known limit. That understates
  it: two targets taken in turn livelock too. The two-target case in the crowd row sweep (seed 4) ends because those
  targets don't pull the chaser along a wall.
- Possible fix: keep `ChaseBest` per remembered target (or don't re-take it on a switch back), or give up a chase whose
  total switches since the last landed hit pass a bound.

## QA verification (2026-10-10-0624, M4-H2)
Verified by QA 2026-10-10-0624 (M4-H2): all 6 un-skipped rows failed on the base 63efab2 and pass on 5c2de85. Option chosen: bound the switches (`UnitStore.ChaseSwitches`, reset by a landed blow, a target loss, an order, or a switch that got closer; `GiveUpScans` = 10, an existing constant) with the friend-fights exception. The switch count is hashed above the stall count and `ChaseChainBest` with the chase memory (`StateHashTests.Hash_CoversEveryCombatField_...`). Brawl rows (`CombatGiveUpQaTests`, counter triangle) still green.
