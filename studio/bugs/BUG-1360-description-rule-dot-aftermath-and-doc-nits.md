# BUG-1360: Description rule nits after BUG-1330 (a damage-over-time aftermath with its rate before "for N seconds" reads red; a doubled `<summary>`)

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-11-13-2306, task D-H8 |
| System | `sim/Rts.Sim.Tests/Content/AbilityContentTests.cs` (the description-number rule) |
| Fixed by | |

## Repro
1. In `QA/Content/DH8DescriptionRuleQaTests.AnAftermathOfBurning_IsReadAsItsOwnKind`, add
   `[InlineData("When it ends, the unit is Burning (5 damage a second) for 4 seconds.", null)]` (the status's `onEnd` is
   `{ "status": "burning", "magnitude": 5, "duration": 4 }` there) and run
   `dotnet test sim/Rts.Sim.Tests --filter FullyQualifiedName~AnAftermathOfBurning`.
2. Read `AbilityContentTests.cs` just above `public enum Role`.

## Expected / Actual
1. **A damage-over-time aftermath stated with its rate first:** a correct tooltip "Burning (5 damage a second) for 4
   seconds" fails twice: `'4 seconds' reads as a duration, but its data duration is none` and `data duration (the
   aftermath's ...) 4, which the text does not state`. `afterNamePct` only lets an optional `N%` sit between the status
   name and "for", so a rate there breaks the link. "Burning for 4 seconds, taking 5 damage a second" passes. No shipped
   or landing row has a non-slow aftermath, so nothing reads wrong today; a later faction's burning aftermath would hit it.
   A fix could allow an optional damage-rate phrase (or any parenthesis) between the name and "for".
2. **Doubled doc comment:** the old one-line `/// <summary>The data field a description number's words name (BUG-0410).</summary>`
   was left above the new multi-line `<summary>` on `Role`. Builds clean; the doc tooling keeps only one.

## Notes
Found while attacking BUG-1330's fix (D-H8). The developer already noted a related limit: an ability that itself applies
the aftermath's status (for example Slowed) would have its own "Slowed N%" read as the aftermath. No data does that today.
