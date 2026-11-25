# BUG-1691: shadow.md's "Passive **Shadowed**" note will fail the passive pin when the roster lands (stillStealth is not a passive); also stale docs

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed (D-H10, 2026-11-25; by the ruling, docs/01 2026-11-24 ACCEPT (3)(a)) |
| Found | 2026-11-24-1431, task D35 |
| System | faction page / content pins (`PassiveContentTests`, `PagePinReadinessTests`) |
| Fixed by | D-H10: `Content/PassiveContentTests`, `Content/PagePinReadinessTests` |

## Repro
1. Unskip `QA_TheShadowedSentence_LandsGreenOnItsStillStealthRow` in
   `sim/Rts.Sim.Tests/QA/Content/D35HitBonusTextQaTests.cs`.
2. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~D35HitBonusTextQaTests"`

The test puts shadow.md's Shadowed sentence on teblor.md's Javelin Thrower and gives that unit `stillStealth
{ status: stealthed, delay: 3 }` (G1's row) on a temp copy of the shipped data. Then it runs `PassiveContentTests.Compare`.

## Expected
D35's goal is that the roster can land without a pin gap, and `PagePinReadinessTests` reports 0 problems for shadow.md.
The Archer's note should land green with its G1 row.

## Actual
`PassiveContentTests` reads "Passive **Shadowed**:" as a data passive. Its wording names no trigger, and `stillStealth`
is a unit field, not a passive. So it reports "its wording names no trigger the sim has", and "Shadowed" is not in
`PendingPassives`. The readiness check only checks the sentence's shape, so it stays at 0. The developer reported this
as deviation (a) and left it outside the brief.

## Notes
- Fix options (the Producer's call): reword the note to not use "Passive **Name**:", teach the pin that a `stillStealth`
  sentence ("Stealthed after <n> s without moving") is the field, or list it as pending.
- Grouped S4: docs/03 "For the data track (M8-3)" still says the D-H2 reading "knows neither field yet", which is stale
  after D35 (sim track's file). Also, D35's temp data copies use `TestDataDir`'s `rts-data-tests/<guid>` folder, not the
  brief's `d35-...` name. They are disposed after use, so this is harmless.

## Fix (D-H10, 2026-11-25)
- shadow.md keeps "Passive **Shadowed**". `PassiveContentTests` reads a stand-still sentence ("[becomes] <Status> after <n> s
  without moving") as the unit's `stillStealth` field, not a data passive: its status by display name and its `delay` in
  seconds, both ways (a unit with `stillStealth` and no such sentence fails; a sentence on a unit without the field fails).
- `QA_TheShadowedSentence_LandsGreenOnItsStillStealthRow` un-skipped and green; new rows
  `ShadowsShadowedSentence_IsTheStillStealthField`, `AMutatedShadowedSentence_FailsNamingTheField` (delay 4 s, 2.5 s, status
  Slowed, "while still"), `AShadowedSentence_OnAUnitWithoutStillStealth_Fails`, `AUnitWithStillStealth_AndNoSentence_Fails`.
- The readiness gap: `PagePinReadinessTests` now names a passive sentence the pin reads but cannot place (no trigger the
  sim has and no stand-still stealth), the pending Shield Wall / Charge excepted
  (`AShadowPassiveThePinCannotPlace_IsOneProblemNamingIt`); every page still has 0 problems.
- The grouped docs/03 line ("knows neither field yet") is the sim track's file: on the sim's M7-H3 brief (item 7), not
  edited here.

## QA verification (D-H10, 2026-11-25-0924)
`QA_TheShadowedSentence_LandsGreenOnItsStillStealthRow` un-skipped and green. QA's own sentences on the Still copy
(`QA/Content/DH10TextPinQaTests`): three true wordings pass; a wrong delay (30 s, 0.3 s, 3.05 s), a wrong status (Hidden),
"4 seconds", a lower-case status and a reworded "when it has not moved for 4 s" each fail naming the passive. None passes
silently.
