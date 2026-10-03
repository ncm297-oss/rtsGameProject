# BUG-0003: SimMath.Sin/Cos return values far outside [-1, 1] for huge angles

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-03-0907, task M1-1 |
| System | determinism / SimMath |
| Fixed by | |

## Repro
1. Un-skip `Rts.Sim.Tests.QA.SimCoreQaTests.SinCos_HugeFiniteAngles_StayWithinUnitRange`.
2. `dotnet test sim/Rts.Sim.Tests --filter FullyQualifiedName~SinCos_HugeFiniteAngles`

## Expected
A sine or cosine is always in [-1, 1] and finite for finite input. docs/03 says accuracy degrades
past a few hundred radians, which is fine, but the result should still be a plausible sine.

## Actual
- `SimMath.Sin(1e9f)` = `3.09523395E+10`, `SimMath.Sin(-1e9f)` = `-3.09523395E+10`
- `SimMath.Sin(1e20f)` = `-Infinity`
- `SimMath.Sin(float.MaxValue)` = `-Infinity`, `SimMath.Sin(-float.MaxValue)` = `+Infinity`
- `1e6` and `1e7` stay in range (inaccurate but bounded); the break starts around |x| ~ 1e8.

## Notes
Range reduction `x - floor(x / 2pi + 0.5) * 2pi` is done in float. Once the float spacing at |x|
exceeds a few radians, the reduced `r` is no longer in [-pi, pi], the single fold does not bring it
into [-pi/2, pi/2], and the degree-9 polynomial explodes (or `inf - inf` gives a huge value).
Not reachable by M1-1 code; it becomes reachable once anything accumulates an angle without
normalizing it (e.g. a facing that integrates turn rate). Cheap fixes: clamp the reduced `r` into
[-pi, pi] (or loop the fold), or reduce in double, or document and assert a max input magnitude.
A sine outside [-1, 1] would turn a direction vector into a teleport.
