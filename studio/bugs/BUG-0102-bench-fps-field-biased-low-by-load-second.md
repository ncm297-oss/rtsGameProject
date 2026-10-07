# BUG-0102: The `bench:` line's `fps` reads low on short runs: it averages Godot's once-a-second counter, whose first sample is the load second

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-06-2114, task M2-7 (QA) |
| System | view: `game/scripts/BenchRunner.cs` (`_fpsSum` / `_fpsSamples`) |
| Fixed by | M2-H2 (f4e1b8d): `fps` = timed frames / elapsed seconds; `BenchTest` checks it in-process and on the binary |

## Repro
1. `& $env:GODOT --path game -- --bench 10 --vsync on` (120 Hz display) and compare `fps` with `frames / seconds`.
2. `& $env:GODOT --headless --path game -- --bench 0.01 --mute`.

## Expected
`fps` matches the frames actually drawn in the timed span (frames / seconds, which is about 1000 / avg).

## Actual
| Run | frames / seconds | `fps` printed |
| --- | --- | --- |
| windowed, vsync on, 120 Hz, 10 s (5 runs) | 119.9 | 110.9-111.1 |
| windowed, vsync off, 10 s | 1,904 | 1,727 |
| headless, 3 s (QaM27Test) | 144.7 | 107.0 |
| headless, 2 s | 145 | 88.0 |
| headless, `--bench 0.01` | 1 frame | 1.0 |

`Performance.Monitor.TimeFps` is Godot's frames-in-the-last-whole-second counter. The bench starts
under a second after launch (tick 2 + 30 warm-up frames), so for its first second every frame adds
the load second's count (often 1; the developer's `ramp-crossing.png` HUD label also shows "1 fps").
On a 60 s run the bias is 1-2 % (the docs/03 table: 59.5 on 60 Hz, 118.5 on 120 Hz); on a 10 s run
it is 7-10 %, which on a 60 Hz display would print about 55 fps for a run that drew every frame.

## Notes
- Fix: print `Stats.Count / Script.Elapsed` (or `1000 / Stats.Average`); docs/03 "Measurement"
  would then drop the "mean of TimeFps" sentence. `BenchTest.LineShape` doesn't change.

## Re-check (2026-10-07-0800, M2-H2 commit f4e1b8d): fixed
Verified: windowed `--bench 10 --vsync off --mute` printed `frames 14336 ... fps 1433.5` over 10.0 s (frames / seconds 1433.6); `BenchTest` in-process 124.7 vs 124.73.
