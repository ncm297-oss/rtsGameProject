# BUG-0242: A map-of-numbers field given the wrong type says "expected a number", not "an object"

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-08-2144, task M4-H1 (QA on BUG-0113 item 1's fix) |
| System | data loader (`DataLoader.ExpectedKind`) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~DataTypeMessageQaTests"`. The skipped theory
   `MapOfNumbersField_GivenANumberOrAList_SaysExpectedAnObject` is this bug. The pin
   `Bug0242Pin_MapOfNumbersField_SaysExpectedANumber` is green today.
2. In a copy of the shipped data, set `malazan_heavy_infantry`'s `attack.bonusVs` to `1.5` or `[1.5]`.

## Expected
`bonusVs` (and `damage_table.json`'s `multipliers`) is a name-to-number map, so the message should say
"expected an object", like `attack` given `12` does.

## Actual
`$.units[1].attack.bonusVs: wrong type of value at line 1, byte 796: expected a number`.

## Notes
`ExpectedKind` reads System.Text.Json's type name and tests `System.Double` before `Dictionary`. The type name for these
fields is ``Dictionary`2[System.String,System.Double]``, so the number test matches first. Check `Dictionary` (and
``List`1``) before the element types. The other shapes are right: object, list of strings, string, whole number, bool,
and a wrong value inside the map (`bonusVs.mounted: "high"` says "expected a number"). Those are now
`WrongTypedValue_NamesWhatTheFieldWants` rows.
