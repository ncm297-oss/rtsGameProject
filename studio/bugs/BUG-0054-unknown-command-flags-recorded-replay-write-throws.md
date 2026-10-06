# BUG-0054: A command with unknown Flags bits is accepted and recorded; ReplayFormat.Write then throws

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-05-2330, task M1-7 |
| System | commands / replays (`Simulation.Enqueue`, `ReplayRecorder`, `ReplayFormat.Write`) |
| Fixed by | |

## Repro
1. `QA/OrderQaTests.UnknownFlagsEnqueued_RecordedReplay_StillReads` (skipped under this bug).
2. A sim with a `ReplayRecorder`; `sim.Enqueue(Command.Stop(0, unit) with { Flags = 2 })`; tick on;
   `ReplayFormat.Write(recorder.ToReplay())`.

## Expected
Everything `Enqueue` accepts can be written and played back. Either `Enqueue` refuses a command
`Replay.Validate` would refuse (unknown flag bits, and the same for an undefined `Kind`), or the
recorder logs it in a form that still reads back. The sim already drops the command at apply, so
gameplay doesn't change.

## Actual
`Enqueue` accepts it and the recorder logs it (the developer's report lists "unknown flags accepted
at enqueue, dropped at apply" as a deliberate deviation). `Replay.Validate` then refuses the log, so
`ReplayFormat.Write` throws
`System.ArgumentException : Replay is not valid (InvalidCommand); it would not read back.`
One bad command loses the whole session's replay, and the save path throws.

## Notes
- No factory sets an unknown bit. It takes a caller bug: view code building `Flags` by hand, or a
  future flag added to the view before the sim knows about it.
- Same thing already happens for an undefined `CommandKind` (`(CommandKind)99`): `Validate` checks
  `Enum.IsDefined`, `Enqueue` doesn't. Fix both together, e.g. make `Enqueue` throw
  `ArgumentException` for an undefined kind or unknown flag bits, like it already does for an
  unknown player.
- S3, not S1: it needs malformed caller input, and there is no replay save in the game yet (M6).
  The Producer may raise it before the view starts writing replays.

## Producer triage (2026-10-05-2330)
S3 stands. Not player-facing until a replay is saved from the game (M6) and no factory sets a bad bit. Fix in the M1 end-of-milestone hardening session: `Enqueue` throws `ArgumentException` for an undefined kind or unknown flag bits (as it does for an unknown player), with a regression test; un-skip the QA row.
