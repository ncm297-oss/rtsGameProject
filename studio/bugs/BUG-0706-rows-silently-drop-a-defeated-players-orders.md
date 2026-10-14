# BUG-0706: 13 green rows now run with a "defeated" enemy whose orders the door drops (a building placed before any unit)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-14-0946, task M5-4e |
| System | tests (defeat rule re-staging) |
| Fixed by | |

## Repro
A scratch clone of `947a61a` with one probe line in `Simulation.Apply` (the door): throw when a defeated player's
non-Noop command is dropped while that player still has a live building (a drop only the new rule causes). Full suite,
excluding the rows about the defeat itself (`MatchOverTests`, `QA/DefeatRuleQaTests`, `DefeatCountsFuzzTests`) and
BUG-0705's row:

| Row | First order dropped |
| --- | --- |
| `AttackOrderTests.AUnitHittingABuilding_TurnsOnTheEnemyUnitHittingIt_AfterItsSwing` | player 1's `Attack` (the Raider's order onto the Heavy Infantry) |
| `AttackOrderTests.AttackMove_WhileHittingABuilding_ANewPointRepicksAUnitInSight_TheLegsOwnPointKeepsTheBuilding` (3 rows) | player 1's `HoldPosition` |
| `QA/AttackMoveRepickQaTests.SameTick_LaterOrder_DecidesTheRepick` (3 rows) | player 1's `HoldPosition` |
| `QA/FightReissueQaTests.AttackMoveOntoAnAttacker_WhileHittingABuilding_TakesTheAttacker` | player 1's `Attack` |
| `QA/ProjectileQaTests.BuildingHitter_ShotByAnArcherInSight_TurnsOnTheArcher` | player 1's `HoldPosition` |
| `QA/CombatViewApiQaTests.MultiTickFrames_SlotReuse_Pauses_InvariantsAndHashTwin` (seeds 1, 2, 7, 19) | a `SpawnUnit` of player 0 or 1 |

## Expected
Each row's scene is what its summary says: e.g. "the Raider ordered onto the Heavy Infantry hits it", "the enemy
Raider holds".

## Actual
The scenes place player 1's Tent straight into the store before player 1 has a unit, so player 1 is defeated on tick 0
(a Tent doesn't keep a side in since M5-4e). Its units are then put in through the store seam (which doesn't check the
flag), and every order sent for them is dropped at the door. The rows stay green for another reason (an idle Raider
auto-acquires and strikes anyway; the checks come before a non-holding unit moves; the hash twin drops the same spawns
on both sides), so they no longer test what they say, and a regression in the dropped path would not show. The state
they build (a defeated player with live units) can't happen in a match.

## Notes
- Fix: re-stage like the ~20 rows M5-4e already re-staged: `TestSim.KeepIn(sim, 1)` (or place the unit before the
  building) so player 1 is in when its orders apply. Don't loosen any check. Several are QA-owned; re-staging is fine.
- A cheap guard for the future: a debug-only assertion in a test helper (`CombatScenes.Place` / `TestSim`) that fails
  when a unit is placed for a defeated player would have caught all 13.
- The probe found no other green row affected. A first probe (throw on any defeat while the player still has a live
  building) hit 155 rows: mostly combat, tower, fog and ghost scenes whose target side owns only a building and is
  defeated on tick 0, but sends no order afterwards, so the defeat changes nothing they check (the rows about the
  defeat itself are among them).
