# BUG-0361: ReplayRecorder accepts a non-default ZoneCapacity, which playback can't reproduce

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-10-0215, task M4-4b-2 |
| System | replays (`ReplayRecorder` constructor), zones (`SimConfig.ZoneCapacity`) |
| Fixed by | 5c2de85 (M4-H2): un-skipped `QA/ZoneQaTests.TheRecorder_RefusesANonDefaultZoneCapacity`, `ReplayPlayerTests.Recorder_RefusesANonDefaultZoneCapacity` (1, 63, 65; 64 attaches) |

## Repro
1. Un-skip `QA/ZoneQaTests.TheRecorder_RefusesANonDefaultZoneCapacity`. It fails with `Assert.Throws() Failure: No exception was thrown`.
2. The consequence is pinned by `QA/ZoneQaTests.ANonDefaultZoneCapacity_Recorded_FailsItsOwnPlayback` (passes):
   - Set up a generated-map sim with `ZoneCapacity = 1` and attach the recorder.
   - Two Priests cast Sandstorm at once. The second finds the store full, so only 1 zone exists.
   - `ReplayPlayer.Run(rec.ToReplay(), data)` returns `CheckpointMismatch`: playback rebuilds the default 64 slots and makes the second zone.

## Expected
The same rule as the building capacity (M3-2) and the projectile capacity (BUG-0181). The replay format has no line for the
size, so the recorder should refuse a non-default one with `InvalidOperationException` when it attaches. Today it records a
replay that reports a false determinism failure on playback.

## Actual
The recorder attaches without complaint, and the replay can't be played back. The doc on `SimConfig.ZoneCapacity` says "Not in
the replay header: a replay plays with the default", but nothing enforces it.

## Notes
The fix is one `if` next to the two existing guards in `ReplayRecorder`'s constructor. Player impact is low: today only tests
and tools set the capacity.

## QA verification (2026-10-10-0624, M4-H2)
Verified by QA 2026-10-10-0624 (M4-H2): the un-skipped row failed on the base 63efab2 and passes on 5c2de85; the guard sits beside the building / projectile ones in `ReplayRecorder`'s constructor.
