# BUG-0380: an extra page damage claim passes the ability pin when the ability has another damage effect

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-10-0215, task D10b |
| System | content tests (`Content/AbilityContentTests.EffectProblems`) |
| Fixed by | D10c (2026-10-10-0624, data track); verified by QA D10c: the row is un-skipped and green, plus `QA/Content/SandstormBlindedPinQaTests` before / after / two-effect cases |

## Repro
1. Un-skip `Rts.Sim.Tests.QA.Content.AbilityPinQaTests.AnExtraPageDamageClaim_WithNoDataEffect_Fails`.
2. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~AnExtraPageDamageClaim"`.

The test edits the Cusser's Effect cell in memory to "Enemy units in the area take 120 siege damage plus 30 magic
damage, ..." and compares it to the unchanged data (one damage effect, 120 siege).

## Expected
The page promises a second hit the data doesn't have, so it is a value mismatch naming 'Cusser' and the field (e.g.
`'Cusser' Effect amount: page says '30 magic damage', data has no matching damage effect`). The brief says the pin
learns "<amount> <type> damage" ↔ `Amount` / `DamageType`, so the match should hold in both directions.

## Actual
No problem. `EffectProblems` only checks that each data damage effect has a claim; unmatched page claims are
reported only when the ability has no damage effect at all (`!anyDamage && claims.Count > 0`).

## Notes
Outside the D10b criteria (each named field's mutant does fail). Fix: after matching, report every claim that no
damage effect consumed. Low risk today (one damage effect in the game); matters once an ability carries two hits.
