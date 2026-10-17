# BUG-0861: The Windows export preset would ship the look-test assets (~22 MB source, ~20 MB imported)

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-17-0056, task M6-V10 |
| System | export (`game/export_presets.cfg`), art assets |
| Fixed by | |

## Repro
1. `game/export_presets.cfg`: `export_filter="all_resources"`, `exclude_filter="tests/*"`.
2. The look-test models and textures live in `game/assets/models/looktest/`, not under `tests/`.

## Expected
Test-only content stays out of a release build (the preset already excludes `tests/*` for that reason).

## Actual
Only `LookTest.tscn`/`.cs` are excluded. The 11 look-test files (22.1 MB in git, the imported
`.ctex`/`.scn` about 20 MB, mostly the 2048² VRAM textures) are all resources and would be packed into
the export. Not reachable yet: `tools/export.ps1` isn't written and nothing in the game references them.

## Notes
Either add `assets/models/looktest/*` to `exclude_filter`, or make sure the art pass (which renames and
re-imports these through the manifest) deletes the folder. Worth a line in the art-pass brief.
