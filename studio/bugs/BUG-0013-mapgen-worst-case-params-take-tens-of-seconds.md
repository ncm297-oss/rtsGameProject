# BUG-0013: Map generation with params Validate allows can take ~35 s

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed |
| Found | 2026-10-03-1235, task M1-3 |
| System | terrain / map generator |
| Fixed by | dd63cca (M1-4a) |

## Repro
In `Rts.Sim.Tests.Stress.MapStressTests.WorstCaseValidParams_StillBounded`, set `MaxAttempts = 64`
(its `Validate` cap), then
`dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~WorstCaseValidParams"`.

Params: 512 x 512, `Level1Plateaus = 64`, `Level1MaxSize = 200`, `Level2Plateaus = 64`,
`RampsPerPlateau = 16`, `RampTries = 1024`, `MinPassableFraction = 1`, `MaxAttempts = 64`.

## Expected
Brief criterion 4: "Generation has a bounded retry/iteration count (never hangs)". Bounded is met;
a load-time stall of tens of seconds within the validated range is the concern.

## Actual
512 x 512: 34.6 s. 1024 x 1024 with the same caps: 30.0 s. Both end on the flat fallback
(100% passable is impossible). With `MaxAttempts = 4` it's about 2 s (the permanent test).

## Notes
The bound is the product of the caps: (64 + 64) rectangles x 16 ramps x 1024 tries x 64 attempts
= 134M ramp placements. Not reachable with sensible params; only a problem if params ever come from
data. A tighter `RampTries`/`MaxAttempts` cap, or an overall placement budget, would fix it.

**Producer triage (2026-10-03-1235):** bundle with BUG-0012 in the first commit of M1-4a:
tighten the `Validate` caps (e.g. `RampTries` <= 128, `MaxAttempts` <= 16, plateaus <= 32) so the
worst case stays under ~2 s, and lower `WorstCaseValidParams_StillBounded`'s guard to match.
Does not block.

**QA verification (2026-10-03-2220, M1-4a):** this repro (512x512 at the new caps) now takes 0.87 s
in a Debug test run, so it is fixed as filed. The caps still don't bound the worst case: wide, long
ramps on a 1024 map (which `Validate` still allows) take ~45 s in Debug. Filed separately as
BUG-0015.
