# BUG-0220: `SfxTest.tscn` flakes under CPU contention ("Select / Command played 0 times"): a 60 ms scene-tree timer against the 50 ms wall-clock rate limit

| Field | Value |
| --- | --- |
| Severity | S3 (flaky test; the scene loop goes red when another track's build or QA runs) |
| Status | fixed |
| Found | 2026-10-08-1435, task M4-V3 (proposed by the developer, confirmed by QA) |
| System | view scene test `game/tests/SfxTest.cs` (M2-6), `game/scripts/Sfx.cs` |
| Fixed by | 08dc8e3 (M4-VH1): `game/tests/WallClock.cs`; `SfxTest.Expect` waits on `Time.GetTicksUsec` since the last action; regression rows "8x game clock" (`SfxTest.FastGameClock`) |

## Repro
1. While another worktree runs `dotnet test` or Godot scenes: `& $env:GODOT --headless --path game res://tests/SfxTest.tscn`.
2. QA 2026-10-08-1435, worktree at a1ccd91 under load: scene loop `SfxTest FAIL` ("double-click a type / click own unit
   again / group recall / click own unit: Select played 0 times, expected 1"). Alone but with the other track's tests
   running, it failed 3 of 6 runs ("H: Command played 0 times", "box: Select played 0 times", "Shift+right-click:
   Command played 0 times") and then 1 of 6.
3. Quiet: a fresh clone at a1ccd91 passed 8 / 8, and at the base 4598751 (no M4-V3 code) also 8 / 8. The developer saw
   12 / 12 quiet and 3 / 23 failing under load.

## Expected
A deterministic pass whatever the machine load (the scene loop is the merge gate).

## Actual
`Expect` waits `CreateTimer(0.06)` plus one frame, then runs the action. It expects one play, but `Sfx.Play` drops a play
of the same event within `MinGapMs` (50 ms) of the last one, measured with `Time.GetTicksUsec`. The timer counts
frame deltas, and that clock can disagree with the wall clock by more than the 10 ms margin when frames stall (for
example, a delta clamped after a long frame). The play is then dropped as too soon.

## Notes
Not caused by M4-V3: same code since M2-6, and the M4-V3 head passes 8 / 8 when the machine is quiet. Possible fixes:
wait on the wall clock in the test (loop frames until `Time.GetTicksUsec()` has moved 60 ms past the last play), or
drive `Sfx.Play(e, frame, nowUsec)` with an explicit clock as the criterion-4 block of the same test already does.

## Verification (QA 2026-10-08-2144, M4-VH1)
- SfxTest under load (the full non-Perf suite, a second Godot scene loop and the other tracks' runs): 10 / 10, then a
  second batch of 10 / 10. Also PASS in both scene loops.
- The regression rows fail with the old wait. In a scratch clone at 08dc8e3, with `Expect` put back to
  `CreateTimer(0.06)` plus one frame, the 8x rows fail 6 of 9 ("8x game clock, click own unit 1: Select played 0
  times, expected 1", ...), and so does the following "right-click" row.
- The seven other scenes that waited out the sound gap with a scene-tree timer now use `WallClock.Wait`.
