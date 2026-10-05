# BUG-0041: View input-handling nits: clock takes negative x negative as time, a missing flag value eats the next flag, seed max prints as -1

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-05-1446, task M2-1 |
| System | view: FixedStepClock (ViewApi), LaunchOptions, Match log |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~FixedStepClockQaTests"`: the skipped row
   `NonFiniteOrNegativeInputs_AddNoTicks_AndKeepAlpha(delta: -1, speed: -1)` (un-skip it) fails: `Advance(-1, -1)` returns 5.
2. `& $env:GODOT --headless --path game --quit-after 30 -- --seed --speed 2`
3. `& $env:GODOT --headless --path game --quit-after 30 -- --seed 18446744073709551615`

## Expected
1. `FixedStepClock.Advance` docs: "Zero, negative or non-finite inputs add nothing".
2. `--seed` warns about its missing value and `--speed 2` still applies.
3. The log shows the seed the sim actually uses (18446744073709551615).

## Actual
1. `Advance` checks only the product `delta * speed`, so two negatives make a positive step (5 ticks).
   Unreachable today because `SimRunner` clamps speed to 0.25-8 and Godot's delta is never negative.
2. `WARNING: Ignoring --seed with bad or missing value '--speed'.` then `speed 1x`: the parser always
   consumes the next token, even when it is another flag, so `--speed 2` is lost.
3. `Match started: seed -1, ...`: `SimRunner.Seed` is a `long` (Godot export), printed signed. The sim
   gets the right `ulong` through the unchecked cast; only the log line is misleading.

## Notes
- Related, sim side (not M2-1's code): with no `data/` folder the error line is
  `ERROR: : : data directory '...' does not exist`; `DataError.ToString()` prints empty file and path
  fields as `: :`.
- Suggested fixes: reject `deltaSeconds <= 0 || speed <= 0` separately; in `LaunchOptions.Parse`, don't
  consume a value that starts with `--`; print `unchecked((ulong)Seed)`.
