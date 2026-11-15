# BUG-1450: shadow.md passes the readiness check but two cells read wrong in the pins: lower-case "passive" sentences are skipped, and Summon Wraiths' Radius "—" fails the ability pin

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed (D-H9, verified by QA 2026-11-15-0506) |
| Found | 2026-11-14-2156, task D-R38 (QA) |
| System | docs (`docs/factions/shadow.md`), content pins (`Content/PassiveContentTests`, `Content/AbilityContentTests`, `Content/PagePinReadinessTests`) |
| Fixed by | D-H9 (session 2026-11-15-0506) |

## Repro
1. **Passives skipped silently.** Remove the `Skip` from
   `QA/Content/DR38ShadowPageQaTests.EveryPassiveTheNotesAnnounce_IsReadByThePassivePin` and run
   `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~DR38ShadowPageQaTests"`.
   Result: `Expected: ["Shadowed", "Pack Hunter"]`, `Actual: []`. shadow.md lines 67 and 74 write `passive **Shadowed**:` and
   `passive **Pack Hunter**:`. `PassiveContentTests.PassiveSentence` is case-sensitive (`Passive \*\*...`), as malazan.md,
   teblor.md and whirlwind.md write it. `PagePassives` returns no passive for the page. `PagePinReadinessTests` doesn't
   check passive sentences, so the readiness check still reports 0 problems.
   `DR38ShadowPageQaTests.WithPassiveCapitalised_ThePinReadsBothSentences` (green) shows the fix: with a capital P the pin
   reads `(Shadow Archer, Shadowed)` and `(Hound of Shadow, Pack Hunter)`.
2. **Summon Wraiths' Radius.** Remove the `Skip` from
   `DR38ShadowPageQaTests.SummonWraithsRow_PassesTheAbilityPin_OnASummonOfTheLoadersShape`. The test makes a teblor copy
   with a spawn-only summon named "Summon Wraiths" on the Shaman. The loader allows that shape only with no `range` and
   no `radius`, which is the shape the page describes. The test inserts shadow.md's row into the copy and runs it through
   `AbilityContentTests.Compare`. Result:
   `teblor.md "Abilities" 'Summon Wraiths' Radius: page '—', data 0 m`. The pin allows "—" for Range on a rangeless
   kind (D-R36), but not for Radius, where `NumberProblem` reports '—' against data 0. Kind, Range, Cast, Cooldown and
   Duration all pass.

## Expected
Brief D-R38 asks for the page "in the shape the content pins read", so that the roster can land in the same session as
its schemas. Every cell a pin reads should parse to what the page means. Run through the pins' own parsers, the page
should give no silent skip (the brief's QA focus: "a column the parser skips silently").

## Actual
Both cells pass `PagePinReadinessTests` and then go wrong in the pins:
- Item 1: the passive pin never sees the page's two passive promises. When the roster lands, the page-to-data direction
  can't report a Shadowed / Pack Hunter promise that the data doesn't keep. It also can't list them as pending. When G3
  later lands a passive on the Hound, data-to-page fails with "no 'Passive **Name**:' sentence". The fault is in the page,
  but that message points at the data.
- Item 2: the landing fails `AbilityContentTests` on the Radius cell. Either the page writes `0 m`, or the pin accepts
  "—" for a summon with spawn effects only (consistent with the Range rule and with the loader refusing a radius there).

## Notes
- Fix for 1: capitalise both "Passive" in shadow.md (one letter each). Optionally, `PagePinReadinessTests` could flag a
  bullet containing `passive **` that `PagePassives` doesn't read.
- Fix for 2 is the Producer's choice between the page and the pin. The pin side matches how the loader thinks about it
  (BUG-0452: a spawn-only summon "has no radius"), so "—" is arguably the right word.
- Neither item blocks D-R38's criteria: the readiness check is structural and reports 0 problems.

## Fix (D-H9)
- Item 1: shadow.md writes "Passive **Shadowed**:" and "Passive **Pack Hunter**:". `PagePinReadinessTests` now names any
  "passive **X**:" (any case) in the unit notes that `PassiveContentTests.PagePassives` does not read, with the page and the
  bullet's line (`AShadowPassiveThePinDoesNotRead_IsOneProblemNamingTheLine`, 3 rows).
- Item 2: the pin side. `AbilityContentTests` accepts "—" for the Radius of a summon whose effects are all `spawn` (radius 0,
  the loader's shape); "0 m" still passes there; any other ability with "—" for Radius is read against its data and fails
  (`AMutatedTelasFireCell_FailsNamingAbilityAndColumn` "| — |" row, `ASummonsRadiusDash_IsAcceptedOnlyForSpawnEffectsOnly`).
- Both `DR38ShadowPageQaTests` rows un-skipped, bodies unedited; both pass.
