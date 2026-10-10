# BUG-0420: `whenBelowHp` counts a unit exactly at the line as below for many decimal fractions (single-precision line)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-10-1015, task M4-4b-3a |
| System | unit passives (`PassiveSystem.IsBelow`) |
| Fixed by | |

## Repro
1. Un-skip `Rts.Sim.Tests.QA.PassiveQaTests.ExactlyAtTheLine_IsNeverBelow_ForDecimalFractions` and run
   `dotnet test sim/Rts.Sim.Tests --filter FullyQualifiedName~ExactlyAtTheLine`.
2. Or: `PassiveSystem.IsBelow(15, 100, (float)0.15)` returns true.

## Expected
docs/03 "Implementation (M4-4b-3a)" and the docs/01 row: "exactly at the line is not below". A unit at 15 / 100 hp with
`"hpFraction": 0.15` is exactly at 15 % and must not be below.

## Actual
`IsBelow` is `hp < fraction * maxHp` in single precision with `fraction = (float)json`. `(float)0.15 * 100` is
15.000001, so 15 counts as below. 107 failures for three-decimal fractions and max hp 1-400, for example
0.15 x 100 = 15, 0.075 x 200 = 15, 0.09 x 300 = 27, 0.135 x 200 = 27, 0.008 x 375 = 3.

The shipped Zealot is not affected: 0.5 is exact in binary and 63 / 83 are odd, so its line is never a whole number.
Any later data with a fraction such as 0.15, 0.3 or 0.35 and a max hp that makes the line whole gets the wrong boundary.

## Notes
Use an exact comparison. For example, have the loader store the fraction as a rational (an integer per mille, or
`decimal`) and compare `hp * 1000 < perMille * maxHp` in integers, or compare in `decimal`. Plain double doesn't fix it,
because 0.15 * 100 is also 15.000000000000002 in double. Whatever form is picked should go in docs/03, which now says
"in single precision" and also "exactly at the line is not below". Those two statements disagree for these fractions.

## Producer triage (2026-10-10-1015 ACCEPT)
S3 confirmed, not blocking: no shipped fraction is affected (0.5 is exact). Fix direction (Producer default): compare in integers
(`hp * 1000 < perMille * maxHp` with the fraction stored per mille at load, or an equivalent exact form) and say so in docs/03
where it now reads "in single precision". Fold into **M4-4b-3b** if that task touches `PassiveSystem` (a few lines), else the
end-of-M4 sim hardening. Un-skip `PassiveQaTests.ExactlyAtTheLine_IsNeverBelow_ForDecimalFractions` with the fix.
