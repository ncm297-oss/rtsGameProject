# BUG-0861: The Windows export preset would ship the look-test assets (~22 MB source, ~20 MB imported)

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed (M6-VH3, verified by QA 2026-10-17-1624) |
| Found | 2026-10-17-0056, task M6-V10 |
| System | export (`game/export_presets.cfg`), art assets |
| Fixed by | c0a8d5b (M6-VH3): `exclude_filter="tests/*, assets/models/looktest/*"`; proof: a pack-only export (no test scene: export has no scene-loop row) |

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

## Verification (QA 2026-10-17-1624, M6-VH3)
`& $env:GODOT --headless --path game --export-pack "Windows Desktop" <scratch>\pack.pck` on c0a8d5b: exit 0, 92,752 bytes,
79 distinct `res://` paths in the pack, none under `assets/models/looktest/` or `tests/`.
