# BUG-1831: `tools/import-assets.ps1` prints IMPORT ASSETS PASS when its Godot import is killed or dies

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed |
| Found | 2026-11-26-0717, task M6-V30 (QA's fresh-clone re-import) |
| System | import tooling (`tools/import-assets.ps1`, `Invoke-GodotImport`, since M6-V20) |
| Fixed by | M6-VH10 (2026-11-26-1847, view track) |

## Repro
1. Fresh LFS clone of the branch into a scratch folder (no `game/.godot/`).
2. `powershell -File tools/import-assets.ps1 -SourceRoot <main checkout>\asset-sources`.
3. While it prints `importing in Godot ...`, end the Godot process tree (`taskkill /T /F` on the `cmd.exe` the script
   started). QA did this because the first import sat at `[ 36% ] reimport | teblor_shaman.glb` for 53 minutes (18
   zero-byte `*.s3tc.ctex*.tmp` files in `.godot/imported`, last written 12:46; the machine was running four other
   test hosts).

## Expected
A Godot import that did not finish (non-zero exit, killed, crashed) fails the script, as an `ERROR` line in its log does.

## Actual
`Invoke-GodotImport` reads only the log for `ERROR` lines and never looks at Godot's exit code. The script went on and
ended `IMPORT ASSETS PASS`, exit 0, with `.godot/imported` incomplete. A plain `--import` afterwards finished in 21 s with
no `ERROR`, so no committed file was wrong this time. The `.glb`, atlas and `.import` files the script writes itself
matched the committed bytes either way (139 of 139 SHA-256 equal).

## Notes
Fix idea: check `$LASTEXITCODE` after the `cmd /c` call (and a missing `[ DONE ]` or a size-0 `.tmp` left in
`.godot/imported`) and fail with a clear line. The hang itself looks like Godot's parallel texture compression starving
under load (the same texture set imported in 21 s once the load eased); it is not this task's code.

## Fix (M6-VH10)
`Invoke-GodotImport` reads `$LASTEXITCODE` after the `cmd /c` call: non-zero (Godot's own failure, or the process or its
`cmd` killed: `taskkill /F` leaves 1) prints the log's last lines and `FAIL: Godot import exited N ...`, exit 1, no PASS
line. An empty `*.tmp` written during that import in `game/.godot/imported` fails the same way. New
`tools/art/test-import-exit.ps1` runs the script with `-Only` matching nothing, a stand-in Blender file, a stand-in source
folder with the manifest's licence files and four stand-in Godot `.cmd`s, in about 8 s: `ok` (exit 0) passes, `exit1`,
`error0` (exit 0 with an ERROR line) and `killed` (its `cmd.exe` tree ended by `taskkill /T /F` by its recorded id) fail
with exit 1 and no PASS line. On the base script `exit1` and `killed` printed IMPORT ASSETS PASS, exit 0.
