# BUG-0260: D7's Ages clause anchor passes a contradicting sentence written after " - " on the same line

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-08-2144, task D7 |
| System | content tests (data track), docs/02 "Ages" pin |
| Fixed by | QA's oracle `QA/Content/AgesRuleQaTests.AgesTrailingClause_MatchesTheAnyOfRule` now reads the bullet from the file's lines and fails this mutant (D7 QA). `TechContentTests.G` still passes it. |

## Repro
1. In a scratch clone, change docs/02 line 115 to
   `Shock Hall, Forge): two production halls, or one hall and the Forge. - Or the Forge alone.`
2. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~TechContentTests"`: G passes.

## Expected
The BUG-0230 item 1 fix ("only the next bullet or the end of the section may follow") rejects any text appended to the
clause's bullet.

## Actual
`TechContentTests.AgeClause` checks `(?=\s+-\s|\s*$)` against `FactionPage.DocText`, which joins every line of the
section with single spaces, so " - Or the Forge alone." on the same line looks exactly like the start of the next bullet.
The eight BUG-0230 mutants (including the plain appended "Or the Forge alone.") all fail G, so the acceptance criterion
is met; this is a narrower residual.

## Notes
Contrived (a markdown author would not normally write " - " mid-line), hence S4. Fix in G: take the clause's bullet from
the file's own lines (a bullet starts at a line beginning "- ") and anchor the clause to the end of that bullet, as QA's
`AgesRuleQaTests.ClauseBullet` now does. A contradiction written as its own new bullet ("- Or the Forge alone.") passes
both pins; no regex on the clause can catch that.
