# BUG-1391: D31 small findings: a misplaced doc comment twice, the Cairn's `scale` field order, stale docs/03 lines, "army pop 42" reads per match

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-11-14-0616, task D31 (QA) |
| System | tests (`QA/Content`), data layout, docs |
| Fixed by | |

## Repro
1. `sim/Rts.Sim.Tests/QA/Content/D30OpeningQaStressTests.cs` and `DH8OwnShareQaStressTests.cs`: the
   `/// <summary>docs/03's four rows, typed from the doc (QA's own copy).</summary>` line now sits on the new `Strip`
   helper, not on `Rows` (D31 inserted `Strip` between them). The same `Strip` is pasted into both files;
   `BloodOilScenes.RemoveWhere` already does it.
2. `game/data/factions/teblor/buildings.json`: nine buildings carry `"scale": 1.25` just before `requires`; the Stone
   Cairn carries it between `attack` and `detector`. Loads the same; reads inconsistent.
3. docs/03 (sim-owned, left by the brief): line 3187 "**For the data track (M7-4).** All four fields are in the schema;
   nothing ships them." and the `units.json` schema row's "no shipped unit sets it yet" (`carry`) are false since D31.
4. `docs/factions/teblor.md` "The reserve share, measured": "army pop 42 at 15:00" (and 64, 56, 36) are sums over the
   64 matches (`TeblorLandedShareReportTests`: `g.Sum(r => r.Army15) / 2.0`), less than 1 pop a match; the sentence
   reads as one match's army.

## Expected
Doc comments on the member they describe; one field order in a file; docs/03 true about shipped data; a figure's unit
stated.

## Actual
As above. No behaviour is affected.

## Notes
Item 3 is for the sim track (docs/03 is theirs); items 1, 2 and 4 for the data track's next pass.
