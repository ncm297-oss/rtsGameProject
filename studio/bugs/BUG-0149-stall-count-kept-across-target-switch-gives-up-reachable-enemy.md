# BUG-0149: A stall count carried across a target switch makes a chaser give up a reachable enemy behind a short detour and stand Idle in sight of it

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-07-2315 (re-check round 2 of 2026-10-07-2014), task M4-1 fix round 2 |
| System | combat (BUG-0137 / BUG-0143 give-up: `CombatSystem.Engage` keeps `ChaseStall` when switching targets) |
| Fixed by | M4-H1 (sim track): `UnitStore.ChasePrev` (hashed): a switch keeps the stall count only back to the target held just before or to the given-up one, else a fresh chase; `QA/CombatFriendExceptionQaTests.StalledChaser_SwitchesToAnEnemyBehindAWall_WalksRoundAndFightsIt` un-skipped; `CrowdRowSweepStressTests.FiveHundredUnitsTo500RandomGoals_Seeds1To4_AllTerminate` green |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~StalledChaser_SwitchesToAnEnemyBehindAWall_WalksRoundAndFightsIt"`
   (`QA/CombatFriendExceptionQaTests`, skipped with this id; remove the `Skip` to run).
2. Scene: 40 x 40 map with the sealed cliff-top plateau of the give-up rows (x 20-35). A Heavy Infantry at cell (18, 20)
   chases a holding Laborer on the cliff top until `ChaseStall` reaches 8. Then a holding Raider is placed at cell
   (13, 20), about 10 m away and in sight, behind a 1-cell wall at x = 15, y 16-24 (the way round is about 10 m longer).

## Expected
The Raider can be reached on foot and is in sight, so the chaser walks round the wall and fights it. A fresh chase gets
`CombatConstants.GiveUpScans` (10) scans before it gives up. docs/03 says a switch keeps the count so that two targets
taken in turn can't restart it forever. It doesn't say a reachable new target only gets the scans the old chase left over.

## Actual
At eace378: `behind a wall: fought False; gave it up True after 135 ticks; give-ups 3`. The scan switches to the Raider
with stall 9. The way round doesn't close the gap within one scan, so the chaser gives the Raider up. It then takes the
Laborer again and gives it up, takes the Raider again and gives it up. That reaches `MaxGiveUps`, and from then on it
stands Idle 10 m from a reachable enemy it can see (the BUG-0143 symptom, in a small scene).
The same test on a scratch copy of eace378 with `Engage` resetting `ChaseStall` on every switch (the round-1 rule):
`fought True; gave it up False after 168 ticks; give-ups 0`.
On open ground (no wall) the switch works at eace378: `StalledChaser_SwitchesToAReachableEnemy_ReachesAndFightsIt`
switches in 4 ticks and fights in 71.

## Notes
- Where it happens: any chaser that stalled on something (a cliff-top or detour target, a crowd-blocked chase with no
  friend fighting) and then switches to a target whose path first leads away. On generated maps every cliff top has a
  ramp, so "reachable by a detour" is common.
- A fix has to keep the livelock the developer found with reset-on-switch
  (`CrowdRowSweepStressTests.FiveHundredUnitsTo500RandomGoals_Seeds1To4_AllTerminate`, seed 4: two targets taken in
  turn). Some options: keep the count only when switching back to the target held just before (or to one already given
  up); or reset it when the new target is closer than the old one's best gap.
- Not a livelock and not on the M4-1 criteria (brawls are fought to a finish, termination rows green), hence S3.
