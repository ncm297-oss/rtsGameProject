# BUG-1064: `FogViewTest.AllyGhost` is red once shared vision (M6-S5) is merged: its skip condition sits after a check an ally's hall can no longer pass

| Field | Value |
| --- | --- |
| Severity | S2 (the scene loop is red on the merged tree: blocks M6-V16's landing; test-only, no product fault) |
| Status | open |
| Found | 2026-11-05-0805 integration (conductor), task M6-V16 |
| System | `game/tests/FogViewTest.cs` `AllyGhost` (BUG-1062 item 3's round-1 row) |
| Fixed by | |

## Repro
1. On `studio/2026-10-27-0658-view` at 2d4dbe4 (M6-V16 merged with `main` 1a90a08, which holds M6-S5):
   `powershell -File tools/qa/scene-loop.ps1 -Filter FogViewTest` (or the whole loop: 1 of 76 red, the same alone).
2. Output: `FOG VIEW TEST FAIL: ally ghost seed 1: the scout never saw the ally's hall`.

## Expected
The row prints its "skipped, the ally's hall stays in sight (shared vision)" line and the scene passes (the dev's intent, docs/03
"Implementation (M6-V16)"), or it tests the `EnemyAt` ghost branch some way that still exists.

## Actual
`AllyGhost` (line ~736) checks `W.Fog.CanSeeBuilding(0, hall) && W.Fog.Ghosts(0)[hall].Known` before the skip branch (line ~744).
Since M6-S5 `FogStore.UpdateGhosts` never enters an ally's building in the ghost list (`enemy = ... && !AreAllied(p, owner)`), so
`Known` is false while `CanSeeBuilding` is true (an ally's building is always seen): the check fails first and the skip is never
reached.

## Fix (direction)
Either: move the shared-vision skip above that check (`CanSeeBuilding` true and `Ghosts(0)[hall].Known` false = allied-as-own: print
the skip line, end the match), or stage the ghost branch with something that can still be a ghost: an **enemy** building seen then
left is the old row; an ally's building is never a ghost while it stands and collapses (no ghost) when the ally is out, so the
ally-ghost branch of `SelectionController.EnemyAt` is unreachable on `main` and the row should say so and go. Then the whole loop
green on the merged branch, smoke PASS, docs/03 "Implementation (M6-V16)"'s test sentence corrected, BUG-1062 item 3 reworded.
