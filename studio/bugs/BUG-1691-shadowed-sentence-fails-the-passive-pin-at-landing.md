# BUG-1691: shadow.md's "Passive **Shadowed**" note will fail the passive pin when the roster lands (stillStealth is not a passive); also stale docs

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-11-24-1431, task D35 |
| System | faction page / content pins (`PassiveContentTests`, `PagePinReadinessTests`) |
| Fixed by | |

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
