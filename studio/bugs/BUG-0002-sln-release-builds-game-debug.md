# BUG-0002: RtsGame.sln Release configuration builds RtsGame in Debug

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-03-0826, task M0-1 |
| System | solution / build config |
| Fixed by | |

## Repro
1. `dotnet build RtsGame.sln -c Release`

## Expected
Either every project builds in Release, or the mapping is explicit and documented (Godot's own
generated solutions use `ExportDebug`/`ExportRelease` configurations for the game project).

## Actual
`RtsGame.sln` maps `Release|Any CPU` of the RtsGame project to `Debug|Any CPU`; the output is
`game\.godot\mono\temp\bin\Debug\RtsGame.dll` while the sim projects go to `bin\Release`.

## Notes
Harmless at M0 (no export yet). Worth settling when `tools/export.ps1` is written (M6), so a
"Release" build doesn't silently ship debug game code.

**Producer triage (2026-10-03-0826):** S4 confirmed, does not block. Fix when `tools/export.ps1`
is written (M6); until then the sim projects are the only ones where Release matters (perf tests).
