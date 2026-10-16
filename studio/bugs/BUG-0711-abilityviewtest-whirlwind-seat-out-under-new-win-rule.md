# BUG-0711: two view tests stage a building before its player has a unit, so under M5-4e that player is out and its later commands are dropped (`AbilityViewTest`, `QA/ViewApi/M6V7QaTests`)

| Field | Value |
| --- | --- |
| Severity | S3 (one scene of the loop and two unit-test rows red on `main` once the sim's M5-4e and the view's M6-V7 are both merged; no product bug) |
| Status | fixed |
| Found | 2026-10-14-0946, task M5-4e (QA's scene loop on the sim branch) and the Producer's merged-tree check; filed by the Producer at ACCEPT |
| System | view test scene `game/tests/AbilityViewTest.cs` (`ProphecyBarRow`); view QA `sim/Rts.Sim.Tests/QA/ViewApi/M6V7QaTests.cs` (`ViewApiDriven_HashesEqualABareTwin_*`) |
| Fixed by | eb21700 (M6-V7 round 1, at the 0946 merge): `AbilityViewTest` spawns a Whirlwind Camp Follower for seat 1 before the Smithy; both `M6V7QaTests.ViewApiDriven_*` twins call `TestSim.KeepIn`. Status set by the Producer at the 2026-10-15-2325 PLAN (the index row already said fixed) |

## Repro
1. `& $env:GODOT --headless --path game res://tests/AbilityViewTest.tscn` on a tree with the sim's M5-4e merged:
   `ABILITY VIEW TEST FAIL: seed 1 prophecy: Zealot max 63, type hp 63` (and seed 6).
2. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~M6V7QaTests.ViewApiDriven"` on the same tree: both rows fail at
   `GatherMaps.Building` (`Expected: 2, Actual: 1`, `M6V7QaTests.cs:154`): the second building never lands.

## Expected
1. The row (BUG-0450 on screen) places a Smithy for seat 1 and researches Dryjhna's Prophecy; the Zealots then spawn at 83 / 83.
2. Player 0 owns a "Billet" and a Barracks, and the view-driven twin hashes equal the bare twin while the Billet is razed.

## Actual
1. `ProphecyBarRow` spawns the Smithy (`Command.SpawnBuilding(1, smithy, ...)`) while seat 1 owns no unit (the match is `--units 0
   --no-bases`; the Raiders of the earlier rows belong to other matches of the scene). Under the M5-4e rule a player that has built
   and has no live unit and no finished unit-producing building is out on the next tick, so the `Research` is dropped at the door and
   the Zealots' max hp stays 63.
2. `M6V7QaTests.cs:153` names `BuildMaps.House` as the "Billet" and places it for player 0 with no unit of player 0's: player 0 is out
   on the next tick (a House keeps nobody in), so the Barracks' `SpawnBuilding` is dropped.

## Fix (view track; a few lines each)
1. Spawn one Whirlwind unit for seat 1 (a Camp Follower on a far passable cell, out of the row's spots and the mages' sight) three
   ticks before the Smithy, so seat 1 is never out; no `Check` loosened.
2. `TestSim.KeepIn(a, 0)` and `TestSim.KeepIn(bsim, 0)` before the first `GatherMaps.Building` (both twins alike, so the hashes stay
   equal), or place a real infantry hall type first; nothing else loosened.
Standing rule 24 in `studio/handoff.md`. Verify both on a tree with both branches merged (`dotnet test ... ~M6V7QaTests` 13 / 13; the
scene PASS).

## Notes
- The M6-V7 brief listed the three match-over scenes for re-staging; QA's round-0 note flagged this fourth scene and the Producer
  routes it: a conductor-dispatched view fix round before the view merge if one can be dispatched (the view used 0 of 2), else the
  view's first item next session with the loop red on `main` by one scene meanwhile.
