# BUG-0390: `QaGhostViewTest` seed 6 fails its "walked at least 10 m" check now that BUG-0311 is fixed

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open (view track / QA scene `game/tests/QaGhostViewTest.cs`; **blocks the merged scene loop**: Producer triage at the 2026-10-10-0624 ACCEPT: a conductor-dispatched view round at integration rewrites the check per the note below (no `Check` weakened); else the view's first item next session) |
| Found | 2026-10-10-0624, task M4-H2 |
| System | Godot-side QA scene (`game/tests/QaGhostViewTest.cs`, `GoneGhostAttack`) |
| Fixed by | |

## Repro
1. On the sim branch at 5c2de85: `powershell -File tools/qa/smoke.ps1`, then
   `& $env:GODOT --headless --path game res://tests/QaGhostViewTest.tscn`.
2. The output ends with `QA GHOST VIEW TEST FAIL: gone ghost seed 6: the units did not walk to the ghost (42.971428 -> 39.166855 m)`.

## Expected
The scene checks what the M4-3b order promises: an Attack on a gone building's ghost walks the units there, and the order
ends when the player sees the ground (the same update that drops the ghost). After BUG-0311's fix that happens on both seeds:
- seed 1: the ghost goes at tick 218 and the orders end at 219;
- seed 6: the ghost goes at 138 and the orders end at 139.

So the `KNOWN BUG-0311` line no longer prints, and the scene could run `--strict`.

## Actual
`Check(after < before - 10f, ...)` (line 296) needs the selected units' *mean* distance to the footprint to drop by 10 m.
On seed 6, attacker 0 walks to 13.8 m (sight 16) and its fog shows 2 footprint cells at tick 138. The ghost goes and every
attacker's order ends at 139. That is correct: the player now knows the building is gone. The other three attackers were
still about 42-49 m away: two on level 2 at 41.7 m, and one idle at 48.6 m that never took the order, which is the same on
the base. So the mean drops only 3.8 m.

On the base 63efab2 the check passed only by accident. The orders ended at 339 (nearest-point sight, the BUG-0311 rule), the
ghost stayed until 862, and in between the units retaliated against nearby enemies and wandered closer (mean 26.0 m).

The sim behaviour is right; the check's assumption ("the run lasts long enough for the group to close 10 m on average") no
longer holds.

## Notes
- Suggested replacement, which says what the scene means and is no weaker: the unit that ended the order (the selected unit
  nearest the footprint when the ghost went) got within its sight of the footprint, *and* walked at least 10 m toward it
  (track per-unit start distances). Also drop the `--strict` gate around the BUG-0311 check so it always fails when
  `|ordersEnded - ghostGone| > UpdateInterval`.
- This is a view-track file and the sim developer was barred from it. Not fixed by QA from the sim worktree, to avoid a
  conflicting edit with the view track's concurrent session. The conductor or the view track should change it before or at
  the merge, since the merged scene loop is the gate.
