# BUG-0103: M2-7 nits: vsync-on rows report smoothed deltas, "no bunching" remark, culture in two log lines, endless huge `--bench`

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-06-2114, task M2-7 (QA) |
| System | view: `BenchRunner`, `BenchScript`, docs/03 "Implementation (M2-7)" |
| Fixed by | |

## Repro
1. `& $env:GODOT --path game -- --bench 10 --vsync on`, five times.
2. Read `BenchScript`'s remarks, then `BenchScriptTests.LongFrame_FiresOneStepPerCall_AndBadDeltasAddNothing`.
3. `& $env:GODOT --headless --path game -- --bench 1e308 --mute`.

## Expected / Actual
1. **Vsync-on figures measure pacing, not cost.** Five 10 s runs at 120 Hz printed `avg 8.34 p50 8.34`
   with p99 8.40-9.10 every time, while the worst frame ranged 9.4-16.5 ms. The likely cause (not
   proven by an A/B run): Godot's
   `application/run/delta_smoothing` (on by default, not overridden in `project.godot`, and only
   active with vsync on) snaps `_Process` deltas to the refresh period, so the vsync-on rows in the
   docs/03 table (16.67 / 16.67 / 16.67 on 60 Hz) read as perfect frames by construction. The
   vsync-off rows (the pinned bar) are raw deltas and unaffected. docs/03 could say so in one sentence.
2. `BenchScript`'s remark says at most one step per call means "a long frame delays the steps behind
   it instead of bunching them". The schedule is cumulative (`_nextAt += step.Seconds`), so after a
   stall the overdue steps fire on consecutive frames (the dev's own test shows it), which is still
   bunching, one per frame. Behaviour is fine; the wording promises more.
3. `Bench running for ...` and `Bench worst frame ...` use the current culture (`0.###`, `0.00`); the
   `bench:` line is invariant. On a comma-decimal PC the two info lines would print `10,56 ms`.
4. `--bench 1e308` (finite, positive) is accepted and the run never ends; it printed the duration as a
   309-digit number. That input is outside any sensible range (a note, not a defect); an upper bound
   such as 3,600 s would match the other flags' "bad values warn and are ignored" rule.
