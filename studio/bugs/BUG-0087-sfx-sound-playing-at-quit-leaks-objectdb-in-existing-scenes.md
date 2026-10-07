# BUG-0087: A sound still playing at quit leaves an ObjectDB leak warning in existing test scenes (intermittent)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-06-1744, task M2-6 |
| System | audio (`game/scripts/Sfx.cs`) |
| Fixed by | M2-H2 (f4e1b8d): `Sfx._ExitTree` stops the pool and waits (bounded, 200 ms) for one mix; close request stops the pool; `SfxTest` has no fixed wait |

## Repro
1. `dotnet build RtsGame.sln`
2. Run 5 times: `& $env:GODOT --headless --verbose --path game res://tests/OrdersTest.tscn` (same with `QaM22Test.tscn`, `QaH1Test.tscn`).

## Expected
Existing scenes exit as cleanly as before M2-6. docs/03 "Audio (M2-6)" presents the leak as a test-only
artefact that `SfxTest` works around with a 0.3 s wait.

## Actual
In about 2 of 5 runs each, OrdersTest and QaM22Test print (QaH1Test once in one sweep):

```
WARNING: 2 ObjectDB instances were leaked at exit (run with `--verbose` for details).
Leaked instance: AudioStreamWAV:9223372066751776288 - Reference count: 1
Leaked instance: AudioStreamPlaybackWAV:9223372072506361717 - Reference count: 1
```

The scene still prints PASS and exits 0, and the line is a WARNING, so the smoke gate and the
`ERROR` scans don't catch it. But the warning now turns up at random in scenes that don't know about
audio, and the same thing happens in the game whenever it quits within ~120 ms of a click or order
(e.g. a scripted run that quits right after input).

## Notes
`Sfx` never stops its pool players. Any scene that quits in the frame after a Select / Command play
leaves the playback in the audio server. A fix in production code, such as stopping the 8 players in
`Sfx._ExitTree` or on `NotificationPredelete`/`NotificationWMCloseRequest`, would remove the need for
the per-test wait. Found by the QA scene sweep; not reproduced in SmokeTest (no input there).

## Re-check (2026-10-07-0800, M2-H2 commit f4e1b8d): fixed
Verified: `SfxTest` 10 / 10 and `QaM26Test` 10 / 10 headless runs exit 0 with no ObjectDB / leak line, under suite load; the exit wait printed 22-93 ms, never near 200 ms. All 18 headless scenes clean.
