# BUG-1953: TeblorBuildingsTest throws (NullReferenceException) when a Teblor model type is drawn as a box, skipping its later rows (BUG-1912's pattern, in the Teblor scene)

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-11-27-1857, task M6-V32 |
| System | view tests: `game/tests/TeblorBuildingsTest.cs` (`ModelPixels`, `TerraceAndPickRows`) |
| Fixed by | |

## Repro
In a scratch clone of the branch, imported (QA's: `<scratchpad>\clone`, after two full `tools/import-assets.ps1` runs):
1. Replace `game/assets/models/teblor/teblor_stables/teblor_stables.glb` by Git LFS pointer text (or delete it and its
   `.import` and the `.godot/imported/teblor_stables.glb-*` cache).
2. `& $env:GODOT --headless --path game res://tests/TeblorBuildingsTest.tscn`

## Expected
As BUG-1912 fixed for `WhirlwindBuildingsTest` (M6-V32): a type drawn as its box fails its own rows with a line naming it,
and the scene goes on to (S), (F) and (3).

## Actual
```
TEBLOR BUILDINGS TEST FAIL: (H) teblor_stables: no model
TEBLOR BUILDINGS TEST FAIL: teblor_stables: not drawn as a model
TEBLOR BUILDINGS TEST FAIL: (T) teblor_stables: not drawn as its model   (x2)
TEBLOR BUILDINGS TEST FAIL: exception: System.NullReferenceException: Object reference not set to an instance of an object.
   at Rts.Game.Tests.TeblorBuildingsTest.ModelPixels(Int32 slot) in ...\game\tests\TeblorBuildingsTest.cs:line 327
   at Rts.Game.Tests.TeblorBuildingsTest.TerraceAndPickRows(Int32[] types) ... line 266
```
The rows after (P) never run. Same for the LFS pointer and the deleted file. `LastBuildingsTest` (the new scene) and
`BuildingModelTest` do it right in the same clone: FAIL lines naming `teblor_stables` in every row, no exception, every row
run (and exactly one LFS warning per pointer).

## Notes
M6-V32 made the Stables, Breaker's Hall (and the rebuilt Cairn) Teblor model types, so this scene now meets them; the
pattern was there for the M6-V30 eight too. The fix is BUG-1912's one-line guard before `ModelPixels` (and before any
`LUMA`-style read of `ModelOf(slot)!`). `QaM6VH9Test` and `MalazanBuildingsTest` call `ModelPixels(slot)` the same way
(`MalazanBuildingsTest` passed with the Armory as a pointer only because its types are the M6-V26 / V28 list).
