# BUG-1370: The Frenzy report's "landed" count still reads the Shaman's first ability slot

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-11-14-0616, task M7-H2 (QA) |
| System | `Stress/FrenzyCastReportTests` (a measurement harness the data track runs read-only) |
| Fixed by | |

## Repro
1. Read `sim/Rts.Sim.Tests/Stress/FrenzyCastReportTests.cs`, `Play`, the per-tick loop:
   `int ready = us.AbilityReadyTick[s * DataLimits.MaxUnitAbilities];`

## Expected
M7-H2 made the report find the Frenzy by id (`data.FindAbility(BloodOilScenes.FrenzyAbility)`, then
`Assert.Contains(data.Units[shaman].Abilities, ...)`) so that a row written in front of it moves nothing. The "landed"
column should read the ready tick at the Frenzy's own slot in the Shaman's ability list
(`data.Units[shaman].Abilities.IndexOf(frenzy.Id)`).

## Actual
"Landed" reads slot 0 of the Shaman's ability list. If the data track gives the Shaman a second ability listed before the
Frenzy, the column counts that ability's casts, and the opening-from-a-file run reports it as Frenzy casts with no error.
Today the Shaman has one ability, so the figures are right: QA re-derived them from the world's resolved Frenzy events
(`Stress/M7H2FrenzyReportQaStressTests`: shipped 2 = 2, diagnostic 65 = 65).

## Notes
- Not in criterion 7's named classes (`StatusLoaderTests`, `BloodOilScenes`), so not an acceptance miss. It is the same kind
  of position lookup in a class this task changed.
- QA's own model, `Stress/FrenzyCastReportQaStressTests`, had the same pattern (`Abilities[0]`, slot 0). QA fixed it this
  session: it now finds the Frenzy by id and reads its slot.
