# BUG-0088: `OrdersTest.tscn`'s double-tap row fails under CPU load (process-time wait vs wall-clock tap window)

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed |
| Found | 2026-10-06-1744, Producer ACCEPT check (view track) |
| System | view test scene `game/tests/OrdersTest.cs` (control-group double-tap row) |
| Fixed by | M2-H2 (f4e1b8d): `OrdersTest` double-tap row waits on `Time.GetTicksMsec()` |

## Repro
1. Start two `dotnet test sim/Rts.Sim.Tests` runs in other worktrees (or anything that keeps every core busy).
2. `& $env:GODOT --headless --path game res://tests/OrdersTest.tscn` a few times.

## Expected
`ORDERS TEST PASS` every time (it did, 15 / 15 scenes, for the dev and QA when run alone).

## Actual
Under the Producer's load (three builds, two full suites and Godot runs at once) 2 of 6 runs printed
`ORDERS TEST FAIL: a single recall moved the camera`; 8 / 8 passed once the load eased, on both the
base `game/` (b9af5f2) and the M2-6 branch.

## Notes
- Mechanism (Producer reading, not proven): the row waits `DoubleTapSeconds + 0.05` on a
  `SceneTreeTimer` (process time) and then presses the digit, but `ControlGroups.Tap` measures the
  window with `Time.GetTicksMsec()` (wall clock). Godot's main timer sync smooths the process step to
  the recent typical physics-step count, so right after a stretch of slow frames a few fast frames can
  advance process time ahead of wall time; the timer then fires while the wall clock is still inside
  the 300 ms window and the single press counts as a double tap.
- Not an M2-6 regression: M2-6 touched neither `Tap` nor the timer; the failure was seen only under
  heavy contention. Pre-existing since M2-3.
- Fix (view hardening): wait on the wall clock in the test (loop frames until `Time.GetTicksMsec()`
  has advanced by 350 ms), or drive `Tap` with an injected clock in the test scene.

## Re-check (2026-10-07-0800, M2-H2 commit f4e1b8d): fixed
Verified: `OrdersTest.tscn` 3 / 3 PASS while the full sim suite ran.
