# BUG-0010: A faction with no units, or a missing or doubled template slot, loads clean

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-03-1151, task M1-2 |
| System | data loader |
| Fixed by | |

## Repro
1. Un-skip `Rts.Sim.Tests.QA.DataLoaderQaTests.FactionWithEmptyUnitsList_IsRejected` and
   `FactionWithTwoUnitsInOneSlot_IsRejected`.
2. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~FactionWith"`

By hand: write `{"units": []}` to `factions/whirlwind/units.json`, or change
`malazan_sapper.slot` to `"ranged"`. Either way `LoadAll` returns `Ok`.

## Expected
docs/02 "Faction template": every faction fills the seven template slots. The loader is the
natural place to enforce "exactly one unit per slot per faction". A faction with zero units can't
train anything.

## Actual
No error. Only the shipped-data test `ShippedData_MalazanAndWhirlwind_EachFillAllSevenSlotsOnce`
checks this, and only for the two shipped factions; a new faction's data gets no such check.

## Notes
Outside the M1-2 validation list (the developer flagged it as a follow-up), so S4. Related
cross-field rules that also aren't checked (no test filed): `attack.minRange` greater than
`attack.range`, and `attack.windup` longer than `attack.cooldown`. Decide whether a faction may
ever have two units in one slot (e.g. a future second unique) before enforcing "exactly one".

**Producer triage (2026-10-03-1151):** S4 confirmed. docs/02 says every faction fills the seven slots; enforce "exactly one unit per slot" with the M3 data task (when `trainedAt` is resolved against `buildings.json`). If a faction ever needs a second unit in a slot, that is a design change for docs/02 first.
