# BUG-0181: ReplayRecorder accepts a sim with a non-default ProjectileCapacity; its replay plays with the default and fails its first checkpoint

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-08-0913, task M4-2b (sim track, QA full) |
| System | sim: replays (`ReplayRecorder` constructor), `SimConfig.ProjectileCapacity` |
| Fixed by | 0aeed2f (`ReplayRecorder` constructor guard); regression `QA/ProjectileQaTests.ARecordingOfASimWithANonDefaultProjectileCapacity_IsRefusedOrPlaysBack` (un-skipped) |

## Repro
`QA/ProjectileQaTests.ARecordingOfASimWithANonDefaultProjectileCapacity_IsRefusedOrPlaysBack` (skipped for this bug;
remove the `Skip` to run): a generated-map sim (seed 2, two players, 64 unit slots) with `ProjectileCapacity = 1`, a
recorder attached, 6 Crossbowmen against 6 Raiders spawned by command, 200 ticks, then `ReplayPlayer.Run`.

## Expected
Either the recorder refuses the sim, as it already does for a non-default `BuildingCapacity` ("The replay format
records only the default building capacity"), or the replay plays back.

## Actual
```
recorded with ProjectileCapacity 1, played with the default: ReplayResult { Error = CheckpointMismatch, Tick = 20, ... }
```
The recorder accepts the sim; the header has no capacity line, the player rebuilds the sim with the default (200
slots), shots that were lost in the recording land in the playback, and the first checkpoint differs.

## Notes
- docs/03 "Implementation (M4-2b)" says "`ProjectileCapacity` is not in the header, a replay plays with the default";
  nothing enforces it at record time. The fix is probably the same guard as the building capacity in the
  `ReplayRecorder` constructor (one line), or a header line.
- The game does not set `ProjectileCapacity` today (grep `game/`), so no shipped replay is affected; dev scenes and
  tests that set it can record replays that silently can't play back.

## Verification (QA re-check round 1, 2026-10-08-0913)
- The un-skipped row passes: `ProjectileCapacity = 1` is refused with `InvalidOperationException`.
- New boundary row `Recorder_TakesTheDefaultProjectileCapacityGivenExplicitly_AndRefusesOneSlotOff` passes. The
  default size minus 1 and plus 1 are both refused. The default given explicitly records, and its replay plays back
  200 ticks of a Crossbowman v Raider fight.
