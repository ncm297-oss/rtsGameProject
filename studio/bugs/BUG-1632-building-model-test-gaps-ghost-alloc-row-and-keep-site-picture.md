# BUG-1632: BuildingModelTest gaps: its allocation row draws no remembered model, and its sites picture has no Keep site

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-11-18-0926, task M6-V26 |
| System | Godot test scenes (`game/tests/BuildingModelTest.cs`) |
| Fixed by | |

## Repro
1. `& $env:GODOT --headless --path game res://tests/BuildingModelTest.tscn` and read the allocation line.
2. `& $env:GODOT --path game --resolution 1600x900 res://tests/BuildingModelTest.tscn -- --shots <dir>`, read the
   `picture site` lines, and look at `sites-40m.png`.

## Expected
- The scene's remarks say: "20 frames with models and remembered models allocate nothing."
- Criterion 7: "a site" of the buildings is pictured. The scene tries to picture a site of each type.

## Actual
1. `20 syncs with 3 models and 0 remembered models drawn: 0 bytes`. The row runs after the remembered models have been seen
   again or razed, so the remembered-model path (`Sync`'s ghost loop setting `Mesh`, `Transform` and `MaterialOverride` on the
   ghost model each frame) is never measured.
2. `picture site malazan_garrison_keep: builder 8, site -1 (placement now None)`: the Keep's site is never placed, so
   `sites-40m.png` shows only the Barracks site.

## Notes
- Item 1 is now covered: `QaM6V26Test` "ghost allocation" draws 2 remembered models and measures 0 bytes over 20 syncs.
  The developer's row can either move before the scout returns or drop "remembered models" from its claim.
- Item 2: the picture still meets the criterion ("a site"). The Laborer probably has no time or path to start the 8 x 8
  site within the 40 ticks the scene waits. Either wait longer, or say in the log that the Keep site was skipped.
