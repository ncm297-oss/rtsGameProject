# BUG-0001: Headless smoke run exits 0 when the C# script fails to load

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-03-0826, task M0-1 |
| System | build / smoke tooling |
| Fixed by | |

## Repro
1. Fresh `git clone` of the branch, do not run `dotnet build`.
2. `& $env:GODOT --headless --path game --import` (exit 0).
3. `& $env:GODOT --headless --path game --quit-after 600`.

## Expected
CLAUDE.md's definition of done treats "headless smoke run exits 0" as the gate, so a boot that
cannot load `Main.cs` should give a non-zero exit code.

## Actual
Exit code 0. The log contains
`ERROR: Cannot instantiate C# script because the associated class could not be found. Script: 'res://scripts/Main.cs'`
and no `Rts.Sim 0.0.1` banner. The same happens if `game/.godot/mono` is deleted after a build.

## Notes
This is Godot behaviour, not a code defect, and the developer already documented it in CLAUDE.md
(build first; check the log for the banner and no `ERROR`). The risk is that agents or scripts
keep trusting the exit code alone. QA added `tools/qa/smoke.ps1`, which builds, imports, boots,
and fails on `ERROR`/`Unhandled`/`Exception` lines or a missing banner (verified to return 1 on
this repro). Suggested fix: point the CLAUDE.md smoke command (and the game-dev agent's definition
of done) at that script, or an equivalent `tools/smoke.ps1` owned by game-dev.

**Producer triage (2026-10-03-0826):** S3 confirmed, does not block M0 acceptance. Fix as a
small item at the start of M1: make CLAUDE.md's smoke command (and the game-dev definition of
done) run `tools/qa/smoke.ps1`, so the exit code is the gate again.
